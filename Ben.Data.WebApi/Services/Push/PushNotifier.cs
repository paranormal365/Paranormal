using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Push;

/// <summary>How a fan-out went, in the terms a lead is told.</summary>
/// <param name="People">How many people it was for.</param>
/// <param name="PeopleWithTheApp">How many of them have a phone registered — "signed in on the app".</param>
/// <param name="Delivered">Phones Apple accepted it for.</param>
/// <param name="Failed">Phones it could not be sent to this time.</param>
/// <param name="Forgotten">Dead tokens Apple told us to drop.</param>
/// <param name="Configured">False when this server has no APNs key, so nothing was sent.</param>
public sealed record PushFanOut(int People, int PeopleWithTheApp, int Delivered, int Failed, int Forgotten, bool Configured);

/// <summary>
/// Sends one push to every phone a set of people has registered, and forgets the ones Apple says
/// are gone (item 252).
/// </summary>
public sealed class PushNotifier
{
    /// <summary>Apple takes many streams on one connection; a few at a time keeps a large tour quick without flooding it.</summary>
    public const int Concurrency = 8;

    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IPushSender _sender;
    private readonly ILogger<PushNotifier> _log;

    public PushNotifier(IDbContextFactory<BenDataContext> db, IPushSender sender, ILogger<PushNotifier> log)
    {
        _db = db;
        _sender = sender;
        _log = log;
    }

    public async Task<PushFanOut> SendAsync(IReadOnlyCollection<Guid> appUserIds, PushMessage message, CancellationToken ct)
    {
        var people = appUserIds.Distinct().ToList();
        await using var db = await _db.CreateDbContextAsync(ct);
        var devices = await db.PushDevices.AsNoTracking()
            .Where(d => people.Contains(d.AppUserId))
            .Select(d => new { d.Id, d.AppUserId, d.Token, d.Environment })
            .ToListAsync(ct);
        var withTheApp = devices.Select(d => d.AppUserId).Distinct().Count();

        if (!_sender.IsConfigured)
        {
            _log.LogInformation("Push not configured: {Phones} phone(s) for {People} people were not sent to", devices.Count, people.Count);
            return new PushFanOut(people.Count, withTheApp, 0, 0, 0, Configured: false);
        }

        var outcomes = new System.Collections.Concurrent.ConcurrentBag<(Guid DeviceId, PushOutcome Outcome)>();
        await Parallel.ForEachAsync(devices, new ParallelOptions { MaxDegreeOfParallelism = Concurrency, CancellationToken = ct },
            async (device, token) =>
                outcomes.Add((device.Id, await _sender.SendAsync(device.Token, device.Environment, message, token))));

        var dead = outcomes.Where(o => o.Outcome == PushOutcome.Unregistered).Select(o => o.DeviceId).ToList();
        if (dead.Count > 0)
        {
            db.PushDevices.RemoveRange(await db.PushDevices.Where(d => dead.Contains(d.Id)).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
        }

        var fanOut = new PushFanOut(people.Count, withTheApp,
            outcomes.Count(o => o.Outcome == PushOutcome.Delivered),
            outcomes.Count(o => o.Outcome == PushOutcome.Failed),
            dead.Count, Configured: true);
        _log.LogInformation("Push fan-out: {People} people, {WithApp} with the app, {Delivered} delivered, {Failed} failed, {Forgotten} tokens forgotten",
            fanOut.People, fanOut.PeopleWithTheApp, fanOut.Delivered, fanOut.Failed, fanOut.Forgotten);
        return fanOut;
    }
}
