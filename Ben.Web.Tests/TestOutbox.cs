using Ben.Data.Common;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ben.Web.Tests;

/// <summary>
/// The outbox as production wires it, for the tests about whether a letter commits with what it
/// is about (item 239b).
/// </summary>
/// <remarks>
/// <para><b>One instance behind both interfaces</b>, exactly as <c>Program.cs</c> registers it: as
/// <c>IEmailService</c> for the callers that send and as <c>IOutboxEmailQueue</c> for the callers
/// that queue. That matters for a test meant to fail against the old code — the old path went
/// through <c>IEmailService.SendAsync</c>, and it must meet the same outbox, swallowing the same
/// failure, that it met in production.</para>
///
/// <para><b>Configured</b>, because the outbox reports what the SMTP service it wraps reports, and
/// every caller asks before it writes a letter. The SMTP service never sends anything here: the
/// outbox only queues, and the sender job that would read the queue is not running.</para>
/// </remarks>
internal static class TestOutbox
{
    public static OutboxEmailService Real(IDbContextFactory<BenDataContext> factory)
        => Build(factory, "smtp.test.invalid");

    /// <summary>
    /// The outbox on a machine with no mail server — every developer's, and the browser tests'
    /// hosts (appsettings.Development.json sets Smtp:Host to null).
    /// </summary>
    /// <remarks>
    /// Letters still queue here; nothing reports them sent. The tests using it hold both halves of
    /// that at once (2026-09-23).
    /// </remarks>
    public static OutboxEmailService WithoutMail(IDbContextFactory<BenDataContext> factory)
        => Build(factory, host: null);

    private static OutboxEmailService Build(IDbContextFactory<BenDataContext> factory, string? host)
    {
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" });
        var smtp = new SmtpEmailService(Options.Create(new SmtpOptions { Host = host }), site);
        return new OutboxEmailService(
            factory, smtp, site,
            new MailComposer(factory, new MemoryCache(new MemoryCacheOptions()), site,
                             NullLogger<MailComposer>.Instance),
            NullLogger<OutboxEmailService>.Instance);
    }
}

/// <summary>
/// Refuses any save that would write a letter to the outbox, while <see cref="Refusing"/> is on.
/// </summary>
/// <remarks>
/// <para><b>The failure the 239b tests inject</b>: the outbox table taking nothing, everything else
/// working. It distinguishes the two designs cleanly. Where the letter is written by its own save
/// after the thing it is about has committed, the refusal lands on that second save, which the
/// outbox swallows — and the invite, token and all, is left behind with no letter. Where the
/// letter is in the same write, the whole write is refused and nothing is left behind.</para>
///
/// <para>Switchable, so a test can create the first invitation normally and then refuse the
/// second.</para>
/// </remarks>
internal sealed class RefuseOutboxWrites : SaveChangesInterceptor
{
    public bool Refusing { get; set; }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Refuse(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Refuse(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Refuse(DbContext? db)
    {
        if (Refusing && db is not null
            && db.ChangeTracker.Entries<OutboxEmail>().Any(e => e.State == EntityState.Added))
            throw new InvalidOperationException("The outbox table refused the letter.");
    }
}
