import SwiftUI

/// The website's buttons, in the app (Signal, 2026-10-02).
///
/// Primary: the violet-to-cyan gradient in a capsule, white words, a soft glow in the accent — the
/// site's `.btn-primary`, and the one thing on a screen that should look pressed. Secondary: a
/// capsule outline with the words in the accent — the site's outline buttons.
///
/// A button that means something other than "go" keeps its own colour and the system style: the
/// live session's red Stop and green Start, EVP mode's amber "waiting". A gradient there would say
/// "the main action" about a button that is saying "careful".
struct SignalPrimaryButtonStyle: ButtonStyle {
    @Environment(\.isEnabled) private var isEnabled
    @Environment(\.controlSize) private var controlSize

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(controlSize == .large ? .body.weight(.semibold) : .subheadline.weight(.semibold))
            .foregroundStyle(.white)
            .padding(.vertical, verticalPadding)
            .padding(.horizontal, horizontalPadding)
            .background(Capsule().fill(Theme.gradient))
            .shadow(color: Theme.ecto.opacity(isEnabled ? 0.35 : 0), radius: 10, y: 4)
            .opacity(isEnabled ? 1 : 0.45)
            .scaleEffect(configuration.isPressed ? 0.97 : 1)
            .brightness(configuration.isPressed ? -0.06 : 0)
            .animation(.easeOut(duration: 0.12), value: configuration.isPressed)
            .contentShape(Capsule())
    }

    private var verticalPadding: CGFloat {
        switch controlSize {
        case .mini, .small: 6
        case .large, .extraLarge: 14
        default: 9
        }
    }

    private var horizontalPadding: CGFloat {
        switch controlSize {
        case .mini, .small: 12
        case .large, .extraLarge: 22
        default: 16
        }
    }
}

struct SignalSecondaryButtonStyle: ButtonStyle {
    @Environment(\.isEnabled) private var isEnabled
    @Environment(\.controlSize) private var controlSize

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(controlSize == .large ? .body.weight(.semibold) : .subheadline.weight(.semibold))
            .foregroundStyle(Theme.ecto)
            .padding(.vertical, controlSize == .large ? 13 : (controlSize == .small || controlSize == .mini ? 5 : 8))
            .padding(.horizontal, controlSize == .large ? 20 : (controlSize == .small || controlSize == .mini ? 11 : 15))
            .background(Capsule().fill(Theme.ecto.opacity(configuration.isPressed ? 0.16 : 0.06)))
            .overlay(Capsule().strokeBorder(Theme.fog.opacity(0.45), lineWidth: 1))
            .opacity(isEnabled ? 1 : 0.45)
            .animation(.easeOut(duration: 0.12), value: configuration.isPressed)
            .contentShape(Capsule())
    }
}

extension ButtonStyle where Self == SignalPrimaryButtonStyle {
    /// The site's primary button: gradient capsule, white words.
    static var signalPrimary: SignalPrimaryButtonStyle { SignalPrimaryButtonStyle() }
}

extension ButtonStyle where Self == SignalSecondaryButtonStyle {
    /// The site's outline button.
    static var signalSecondary: SignalSecondaryButtonStyle { SignalSecondaryButtonStyle() }
}
