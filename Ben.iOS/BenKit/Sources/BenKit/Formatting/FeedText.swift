import Foundation

/// Linkifies a feed post's plain-text body: `@mentions` become links to the
/// account they RESOLVED to (rename-proof — the id came from the server's
/// mention table, never from re-parsing), `#tags` become links to the tag page.
/// Links use the `ishaunted://` scheme, which the app routes internally.
public enum FeedText {
    public static func attributed(body: String, mentions: [FeedMentionRecord]) -> AttributedString {
        var text = AttributedString(body)

        // Mentions first, by the handle the server says was typed.
        for mention in mentions {
            link(in: &text, token: "@\(mention.handle)",
                 url: URL(string: "ishaunted://feed/people/\(mention.appUserId.uuidString.lowercased())"))
        }

        // Hashtags from the text itself — display concern only; the server's
        // hashtag table is authoritative for search, this is authoritative for taps.
        for token in hashtags(in: body) {
            link(in: &text, token: "#\(token)",
                 url: URL(string: "ishaunted://feed/tags/\(token.lowercased())"))
        }
        return text
    }

    /// The server's tag rule, mirrored: letters lead (a leading digit is a list
    /// or a year, not a subject), then letters/digits.
    static func hashtags(in body: String) -> [String] {
        var found: [String] = []
        var seen = Set<String>()
        let matches = body.matches(of: /#([A-Za-z][A-Za-z0-9]*)/)
        for match in matches {
            let tag = String(match.1)
            if seen.insert(tag.lowercased()).inserted { found.append(tag) }
        }
        return found
    }

    private static func link(in text: inout AttributedString, token: String, url: URL?) {
        guard let url else { return }
        var searchStart = text.startIndex
        while searchStart < text.endIndex,
              let range = text[searchStart...].range(of: token, options: .caseInsensitive) {
            text[range].link = url
            searchStart = range.upperBound
        }
    }
}
