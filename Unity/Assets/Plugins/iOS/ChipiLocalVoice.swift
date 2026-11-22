import AVFoundation
import Foundation
import FoundationModels
import Speech

@MainActor
private final class LocalVoice: NSObject, AVSpeechSynthesizerDelegate {
    static let shared = LocalVoice()
    private let speaker = AVSpeechSynthesizer()
    private var utterance: AVSpeechUtterance?
    private var recognition: SFSpeechRecognitionTask?
    private var generation: Task<Void, Never>?
    private var recording: URL?
    private var turn = UUID()
    var result = ""
    var playback: Int32 = 0

    override init() {
        super.init()
        speaker.delegate = self
    }

    func cancel() {
        turn = UUID()
        recognition?.cancel()
        recognition = nil
        generation?.cancel()
        generation = nil
        if let recording { try? FileManager.default.removeItem(at: recording) }
        recording = nil
        utterance = nil
        speaker.stopSpeaking(at: .immediate)
        playback = 0
        result = ""
    }

    func finish(_ fields: [String: String], id: UUID) {
        guard turn == id else { return }
        if let recording { try? FileManager.default.removeItem(at: recording) }
        recording = nil
        recognition = nil
        generation = nil
        if let data = try? JSONSerialization.data(withJSONObject: fields) {
            result = String(decoding: data, as: UTF8.self)
        }
    }

    func start(_ wav: Data, context: String) {
        cancel()
        let id = turn
        guard #available(iOS 26.0, *) else {
            finish(["error": "On-device conversation requires iOS 26 or later."], id: id)
            return
        }
        guard case .available = SystemLanguageModel.default.availability else {
            finish(["error": "On-device replies need Apple Intelligence. Check its availability in Settings."], id: id)
            return
        }
        SFSpeechRecognizer.requestAuthorization { permission in
            Task { @MainActor in
                guard self.turn == id else { return }
                guard permission == .authorized else {
                    self.finish(["error": "Allow Speech Recognition for Chipi in Settings."], id: id)
                    return
                }
                self.transcribe(wav, context: context, id: id)
            }
        }
    }

    @available(iOS 26.0, *)
    private func transcribe(_ wav: Data, context: String, id: UUID) {
        guard let recognizer = SFSpeechRecognizer(locale: Locale(identifier: "en-US")),
              recognizer.supportsOnDeviceRecognition else {
            finish(["error": "On-device English speech recognition is unavailable."], id: id)
            return
        }
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("chipi-\(id).wav")
        do { try wav.write(to: url, options: [.atomic, .completeFileProtection]) }
        catch {
            finish(["error": "Chipi couldn't prepare the recording."], id: id)
            return
        }
        recording = url
        let request = SFSpeechURLRecognitionRequest(url: url)
        request.requiresOnDeviceRecognition = true
        request.shouldReportPartialResults = false
        recognition = recognizer.recognitionTask(with: request) { response, error in
            let final = response?.isFinal == true
            let heard = response?.bestTranscription.formattedString ?? ""
            let failed = error != nil
            Task { @MainActor in
                guard self.turn == id, self.result.isEmpty, self.generation == nil else { return }
                if final, !heard.isEmpty { self.reply(heard, context: context, id: id) }
                else if failed || final { self.finish(["error": "I didn't catch that. Try again."], id: id) }
            }
        }
    }

    @available(iOS 26.0, *)
    private func reply(_ heard: String, context: String, id: UUID) {
        generation = Task {
            do {
                let model = LanguageModelSession(instructions: """
                    You are Chipi, a warm conversation partner helping an intermediate English learner.
                    Use everyday words and natural contractions. Keep the whole reply to one or two short sentences.
                    Respond to the meaning first, especially when the learner is sharing something personal.
                    When you hear a clear English mistake, gently model the corrected phrase in the same reply.
                    Fix one important mistake per turn by default. Give a short reason when asked or when the fix would be unclear.
                    Preserve the learner’s meaning in every correction. If the English is already correct, skip language feedback entirely.
                    If the audio is unclear, ask the learner to repeat it. Use relevant details from earlier turns.
                    Ask a question when it fits. A short, friendly statement can be a complete reply.
                    Use plain text that sounds natural aloud. Speak as Chipi and avoid claiming human experiences.
                    Example: “Yesterday I go to the gym.” → “Nice! You’d say ‘I went to the gym yesterday.’”
                    Example: “My shift ran late, so I’m tired.” → “That sounds tiring. I hope you get some rest tonight.”
                    Example: “Why is it ‘went’?” → “‘Went’ is the past tense of ‘go.’ Use it when you talk about something that already happened.”
                    """)
                let answer = try await model.respond(to: """
                    Recent turns (JSON): \(context)
                    Latest learner utterance: \(heard)
                    """, options: GenerationOptions(maximumResponseTokens: 96))
                let reply = answer.content.trimmingCharacters(in: .whitespacesAndNewlines)
                if reply.isEmpty { throw CocoaError(.coderInvalidValue) }
                finish(["transcript": heard, "reply": reply], id: id)
            } catch {
                finish(["error": "Chipi couldn't generate an on-device reply. Try again."], id: id)
            }
        }
    }

    func speak(_ text: String) {
        let item = AVSpeechUtterance(string: text)
        item.voice = AVSpeechSynthesisVoice(language: "en-US")
        item.rate = AVSpeechUtteranceDefaultSpeechRate
        utterance = item
        playback = 1
        speaker.speak(item)
    }

    nonisolated func speechSynthesizer(_ synthesizer: AVSpeechSynthesizer, didStart utterance: AVSpeechUtterance) {
        updatePlayback(utterance, state: 2)
    }

    nonisolated func speechSynthesizer(_ synthesizer: AVSpeechSynthesizer, didFinish utterance: AVSpeechUtterance) {
        updatePlayback(utterance, state: 3)
    }

    nonisolated func speechSynthesizer(_ synthesizer: AVSpeechSynthesizer, didCancel utterance: AVSpeechUtterance) {
        updatePlayback(utterance, state: -1)
    }

    private nonisolated func updatePlayback(_ utterance: AVSpeechUtterance, state: Int32) {
        let identity = ObjectIdentifier(utterance)
        Task { @MainActor in
            guard let current = self.utterance, ObjectIdentifier(current) == identity else { return }
            self.playback = state
        }
    }
}

@_cdecl("ChipiLocalStart")
func chipiLocalStart(_ data: UnsafeRawPointer, _ size: Int32, _ context: UnsafePointer<CChar>) {
    MainActor.assumeIsolated { LocalVoice.shared.start(Data(bytes: data, count: Int(size)), context: String(cString: context)) }
}

@_cdecl("ChipiLocalRead")
func chipiLocalRead(_ buffer: UnsafeMutableRawPointer, _ size: Int32) -> Int32 {
    MainActor.assumeIsolated {
        let data = Data(LocalVoice.shared.result.utf8)
        guard !data.isEmpty else { return 0 }
        guard data.count <= Int(size) else { return -1 }
        _ = data.copyBytes(to: UnsafeMutableRawBufferPointer(start: buffer, count: data.count))
        return Int32(data.count)
    }
}

@_cdecl("ChipiLocalSpeak")
func chipiLocalSpeak(_ text: UnsafePointer<CChar>) {
    MainActor.assumeIsolated { LocalVoice.shared.speak(String(cString: text)) }
}

@_cdecl("ChipiLocalPlayback")
func chipiLocalPlayback() -> Int32 {
    MainActor.assumeIsolated { LocalVoice.shared.playback }
}

@_cdecl("ChipiLocalCancel")
func chipiLocalCancel() {
    MainActor.assumeIsolated { LocalVoice.shared.cancel() }
}
