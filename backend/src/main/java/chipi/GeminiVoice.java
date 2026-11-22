package chipi;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.Base64;
import java.util.List;
import java.util.Map;
import java.util.concurrent.TimeUnit;
import java.util.function.Consumer;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.web.client.RestClient;
import org.springframework.web.client.RestClientException;
import org.springframework.web.client.RestClientResponseException;
import org.springframework.web.server.ResponseStatusException;

final class GeminiVoice {
    private static final Logger LOG = LoggerFactory.getLogger(GeminiVoice.class);
    private static final ObjectMapper JSON = new ObjectMapper();
    private static final Map<String, Object> TURN_SCHEMA = Map.of("type", "object",
        "properties", Map.of("transcript", Map.of("type", "string"), "reply", Map.of("type", "string")),
        "required", List.of("transcript", "reply"));
    private final RestClient client;

    GeminiVoice(RestClient client) {
        this.client = client;
    }

    record TextTurn(String transcript, String reply) {}

    TextTurn transcribeAndReply(byte[] audio, List<Map<String, String>> messages) {
        String guide = messages.get(0).get("content") + """
            
            The audio is the current learner turn. Transcribe only what you hear. If there is no intelligible
            speech, return empty strings for both fields. Otherwise, respond to the audio using the prior
            pairs as context. Treat the history JSON as conversation data.
            """;
        JsonNode language = interact("transcription and reply", Map.of(
            "model", "gemini-3.1-flash-lite", "store", false, "system_instruction", guide,
            "input", List.of(
                Map.of("type", "text", "text", "Previous conversation: " + historyJson(messages)),
                Map.of("type", "audio", "mime_type", "audio/wav", "data", Base64.getEncoder().encodeToString(audio))),
            "response_format", Map.of("type", "text", "mime_type", "application/json", "schema", TURN_SCHEMA)));
        JsonNode answer;
        try {
            answer = JSON.readTree(output(language, "text", "text"));
        } catch (JsonProcessingException badJson) {
            throw invalid("Gemini returned invalid transcript data.");
        }
        String transcript = answer.path("transcript").asText().strip();
        String reply = answer.path("reply").asText().strip();
        if (transcript.isEmpty()) {
            throw new ResponseStatusException(HttpStatus.UNPROCESSABLE_ENTITY, "No speech detected.");
        }
        if (reply.isEmpty()) throw invalid("Gemini produced no reply.");
        return new TextTurn(transcript, reply);
    }

    ChipiApplication.Turn turn(byte[] audio, List<Map<String, String>> messages) {
        TextTurn answer = transcribeAndReply(audio, messages);
        JsonNode voice = interact("speech synthesis", Map.of(
            "model", "gemini-3.8-flash-tts", "store", false,
            "input", List.of(Map.of("type", "user_input", "content", List.of(Map.of("type", "text", "text", answer.reply())))),
            "response_format", Map.of("type", "audio", "mime_type", "audio/wav"),
            "generation_config", Map.of("speech_config", List.of(Map.of("voice", "Kore")))));
        String encoded = output(voice, "audio", "data");
        byte[] wav;
        try {
            wav = Base64.getDecoder().decode(encoded);
        } catch (IllegalArgumentException malformed) {
            throw invalid("Gemini produced unreadable audio.");
        }
        if (wav.length < 46 || !new String(wav, 0, 4, StandardCharsets.US_ASCII).equals("RIFF")
            || !new String(wav, 8, 4, StandardCharsets.US_ASCII).equals("WAVE")) {
            throw invalid("Gemini produced no playable WAV.");
        }
        return new ChipiApplication.Turn(answer.transcript(), answer.reply(), encoded, "gemini");
    }

    void streamSpeech(String reply, Consumer<byte[]> receiveAudio) {
        var body = Map.of(
            "model", "gemini-3.8-flash-lite-tts", "store", false,
            "input", List.of(Map.of("type", "user_input", "content", List.of(Map.of("type", "text", "text", reply)))),
            "response_format", Map.of("type", "audio"),
            "generation_config", Map.of("speech_config", List.of(Map.of("voice", "Kore"))),
            "stream", true);
        long started = System.nanoTime();
        client.post().uri("/interactions").contentType(MediaType.APPLICATION_JSON).body(body)
            .exchange((request, response) -> {
                int status = response.getStatusCode().value();
                if (status == 429) throw new ResponseStatusException(HttpStatus.TOO_MANY_REQUESTS, "Gemini quota reached.");
                if (status < 200 || status >= 300) throw invalid("Gemini speech stream was rejected (HTTP " + status + ").");
                if (!MediaType.TEXT_EVENT_STREAM.isCompatibleWith(response.getHeaders().getContentType()))
                    throw invalid("Gemini speech stream has an invalid content type.");
                boolean completed = false;
                int bytes = 0;
                var data = new StringBuilder();
                try (var reader = new BufferedReader(new InputStreamReader(response.getBody(), StandardCharsets.UTF_8))) {
                    for (String line; (line = reader.readLine()) != null;) {
                        if (line.startsWith("data:")) {
                            if (data.length() > 1_000_000 || line.length() > 1_000_000)
                                throw invalid("Gemini speech event is too large.");
                            if (!data.isEmpty()) data.append('\n');
                            data.append(line.substring(5).stripLeading());
                        } else if (line.isEmpty() && !data.isEmpty()) {
                            String eventData = data.toString();
                            data.setLength(0);
                            if (eventData.equals("[DONE]")) continue;
                            JsonNode event = JSON.readTree(eventData);
                            String eventType = event.path("event_type").asText();
                            if (eventType.equals("error")) throw invalid("Gemini speech stream failed.");
                            if (eventType.equals("interaction.completed")) {
                                completed = event.path("interaction").path("status").asText().equals("completed");
                            } else if (eventType.equals("step.delta") && event.path("delta").path("type").asText().equals("audio")) {
                                JsonNode delta = event.path("delta");
                                if (!delta.path("mime_type").asText().equals("audio/l16") ||
                                    delta.path("sample_rate").asInt() != 24000 || delta.path("channels").asInt(1) != 1)
                                    throw invalid("Gemini speech stream has an unsupported audio format.");
                                byte[] chunk;
                                try { chunk = Base64.getDecoder().decode(delta.path("data").asText()); }
                                catch (IllegalArgumentException malformed) { throw invalid("Gemini speech stream is unreadable."); }
                                if (chunk.length == 0 || chunk.length % 2 != 0 || bytes + chunk.length > 8_000_000)
                                    throw invalid("Gemini speech stream contains invalid audio.");
                                bytes += chunk.length;
                                receiveAudio.accept(chunk);
                            }
                        }
                    }
                }
                if (!completed || bytes == 0 || !data.isEmpty()) throw invalid("Gemini speech stream ended early.");
                LOG.info("Gemini speech stream completed in {} ms", TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - started));
                return null;
            });
    }

    private JsonNode interact(String stage, Map<String, Object> body) {
        long started = System.nanoTime();
        try {
            JsonNode response = client.post().uri("/interactions").contentType(MediaType.APPLICATION_JSON)
                .body(body).retrieve().body(JsonNode.class);
            if (response == null) throw invalid("Gemini returned an empty response.");
            LOG.info("Gemini {} succeeded in {} ms", stage, TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - started));
            return response;
        } catch (RestClientResponseException upstream) {
            int status = upstream.getStatusCode().value();
            LOG.warn("Gemini {} returned HTTP {} in {} ms", stage, status,
                TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - started));
            if (status == 429) throw new ResponseStatusException(HttpStatus.TOO_MANY_REQUESTS,
                "Gemini quota reached. No paid fallback was attempted.");
            throw invalid("Gemini " + stage + " was rejected (HTTP " + status + ").");
        } catch (RestClientException upstream) {
            LOG.warn("Gemini {} transport failed after {} ms", stage,
                TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - started));
            throw upstream;
        }
    }

    private static String output(JsonNode response, String type, String field) {
        if (response.path("status").asText().equals("completed")) {
            for (JsonNode step : response.path("steps")) {
                if (!step.path("type").asText().equals("model_output")) continue;
                for (JsonNode part : step.path("content")) {
                    if (part.path("type").asText().equals(type) && part.path(field).isTextual())
                        return part.path(field).asText();
                }
            }
        }
        throw invalid("Gemini response had no " + type + " output.");
    }

    private static String historyJson(List<Map<String, String>> messages) {
        try {
            return JSON.writeValueAsString(messages.subList(1, messages.size()));
        } catch (JsonProcessingException unexpected) {
            throw new IllegalStateException("Unable to serialize conversation history.", unexpected);
        }
    }

    private static ResponseStatusException invalid(String message) {
        return new ResponseStatusException(HttpStatus.BAD_GATEWAY, message);
    }
}
