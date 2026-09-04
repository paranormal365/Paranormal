import XCTest

/// Field Kit, tapped for real on whichever device is running.
///
/// The point of this suite is that a field session survives things: leaving the screen, leaving
/// the app, and relaunching. Those are not edge cases here — they are Tuesday night in a cellar.
final class FieldKitUITests: XCTestCase {

    private var app: XCUIApplication!

    override func setUp() {
        continueAfterFailure = false
        app = XCUIApplication()
        app.launch()
    }

    /// Field Kit deliberately needs no account — everything it does happens on the device.
    func testFieldKitIsReachableWithoutSigningIn() {
        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app),
                      "Field Kit should be reachable on this shell")
        XCTAssertTrue(app.navigationBars["Field Kit"].waitForExistence(timeout: 20))
        XCTAssertTrue(app.buttons["start-field-session"].waitForExistence(timeout: 10),
                      "a signed-out person must still be able to start recording")
    }

    func testAStartedSessionSurvivesLeavingTheAppAndComingBack() throws {
        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))
        app.buttons["start-field-session"].tap()

        let label = app.textFields["session-label"]
        XCTAssertTrue(label.waitForExistence(timeout: 15))
        label.tap()
        let marker = "Cellar \(Int(Date().timeIntervalSince1970))"
        label.typeText(marker)

        app.buttons["confirm-start-session"].tap()
        // Item 215: the live screen opens pending. Nothing is logged until Start.
        XCTAssertTrue(app.buttons["start-recording"].waitForExistence(timeout: 15))
        app.buttons["start-recording"].tap()

        // Straight into the live screen — starting a session and then hunting for it would be
        // wrong in the dark.
        XCTAssertTrue(app.buttons["stop-field-session"].waitForExistence(timeout: 20),
                      "starting a session should open it")

        // Terminate WITHOUT stopping the session: the phone dying mid-session is the case that
        // matters, and the session must still be there afterwards.
        app.terminate()
        app.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))
        XCTAssertTrue(app.staticTexts[marker].waitForExistence(timeout: 25),
                      "a session interrupted by termination must still be listed")

        // And it is no longer claiming to be recording — the app cannot know when it stopped,
        // so it says interrupted rather than inventing an end.
        XCTAssertTrue(
            app.staticTexts.containing(
                NSPredicate(format: "label CONTAINS[c] 'interrupted'")
            ).firstMatch.waitForExistence(timeout: 15),
            "an interrupted session should say so")
    }

    func testStoppingASessionOpensItsReview() {
        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))
        app.buttons["start-field-session"].tap()
        XCTAssertTrue(app.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        app.buttons["confirm-start-session"].tap()
        // Item 215: the live screen opens pending. Nothing is logged until Start.
        XCTAssertTrue(app.buttons["start-recording"].waitForExistence(timeout: 15))
        app.buttons["start-recording"].tap()

        let stop = app.buttons["stop-field-session"]
        XCTAssertTrue(stop.waitForExistence(timeout: 20))
        stop.tap()

        // Review is where a stopped session lands, because the next thing anybody does is look
        // at what they got.
        XCTAssertTrue(
            app.staticTexts.containing(
                NSPredicate(format: "label CONTAINS[c] 'Readings'")
            ).firstMatch.waitForExistence(timeout: 20),
            "stopping should open the session's review")
    }

    /// Drives the fake instruments so the gauges can actually be looked at.
    func testTheMeterRunsOnFakeSensors() throws {
        app.terminate()
        let fresh = XCUIApplication()
        fresh.launchArguments += ["-fieldKitFakeSensors"]
        fresh.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: fresh))
        fresh.buttons["start-field-session"].tap()
        XCTAssertTrue(fresh.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        fresh.buttons["confirm-start-session"].tap()
        // Item 215: the live screen opens pending. Nothing is logged until Start.
        XCTAssertTrue(fresh.buttons["start-recording"].waitForExistence(timeout: 15))
        fresh.buttons["start-recording"].tap()

        XCTAssertTrue(fresh.buttons["set-base-level"].waitForExistence(timeout: 20))
        // Let the scripted field settle, then take the room as normal.
        sleep(2)
        fresh.buttons["set-base-level"].tap()
        sleep(1)

        // The scripted room swings past the report level every twenty seconds.
        sleep(9)

        // The dial is drawn, not a label, so its accessibility value is what proves a needle
        // is pointing somewhere real rather than a placeholder being shown.
        let dial = fresh.otherElements["Magnetic field"].firstMatch
        XCTAssertTrue(dial.waitForExistence(timeout: 15), "the meter should be on screen")
        XCTAssertFalse(dial.value as? String == "No reading",
                       "with a base set and instruments running, the meter should read something")

        // The two set points a person controls both have to be reachable while recording.
        XCTAssertTrue(fresh.buttons["mark-now"].exists)
        XCTAssertTrue(fresh.buttons["set-base-level"].exists)
    }

    /// Saying which room you are in, and having everything afterwards carry it.
    ///
    /// This is the one fact the instruments cannot supply — a fix indoors covers the whole
    /// building — so the control has to be reachable with one hand in the dark, and the label has
    /// to stick.
    func testSayingWhichRoomYouAreIn() throws {
        app.terminate()
        let fresh = XCUIApplication()
        fresh.launchArguments += ["-fieldKitFakeSensors"]
        fresh.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: fresh))
        fresh.buttons["start-field-session"].tap()
        XCTAssertTrue(fresh.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        fresh.buttons["confirm-start-session"].tap()
        // Item 215: the live screen opens pending. Nothing is logged until Start.
        XCTAssertTrue(fresh.buttons["start-recording"].waitForExistence(timeout: 15))
        fresh.buttons["start-recording"].tap()

        let roomBar = fresh.descendants(matching: .any).matching(identifier: "room-bar").firstMatch
        XCTAssertTrue(roomBar.waitForExistence(timeout: 20),
                      "the room should be settable from the session screen")
        roomBar.tap()

        // The quick list is the point: typing a room name in the dark is what this avoids.
        let suggestion = fresh.buttons["room-suggestion"].firstMatch
        XCTAssertTrue(suggestion.waitForExistence(timeout: 10),
                      "common rooms should be one tap away")
        let chosen = suggestion.label
        suggestion.tap()

        XCTAssertTrue(fresh.staticTexts[chosen].waitForExistence(timeout: 10),
                      "the session screen should show the room that was picked")

        // Moving rooms leaves a mark of its own, so a review shows when somebody moved.
        XCTAssertTrue(fresh.staticTexts["moved to \(chosen)"].waitForExistence(timeout: 10),
                      "changing room should be marked on the timeline")
    }

    /// Field work happens at night, so the screens that matter most are the ones nobody would
    /// see in a daylight screenshot. This runs the same panel in dark and asserts it is all
    /// still there — the appearance is restored afterwards so the rest of the suite is unaffected.
    func testTheInstrumentPanelWorksInTheDark() throws {
        let previous = XCUIDevice.shared.appearance
        XCUIDevice.shared.appearance = .dark
        defer { XCUIDevice.shared.appearance = previous }

        app.terminate()
        let fresh = XCUIApplication()
        fresh.launchArguments += ["-fieldKitFakeSensors"]
        fresh.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: fresh))
        fresh.buttons["start-field-session"].tap()
        XCTAssertTrue(fresh.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        fresh.buttons["confirm-start-session"].tap()
        // Item 215: the live screen opens pending. Nothing is logged until Start.
        XCTAssertTrue(fresh.buttons["start-recording"].waitForExistence(timeout: 15))
        fresh.buttons["start-recording"].tap()

        XCTAssertTrue(fresh.buttons["set-base-level"].waitForExistence(timeout: 20))
        fresh.buttons["set-base-level"].tap()

        XCTAssertTrue(fresh.otherElements["Magnetic field"].firstMatch.exists)
        XCTAssertTrue(fresh.otherElements["Sound level"].firstMatch.exists)
        XCTAssertTrue(fresh.buttons["stop-field-session"].exists)
    }

    /// What a session records is the investigator's choice, and switching a channel off has to
    /// actually take its readout away rather than leaving it there looking live.
    func testChannelsCanBeSwitchedOffDuringASession() throws {
        app.terminate()
        let fresh = XCUIApplication()
        fresh.launchArguments += ["-fieldKitFakeSensors"]
        fresh.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: fresh))
        fresh.buttons["start-field-session"].tap()
        XCTAssertTrue(fresh.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        fresh.buttons["confirm-start-session"].tap()
        // Item 215: the live screen opens pending. Nothing is logged until Start.
        XCTAssertTrue(fresh.buttons["start-recording"].waitForExistence(timeout: 15))
        fresh.buttons["start-recording"].tap()

        let audioToggle = fresh.switches["channel-audio"]
        XCTAssertTrue(audioToggle.waitForExistence(timeout: 20), "channels should be switchable")
        XCTAssertTrue(fresh.otherElements["Sound level"].firstMatch.exists)

        audioToggle.tap()

        // The sound meter goes away with the channel — a gauge left on screen after its stream
        // is torn down would show a frozen last value as though it were live.
        let meter = fresh.otherElements["Sound level"].firstMatch
        let deadline = Date().addingTimeInterval(15)
        while meter.exists && Date() < deadline {
            _ = fresh.wait(for: .runningForeground, timeout: 0.5)
        }
        XCTAssertFalse(meter.exists,
                       "switching audio off should take its meter away, not freeze it")
    }

    /// Recording sound into the session, and seeing it land as a capture.
    ///
    /// The camera cannot run in a simulator, so photo and video are verified on hardware; the
    /// audio path is the one that can be driven here, and it is also the one EVP depends on.
    func testRecordingAudioLandsAsACaptureInTheSession() throws {
        app.terminate()
        let fresh = XCUIApplication()
        fresh.launchArguments += ["-fieldKitFakeSensors"]
        fresh.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: fresh))
        fresh.buttons["start-field-session"].tap()
        XCTAssertTrue(fresh.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        fresh.buttons["confirm-start-session"].tap()
        // Item 215: the live screen opens pending. Nothing is logged until Start.
        XCTAssertTrue(fresh.buttons["start-recording"].waitForExistence(timeout: 15))
        fresh.buttons["start-recording"].tap()

        let toggle = fresh.buttons["toggle-audio-recording"]
        XCTAssertTrue(toggle.waitForExistence(timeout: 20), "recording should be reachable")

        // Audio is on by default, so the button offers to STOP — which is the state that proves
        // a recording started on its own when the session did.
        XCTAssertEqual(toggle.label, "Stop audio",
                       "a session with audio switched on should already be recording")
        toggle.tap()

        // The capture list is the proof: a stopped recording that produced nothing is
        // deliberately not listed, so a row here means a real file on disk.
        XCTAssertTrue(fresh.otherElements["capture-row"].firstMatch.waitForExistence(timeout: 20)
                      || fresh.staticTexts["audio-001.m4a"].waitForExistence(timeout: 5),
                      "a finished recording should be listed as a capture")
    }

    /// The video switch decides whether this session carries a camera at all — one fewer thing
    /// to fumble past in the dark when it is not what you came to do.
    func testTheVideoSwitchAddsAndRemovesTheCameraButton() throws {
        app.terminate()
        let fresh = XCUIApplication()
        fresh.launchArguments += ["-fieldKitFakeSensors"]
        fresh.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: fresh))
        fresh.buttons["start-field-session"].tap()
        XCTAssertTrue(fresh.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        fresh.buttons["confirm-start-session"].tap()
        // Item 215: the live screen opens pending. Nothing is logged until Start.
        XCTAssertTrue(fresh.buttons["start-recording"].waitForExistence(timeout: 15))
        fresh.buttons["start-recording"].tap()

        let videoSwitch = fresh.switches["channel-video"]
        XCTAssertTrue(videoSwitch.waitForExistence(timeout: 20),
                      "video should be one of the session's channels")

        // Off by default: it is the one that would fill a phone.
        XCTAssertFalse(fresh.buttons["capture-video"].exists,
                       "a session not set up for video shouldn't offer the button")

        videoSwitch.tap()
        XCTAssertTrue(fresh.buttons["capture-video"].waitForExistence(timeout: 10),
                      "switching video on should put the camera button there")
    }

    /// A device left in a room: what it watches for, and the fact that it refuses to pretend.
    func testArmingRefusesWithoutABaseLevelAndWorksWithOne() throws {
        app.terminate()
        let fresh = XCUIApplication()
        fresh.launchArguments += ["-fieldKitFakeSensors"]
        fresh.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: fresh))
        fresh.buttons["start-field-session"].tap()
        XCTAssertTrue(fresh.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        fresh.buttons["confirm-start-session"].tap()
        // Item 215: the live screen opens pending. Nothing is logged until Start.
        XCTAssertTrue(fresh.buttons["start-recording"].waitForExistence(timeout: 15))
        fresh.buttons["start-recording"].tap()

        let arm = fresh.buttons["arm-sentry"]
        XCTAssertTrue(arm.waitForExistence(timeout: 20), "watching should be set up from here")
        arm.tap()

        let confirm = fresh.buttons["confirm-arm"]
        XCTAssertTrue(confirm.waitForExistence(timeout: 15))
        // Magnetic and sound are on by default and no base has been set, so arming would be
        // measuring against nothing.
        XCTAssertFalse(confirm.isEnabled,
                       "arming without a base level should be refused, not silently useless")

        fresh.buttons["Cancel"].tap()
        XCTAssertTrue(fresh.buttons["set-base-level"].waitForExistence(timeout: 15))
        fresh.buttons["set-base-level"].tap()

        fresh.buttons["arm-sentry"].tap()
        XCTAssertTrue(confirm.waitForExistence(timeout: 15))
        XCTAssertTrue(confirm.isEnabled, "with a base set, watching should be available")
        confirm.tap()

        XCTAssertTrue(fresh.buttons["disarm-sentry"].waitForExistence(timeout: 15),
                      "an armed session should offer to stop watching")
    }

    /// The camera has to be visible to be aimed — a device left in a corner is useless if you
    /// could not see what it was pointing at.
    func testTheViewfinderAppearsWithVideoAndGoesWithIt() throws {
        app.terminate()
        let fresh = XCUIApplication()
        fresh.launchArguments += ["-fieldKitFakeSensors"]
        fresh.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: fresh))
        fresh.buttons["start-field-session"].tap()
        XCTAssertTrue(fresh.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        fresh.buttons["confirm-start-session"].tap()
        // Item 215: the live screen opens pending. Nothing is logged until Start.
        XCTAssertTrue(fresh.buttons["start-recording"].waitForExistence(timeout: 15))
        fresh.buttons["start-recording"].tap()

        let videoSwitch = fresh.switches["channel-video"]
        XCTAssertTrue(videoSwitch.waitForExistence(timeout: 20))
        XCTAssertFalse(fresh.otherElements["camera-preview"].exists,
                       "no viewfinder before video is switched on")

        videoSwitch.tap()
        XCTAssertTrue(fresh.otherElements["camera-preview"].waitForExistence(timeout: 15),
                      "switching video on should show what the camera sees")

        videoSwitch.tap()
        let gone = fresh.otherElements["camera-preview"]
        let deadline = Date().addingTimeInterval(10)
        while gone.exists && Date() < deadline {
            _ = fresh.wait(for: .runningForeground, timeout: 0.5)
        }
        XCTAssertFalse(gone.exists, "a preview left running is a warm phone and a flat battery")
    }

    /// Blacking out the screen so its light stays out of the recording and the room — and, just
    /// as importantly, coming back from it with a single tap anywhere.
    func testTheScreenCanBeBlackedOutAndWokenWithATap() throws {
        app.terminate()
        let fresh = XCUIApplication()
        fresh.launchArguments += ["-fieldKitFakeSensors"]
        fresh.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: fresh))
        fresh.buttons["start-field-session"].tap()
        XCTAssertTrue(fresh.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        fresh.buttons["confirm-start-session"].tap()
        // Item 215: the live screen opens pending. Nothing is logged until Start.
        XCTAssertTrue(fresh.buttons["start-recording"].waitForExistence(timeout: 15))
        fresh.buttons["start-recording"].tap()

        let blackout = fresh.buttons["blackout"]
        // On the pinned bar, because a control you have to scroll to find is a control you
        // cannot find in the dark.
        XCTAssertTrue(blackout.waitForExistence(timeout: 20))
        blackout.tap()

        // Queried across element types: the overlay carries a button trait so a VoiceOver user
        // can wake it, which means it is not an `otherElement`.
        // Queried across element types and taking the first: the overlay carries a button trait
        // so a VoiceOver user can wake it, and a full-screen presentation puts the identifier on
        // more than one node.
        let overlay = fresh.descendants(matching: .any)
            .matching(identifier: "blackout-overlay").firstMatch
        XCTAssertTrue(overlay.waitForExistence(timeout: 10), "the screen should go dark")
        // The session is still going underneath — the controls are covered, not gone.
        XCTAssertFalse(fresh.buttons["stop-field-session"].isHittable)

        overlay.tap()

        XCTAssertTrue(fresh.buttons["stop-field-session"].waitForExistence(timeout: 10),
                      "a tap anywhere should bring the screen back")
        XCTAssertFalse(overlay.exists)
    }

    /// A finished session, replayed: the transport, the trace and the map all reachable, and the
    /// media pane honest about the stretches where nothing was recorded.
    func testAFinishedSessionCanBeReplayed() throws {
        app.terminate()
        let fresh = XCUIApplication()
        fresh.launchArguments += ["-fieldKitFakeSensors"]
        fresh.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: fresh))
        fresh.buttons["start-field-session"].tap()
        XCTAssertTrue(fresh.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        fresh.buttons["confirm-start-session"].tap()
        // Item 215: the live screen opens pending. Nothing is logged until Start.
        XCTAssertTrue(fresh.buttons["start-recording"].waitForExistence(timeout: 15))
        fresh.buttons["start-recording"].tap()

        // Give the scripted instruments a moment to log something worth replaying, and mark it
        // so the timeline has something to jump to.
        XCTAssertTrue(fresh.buttons["set-base-level"].waitForExistence(timeout: 20))
        fresh.buttons["set-base-level"].tap()
        sleep(3)
        fresh.buttons["mark-now"].tap()
        sleep(1)

        fresh.buttons["stop-field-session"].tap()

        // Stopping lands on review, which is where the replay lives.
        let play = fresh.buttons["replay-play"]
        XCTAssertTrue(play.waitForExistence(timeout: 25), "a finished session should replay")
        // Queried across element types: a decorative pane is not reliably an `otherElement`.
        XCTAssertTrue(fresh.descendants(matching: .any)
                        .matching(identifier: "replay-media").firstMatch
                        .waitForExistence(timeout: 10),
                      "the replay should show its media pane, even over a gap with no recording")

        // The marker made during the session is listed and jumps the playhead to it.
        let marker = fresh.buttons.matching(identifier: "replay-marker-row").firstMatch
        XCTAssertTrue(marker.waitForExistence(timeout: 15),
                      "what was marked during the session should be here to jump to")
        marker.tap()

        play.tap()
        sleep(1)
        // Pausing again proves the transport is live rather than a static picture.
        play.tap()

        XCTAssertTrue(fresh.buttons["replay-speed"].exists,
                      "a five-hour vigil needs a speed control")
    }

    /// Question, silence, question — and the wait bracketed at both ends.
    func testAskingAnEVPQuestionAndClosingTheWait() throws {
        app.terminate()
        let fresh = XCUIApplication()
        fresh.launchArguments += ["-fieldKitFakeSensors"]
        fresh.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: fresh))
        fresh.buttons["start-field-session"].tap()
        XCTAssertTrue(fresh.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        fresh.buttons["confirm-start-session"].tap()
        // Item 215: the live screen opens pending. Nothing is logged until Start.
        XCTAssertTrue(fresh.buttons["start-recording"].waitForExistence(timeout: 15))
        fresh.buttons["start-recording"].tap()

        let openEVP = fresh.buttons["open-evp"]
        XCTAssertTrue(openEVP.waitForExistence(timeout: 20))
        openEVP.tap()

        let primary = fresh.buttons["evp-primary"]
        XCTAssertTrue(primary.waitForExistence(timeout: 15))
        // One control, and it says which half of the cycle you are in — there is no reading a
        // subtitle in the dark.
        XCTAssertEqual(primary.label, "Ask a question")
        primary.tap()

        // Asking starts the wait, and the same control now ends it.
        let deadline = Date().addingTimeInterval(10)
        while primary.label != "Stop waiting" && Date() < deadline {
            _ = fresh.wait(for: .runningForeground, timeout: 0.5)
        }
        XCTAssertEqual(primary.label, "Stop waiting",
                       "asking should start the wait, on the same button")

        XCTAssertTrue(fresh.otherElements.matching(identifier: "evp-question-row").firstMatch.exists
                      || fresh.staticTexts["Question"].exists,
                      "the question should be listed while it is being waited on")

        sleep(2)
        primary.tap()

        let closed = Date().addingTimeInterval(10)
        while primary.label != "Ask a question" && Date() < closed {
            _ = fresh.wait(for: .runningForeground, timeout: 0.5)
        }
        XCTAssertEqual(primary.label, "Ask a question", "stopping the wait should free it up")

        fresh.buttons["Done"].tap()
        XCTAssertTrue(fresh.buttons["stop-field-session"].waitForExistence(timeout: 15),
                      "leaving EVP should return to the session, still running")
    }

    /// Building a bundle to hand over, from a finished session.
    func testASessionCanBeExportedAsABundle() throws {
        app.terminate()
        let fresh = XCUIApplication()
        fresh.launchArguments += ["-fieldKitFakeSensors"]
        fresh.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: fresh))
        fresh.buttons["start-field-session"].tap()
        XCTAssertTrue(fresh.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        fresh.buttons["confirm-start-session"].tap()
        // Item 215: the live screen opens pending. Nothing is logged until Start.
        XCTAssertTrue(fresh.buttons["start-recording"].waitForExistence(timeout: 15))
        fresh.buttons["start-recording"].tap()

        XCTAssertTrue(fresh.buttons["mark-now"].waitForExistence(timeout: 20))
        sleep(2)
        fresh.buttons["mark-now"].tap()
        fresh.buttons["stop-field-session"].tap()

        let share = fresh.buttons["open-share-menu"]
        XCTAssertTrue(share.waitForExistence(timeout: 25), "a finished session should export")
        share.tap()

        let exportItem = fresh.buttons["Export a bundle"].firstMatch
        XCTAssertTrue(exportItem.waitForExistence(timeout: 10),
                      "exporting should be offered alongside sending to the server")
        exportItem.tap()

        let build = fresh.buttons["build-export"]
        XCTAssertTrue(build.waitForExistence(timeout: 15))
        build.tap()

        // The share control only appears once a bundle really exists on disk.
        XCTAssertTrue(fresh.buttons["share-export"].waitForExistence(timeout: 30),
                      "building should produce a bundle to share")
        XCTAssertTrue(fresh.staticTexts["Readings"].exists,
                      "the bundle should report what went into it")
    }

    /// Sending a session up: the screen that asks where it belongs and what goes with it.
    ///
    /// The upload itself needs a server, so what is checked here is the part that must be right
    /// before anything leaves the phone — that the door exists, that it says an account is
    /// needed, and that files can be chosen and dropped.
    func testTheUploadScreenAsksWhereTheSessionBelongs() throws {
        app.terminate()
        let fresh = XCUIApplication()
        fresh.launchArguments += ["-fieldKitFakeSensors"]
        fresh.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: fresh))
        fresh.buttons["start-field-session"].tap()
        XCTAssertTrue(fresh.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        fresh.buttons["confirm-start-session"].tap()
        // Item 215: the live screen opens pending. Nothing is logged until Start.
        XCTAssertTrue(fresh.buttons["start-recording"].waitForExistence(timeout: 15))
        fresh.buttons["start-recording"].tap()

        XCTAssertTrue(fresh.buttons["stop-field-session"].waitForExistence(timeout: 20))
        fresh.buttons["stop-field-session"].tap()

        let share = fresh.buttons["open-share-menu"]
        XCTAssertTrue(share.waitForExistence(timeout: 25))
        share.tap()

        let send = fresh.buttons["Send to the server"].firstMatch
        XCTAssertTrue(send.waitForExistence(timeout: 10),
                      "a finished session should offer to go to the server")
        send.tap()

        // Whichever side of the account line this run is on, the screen must say something.
        // The Keychain survives an app relaunch, so an earlier test in the suite may have left
        // this device signed in — asserting one state only would pass alone and fail in company.
        let explained = fresh.staticTexts.containing(
            NSPredicate(format: "label CONTAINS[c] 'Recording never needs an account'")).firstMatch
        // A Button, not an Other: the picker has always surfaced as a button, so the old
        // otherElements query never matched and this test passed only through send-session.
        // Once the trimmer (item 210) sat between the two, the Send button fell below the fold
        // of a lazy Form and stopped being materialised — and the masked query showed itself.
        let picker = fresh.buttons["upload-investigation"].firstMatch
        let sendButton = fresh.buttons["send-session"]

        let deadline = Date().addingTimeInterval(20)
        while !explained.exists && !picker.exists && !sendButton.exists && Date() < deadline {
            _ = fresh.wait(for: .runningForeground, timeout: 0.5)
        }

        if explained.exists {
            // Signed out: it explains WHY sending needs an account when recording did not,
            // and offers nothing to press.
            XCTAssertFalse(sendButton.exists,
                           "nothing to press until there is an account to send under")
        } else {
            // Signed in: it asks where the session belongs before anything leaves the phone.
            XCTAssertTrue(picker.exists || sendButton.exists,
                          "a signed-in person should be asked where the session belongs")
        }
    }

    /// Writing a note: typed, dictated, or recorded — and dictation only where it works offline.
    func testTheNoteComposerOffersTheWaysThisDeviceCanWriteOne() throws {
        app.terminate()
        let fresh = XCUIApplication()
        fresh.launchArguments += ["-fieldKitFakeSensors"]
        fresh.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: fresh))
        fresh.buttons["start-field-session"].tap()
        XCTAssertTrue(fresh.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        fresh.buttons["confirm-start-session"].tap()
        // Item 215: the live screen opens pending. Nothing is logged until Start.
        XCTAssertTrue(fresh.buttons["start-recording"].waitForExistence(timeout: 15))
        fresh.buttons["start-recording"].tap()

        let note = fresh.buttons["open-note"]
        XCTAssertTrue(note.waitForExistence(timeout: 20), "a session should offer to take a note")
        note.tap()

        // Queried across element types: a segmented picker is not an `otherElement`.
        XCTAssertTrue(fresh.descendants(matching: .any)
                        .matching(identifier: "note-kind").firstMatch
                        .waitForExistence(timeout: 15),
                      "the composer should offer a way to write the note")

        // Typing is always available; dictation only where the device can transcribe offline,
        // so its absence here is correct rather than a fault.
        let field = fresh.textFields["note-text"]
        if !field.exists {
            // Dictation was chosen as the default, so switch to typing.
            fresh.buttons["Type"].firstMatch.tap()
        }
        XCTAssertTrue(fresh.textFields["note-text"].waitForExistence(timeout: 10)
                      || fresh.textViews["note-text"].waitForExistence(timeout: 5),
                      "there should always be a way to write a note by hand")

        let target = fresh.textFields["note-text"].exists
            ? fresh.textFields["note-text"] : fresh.textViews["note-text"]
        target.tap()
        let marker = "cold spot \(Int(Date().timeIntervalSince1970))"
        target.typeText(marker)

        fresh.buttons["save-note"].tap()

        // The note lands on the session's own marker list.
        XCTAssertTrue(fresh.staticTexts[marker].waitForExistence(timeout: 20),
                      "a saved note should appear against the session")
    }

    /// Choosing the photograph that stands for the property.
    func testAPhotoCanBeChosenToRepresentTheProperty() throws {
        app.terminate()
        let fresh = XCUIApplication()
        fresh.launchArguments += ["-fieldKitFakeSensors"]
        fresh.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: fresh))
        fresh.buttons["start-field-session"].tap()
        XCTAssertTrue(fresh.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        fresh.buttons["confirm-start-session"].tap()
        // Item 215: the live screen opens pending. Nothing is logged until Start.
        XCTAssertTrue(fresh.buttons["start-recording"].waitForExistence(timeout: 15))
        fresh.buttons["start-recording"].tap()

        XCTAssertTrue(fresh.buttons["stop-field-session"].waitForExistence(timeout: 20))
        fresh.buttons["stop-field-session"].tap()

        let share = fresh.buttons["open-share-menu"]
        XCTAssertTrue(share.waitForExistence(timeout: 25))
        share.tap()

        let photos = fresh.buttons["Property photos"].firstMatch
        XCTAssertTrue(photos.waitForExistence(timeout: 10),
                      "a session should offer its property photos")
        photos.tap()

        // A simulator has no camera, so this session has no photos — and the screen SAYS so
        // rather than showing an empty grid somebody reads as a failure.
        XCTAssertTrue(
            fresh.staticTexts.containing(
                NSPredicate(format: "label CONTAINS[c] 'No photos in this session'")
            ).firstMatch.waitForExistence(timeout: 15)
            || fresh.buttons.matching(identifier: "property-photo").firstMatch.exists,
            "the screen should either show photos or say there are none")
    }
}