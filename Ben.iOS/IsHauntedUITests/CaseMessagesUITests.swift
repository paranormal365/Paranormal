import XCTest

/// Writing to your group from the case screen, and seeing it land.
final class CaseMessagesUITests: XCTestCase {

    private var app: XCUIApplication!

    override func setUp() {
        continueAfterFailure = false
        app = XCUIApplication()
        let email = ProcessInfo.processInfo.environment["BEN_CLIENT_EMAIL"] ?? "haveben@msn.com"
        let password = ProcessInfo.processInfo.environment["BEN_CLIENT_PASSWORD"] ?? "Y@ung615"
        app.launchArguments += ["-autoSignIn", "\(email):\(password)"]
        app.launch()
    }

    func testSendingAMessageToTheGroup() throws {
        try AppNavigator.openFirstCase(in: app)

        let messages = app.buttons["Messages"].firstMatch
        XCTAssertTrue(messages.waitForExistence(timeout: 20), "a case must offer its messages")
        messages.tap()

        let draft = app.textFields["message-draft"]
        XCTAssertTrue(draft.waitForExistence(timeout: 20))
        draft.tap()
        let marker = "UI message \(Int(Date().timeIntervalSince1970))"
        draft.typeText(marker)

        app.buttons["Send"].tap()

        // The bubble is the proof — a cleared field only proves the field cleared.
        XCTAssertTrue(app.staticTexts[marker].waitForExistence(timeout: 30),
                      "the sent message should appear in the conversation")
    }
}
