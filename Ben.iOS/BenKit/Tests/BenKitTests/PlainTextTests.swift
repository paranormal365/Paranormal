import Testing
@testable import BenKit

/// Case bodies written in the website's rich-text editor, read on a phone.
///
/// The group's side of a case (iOS-8) shows the same timeline entries and messages the website's
/// case page does, and those are stored as HTML. Putting them straight into a `Text` shows `<p>`
/// on the screen — which is exactly what W-CL1 found on the client's case page.
struct PlainTextTests {
    @Test("A paragraph loses its tags and keeps its words")
    func paragraph() {
        #expect(PlainText.from("<p>Two knocks on the north wall.</p>")
                == "Two knocks on the north wall.")
    }

    @Test("Paragraphs become line breaks, not run-on text")
    func paragraphsSeparate() {
        let text = PlainText.from("<p>First night.</p><p>Second night.</p>")
        #expect(text.contains("First night."))
        #expect(text.contains("Second night."))
        #expect(text.contains("\n"))
        #expect(!text.contains("First night.Second night."))
    }

    /// Inline markup closes without leaving a gap in the middle of a sentence.
    @Test("Bold and italics vanish without splitting the words around them")
    func inlineTagsDoNotBreakLines() {
        #expect(PlainText.from("<p>It was <strong>very</strong> cold.</p>")
                == "It was very cold.")
    }

    @Test("A line break tag is a line break")
    func lineBreak() {
        let text = PlainText.from("Upstairs<br/>Downstairs")
        #expect(text == "Upstairs\nDownstairs")
    }

    @Test("List items each get their own line")
    func listItems() {
        let text = PlainText.from("<ul><li>EMF spike</li><li>Cold spot</li></ul>")
        #expect(text.contains("EMF spike"))
        #expect(text.contains("Cold spot"))
        #expect(!text.contains("EMF spikeCold spot"))
    }

    /// The ordering trap: `&amp;` has to be decoded last.
    @Test("Entities decode, and an escaped ampersand does not re-decode")
    func entities() {
        #expect(PlainText.from("Smith &amp; Sons") == "Smith & Sons")
        #expect(PlainText.from("&amp;lt;") == "&lt;")
        #expect(PlainText.from("a&nbsp;b") == "a b")
    }

    /// The common case, and the one worth being fast: no markup at all.
    @Test("Plain text passes through unchanged")
    func plainPassesThrough() {
        #expect(PlainText.from("Just a note.") == "Just a note.")
    }

    @Test("An empty body stays empty rather than becoming whitespace")
    func empty() {
        #expect(PlainText.from("") == "")
        #expect(PlainText.from("<p></p>") == "")
    }

    /// An attribute value with a space in it must not read as a second tag.
    @Test("Attributes are dropped whole")
    func attributes() {
        #expect(PlainText.from("<p class=\"a b\">Text</p>") == "Text")
        #expect(PlainText.from("<a href=\"https://example.com/x y\">Link</a>") == "Link")
    }

    /// Three blank lines in the source do not become three on the screen.
    @Test("Runs of blank lines collapse")
    func collapsesBlankLines() {
        let text = PlainText.from("<p>A</p><br/><br/><br/><p>B</p>")
        #expect(!text.contains("\n\n\n"))
    }
}
