using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Whether a session sent to a hosted event may be played by anybody, as part of the event's results
/// (event-scenario-gaps; Ben's scenario: "On the site, they can see the app and all the sessions will run in
/// the session player. Any EVPs will be displayed").
/// </summary>
/// <remarks>
/// <para><b>Three yeses, asked on every request.</b> The sender's (they were told only the group would see
/// it), the organizer's (who curates the results, as with evidence), and the event's own standing: on the
/// public site, taking evidence, at a public place. Withdrawing any of them takes the session off at once,
/// with nothing to remember. A session filed with an investigation is never shown this way: that is the
/// investigation's group's to publish.</para>
///
/// <para><b>Media as the archive serves it.</b> Recordings only once the media is approved, positions only
/// as the place's public point. This is the same player the place archive uses; it never puts the session in
/// the place's archive, which stays the sender's own act.</para>
/// </remarks>
public static class EventSessionPublication
{
    private static readonly HostedEventLifecycleState[] Showing =
        [HostedEventLifecycleState.Published, HostedEventLifecycleState.Live, HostedEventLifecycleState.Ended];

    /// <summary>Sessions shown with a hosted event's results right now.</summary>
    public static IQueryable<FieldSessionUpload> Shown(BenDataContext db)
        => db.FieldSessionUploads.AsNoTracking()
            .Where(s => s.HostedEventId != null && s.InvestigationId == null
                     && s.EventResultsConsentAtUtc != null && s.ShownWithEventResultsAtUtc != null
                     && db.HostedEvents.Any(h => h.Id == s.HostedEventId && h.CollectsEvidence
                                              && Showing.Contains(h.LifecycleState)
                                              && h.Place.Kind == PlaceKind.PublicLocation));

    /// <summary>Why the organizer can't show this session with the results now, or null when they can.</summary>
    public static string? WhyNotShown(FieldSessionUpload session, HostedEvent? hosted)
    {
        if (hosted is null) return "This session wasn't sent to one of your events.";
        if (session.InvestigationId is not null)
            return "This session was filed with an investigation, so it's that investigation's to publish.";
        if (session.EventResultsConsentAtUtc is null)
            return "The person who sent it hasn't agreed to it being shown. They can, from their sessions on the website.";
        if (!hosted.CollectsEvidence) return "This event isn't taking evidence, so it has no results to show it with.";
        if (hosted.Place?.Kind != PlaceKind.PublicLocation)
            return "This event isn't at a public place, so its sessions stay with the group.";
        if (!Showing.Contains(hosted.LifecycleState))
            return "The event isn't on the public site, so nothing of it is shown.";
        return null;
    }
}
