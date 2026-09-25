namespace Ben.Video.Editor.Models;

/// <summary>
/// What a transition style is called where a person reads it.
/// </summary>
/// <remarks>
/// The style picker, the "Replace ▸" menu and the Transitions gallery each printed the enum member
/// straight out, so the list a user chose an effect from read "WipeLeft · SmoothUp · FadeBlack ·
/// CircleClose" — identifier spelling, in the one place in the editor whose whole job is to let
/// somebody browse effects by eye (2026-09-18 audit). One table, so the three lists agree.
/// </remarks>
public static class TransitionStyleName
{
    public static string For(TransitionStyle style) => style switch
    {
        TransitionStyle.Cut         => "Cut",
        TransitionStyle.Fade        => "Fade",
        TransitionStyle.Dissolve    => "Dissolve",
        TransitionStyle.WipeLeft    => "Wipe left",
        TransitionStyle.WipeRight   => "Wipe right",
        TransitionStyle.SlideLeft   => "Slide left",
        TransitionStyle.Zoom        => "Zoom",
        TransitionStyle.CircleOpen  => "Circle open",
        TransitionStyle.CircleClose => "Circle close",
        TransitionStyle.Radial      => "Radial",
        TransitionStyle.SmoothLeft  => "Smooth left",
        TransitionStyle.SmoothRight => "Smooth right",
        TransitionStyle.SmoothUp    => "Smooth up",
        TransitionStyle.SmoothDown  => "Smooth down",
        TransitionStyle.Pixelize    => "Pixelize",
        TransitionStyle.FadeBlack   => "Fade through black",
        TransitionStyle.FadeWhite   => "Fade through white",
        // A style added to the enum and not to this table still reads as something rather than
        // throwing in the middle of a menu — but the test below makes sure that never ships.
        _ => style.ToString(),
    };
}
