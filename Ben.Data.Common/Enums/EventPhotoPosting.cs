namespace Ben.Data.Common.Enums;

/// <summary>
/// Who may add photos and videos to an event's room and its photo wall (item 235 phase 11).
/// </summary>
/// <remarks>
/// Ben, 2026-09-13: "give the organizer the option of only allowing organizer employees to post them
/// or to allow employees and attendees." A séance with a no-cameras rule and a wedding-style weekend
/// where everybody's pictures are the point are both real events. Append only.
/// </remarks>
public enum EventPhotoPosting
{
    /// <summary>
    /// The event's own people and every confirmed guest. Zero, so an event that existed before this
    /// setting behaves the way its room always did.
    /// </summary>
    TeamAndGuests = 0,

    /// <summary>The event's own people only: the organizing group and the helpers at the event.</summary>
    TeamOnly = 1,
}
