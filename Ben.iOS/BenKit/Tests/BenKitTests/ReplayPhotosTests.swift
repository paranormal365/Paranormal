import Foundation
import Testing
@testable import BenKit

/// Photographs on the replay (Ben, 2026-09-27: "the photo never shows").
@Suite("Replay photos")
struct ReplayPhotosTests {
    private let start = Date(timeIntervalSince1970: 1_787_600_000)

    private func photo(_ offset: TimeInterval, kind: CaptureKind = .photo) -> CaptureMark {
        CaptureMark(id: UUID(), at: start.addingTimeInterval(offset), kind: kind,
                    relativePath: "media/photo-\(Int(offset)).jpg")
    }

    @Test func aPhotoAppearsWhenThePlayheadReachesIt() {
        let stills = [photo(30)]
        #expect(ReplayPhotos.onScreen(stills, at: start.addingTimeInterval(29.9)) == nil)
        #expect(ReplayPhotos.onScreen(stills, at: start.addingTimeInterval(30)) == stills[0])
        #expect(ReplayPhotos.onScreen(stills, at: start.addingTimeInterval(35)) == stills[0])
    }

    @Test func itIsHeldForAWhileAndThenGoes() {
        let stills = [photo(30)]
        let justBefore = start.addingTimeInterval(30 + ReplayPhotos.holdSeconds - 0.1)
        #expect(ReplayPhotos.onScreen(stills, at: justBefore) != nil)
        #expect(ReplayPhotos.onScreen(stills, at: start.addingTimeInterval(30 + ReplayPhotos.holdSeconds)) == nil)
    }

    @Test func theLaterOfTwoPhotosWins() {
        let stills = [photo(30), photo(33)]
        #expect(ReplayPhotos.onScreen(stills, at: start.addingTimeInterval(34))?.relativePath
                == "media/photo-33.jpg")
    }

    @Test func aPhotoWithoutCoordinatesStillShows() {
        // The indoor case: no fix, so no map pin — and it must still reach the screen.
        let indoor = photo(10)
        #expect(indoor.latitude == nil && indoor.longitude == nil)
        #expect(ReplayPhotos.onScreen([indoor], at: start.addingTimeInterval(11)) == indoor)
    }

    @Test func aStillThatIsNotAPhotoIsNotShownAsOne() {
        let unmeasuredClip = photo(10, kind: .video)
        #expect(ReplayPhotos.onScreen([unmeasuredClip], at: start.addingTimeInterval(11)) == nil)
        #expect(ReplayPhotos.taken([unmeasuredClip, photo(5)]).count == 1)
    }

    @Test func theStripIsInTheOrderTheyWereTaken() {
        let strip = ReplayPhotos.taken([photo(40), photo(10), photo(25)])
        #expect(strip.map(\.relativePath) == ["media/photo-10.jpg", "media/photo-25.jpg", "media/photo-40.jpg"])
    }

    @Test func aThumbnailGlowsForACoupleOfSecondsAsItsMomentPasses() {
        let taken = photo(30)
        #expect(!ReplayPhotos.isGlowing(taken, at: start.addingTimeInterval(29.9)))
        #expect(ReplayPhotos.isGlowing(taken, at: start.addingTimeInterval(30)))
        #expect(ReplayPhotos.isGlowing(taken, at: start.addingTimeInterval(30 + ReplayPhotos.glowSeconds - 0.1)))
        #expect(!ReplayPhotos.isGlowing(taken, at: start.addingTimeInterval(30 + ReplayPhotos.glowSeconds)))
    }

    @Test func theStripFollowsTheLastPhotoPassed() {
        let stills = [photo(10), photo(40)]
        #expect(ReplayPhotos.lastPassed(stills, at: start.addingTimeInterval(5)) == nil)
        #expect(ReplayPhotos.lastPassed(stills, at: start.addingTimeInterval(20))?.relativePath == "media/photo-10.jpg")
        #expect(ReplayPhotos.lastPassed(stills, at: start.addingTimeInterval(400))?.relativePath == "media/photo-40.jpg")
    }
}
