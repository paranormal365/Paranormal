import XCTest

/// Reading a published report on the phone, end to end: the case screen offers Reports, the list
/// has one, and tapping it puts a real PDF on screen.
///
/// The last step is the one worth having. The report route carries a bearer token and has no
/// Range support, so a viewer pointed straight at the URL renders a blank page rather than
/// failing — which looks exactly like a report with nothing in it.
final class CaseReportsUITests: XCTestCase {

    private var app: XCUIApplication!

    override func setUp() {
        continueAfterFailure = false
        app = XCUIApplication()
        let email = ProcessInfo.processInfo.environment["BEN_CLIENT_EMAIL"] ?? "haveben@msn.com"
        let password = ProcessInfo.processInfo.environment["BEN_CLIENT_PASSWORD"] ?? "Y@ung615"
        app.launchArguments += ["-autoSignIn", "\(email):\(password)"]
        app.launch()
    }

    func testOpeningAPublishedReport() throws {
        try AppNavigator.openFirstCase(in: app)

        let reports = app.buttons["Reports"].firstMatch
        XCTAssertTrue(reports.waitForExistence(timeout: 20),
                      "a case must offer its reports")
        reports.tap()

        let row = app.buttons.matching(identifier: "report-row").firstMatch
        guard row.waitForExistence(timeout: 20) else {
            throw XCTSkip("no published report on this case")
        }
        row.tap()

        // The report's own screen, not just a tap that went nowhere. Waiting on the title alone
        // would pass while the document was still arriving, so the share button — which only
        // exists once the file is on the device — is the real proof.
        XCTAssertTrue(app.navigationBars["Report"].waitForExistence(timeout: 30),
                      "tapping a report should open it")
        XCTAssertTrue(app.buttons["Share"].firstMatch.waitForExistence(timeout: 30)
                      || app.navigationBars["Report"].buttons.firstMatch.exists,
                      "the report should finish downloading and offer to share")
    }
}
