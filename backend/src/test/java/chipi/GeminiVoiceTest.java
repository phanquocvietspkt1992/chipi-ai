package chipi;

import com.fasterxml.jackson.databind.ObjectMapper;
import java.util.Base64;
import java.util.List;
import java.util.Map;
import java.io.ByteArrayOutputStream;
import org.junit.jupiter.api.Test;
import org.springframework.http.MediaType;
import org.springframework.test.web.client.MockRestServiceServer;
import org.springframework.web.client.RestClient;
import static org.junit.jupiter.api.Assertions.*;
import static org.springframework.test.web.client.match.MockRestRequestMatchers.*;
import static org.springframework.test.web.client.response.MockRestResponseCreators.withSuccess;

class GeminiVoiceTest {
    @Test
    void streamsPcmChunksAndRequiresCompletion() {
        var builder = RestClient.builder().baseUrl("https://example.test/v1beta");
        var server = MockRestServiceServer.bindTo(builder).build();
        String chunk = "data: {\"event_type\":\"step.delta\",\"delta\":{\"type\":\"audio\",\"mime_type\":\"audio/l16\",\"sample_rate\":24000,\"data\":\"AQIDBA==\"}}\n\n";
        String completed = "data: {\"event_type\":\"interaction.completed\",\"interaction\":{\"status\":\"completed\"}}\n\n";
        server.expect(requestTo("https://example.test/v1beta/interactions"))
            .andExpect(jsonPath("$.stream").value(true))
            .andExpect(jsonPath("$.generation_config.speech_config[0].voice").value("Kore"))
            .andRespond(withSuccess(chunk + chunk + completed, MediaType.TEXT_EVENT_STREAM));
        var output = new ByteArrayOutputStream();
        new GeminiVoice(builder.build()).streamSpeech("Hello", output::writeBytes);
        assertArrayEquals(new byte[] {1, 2, 3, 4, 1, 2, 3, 4}, output.toByteArray());
        server.verify();

        var incompleteBuilder = RestClient.builder().baseUrl("https://example.test/v1beta");
        var incompleteServer = MockRestServiceServer.bindTo(incompleteBuilder).build();
        incompleteServer.expect(requestTo("https://example.test/v1beta/interactions"))
            .andRespond(withSuccess(chunk, MediaType.TEXT_EVENT_STREAM));
        assertThrows(org.springframework.web.server.ResponseStatusException.class,
            () -> new GeminiVoice(incompleteBuilder.build()).streamSpeech("Hello", ignored -> {}));
        incompleteServer.verify();
    }

    @Test
    void stopsReadingWhenAudioConsumerDisconnects() {
        var builder = RestClient.builder().baseUrl("https://example.test/v1beta");
        var server = MockRestServiceServer.bindTo(builder).build();
        String chunk = "data: {\"event_type\":\"step.delta\",\"delta\":{\"type\":\"audio\",\"mime_type\":\"audio/l16\",\"sample_rate\":24000,\"data\":\"AQIDBA==\"}}\n\n";
        server.expect(requestTo("https://example.test/v1beta/interactions"))
            .andRespond(withSuccess(chunk + chunk, MediaType.TEXT_EVENT_STREAM));
        var calls = new java.util.concurrent.atomic.AtomicInteger();
        assertThrows(java.io.UncheckedIOException.class, () -> new GeminiVoice(builder.build())
            .streamSpeech("Hello", ignored -> {
                calls.incrementAndGet();
                throw new java.io.UncheckedIOException(new java.io.IOException("disconnected"));
            }));
        assertEquals(1, calls.get());
        server.verify();
    }

    @Test
    void requestsTranscriptAndReplyThenSynthesizesWav() throws Exception {
        byte[] wav;
        try (var audio = getClass().getResourceAsStream("/demo.wav")) {
            wav = audio.readAllBytes();
        }
        var builder = RestClient.builder().baseUrl("https://example.test/v1beta");
        var server = MockRestServiceServer.bindTo(builder).build();
        var json = new ObjectMapper();

        server.expect(requestTo("https://example.test/v1beta/interactions"))
            .andExpect(jsonPath("$.model").value("gemini-3.1-flash-lite"))
            .andExpect(jsonPath("$.store").value(false))
            .andExpect(jsonPath("$.input[1].mime_type").value("audio/wav"))
            .andRespond(withSuccess(json.writeValueAsString(response("text", "text",
                "{\"transcript\":\"I like apples.\",\"reply\":\"Why do you like apples?\"}")), MediaType.APPLICATION_JSON));
        server.expect(requestTo("https://example.test/v1beta/interactions"))
            .andExpect(jsonPath("$.model").value("gemini-3.8-flash-tts"))
            .andExpect(jsonPath("$.response_format.mime_type").value("audio/wav"))
            .andExpect(jsonPath("$.input[0].content[0].text").value("Why do you like apples?"))
            .andRespond(withSuccess(json.writeValueAsString(response("audio", "data",
                Base64.getEncoder().encodeToString(wav))), MediaType.APPLICATION_JSON));

        var turn = new GeminiVoice(builder.build()).turn(wav, List.of(Map.of("role", "system", "content", "Tutor")));
        assertEquals("gemini", turn.mode());
        assertEquals("I like apples.", turn.transcript());
        assertEquals("Why do you like apples?", turn.reply());
        assertArrayEquals(wav, Base64.getDecoder().decode(turn.audioBase64()));
        server.verify();
    }

    private static Map<String, Object> response(String type, String field, String value) {
        return Map.of("status", "completed", "steps", List.of(
            Map.of("type", "model_output", "content", List.of(Map.of("type", type, field, value)))));
    }
}
