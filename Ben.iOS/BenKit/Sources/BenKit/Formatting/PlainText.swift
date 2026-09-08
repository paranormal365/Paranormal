import Foundation

/// Turns the site's stored HTML into something a phone can put in a `Text`.
///
/// **Why this exists.** Case timeline entries and case messages are written in a rich-text editor
/// on the website, so their bodies are HTML. The client's own case screen has always shown plain
/// bodies; the group's side (iOS-8) shows the same fields the website's case page does, and those
/// carry markup. Rendering them raw puts `<p>` on the screen — which is precisely what W-CL1 found
/// on the client's case page and what got fixed there.
///
/// **Not an HTML renderer, and not trying to be.** It reduces markup to the words and the line
/// breaks a reader needs. Bold and italics are lost on purpose: a case note read on a phone in
/// somebody's hallway wants to be legible, and the alternative is `AttributedString(html:)`, which
/// needs WebKit, runs on the main thread and is slower than the screen it is drawing.
public enum PlainText {
    /// The readable text of `html`, or the string unchanged when it holds no markup.
    public static func from(_ html: String) -> String {
        // Nothing that looks like a tag: the overwhelming majority of bodies, and worth not
        // walking character by character for.
        guard html.contains("<") else { return decodingEntities(html) }

        var out = ""
        var insideTag = false
        var tagName = ""

        for character in html {
            switch character {
            case "<":
                insideTag = true
                tagName = ""
            case ">":
                insideTag = false
                // The tags that mean "a new line starts here". Everything else closes silently,
                // so <strong>bold</strong> comes out as bold rather than as bold with gaps.
                if Self.breaksLine(tagName) && !out.hasSuffix("\n") { out.append("\n") }
            default:
                if insideTag {
                    // Only the name matters, and only while it is still being read: an attribute
                    // value containing a space must not be mistaken for another tag.
                    if !character.isWhitespace || tagName.isEmpty { tagName.append(character) }
                } else {
                    out.append(character)
                }
            }
        }

        return decodingEntities(out)
            .replacingOccurrences(of: "\n{3,}", with: "\n\n", options: .regularExpression)
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// Whether closing or opening this tag should start a new line.
    private static func breaksLine(_ rawTagName: String) -> Bool {
        let name = rawTagName
            .trimmingCharacters(in: CharacterSet(charactersIn: "/ \n\t"))
            .lowercased()
            .prefix(while: { $0.isLetter || $0.isNumber })

        return ["p", "br", "div", "li", "tr", "h1", "h2", "h3", "h4", "h5", "h6",
                "blockquote", "ul", "ol"].contains(String(name))
    }

    /// The five entities the site's editor actually emits, plus the two spaces.
    ///
    /// Deliberately not a general entity table: `&amp;` has to be last or it would re-decode the
    /// ampersands the others just produced, and that ordering is the only subtle part of this.
    private static func decodingEntities(_ text: String) -> String {
        var out = text
        for (entity, character) in [("&nbsp;", " "), ("&#160;", " "),
                                    ("&lt;", "<"), ("&gt;", ">"),
                                    ("&quot;", "\""), ("&#39;", "'"), ("&apos;", "'"),
                                    ("&amp;", "&")] {
            out = out.replacingOccurrences(of: entity, with: character)
        }
        return out
    }
}
