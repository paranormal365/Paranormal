import SwiftUI

/// The IsHaunted palette — dark-first paranormal brand. Every color comes from
/// the asset catalog (both appearances defined); nothing hard-codes a color.
enum Theme {
    /// Near-black page background.
    static let ink = Color("Ink")
    /// Elevated surface (cards, bars).
    static let mist = Color("Mist")
    /// Primary accent — spectral green.
    static let ecto = Color("Ecto")
    /// Secondary accent — violet.
    static let haunt = Color("Haunt")
    /// Primary text.
    static let bone = Color("Bone")
    /// Secondary text.
    static let fog = Color("Fog")
    static let danger = Color("Danger")
    static let warning = Color("Warning")
    static let success = Color("Success")
}
