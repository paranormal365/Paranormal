using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Investigations;

/// <summary>
/// Changing a visit after it is booked: keeping its calendar entry true, and deleting it without
/// losing what was recorded there (10/16/2026).
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> A visit booked from the group's Investigations page has no case,
/// and the only edit, cancel and delete were nested under a case. So a visit saved an hour late
/// before the time-zone fix of 10/16/2026 could never be put right. The group's page now has its
/// own doors (<c>OrgInvestigationsController</c>), and both controllers share what is here.</para>
///
/// <para>Nothing here saves. The caller owns the transaction and the audit row.</para>
/// </remarks>
public static class InvestigationLifecycle
{
    /// <summary>What a visit's own calendar entry starts with. Set when the visit is booked.</summary>
    public const string CalendarPrefix = "Investigation: ";

    /// <summary>What it starts with once the visit is canceled, so the calendar does not show it as on.</summary>
    public const string CanceledPrefix = "Canceled: ";

    /// <summary>The title a visit's own calendar entry should carry.</summary>
    public static string CalendarTitle(Investigation visit)
        => (visit.Status == InvestigationStatus.Cancelled ? CanceledPrefix : "") + CalendarPrefix + visit.Title;

    /// <summary>
    /// Whether this calendar entry is the one made for the visit when it was booked.
    /// </summary>
    /// <remarks>
    /// A case-bound visit can be pointed at an entry somebody made by hand. That one is theirs:
    /// its title, its public listing and its guests are not the visit's to rewrite. The entry a
    /// booking makes is private, belongs to no hosted event or tour, and carries the prefix.
    /// </remarks>
    public static bool IsVisitsOwn(OrgCalendarEvent entry)
        => !entry.IsPublic
           && entry.HostedEventId is null
           && entry.TourId is null
           && (entry.Title.StartsWith(CalendarPrefix, StringComparison.Ordinal)
               || entry.Title.StartsWith(CanceledPrefix + CalendarPrefix, StringComparison.Ordinal));

    /// <summary>
    /// Brings the visit's own calendar entry up to date with the visit. Does not save.
    /// </summary>
    /// <remarks>
    /// Until 10/16/2026 nothing did this: moving a visit left its calendar entry at the old time,
    /// and a canceled visit still sat on the calendar as though it were happening.
    /// </remarks>
    public static async Task SyncCalendarAsync(BenDataContext db, Investigation visit, CancellationToken ct)
    {
        if (visit.OrgCalendarEventId is not { } entryId) return;

        var entry = await db.OrgCalendarEvents.FirstOrDefaultAsync(
            e => e.Id == entryId && e.OrganizationId == visit.OrganizationId, ct);
        if (entry is null || !IsVisitsOwn(entry)) return;

        entry.Title = Clip(CalendarTitle(visit), 256);
        entry.Description = visit.Description;
        entry.Location = visit.Location;
        entry.StartDateTime = visit.ScheduledDateTime;
        entry.EndDateTime = visit.EndDateTime ?? visit.ScheduledDateTime.AddHours(2);
        if (visit.TimeZoneId is not null) entry.TimeZoneId = visit.TimeZoneId;
        entry.DateUpdated = DateTime.UtcNow;
        entry.UpdatedByAppUserId = visit.UpdatedByAppUserId;
    }

    /// <summary>
    /// Why this visit cannot be deleted, or null when it can.
    /// </summary>
    /// <remarks>
    /// Recordings, written accounts and who was there are what a visit is for. Deleting the visit
    /// takes all three with it (they hang off it), so a visit that has any of them is refused,
    /// and the sentence says what to do instead: cancel it, which keeps the record.
    /// </remarks>
    public static async Task<string?> WhyCannotDeleteAsync(
        BenDataContext db, Investigation visit, CancellationToken ct)
    {
        var recordings = await db.FieldSessionUploads.AsNoTracking()
            .CountAsync(s => s.InvestigationId == visit.Id, ct);
        var accounts = await db.InvestigationFindings.AsNoTracking()
            .CountAsync(f => f.InvestigationId == visit.Id, ct);
        var wereThere = await db.InvestigationAttendees.AsNoTracking()
            .CountAsync(a => a.InvestigationId == visit.Id && a.DidAttend == true, ct);

        var kept = new List<string>();
        if (recordings > 0) kept.Add(Count(recordings, "recording", "recordings"));
        if (accounts > 0) kept.Add(Count(accounts, "written account", "written accounts"));
        if (wereThere > 0) kept.Add(Count(wereThere, "person marked as there", "people marked as there"));
        if (kept.Count == 0) return null;

        var instead = visit.Status == InvestigationStatus.Cancelled
            ? "It's already canceled, which keeps them on record."
            : "Cancel it instead. A canceled visit keeps them on record.";

        return $"This visit has {Join(kept)}, and deleting it would lose them. {instead}";
    }

    /// <summary>
    /// Clears what would stop the delete, or what would be left pointing at nothing. Does not save.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Duty assignments: their key to the team row is NoAction, so SQL Server refused to
    /// delete any visit where somebody held a duty. The team rows themselves cascade.</item>
    /// <item>Case binder entries and borrowed equipment: their keys are NoAction too, and both
    /// outlive the visit (a note, a checkout record), so they are detached rather than removed.</item>
    /// <item>Files shared with the visit's team: the share goes, the file stays with its owner.</item>
    /// <item>The visit's own calendar entry goes too, when nobody is on its guest list and nothing
    /// was recorded against it. Otherwise it stays, titled as canceled.</item>
    /// </list>
    /// </remarks>
    public static async Task PrepareDeleteAsync(BenDataContext db, Investigation visit, CancellationToken ct)
    {
        var teamRows = db.InvestigationAttendees
            .Where(a => a.InvestigationId == visit.Id).Select(a => a.Id);
        db.InvestigationDutyAssignments.RemoveRange(await db.InvestigationDutyAssignments
            .Where(x => teamRows.Contains(x.InvestigationAttendeeId)).ToListAsync(ct));

        foreach (var entry in await db.CaseTimelineEntries.Where(e => e.InvestigationId == visit.Id).ToListAsync(ct))
            entry.InvestigationId = null;

        foreach (var checkout in await db.EquipmentCheckouts.Where(c => c.InvestigationId == visit.Id).ToListAsync(ct))
            checkout.InvestigationId = null;

        db.UploadFileShares.RemoveRange(await db.UploadFileShares
            .Where(s => s.TargetInvestigationId == visit.Id).ToListAsync(ct));

        if (visit.OrgCalendarEventId is not { } entryId) return;

        var calendarEntry = await db.OrgCalendarEvents.FirstOrDefaultAsync(
            e => e.Id == entryId && e.OrganizationId == visit.OrganizationId, ct);
        if (calendarEntry is null || !IsVisitsOwn(calendarEntry)) return;

        var inUse = await db.OrgCalendarEventAttendees.AnyAsync(a => a.OrgCalendarEventId == entryId, ct)
                 || await db.OrgCalendarEventGuides.AnyAsync(g => g.OrgCalendarEventId == entryId, ct)
                 || await db.FieldSessionUploads.AnyAsync(s => s.OrgCalendarEventId == entryId, ct)
                 || await db.Investigations.AnyAsync(i => i.OrgCalendarEventId == entryId && i.Id != visit.Id, ct);

        visit.OrgCalendarEventId = null;
        if (inUse)
        {
            calendarEntry.Title = Clip(CanceledPrefix + CalendarPrefix + visit.Title, 256);
            return;
        }

        db.OrgCalendarEvents.Remove(calendarEntry);
    }

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    private static string Join(List<string> parts) => parts.Count switch
    {
        1 => parts[0],
        2 => $"{parts[0]} and {parts[1]}",
        _ => $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}",
    };

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];
}
