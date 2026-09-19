import Foundation
import Testing
@testable import BenKit

@Suite("Multipart encoding — golden bytes for the feed post shape")
struct MultipartTests {

    @Test func feedPostWithImageFramesExactly() throws {
        let body = MultipartBody(
            parts: [
                .field("Body", "A cold spot on the landing #evp"),
                .field("ParentMessageId", "11111111-2222-3333-4444-555555555555"),
                .file("media", filename: "photo.jpg", contentType: "image/jpeg",
                      data: Data([0xFF, 0xD8, 0xFF])),
            ],
            boundary: "TESTBOUNDARY")

        let expected =
            "--TESTBOUNDARY\r\n" +
            "Content-Disposition: form-data; name=\"Body\"\r\n" +
            "\r\n" +
            "A cold spot on the landing #evp\r\n" +
            "--TESTBOUNDARY\r\n" +
            "Content-Disposition: form-data; name=\"ParentMessageId\"\r\n" +
            "\r\n" +
            "11111111-2222-3333-4444-555555555555\r\n" +
            "--TESTBOUNDARY\r\n" +
            "Content-Disposition: form-data; name=\"media\"; filename=\"photo.jpg\"\r\n" +
            "Content-Type: image/jpeg\r\n" +
            "\r\n"

        var expectedData = Data(expected.utf8)
        expectedData.append(Data([0xFF, 0xD8, 0xFF]))
        expectedData.append(Data("\r\n--TESTBOUNDARY--\r\n".utf8))

        #expect(try body.composedData() == expectedData)
    }

    @Test func filePayloadStreamsFromDisk() throws {
        let source = FileManager.default.temporaryDirectory
            .appendingPathComponent("multipart-source-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: source) }
        let payload = Data(repeating: 0xAB, count: 3 * 1024 * 1024)
        try payload.write(to: source)

        let body = MultipartBody(
            parts: [.file("media", filename: "clip.mp4", contentType: "video/mp4", url: source)],
            boundary: "B")
        let composed = try body.composedData()

        let header = "--B\r\nContent-Disposition: form-data; name=\"media\"; filename=\"clip.mp4\"\r\nContent-Type: video/mp4\r\n\r\n"
        let trailer = "\r\n--B--\r\n"
        // utf8.count, not count: Swift folds "\r\n" into one grapheme cluster.
        #expect(composed.count == payload.count + header.utf8.count + trailer.utf8.count)
        // The payload bytes appear verbatim in the middle.
        #expect(composed.range(of: Data([0xAB, 0xAB, 0xAB, 0xAB])) != nil)
    }
}
