import Foundation

/// A room's name as the server keys it: one current scan per room per place, so "Bedroom 1" and
/// " bedroom  1" must be the same room on both sides (LiDAR plan, phase 1).
///
/// The same rule as `PlaceScans.NormalizeRoomKey` on the server — trimmed, inner whitespace
/// collapsed to one space, lower-cased without regard to the phone's language. Both are held to
/// `Fixtures/place-scans/room-key-vectors.json`.
///
/// Walked a Unicode scalar at a time, because that is what the server walks (a UTF-16 unit at a
/// time): a grapheme cluster can glue a space to the mark after it, and the two would disagree.
public enum RoomKey {

    /// The name as it is kept for showing: trimmed, inner whitespace collapsed. Nil when there is
    /// no name at all.
    public static func cleanName(_ name: String?) -> String? {
        guard let name else { return nil }
        var result = String.UnicodeScalarView()
        var pendingSpace = false
        for scalar in name.unicodeScalars {
            if scalar.properties.isWhitespace {
                pendingSpace = !result.isEmpty
                continue
            }
            if pendingSpace {
                result.append(" ")
                pendingSpace = false
            }
            result.append(scalar)
        }
        return result.isEmpty ? nil : String(result)
    }

    /// The key: the clean name, lower-cased. Empty when there is no name.
    public static func key(_ name: String?) -> String {
        guard let clean = cleanName(name) else { return "" }
        var result = String.UnicodeScalarView()
        for scalar in clean.unicodeScalars {
            // The simple mapping, as the server's invariant lower-casing uses: one scalar for one.
            // The full mapping only differs where a capital becomes two scalars (İ), and there the
            // server keeps the first.
            result.append(scalar.properties.lowercaseMapping.unicodeScalars.first ?? scalar)
        }
        return String(result)
    }

    /// Whether two names are the same room.
    public static func same(_ a: String?, _ b: String?) -> Bool {
        let left = key(a)
        return !left.isEmpty && left == key(b)
    }
}
