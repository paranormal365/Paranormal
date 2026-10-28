using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.FieldSessions;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.FieldSessions;

/// <summary>Names the investigation, event, place and group a session belongs to (10/28/2026).</summary>
public static class FieldSessionBelongings
{
    public static async Task<FieldSessionBelonging?> ForAsync(BenDataContext db, FieldSessionUpload session, CancellationToken ct)
    {
        Guid? groupId = null;
        string? investigation = null, eventTitle = null, eventUrl = null, placeName = null;
        DateTime? eventStarts = null;
        var hosted = false;

        if (session.InvestigationId is { } investigationId
            && await db.Investigations.AsNoTracking().Where(i => i.Id == investigationId)
                   .Select(i => new { i.Title, i.OrganizationId }).FirstOrDefaultAsync(ct) is { } inv)
        {
            investigation = inv.Title;
            groupId = inv.OrganizationId;
        }

        if (session.HostedEventId is { } hostedId
            && await db.HostedEvents.AsNoTracking().Where(h => h.Id == hostedId)
                   .Select(h => new { h.Name, h.UrlName, h.StartsOn, h.OrganizationId, Org = h.Organization.UrlName, h.LifecycleState })
                   .FirstOrDefaultAsync(ct) is { } ev)
        {
            hosted = true;
            eventTitle = ev.Name;
            eventStarts = ev.StartsOn;
            // The event's own rule for whether it is on the public site, asked of its state.
            eventUrl = new HostedEvent { LifecycleState = ev.LifecycleState }.IsOnThePublicSite
                ? $"/o/{ev.Org}/events/{ev.UrlName}" : null;
            groupId ??= ev.OrganizationId;
        }
        else if (session.OrgCalendarEventId is { } calendarId
                 && await db.OrgCalendarEvents.AsNoTracking().Where(c => c.Id == calendarId)
                        .Select(c => new
                        {
                            c.Title, c.StartDateTime, c.OrganizationId, Org = c.Organization.UrlName,
                            Tour = c.TourId == null ? null : db.Tours.Where(t => t.Id == c.TourId).Select(t => t.UrlName).FirstOrDefault(),
                        })
                        .FirstOrDefaultAsync(ct) is { } date)
        {
            eventTitle = date.Title;
            eventStarts = date.StartDateTime;
            eventUrl = date.Tour is { Length: > 0 } tour ? $"/o/{date.Org}/tours/{tour}" : null;
            groupId ??= date.OrganizationId;
        }

        if (session.PlaceId is { } placeId)
            placeName = await db.Places.AsNoTracking().Where(p => p.Id == placeId).Select(p => p.Name).FirstOrDefaultAsync(ct);

        var group = groupId is { } orgId
            ? await db.Organizations.AsNoTracking().Where(o => o.Id == orgId)
                  .Select(o => new { o.Name, o.UrlName }).FirstOrDefaultAsync(ct)
            : null;

        if (group is null && investigation is null && eventTitle is null && placeName is null && session.FieldLaunchId is null)
            return null;
        return new FieldSessionBelonging(
            group?.Name, group?.UrlName, investigation, eventTitle, eventStarts, eventUrl, hosted,
            placeName, session.PlaceId, session.FieldLaunchId is not null);
    }
}
