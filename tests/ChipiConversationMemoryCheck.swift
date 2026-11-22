import Foundation
import FoundationModels

@main struct CheckMemory {
    static func main() async throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: dir) }
        let original = try ChipiConversationMemory(directory: dir)
        precondition(original.latest == nil)
        try original.append(transcript: "My parrot Jasper loves mango.", reply: "Mango sounds like Jasper’s favorite.")
        for n in 1...20 {
            try original.append(transcript: "Today I talked about homework number \(n).", reply: "Sounds good.")
        }
        let longText = String(repeating: "📚 English practice. ", count: 1000)
        try original.append(transcript: "My reading journal: " + longText, reply: longText)
        try Data("interrupted write".utf8).write(to: dir.appendingPathComponent("incomplete.tmp"))
        let restored = try ChipiConversationMemory(directory: dir)
        precondition(restored.latest?.reply == longText)
        let files = try FileManager.default.contentsOfDirectory(atPath: dir.path)
        precondition(files.filter { $0.hasSuffix(".json") }.count == 22)
        let context = restored.context(for: "What fruit does Jasper love?")
        let payload = Data(context.utf8)
        precondition(payload.count <= 4200)
        precondition(context.contains("parrot Jasper") && context.contains("homework number 20"))
        precondition(!restored.context(for: "What happened lately?").contains("parrot Jasper"))
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        let exchanges = try decoder.decode([ChipiConversationMemory.Exchange].self, from: payload)
        precondition(exchanges.first?.transcript.contains("Jasper") == true && exchanges.count <= 10)
        print("PASS: 22 full exchanges, replay-safe writes, old-topic retrieval after reopen, bounded valid JSON")

        if #available(macOS 26.0, *), case .available = SystemLanguageModel.default.availability {
            let model = LanguageModelSession(instructions: "Use relevant details from the saved exchanges to continue earlier topics. If a past detail is missing, say you don’t recall it rather than guessing.")
            let answer = try await model.respond(to: "Relevant and recent saved exchanges (JSON): \(context)\nLatest learner utterance: What fruit does Jasper love?")
            precondition(answer.content.localizedCaseInsensitiveContains("mango"))
            print("PASS: Apple model recalled the fruit from the reopened archive")
        }
    }
}
