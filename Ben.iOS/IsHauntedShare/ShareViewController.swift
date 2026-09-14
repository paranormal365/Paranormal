import UIKit
import SwiftUI

/// The Share Extension's entry point (item 235 phase 14d): photos or videos shared from Photos or the camera roll,
/// added to one of the person's events.
///
/// **It only keeps; the app sends.** The extension has no sign-in of its own — sharing the Keychain with it would
/// move every existing person's session — so it copies what was shared into the outbox the app and the extension
/// share, and IsHaunted sends it the next time it's open with a signal.
final class ShareViewController: UIViewController {
    private var model: ShareModel?

    override func viewDidLoad() {
        super.viewDidLoad()
        let model = ShareModel(context: extensionContext)
        self.model = model

        let host = UIHostingController(rootView: ShareToEventView(model: model))
        addChild(host)
        host.view.frame = view.bounds
        host.view.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        view.addSubview(host.view)
        host.didMove(toParent: self)

        Task { await model.loadAttachments() }
    }
}
