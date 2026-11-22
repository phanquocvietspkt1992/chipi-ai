import Foundation

final class ChipiConversationMemory {
    struct Exchange: Codable {
        let id: UUID
        let date: Date
        let transcript: String
        let reply: String
    }

    private struct Entry {
        let exchange: Exchange
        let words: Set<String>
    }

    private static let ignoredWords: Set<String> = [
        "about", "after", "also", "been", "before", "could", "does", "from", "have", "just", "like", "much",
        "said", "some", "talk", "tell", "than", "that", "them", "then", "there", "these", "they",
        "thing", "this", "those", "today", "very", "want", "were", "what", "when", "where", "which",
        "with", "would", "your", "remember", "conversation", "chipi"
    ]
    private static let contextByteLimit = 4200
    private static let recentCount = 4
    private static let olderCount = 6
    private let directory: URL
    private var entries: [Entry]

    init(directory: URL) throws {
        self.directory = directory
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
#if os(iOS)
        var folder = directory
        var attributes = URLResourceValues()
        attributes.isExcludedFromBackup = true
        try folder.setResourceValues(attributes)
#endif
        let files = try FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)
        let decoder = JSONDecoder()
        entries = files.filter { $0.pathExtension == "json" }.compactMap { file in
            guard let data = try? Data(contentsOf: file),
                  let exchange = try? decoder.decode(Exchange.self, from: data) else { return nil }
            return Self.entry(for: exchange)
        }.sorted {
            if $0.exchange.date == $1.exchange.date {
                return $0.exchange.id.uuidString < $1.exchange.id.uuidString
            }
            return $0.exchange.date < $1.exchange.date
        }
    }

    var latest: Exchange? { entries.last?.exchange }

    func append(transcript: String, reply: String) throws {
        let exchange = Exchange(id: UUID(), date: Date(), transcript: transcript, reply: reply)
        let url = directory.appendingPathComponent(exchange.id.uuidString).appendingPathExtension("json")
        let data = try JSONEncoder().encode(exchange)
#if os(iOS)
        try data.write(to: url, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
#else
        try data.write(to: url, options: .atomic)
#endif
        entries.append(Self.entry(for: exchange))
    }

    func context(for transcript: String) -> String {
        let query = Self.keywords(transcript)
        let recent = entries.indices.suffix(Self.recentCount).reversed()
        let candidates = entries.indices.dropLast(Self.recentCount)
        var matches: [(index: Int, score: Int)] = []
        if !query.isEmpty {
            for index in candidates {
                let score = entries[index].words.intersection(query).count
                if score > 0 { matches.append((index, score)) }
            }
        }
        matches.sort { $0.score == $1.score ? $0.index > $1.index : $0.score > $1.score }

        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        var selected: [(index: Int, data: Data)] = []
        var remaining = Self.contextByteLimit - 2
        for index in Array(recent) + matches.prefix(Self.olderCount).map(\.index) {
            let exchange = entries[index].exchange
            // Bound the prompt only. The exchange on disk retains its complete text.
            let excerpt = Exchange(id: exchange.id, date: exchange.date,
                                   transcript: String(exchange.transcript.prefix(260)),
                                   reply: String(exchange.reply.prefix(260)))
            guard let data = try? encoder.encode(excerpt), data.count + 1 <= remaining else { continue }
            remaining -= data.count + 1
            selected.append((index, data))
        }
        let json = selected.sorted { $0.index < $1.index }.map { String(decoding: $0.data, as: UTF8.self) }
        return "[" + json.joined(separator: ",") + "]"
    }

    private static func entry(for exchange: Exchange) -> Entry {
        Entry(exchange: exchange, words: keywords(exchange.transcript + " " + exchange.reply))
    }

    private static func keywords(_ text: String) -> Set<String> {
        Set(text.lowercased().split(whereSeparator: { !$0.isLetter && !$0.isNumber })
            .map(String.init).filter { $0.count >= 3 && !ignoredWords.contains($0) })
    }
}
