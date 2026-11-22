package chipi;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.DeserializationFeature;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import java.io.BufferedInputStream;
import java.io.OutputStream;
import java.io.UncheckedIOException;
import java.time.Duration;
import java.util.ArrayList;
import java.util.Base64;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.Semaphore;
import javax.sound.sampled.AudioSystem;
import javax.sound.sampled.UnsupportedAudioFileException;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.core.io.ByteArrayResource;
import org.springframework.core.io.ClassPathResource;
import org.springframework.http.*;
import org.springframework.http.client.JdkClientHttpRequestFactory;
import org.springframework.util.LinkedMultiValueMap;
import org.springframework.web.bind.annotation.*;
import org.springframework.web.client.RestClient;
import org.springframework.web.client.RestClientException;
import org.springframework.web.multipart.MultipartFile;
import org.springframework.web.server.ResponseStatusException;
import org.springframework.web.servlet.mvc.method.annotation.StreamingResponseBody;

@SpringBootApplication
@RestController
public class ChipiApplication {
    private static final Logger LOG = LoggerFactory.getLogger(ChipiApplication.class);
    private static final int MAX_HISTORY_TURNS = 3;
    private static final int MAX_HISTORY_MESSAGE_LENGTH = 1000;
    private static final int MAX_HISTORY_JSON_LENGTH = 36_200;
    private static final ObjectMapper HISTORY_JSON = new ObjectMapper()
        .enable(DeserializationFeature.FAIL_ON_READING_DUP_TREE_KEY)
        .enable(DeserializationFeature.FAIL_ON_TRAILING_TOKENS);
    private static final String TUTOR_INSTRUCTIONS = "You are Chipi, a friendly English tutor. Reply in simple English in at most two short sentences. Offer one gentle correction when useful and ask a short follow-up question.";

    private final RestClient openAI;
    private final boolean demo;
    private final GeminiVoice gemini;
    private final Semaphore capacity = new Semaphore(2);

    @Autowired
    public ChipiApplication(@Value("${OPENAI_API_KEY:}") String key,
                            @Value("${CHIPI_DEMO:true}") boolean demo,
                            @Value("${CHIPI_VOICE_PROVIDER:openai}") String provider,
                            @Value("${GEMINI_API_KEY:}") String geminiKey) {
        this(key, demo, "https://api.openai.com/v1", provider, geminiKey);
    }

    ChipiApplication(String key, boolean demo, String providerUrl) {
        this(key, demo, providerUrl, "openai", "");
    }

    ChipiApplication(GeminiVoice gemini) {
        this.demo = false;
        this.gemini = gemini;
        this.openAI = null;
    }

    private ChipiApplication(String key, boolean demo, String providerUrl, String provider, String geminiKey) {
        if (!provider.equals("openai") && !provider.equals("gemini")) {
            throw new IllegalStateException("CHIPI_VOICE_PROVIDER must be openai or gemini.");
        }
        this.demo = demo && provider.equals("openai");
        boolean useGemini = provider.equals("gemini");
        if (useGemini && geminiKey.isBlank()) {
            throw new IllegalStateException("Set GEMINI_API_KEY to use Gemini voice.");
        }
        if (!this.demo && !useGemini && key.isBlank()) {
            throw new IllegalStateException("Set OPENAI_API_KEY for live voice.");
        }
        var transport = java.net.http.HttpClient.newBuilder()
            .connectTimeout(Duration.ofSeconds(8)).build();
        var factory = new JdkClientHttpRequestFactory(transport);
        factory.setReadTimeout(Duration.ofSeconds(18));
        this.gemini = useGemini ? new GeminiVoice(RestClient.builder().requestFactory(factory)
            .baseUrl("https://generativelanguage.googleapis.com/v1beta")
            .defaultHeader("x-goog-api-key", geminiKey).build()) : null;
        this.openAI = RestClient.builder().requestFactory(factory)
            .baseUrl(providerUrl)
            .defaultHeaders(headers -> headers.setBearerAuth(key))
            .build();
    }

    public static void main(String[] args) {
        SpringApplication.run(ChipiApplication.class, args);
    }

    @GetMapping("/health")
    public Map<String, String> health() {
        return Map.of("status", "ok", "mode", demo ? "demo" : gemini != null ? "gemini" : "live");
    }

    record Turn(String transcript, String reply, String audioBase64, String mode) {}

    @PostMapping(value = "/api/turn", params = "!stream", consumes = MediaType.MULTIPART_FORM_DATA_VALUE)
    public Turn turn(@RequestParam("audio") MultipartFile upload,
                     @RequestParam(name = "history", required = false) String history) throws IOException {
        byte[] audio = audioBytes(upload);
        List<Map<String, String>> messages = messages(history);
        if (!capacity.tryAcquire()) {
            throw new ResponseStatusException(HttpStatus.TOO_MANY_REQUESTS, "Chipi is busy. Try again.");
        }
        try {
            if (demo) {
                try (var fixture = new ClassPathResource("demo.wav").getInputStream()) {
                    return result("Demo: I like apples.", "Nice! What is your favorite fruit?",
                        fixture.readAllBytes(), "demo");
                }
            }
            if (gemini != null) return gemini.turn(audio, messages);
            var form = new LinkedMultiValueMap<String, Object>();
            form.add("model", "whisper-1");
            form.add("language", "en");
            var fileHeaders = new HttpHeaders();
            fileHeaders.setContentDispositionFormData("file", "recording.wav");
            fileHeaders.setContentType(MediaType.parseMediaType("audio/wav"));
            form.add("file", new HttpEntity<>(new ByteArrayResource(audio), fileHeaders));
            String transcript = json("/audio/transcriptions", form, MediaType.MULTIPART_FORM_DATA)
                .path("text").asText().strip();
            if (transcript.isEmpty()) {
                throw new ResponseStatusException(HttpStatus.UNPROCESSABLE_ENTITY, "No speech detected.");
            }
            messages.add(Map.of("role", "user", "content", transcript));
            String reply = json("/chat/completions", Map.of(
                "model", "gpt-4o-mini", "max_completion_tokens", 100,
                "messages", messages), MediaType.APPLICATION_JSON)
                .path("choices").path(0).path("message").path("content").asText().strip();
            if (reply.isEmpty()) {
                throw new ResponseStatusException(HttpStatus.BAD_GATEWAY, "No reply received.");
            }
            byte[] spoken = openAI.post().uri("/audio/speech").contentType(MediaType.APPLICATION_JSON)
                .body(Map.of("model", "gpt-4o-mini-tts", "voice", "nova", "input", reply, "response_format", "wav"))
                .retrieve().body(byte[].class);
            if (spoken == null || spoken.length < 46 || !tag(spoken, 0, "RIFF") || !tag(spoken, 8, "WAVE")) {
                throw new ResponseStatusException(HttpStatus.BAD_GATEWAY, "No playable speech received.");
            }
            return result(transcript, reply, spoken, "live");
        } catch (RestClientException upstream) {
            throw new ResponseStatusException(HttpStatus.BAD_GATEWAY, "Voice provider unavailable.");
        } finally {
            capacity.release();
        }
    }

    @PostMapping(value = "/api/turn", params = "stream", consumes = MediaType.MULTIPART_FORM_DATA_VALUE,
                 produces = "application/x-ndjson")
    public StreamingResponseBody streamTurn(@RequestParam("audio") MultipartFile upload,
                                            @RequestParam(name = "history", required = false) String history,
                                            @RequestHeader(name = "X-Chipi-Turn-Id", required = false) String requestTurnId,
                                            HttpServletResponse response) throws IOException {
        if (gemini == null && !demo) throw new ResponseStatusException(HttpStatus.NOT_IMPLEMENTED, "Streaming is unavailable.");
        byte[] audio = audioBytes(upload);
        List<Map<String, String>> messages = messages(history);
        String turnId = diagnosticTurnId(requestTurnId);
        String mode = demo ? "demo" : gemini != null ? "gemini" : "openai";
        LOG.info("voice.turn accepted id={} mode={} audioBytes={} historyTurns={}", turnId, mode, audio.length,
            Math.max(0, messages.size() - 1));
        response.setContentType("application/x-ndjson");
        return output -> {
            long turnStarted = System.nanoTime();
            String stage = "capacity";
            String outcome = "failed";
            if (!capacity.tryAcquire()) {
                LOG.warn("voice.turn failed id={} stage={} status=429", turnId, stage);
                writeEvent(output, Map.of("type", "error", "status", 429, "stage", stage));
                LOG.info("voice.turn finished id={} stage={} outcome=busy", turnId, stage);
                return;
            }
            try {
                writeEvent(output, Map.of("type", "ready", "timing", true));
                if (demo) {
                    stage = "demo_audio";
                    try (var fixture = new BufferedInputStream(new ClassPathResource("demo.wav").getInputStream());
                        var speech = AudioSystem.getAudioInputStream(fixture)) {
                        var format = speech.getFormat();
                        if (format.getChannels() != 1 || format.getSampleSizeInBits() != 16 || format.isBigEndian())
                            throw new IOException("Unsupported demo audio format.");
                        writeEvent(output, Map.of("type", "metadata", "transcript", "Demo: I like apples.",
                            "reply", "Nice! What is your favorite fruit?", "rate", (int)format.getSampleRate(), "mode", "demo", "textMs", 0));
                        byte[] buffer = new byte[4096];
                        for (int count; (count = speech.read(buffer)) != -1;) {
                            if (count > 0) writeEvent(output, Map.of("type", "audio", "data",
                                Base64.getEncoder().encodeToString(java.util.Arrays.copyOf(buffer, count))));
                        }
                    } catch (UnsupportedAudioFileException invalid) {
                        throw new IOException("Unsupported demo audio.", invalid);
                    }
                    writeEvent(output, Map.of("type", "complete"));
                    outcome = "complete";
                    return;
                }
                stage = "transcription";
                long textStarted = System.nanoTime();
                LOG.info("voice.turn stage_started id={} stage={} audioBytes={} historyTurns={}", turnId,
                    stage, audio.length, Math.max(0, messages.size() - 1));
                GeminiVoice.TextTurn answer = gemini.transcribeAndReply(audio, messages);
                long textMs = java.util.concurrent.TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - textStarted);
                LOG.info("voice.turn stage_succeeded id={} stage={} elapsedMs={} transcriptChars={} replyChars={}",
                    turnId, stage, textMs, answer.transcript().length(), answer.reply().length());
                writeEvent(output, Map.of("type", "metadata", "transcript", answer.transcript(),
                    "reply", answer.reply(), "rate", 24000, "mode", "gemini", "textMs", textMs));
                stage = "speech_synthesis";
                long ttsStarted = System.nanoTime();
                LOG.info("voice.turn stage_started id={} stage={}", turnId, stage);
                var firstAudio = new java.util.concurrent.atomic.AtomicBoolean(true);
                gemini.streamSpeech(answer.reply(), chunk -> {
                    try {
                        String data = Base64.getEncoder().encodeToString(chunk);
                        if (firstAudio.getAndSet(false)) {
                            long ttsMs = java.util.concurrent.TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - ttsStarted);
                            writeEvent(output, Map.of("type", "audio", "data", data, "ttsFirstMs", ttsMs));
                        } else {
                            writeEvent(output, Map.of("type", "audio", "data", data));
                        }
                    } catch (IOException disconnected) { throw new UncheckedIOException(disconnected); }
                });
                LOG.info("voice.turn stage_succeeded id={} stage={} elapsedMs={}", turnId, stage,
                    java.util.concurrent.TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - ttsStarted));
                writeEvent(output, Map.of("type", "complete"));
                stage = "complete";
                outcome = "complete";
            } catch (UncheckedIOException disconnected) {
                outcome = "client_disconnected";
                LOG.warn("voice.turn client_disconnected id={} stage={}", turnId, stage);
                throw disconnected.getCause();
            } catch (ResponseStatusException failure) {
                outcome = "provider_error";
                int status = failure.getStatusCode().value();
                LOG.warn("voice.turn failed id={} stage={} status={}", turnId, stage, status);
                writeEvent(output, Map.of("type", "error", "status", status, "stage", stage));
            } catch (RestClientException failure) {
                outcome = "provider_transport_error";
                LOG.warn("voice.turn failed id={} stage={} status=502 cause={}", turnId, stage,
                    failure.getClass().getSimpleName());
                writeEvent(output, Map.of("type", "error", "status", 502, "stage", stage));
            } finally {
                LOG.info("voice.turn finished id={} stage={} outcome={} elapsedMs={}", turnId, stage, outcome,
                    java.util.concurrent.TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - turnStarted));
                capacity.release();
            }
        };
    }

    private static String diagnosticTurnId(String candidate) {
        if (candidate != null) {
            try {
                UUID parsed = UUID.fromString(candidate);
                if (parsed.toString().equalsIgnoreCase(candidate)) return parsed.toString();
            } catch (IllegalArgumentException ignored) {
            }
        }
        return UUID.randomUUID().toString();
    }

    private static void writeEvent(OutputStream output, Map<String, Object> event) throws IOException {
        output.write((HISTORY_JSON.writeValueAsString(event) + "\n").getBytes(java.nio.charset.StandardCharsets.UTF_8));
        output.flush();
    }

    private static byte[] audioBytes(MultipartFile upload) throws IOException {
        byte[] audio = upload.getBytes();
        if (audio.length < 46 || !tag(audio, 0, "RIFF") || !tag(audio, 8, "WAVE"))
            throw new ResponseStatusException(HttpStatus.BAD_REQUEST, "A WAV recording is required.");
        return audio;
    }

    private static List<Map<String, String>> messages(String history) {
        List<Map<String, String>> messages = new ArrayList<>();
        messages.add(Map.of("role", "system", "content", TUTOR_INSTRUCTIONS));
        appendHistory(messages, history);
        return messages;
    }

    private static void appendHistory(List<Map<String, String>> messages, String history) {
        if (history == null) return;
        if (history.length() > MAX_HISTORY_JSON_LENGTH) throw invalidHistory();
        try {
            JsonNode envelope = HISTORY_JSON.readTree(history);
            if (envelope == null || !envelope.isObject() || envelope.size() != 1) throw invalidHistory();
            JsonNode turns = envelope.path("turns");
            if (!turns.isArray() || turns.size() > MAX_HISTORY_TURNS) throw invalidHistory();
            for (JsonNode turn : turns) {
                JsonNode transcript = turn.path("transcript");
                JsonNode reply = turn.path("reply");
                if (!turn.isObject() || turn.size() != 2 || !validHistoryMessage(transcript)
                    || !validHistoryMessage(reply)) throw invalidHistory();
                messages.add(Map.of("role", "user", "content", transcript.textValue()));
                messages.add(Map.of("role", "assistant", "content", reply.textValue()));
            }
        } catch (JsonProcessingException e) {
            throw invalidHistory();
        }
    }

    private static boolean validHistoryMessage(JsonNode value) {
        return value.isTextual() && !value.textValue().isBlank()
            && value.textValue().length() <= MAX_HISTORY_MESSAGE_LENGTH;
    }

    private static ResponseStatusException invalidHistory() {
        return new ResponseStatusException(HttpStatus.BAD_REQUEST, "Invalid conversation history.");
    }

    private JsonNode json(String path, Object body, MediaType mediaType) {
        JsonNode response = openAI.post().uri(path).contentType(mediaType).body(body)
            .retrieve().body(JsonNode.class);
        if (response == null) {
            throw new ResponseStatusException(HttpStatus.BAD_GATEWAY, "Empty voice provider response.");
        }
        return response;
    }

    private static Turn result(String transcript, String reply, byte[] audio, String mode) {
        return new Turn(transcript, reply, Base64.getEncoder().encodeToString(audio), mode);
    }

    private static boolean tag(byte[] data, int offset, String tag) {
        for (int index = 0; index < tag.length(); index++) {
            if (data[offset + index] != tag.charAt(index)) return false;
        }
        return true;
    }
}
