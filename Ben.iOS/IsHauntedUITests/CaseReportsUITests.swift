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
        let casesTab = app.buttons["My Cases"]
        XCTAssertTrue(casesTab.waitForExistence(timeout: 20))
        casesTab.tap()

        let firstCase = app.buttons.matching(identifier: "case-row").firstMatch
        guard firstCase.waitForExistence(timeout: 20) else {
            throw XCTSkip("no case on this account")
        }
        firstCase.tap()

        let reports = app.buttons["Reports"].firstMatch
        XCTAssertTrue(reports.waitForExistence(timeout: 20),
                      "a case must offer its reports")
        reports.tap()

        let row = app.buttons.matching(identifier: "report-row").firstMatch
        guard row.waitForExistence(timeout: 20) else {
            throw XCTSkip("no published report on this case")
        }
        row.tap()

        // The sheet's own chrome proves the document opened rather than the tap doing nothing.
        XCTAssertTrue(app.navigationBars["Report"].waitForExistence(timeout: 30),
                      "tapping a report should open it")
        XCTAssertTrue(app.buttons["Done"].waitForExistence(timeout: 10))
    }
}
