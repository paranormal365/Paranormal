import Foundation

/// How a field reading is written for a person: whole numbers, signed from the base, and never
/// "-0".
///
/// `String(format: "%+.0f", -0.4)` gives "-0", because the sign is decided before the rounding.
/// The live meter showed "-0 mG" on an iPad while the help pictures were retaken (10/17/2026),
/// and VoiceOver would have read the same reading as "0 mG below base". Rounding first, and
/// treating whatever rounds to zero as exactly at the base, fixes both.
public enum ReadingText {
    /// A whole number with no forced sign, and never "-0": "-51", "0", "12". Sound levels are
    /// negative (dB below full scale), so a very loud one near 0 would otherwise print "-0 dB".
    public static func whole(_ value: Double) -> String {
        let whole = Int(value.rounded())
        return whole == 0 ? "0" : "\(whole)"
    }

    /// A whole number with its sign: "+20", "-3", and "0" for anything that rounds to zero.
    public static func signedWhole(_ value: Double) -> String {
        let whole = Int(value.rounded())
        if whole == 0 { return "0" }
        return whole > 0 ? "+\(whole)" : "-\(-whole)"
    }

    /// The same, with its unit: "+20 mG", "-3 mG", "0 mG".
    public static func signedWhole(_ value: Double, unit: String) -> String {
        "\(signedWhole(value)) \(unit)"
    }

    /// Said in words from the base: "3 mG above base", "3 mG below base", "at base".
    public static func fromBase(_ value: Double, unit: String) -> String {
        let whole = Int(value.rounded())
        if whole == 0 { return "at base" }
        return "\(abs(whole)) \(unit) \(whole > 0 ? "above" : "below") base"
    }
}
