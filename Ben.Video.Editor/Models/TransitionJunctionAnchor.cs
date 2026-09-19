namespace Ben.Video.Editor.Models;

/// <summary>
/// Where the "add a transition here" button belongs on a track: on the cut between two clips.
/// </summary>
/// <remarks>
/// Item #57 T1 moved the transition CHIP out of the track's sequential flow and onto an absolute
/// left/width computed from its position, so it straddles the real cut. The insert BUTTON was left
/// behind in normal flow — and because every clip chip is absolutely positioned, normal flow in
/// that lane is empty, so each junction's button was drawn at x=0 of the track no matter where its
/// cut was. One junction put it on top of the first clip's start handle; three clips stacked three
/// identical buttons in the same spot. It also broke the drag path the button's own tooltip
/// advertises: clipDragBridge hit-tests <c>.bv-transition-insert</c> under the pointer, so dropping
/// a style on the actual cut landed on nothing (2026-09-18 audit).
/// </remarks>
public static class TransitionJunctionAnchor
{
    /// <summary>
    /// The seconds mark to centre the button on, given where the first clip ends and the second
    /// begins. Normally those are the same number and the answer is the cut itself; when a gap
    /// separates them the midpoint keeps the button inside the gap where it is visible and
    /// unambiguous, and when the clips overlap it stays inside the overlap.
    /// </summary>
    public static double Seconds(double firstClipEndSeconds, double secondClipStartSeconds) =>
        Math.Max(0, (firstClipEndSeconds + secondClipStartSeconds) / 2);
}
