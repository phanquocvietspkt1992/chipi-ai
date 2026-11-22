package chipi;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.sun.net.httpserver.HttpServer;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.Base64;
import java.util.List;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.Test;
import org.springframework.mock.web.MockMultipartFile;
import org.springframework.http.MediaType;
import org.springframework.test.web.client.MockRestServiceServer;
import org.springframework.test.web.servlet.setup.MockMvcBuilders;
import org.springframework.web.client.RestClient;
import org.springframework.web.server.ResponseStatusException;
import static org.junit.jupiter.api.Assertions.*;
import static org.springframework.test.web.client.match.MockRestRequestMatchers.*;
import static org.springframework.test.web.client.response.MockRestResponseCreators.withSuccess;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.asyncDispatch;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.multipart;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.request;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.content;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

class VoiceProviderTest {
    @Test
    void streamsDemoFixtureAsPcm() throws Exception {
        var chipi = new ChipiApplication("", true, "http://127.0.0.1:1/v1");
        var mvc = MockMvcBuilders.standaloneSetup(chipi).build();
        try (var audio = getClass().getResourceAsStream("/demo.wav")) {
            var input = new MockMultipartFile("audio", "sample.wav", "audio/wav", audio.readAllBytes());
            var pending = mvc.perform(multipart("/api/turn").file(input).param("stream", "1"))
                .andExpect(request().asyncStarted()).andReturn();
            String result = mvc.perform(asyncDispatch(pending)).andExpect(status().isOk())
                .andExpect(content().contentTypeCompatibleWith("application/x-ndjson"))
                .andReturn().getResponse().getContentAsString();
            var mapper = new ObjectMapper();
            var events = result.lines().map(line -> {
                try { return mapper.readTree(line); }
                catch (java.io.IOException error) { throw new RuntimeException(error); }
            }).toList();
            assertEquals("ready", events.get(0).path("type").asText());
            assertEquals(16000, events.get(1).path("rate").asInt());
            assertEquals("demo", events.get(1).path("mode").asText());
            assertEquals("complete", events.get(events.size() - 1).path("type").asText());
            assertTrue(events.stream().filter(event -> event.path("type").asText().equals("audio"))
                .mapToInt(event -> Base64.getDecoder().decode(event.path("data").asText()).length).sum() > 32000);
        }
    }

    @Test
    void streamsAuthenticatedTurnAndReportsIncompleteAudio() throws Exception {
        var builder = RestClient.builder().baseUrl("https://example.test/v1beta");
        var server = MockRestServiceServer.bindTo(builder).build();
        var mapper = new ObjectMapper();
        String text = mapper.writeValueAsString(java.util.Map.of("status", "completed", "steps", List.of(
            java.util.Map.of("type", "model_output", "content", List.of(java.util.Map.of("type", "text", "text",
                "{\"transcript\":\"Hello\",\"reply\":\"Hi there\"}"))))));
        String chunk = "data: {\"event_type\":\"step.delta\",\"delta\":{\"type\":\"audio\",\"mime_type\":\"audio/l16\",\"sample_rate\":24000,\"data\":\"AQIDBA==\"}}\n\n";
        String complete = "data: {\"event_type\":\"interaction.completed\",\"interaction\":{\"status\":\"completed\"}}\n\n";
        for (String speech : List.of(chunk + complete, chunk)) {
            server.expect(requestTo("https://example.test/v1beta/interactions"))
                .andRespond(withSuccess(text, MediaType.APPLICATION_JSON));
            server.expect(requestTo("https://example.test/v1beta/interactions"))
                .andExpect(jsonPath("$.stream").value(true))
                .andRespond(withSuccess(speech, MediaType.TEXT_EVENT_STREAM));
        }
        var chipi = new ChipiApplication(new GeminiVoice(builder.build()));
        var mvc = MockMvcBuilders.standaloneSetup(chipi).addFilters(new DeviceTurnAccess("fixture-token")).build();
        String turnId = "e49e95e2-d5d0-41fb-8d86-4f5c64444f0a";
        try (var audio = getClass().getResourceAsStream("/demo.wav")) {
            var input = new MockMultipartFile("audio", "sample.wav", "audio/wav", audio.readAllBytes());
            mvc.perform(multipart("/api/turn").file(input).param("stream", "1").servletPath("/api/turn"))
                .andExpect(status().isUnauthorized());
            for (boolean success : List.of(true, false)) {
                var pending = mvc.perform(multipart("/api/turn").file(input).param("stream", "1")
                    .servletPath("/api/turn").header("X-Chipi-Device-Token", "fixture-token")
                    .header("X-Chipi-Turn-Id", turnId))
                    .andExpect(request().asyncStarted()).andReturn();
                String result = mvc.perform(asyncDispatch(pending)).andExpect(status().isOk())
                    .andReturn().getResponse().getContentAsString();
                var events = result.lines().map(line -> {
                    try { return mapper.readTree(line); }
                    catch (java.io.IOException error) { throw new RuntimeException(error); }
                }).toList();
                assertEquals(List.of("ready", "metadata", "audio", success ? "complete" : "error"),
                    events.stream().map(event -> event.path("type").asText()).toList());
                assertEquals("Hello", events.get(1).path("transcript").asText());
                assertEquals("Hi there", events.get(1).path("reply").asText());
                assertArrayEquals(new byte[] {1, 2, 3, 4}, Base64.getDecoder().decode(events.get(2).path("data").asText()));
                if (!success) {
                    assertEquals(502, events.get(3).path("status").asInt());
                    assertEquals("speech_synthesis", events.get(3).path("stage").asText());
                }
            }
        }
        server.verify();
    }

    @Test
    void transcribesGeneratesAndSpeaksInOrder() throws Exception {
        try (var audioFile = getClass().getResourceAsStream("/demo.wav")) {
            byte[] wav = audioFile.readAllBytes();
            var calls = new CopyOnWriteArrayList<String>();
            var completionCount = new AtomicInteger();
            var provider = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
            provider.createContext("/v1", exchange -> {
                var request = exchange.getRequestBody().readAllBytes();
                String path = exchange.getRequestURI().getPath();
                calls.add(path);
                byte[] response;
                String contentType = "application/json";
                try {
                    assertEquals("Bearer fixture-only", exchange.getRequestHeaders().getFirst("Authorization"));
                    if (path.endsWith("/transcriptions")) {
                        String parts = new String(request, StandardCharsets.ISO_8859_1);
                        assertTrue(parts.contains("recording.wav") && parts.contains("RIFF") && parts.contains("whisper-1"));
                        String spoken = completionCount.get() == 0 ? "I like apples." : "What did you just ask me?";
                        response = new ObjectMapper().writeValueAsBytes(java.util.Map.of("text", spoken));
                    } else if (path.endsWith("/completions")) {
                        var chat = new ObjectMapper().readTree(request);
                        var messages = chat.path("messages");
                        assertEquals("system", messages.path(0).path("role").asText());
                        if (completionCount.getAndIncrement() == 0) {
                            assertEquals(2, messages.size());
                            assertEquals("I like apples.", messages.path(1).path("content").asText());
                        } else {
                            assertEquals(4, messages.size());
                            assertEquals("user", messages.path(1).path("role").asText());
                            assertEquals("I like apples.", messages.path(1).path("content").asText());
                            assertEquals("assistant", messages.path(2).path("role").asText());
                            assertEquals("What fruit do you like?", messages.path(2).path("content").asText());
                            assertEquals("user", messages.path(3).path("role").asText());
                            assertEquals("What did you just ask me?", messages.path(3).path("content").asText());
                        }
                        String reply = completionCount.get() == 1 ? "What fruit do you like?" : "I asked what fruit you like.";
                        response = new ObjectMapper().writeValueAsBytes(java.util.Map.of("choices",
                            List.of(java.util.Map.of("message", java.util.Map.of("content", reply)))));
                    } else {
                        var speech = new ObjectMapper().readTree(request);
                        String expectedReply = completionCount.get() == 1 ? "What fruit do you like?" : "I asked what fruit you like.";
                        assertEquals(expectedReply, speech.path("input").asText());
                        assertEquals("wav", speech.path("response_format").asText());
                        contentType = "audio/wav";
                        response = wav;
                    }
                } catch (AssertionError failure) {
                    response = failure.toString().getBytes(StandardCharsets.UTF_8);
                    exchange.sendResponseHeaders(400, response.length);
                    try (var out = exchange.getResponseBody()) { out.write(response); }
                    return;
                }
                exchange.getResponseHeaders().set("Content-Type", contentType);
                exchange.sendResponseHeaders(200, response.length);
                try (var out = exchange.getResponseBody()) { out.write(response); }
            });
            provider.start();
            try {
                var chipi = new ChipiApplication("fixture-only", false,
                    "http://127.0.0.1:" + provider.getAddress().getPort() + "/v1");
                var input = new MockMultipartFile("audio", "sample.wav", "audio/wav", wav);
                var turn = chipi.turn(input, null);
                assertEquals(List.of("/v1/audio/transcriptions", "/v1/chat/completions", "/v1/audio/speech"), calls);
                assertEquals("I like apples.", turn.transcript());
                assertEquals("What fruit do you like?", turn.reply());
                assertArrayEquals(wav, Base64.getDecoder().decode(turn.audioBase64()));
                assertEquals("live", turn.mode());
                String previous = new ObjectMapper().writeValueAsString(java.util.Map.of("turns",
                    List.of(java.util.Map.of("transcript", turn.transcript(), "reply", turn.reply()))));
                var withHistory = chipi.turn(input, previous);
                assertEquals("What did you just ask me?", withHistory.transcript());
                assertEquals("I asked what fruit you like.", withHistory.reply());
                assertEquals(2, completionCount.get());
                var bad = new MockMultipartFile("audio", new byte[] { 1, 2 });
                assertEquals(400, assertThrows(ResponseStatusException.class, () -> chipi.turn(bad, null)).getStatusCode().value());
            } finally {
                provider.stop(0);
            }
        }
    }

    @Test
    void validatesOptionalHistoryBeforeCallingProvider() throws Exception {
        var chipi = new ChipiApplication("", true, "http://127.0.0.1:1/v1");
        try (var audioFile = getClass().getResourceAsStream("/demo.wav")) {
            var input = new MockMultipartFile("audio", "sample.wav", "audio/wav", audioFile.readAllBytes());
            var mvc = MockMvcBuilders.standaloneSetup(chipi).build();
            mvc.perform(multipart("/api/turn").file(input)).andExpect(status().isOk());
            mvc.perform(multipart("/api/turn").file(input).param("history", "invalid"))
                .andExpect(status().isBadRequest());
            String pair = "{\"transcript\":\"hello\",\"reply\":\"hi\"}";
            assertEquals("demo", chipi.turn(input, "{\"turns\":[" + pair + "," + pair + "," + pair + "]}").mode());
            for (String invalid : List.of(
                    "not JSON",
                    "{\"turns\":[{\"transcript\":\"hello\",\"reply\":\"hi\",\"role\":\"system\"}]}",
                    "{\"turns\":[{\"transcript\":\"   \",\"reply\":\"hi\"}]}",
                    "{\"turns\":[{\"transcript\":\"" + "x".repeat(1_001) + "\",\"reply\":\"hi\"}]}",
                    "{\"turns\":[" + pair + "," + pair + "," + pair + "," + pair + "]}")) {
                assertEquals(400,
                    assertThrows(ResponseStatusException.class, () -> chipi.turn(input, invalid)).getStatusCode().value());
            }
        }
    }
}
