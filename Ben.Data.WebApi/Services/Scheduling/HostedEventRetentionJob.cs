using Ben.Data.Common;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Warns an organizer a month and a week before an event's files go, then removes them (item 235 phase 12).
/// </summary>
/// <remarks>
/// <para><b>Each step once, one step per pass,</b> guarded by its own stamp, like the lifecycle job. An event
/// the job first sees after its date is still warned a week ahead rather than cleared on the spot: the clearing
/// waits until a week after whichever warning went last, so nobody loses files on a letter they never got.</para>
///
/// <para><b>An event with nothing to lose is stamped without a letter.</b> A warning about nothing is the
/// letter that teaches somebody to ignore the next one.</para>
/// </remarks>
public sealed class HostedEventRetentionJob : IScheduledJob
{
    public string Name => "hosted-event-retention";

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IEmailService _email;
    private readonly PlatformMessageService _messages;
    private readonly IMediaIngestService _ingest;
    private readonly SiteIdentity _site;
    private readonly ILogger<HostedEventRetentionJob> _logger;

    public HostedEventRetentionJob(
        IDbContextFactory<BenDataContext> dbFactory, IEmailService email, PlatformMessageService messages,
        IMediaIngestService ingest, IOptions<SiteIdentity> site, ILogger<HostedEventRetentionJob> logger)
    {
        _dbFactory = dbFactory;
        _email = email;
        _messages = messages;
        _ingest = ingest;
        _site = site.Value;
        _logger = logger;
    }

    public Task RunAsync(CancellationToken ct) => RunAtAsync(DateTime.UtcNow, ct);

    internal async Task RunAtAsync(DateTime now, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var days = await EventRetention.DaysAsync(db, ct);

        // Everything within a month of its date, or past it.
        var horizon = now.Date.AddDays(-(days - 31));
        var due = await db.HostedEvents
            .Where(e => e.MediaClearedUtc == null
                     && EventRetention.Applies.Contains(e.LifecycleState)
                     && e.EndsOn <= horizon)
            .ToListAsync(ct);

        foreach (var ev in due)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await StepAsync(db, ev, days, now, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogError(e, "Could not apply the retention rule to {EventId}.", ev.Id);
            }
        }
    }

    private async Task StepAsync(BenDataContext db, HostedEvent ev, int days, DateTime now, CancellationToken ct)
    {
        var clearsOn = EventRetention.ClearsOn(ev, days);
        var holding = await EventRetention.HoldingAsync(db, ev.Id, ct);

        if (holding.IsEmpty)
        {
            if (now >= clearsOn)
            {
                ev.MediaClearedUtc = now;
                await db.SaveChangesAsync(ct);
            }
            return;
        }

        // Never clear within a week of the last warning, whenever the warnings happened to go.
        var lastWarned = ev.RetentionWarnedWeekUtc ?? ev.RetentionWarnedMonthUtc;
        var clearAt = lastWarned is { } warned && warned.AddDays(7) > clearsOn ? warned.AddDays(7) : clearsOn;

        if (ev.RetentionWarnedWeekUtc is not null && now >= clearAt)
        {
            var paths = await EventRetention.ClearAsync(db, ev, now, ct);
            foreach (var path in paths)
                await _ingest.DeleteAllAsync(path, CancellationToken.None);
            _logger.LogInformation("Removed the files of hosted event {EventId} under the retention rule.", ev.Id);
            return;
        }

        if (ev.RetentionWarnedWeekUtc is null && now >= clearsOn.AddDays(-7))
        {
            await WarnAsync(db, ev, holding, clearsOn > now.AddDays(7) ? clearsOn : now.AddDays(7), ct);
            ev.RetentionWarnedWeekUtc = now;
            ev.RetentionWarnedMonthUtc ??= now;
            await db.SaveChangesAsync(ct);
            return;
        }

        if (ev.RetentionWarnedMonthUtc is null && now >= clearsOn.AddDays(-30))
        {
            await WarnAsync(db, ev, holding, clearsOn, ct);
            ev.RetentionWarnedMonthUtc = now;
            await db.SaveChangesAsync(ct);
        }
    }

    internal static (string Subject, string Body) Letter(HostedEvent ev, EventRetention.Holding holding, DateTime on, string keepUrl)
    {
        var parts = new List<string>();
        if (holding.Files > 0) parts.Add($"{holding.Files} {(holding.Files == 1 ? "file" : "files")}");
        if (holding.Pictures > 0) parts.Add($"{holding.Pictures} gallery {(holding.Pictures == 1 ? "picture" : "pictures")}");
        if (holding.Photos > 0) parts.Add($"{holding.Photos} {(holding.Photos == 1 ? "photo" : "photos")} in its room");

        return ($"{ev.Name}: its files are removed on {on:MM/dd/yyyy}",
            $"{ev.Name} ended on {ev.EndsOn:MM/dd/yyyy}, and its {string.Join(", ", parts)} will be removed on "
            + $"{on:MM/dd/yyyy}. Pick anything you want to keep and download it as one zip: {keepUrl} . "
            + "The event itself, its bookings and its reviews stay, and guests keep their own photos.");
    }

    private async Task WarnAsync(BenDataContext db, HostedEvent ev, EventRetention.Holding holding, DateTime on, CancellationToken ct)
    {
        var keep = _site.AbsoluteUrl($"/organizations/{ev.OrganizationId}/events/{ev.Id}/keep");
        var (subject, body) = Letter(ev, holding, on, keep);

        var ids = await _messages.BillingRecipientsAsync(ev.OrganizationId, ct);
        if (ids.Count == 0) return;

        await _messages.SendAsync(subject, body, ids, ev.CreatedByAppUserId, ct);
        if (!_email.IsConfigured) return;

        var addresses = await db.AppUsers.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.Email != null).Select(u => u.Email!).ToListAsync(ct);
        foreach (var address in addresses)
        {
            try
            {
                await _email.SendAsync(address, $"{_site.Name}: {subject}",
                    $"<p>{NotificationText.Safe(body).Replace(keep, $"<a href=\"{keep}\">{keep}</a>")}</p>", ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogWarning(e, "Could not email {Address} about the files of {EventId}.", address, ev.Id);
            }
        }
    }
}
