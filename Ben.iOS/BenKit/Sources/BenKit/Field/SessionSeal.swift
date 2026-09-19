import Foundation
import CryptoKit

/// What a `.ben` bundle says about itself: who recorded it, and what was in it when it was sealed.
///
/// Ben, 2026-09-16: "Is there a way to include a fingerprint inside the .ben file to validate who
/// recorded the session? Not their name, but maybe the id."
///
/// **What this is, said exactly.** The seal lists every member of the bundle with its SHA-256, and
/// a single digest computed over that list. Change one byte of one recording and the digest stops
/// matching; add a file, remove a file, or swap two, and it stops matching. That makes a bundle
/// TAMPER-EVIDENT: anybody holding the file can tell whether it is still what was sealed.
///
/// **What this is not.** It is not proof of who recorded the session. A ZIP is a container anybody
/// can open, so a determined person can change a recording, recompute the digests, and write a new
/// seal — including a different account id. What closes that is a signature: the digest below
/// signed by a key the recording device holds and the account has registered, which is the
/// `signature` field this deliberately leaves room for and does not yet fill. Until then the
/// account id is a claim the file makes about itself, and the honest way to describe it is
/// provenance rather than proof.
///
/// Even unsigned it is worth carrying, because the failure it catches is the common one: a bundle
/// that lost bytes in transit, a recording replaced by accident, a file half-written to a share.
/// Those are mistakes, not attacks, and a digest catches every one of them.
public struct SessionSeal: Codable, Sendable, Equatable {

    /// What the seal is called inside the bundle.
    public static let entryPath = "seal.json"

    /// Bumped when the shape changes. A reader that does not know a version refuses it rather
    /// than guessing at fields it has never seen.
    public var version: Int

    /// The account signed in on the device when the session was recorded, when there was one.
    /// Null is an ordinary answer: the app records without an account on purpose.
    public var recordedByAccountId: UUID?

    /// Apple's own identifier for this app on this device — `identifierForVendor`.
    ///
    /// Ben, 2026-09-16: "Maybe the apple assigned ID and if they sign up for an account on our
    /// site after recording a session, we could tell it was them who recorded it when they upload
    /// it." Exactly the case an account id cannot cover, because recording needs no account: a
    /// person walks a building, records four nights, and only then signs up. Those bundles carry
    /// no account id because there was none, and the device id is what connects them to whoever
    /// that device turns out to belong to.
    ///
    /// **It identifies a device, not a person.** Two people sharing a phone seal identically, and
    /// one person with a phone and an iPad seals differently on each. Apple also resets it once
    /// every app from this vendor is removed from the device, so the link is best-effort by
    /// design — useful for "this was recorded on your own phone", never for "only you could have
    /// recorded this".
    ///
    /// The other half of its worth is the opposite question: a bundle somebody AirDropped to you
    /// carries a device id that is not yours, so an imported session can be shown as somebody
    /// else's work rather than quietly attributed to whoever uploaded it.
    public var deviceId: String?

    /// The session's own id, so a seal cannot be lifted off one bundle onto another.
    public var sessionId: UUID

    public var sealedAt: Date

    /// Every member of the bundle except this file, by path, with its digest.
    public var entries: [Entry]

    /// SHA-256 over the canonical rendering of `entries` — the one value a signature would cover.
    public var digest: String

    public struct Entry: Codable, Sendable, Equatable {
        public var path: String
        public var sha256: String
        public var byteCount: Int64

        public init(path: String, sha256: String, byteCount: Int64) {
            self.path = path
            self.sha256 = sha256
            self.byteCount = byteCount
        }

        private enum CodingKeys: String, CodingKey {
            case path, sha256
            case byteCount = "byte_count"
        }
    }

    public init(version: Int = 1, recordedByAccountId: UUID?, deviceId: String?,
                sessionId: UUID, sealedAt: Date, entries: [Entry]) {
        self.version = version
        self.recordedByAccountId = recordedByAccountId
        self.deviceId = deviceId
        self.sessionId = sessionId
        self.sealedAt = sealedAt
        // Sorted before digesting, so two bundles holding the same files in a different order
        // seal to the same value and the digest describes CONTENT rather than packing order.
        let ordered = entries.sorted { $0.path < $1.path }
        self.entries = ordered
        self.digest = Self.digest(of: ordered)
    }

    /// The value a signature would cover: one line per entry, in path order, hashed.
    ///
    /// Built by hand rather than by hashing the encoded JSON, because JSON encoding is not stable
    /// — key order, spacing and date formatting can all change under a library upgrade, and a
    /// digest that moves when nothing moved is a digest nobody can check twice.
    public static func digest(of entries: [Entry]) -> String {
        var hasher = SHA256()
        for entry in entries.sorted(by: { $0.path < $1.path }) {
            hasher.update(data: Data("\(entry.path)\n\(entry.sha256)\n\(entry.byteCount)\n".utf8))
        }
        return hasher.finalize().map { String(format: "%02x", $0) }.joined()
    }

    /// Whether this seal still describes the files it was made for.
    public func matches(_ entries: [Entry]) -> Bool {
        Self.digest(of: entries) == digest
    }

    private enum CodingKeys: String, CodingKey {
        case version, entries, digest
        case recordedByAccountId = "recorded_by_account_id"
        case deviceId = "device_id"
        case sessionId = "session_id"
        case sealedAt = "sealed_at"
    }
}
