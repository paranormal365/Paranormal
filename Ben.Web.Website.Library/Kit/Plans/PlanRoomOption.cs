namespace Ben.Web.Website.Library.Kit.Plans;

/// <summary>
/// One of the venue's own rooms, as the designer's tray needs it.
/// </summary>
/// <remarks>
/// <para>Its own small record rather than the room record the client returns, so the plan
/// components stay a Kit that knows nothing about how a page fetched anything. The page maps into
/// it, which is one line and keeps the designer testable with three rooms invented on the spot.</para>
/// </remarks>
/// <param name="Capacity">
/// What the venue says it sleeps, which the event may override. Null is "nobody has said", and a
/// unit made from it inherits the null rather than guessing a number.
/// </param>
/// <param name="BedNote">
/// What the beds actually are. Shown in the tray because "will the two of us have to share a bed"
/// is the question a host is answering while deciding which rooms to offer.
/// </param>
public sealed record PlanRoomOption(
    Guid PlaceRoomId,
    string Name,
    string? Floor = null,
    int? Capacity = null,
    string? BedNote = null);
