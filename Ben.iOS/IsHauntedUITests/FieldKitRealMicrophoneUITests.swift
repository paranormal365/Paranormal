import XCTest

/// The session's sound, recorded by the REAL microphone code rather than the fake sensors.
///
/// Ben, 10/23/2026: his first session on 1.1.5 had a moving sound reading and no sound at all.
/// Changing a setting after Start restarts the sensor feeds, and the meter's old feed ending
/// closed the recording underneath the session; at Stop the file held nothing and was dropped.
/// The fake sensors never close anything, so only the real `LiveAudioCapture` can show it. The
/// simulator's microphone is the Mac's, so grant it first:
/// `xcrun simctl privacy <udid> grant microphone com.ishaunted.ios`.
final class FieldKitRealMicrophoneUITests: XCTestCase {

    func testChangingASettingMidSessionKeepsTheSoundRecording() throws {
        continueAfterFailure = false
        let app = XCUIApplication()
        app.launch()

        XCTAssertTrue(AppNavigator.openSection("Paranormal", in: app))
        AppNavigator.startNewSession(in: app)
        XCTAssertTrue(app.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        app.buttons["confirm-start-session"].tap()
        XCTAssertTrue(app.buttons["start-recording"].waitForExistence(timeout: 15))
        app.buttons["start-recording"].tap()

        let toggle = app.buttons["toggle-audio-recording"]
        XCTAssertTrue(toggle.waitForExistence(timeout: 20), "recording should be reachable")
        XCTAssertEqual(toggle.label, "Stop audio", "the recording should start with the session")
        sleep(3)

        // The trigger: any setting that restarts the sensor feeds after Start.
        let magnetic = app.switches["channel-magnetic field"]
        XCTAssertTrue(magnetic.waitForExistence(timeout: 10))
        magnetic.tap()
        sleep(3)

        toggle.tap()
        XCTAssertTrue(app.otherElements["capture-row"].firstMatch.waitForExistence(timeout: 20)
                      || app.staticTexts["audio-001.m4a"].waitForExistence(timeout: 5),
                      "the sound recorded before and after the setting changed should be kept")
    }
}
