import SwiftUI

/// The IsHaunted palette — the website's Signal skin, so the app and the site read as one product
/// (Ben, 2026-10-02: "style the iOS app similar to the site so they flow together seamlessly").
/// Every color comes from the asset catalog, both appearances defined, with the same values as the
/// site's `signal-tokens.css`; nothing hard-codes a color.
///
/// The names are the app's originals, kept so no screen had to change to take the new look:
/// `ecto` was spectral green and is now the site's violet, `haunt` was violet and is now its cyan.
/// The semantic three — danger, warning, success — are deliberately NOT the site's: on a Field Kit
/// meter red and amber mean something, and Signal's mauve danger sits too close to its violet to
/// read as an alarm at a glance in the dark.
enum Theme {
    /// The page: Signal's night (#0C1017) in dark, its sunken paper (#F4F7FC) in light.
    static let ink = Color("Ink")
    /// Cards and bars that sit on the page (#151C28 / #FFFFFF).
    static let mist = Color("Mist")
    /// The accent — Signal violet (#7C5CFF dark, #5B3DF5 light). Also the app's AccentColor.
    static let ecto = Color("Ecto")
    /// The second accent — Signal cyan (#22D3EE dark, #0891A6 light).
    static let haunt = Color("Haunt")
    /// Words.
    static let bone = Color("Bone")
    /// Quieter words.
    static let fog = Color("Fog")
    static let danger = Color("Danger")
    static let warning = Color("Warning")
    static let success = Color("Success")

    /// The site's gradient — violet into cyan — for the one thing on a screen that should look
    /// pressed, exactly as the website's primary button and its hero accents use it.
    static let gradient = LinearGradient(colors: [ecto, haunt], startPoint: .topLeading, endPoint: .bottomTrailing)

    /// Signal's corners: small for fields and chips, the default for cards, large for heroes.
    enum Radius {
        static let small: CGFloat = 8
        static let card: CGFloat = 12
        static let large: CGFloat = 20
    }
}
