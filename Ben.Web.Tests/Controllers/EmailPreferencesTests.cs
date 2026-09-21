using Ben.Data.Common;
using Ben.Data.Common.Interfaces;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Which letters somebody wants, and the ones that are not a choice.
/// </summary>
/// <remarks>
/// <para>Item 246 gave nine notices a letter as well as a bell, and there was no way to decline
/// any of them. This is the other half: a person may say no, and the rule is enforced where every
/// letter passes rather than at each mailer — because a preference honoured by the mailers
/// somebody remembered is a preference that fails on the one they forgot, which is the letter
/// that makes a person stop trusting the switch.</para>
///
/// <para><b>The letters that cannot be declined are the load-bearing half.</b> A screen offering
/// to switch off a password reset would be a defect wearing a feature's clothes, so the send path
/// checks the kind's own flag before it checks anybody's preferences.</para>
/// </remarks>
public sealed class EmailPreferencesTests
{
    private static readonly Guid Person = Guid.NewGuid();
    private const string Address = "casey@example.test";

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync(params string[] declined)
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();

        db.AppUsers.Add(new AppUser
        {
            Id = Person, Email = Address, UserName = Address,
            DisplayName = "Casey", DateCreated = DateTime.UtcNow,
        });

        foreach (var kind in declined)
        {
            db.UserEmailOptOuts.Add(new UserEmailOptOut
            {
                Id = Guid.NewGuid(), AppUserId = Person, Kind = kind,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Person,
            });
        }

        await db.SaveChangesAsync();
        return factory;
    }

    private static OutboxEmailService Outbox(IDbContextFactory<BenDataContext> factory)
    {
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com" });
        return new OutboxEmailService(
            factory, sender: null!, site,
            new MailComposer(factory, new MemoryCache(new MemoryCacheOptions()), site,
                             NullLogger<MailComposer>.Instance),
            NullLogger<OutboxEmailService>.Instance);
    }

    private static async Task<int> QueuedAsync(IDbContextFactory<BenDataContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.OutboxEmails.CountAsync();
    }

    [Fact]
    public async Task A_letter_nobody_declined_is_sent()
    {
        var factory = await SeedAsync();

        await Outbox(factory).SendAsync(new EmailMessage(
            Address, "Cancelled: Ovilus class", "<p>It is off.</p>",
            Kind: MailKinds.SessionCancelled.Key));

        Assert.Equal(1, await QueuedAsync(factory));
    }

    [Fact]
    public async Task A_letter_somebody_declined_is_not_sent()
    {
        var factory = await SeedAsync(MailKinds.SessionCancelled.Key);

        await Outbox(factory).SendAsync(new EmailMessage(
            Address, "Cancelled: Ovilus class", "<p>It is off.</p>",
            Kind: MailKinds.SessionCancelled.Key));

        Assert.Equal(0, await QueuedAsync(factory));
    }

    /// <summary>
    /// Declining one letter declines one letter.
    /// </summary>
    [Fact]
    public async Task Declining_one_letter_does_not_silence_the_others()
    {
        var factory = await SeedAsync(MailKinds.SessionCancelled.Key);

        await Outbox(factory).SendAsync(new EmailMessage(
            Address, "Changed: Ovilus class", "<p>It moved.</p>",
            Kind: MailKinds.SessionMoved.Key));

        Assert.Equal(1, await QueuedAsync(factory));
    }

    /// <summary>
    /// The load-bearing one: a letter that cannot be declined is sent even with a row saying
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// The screen never offers these, but this is the rule rather than the screen's good manners.
    /// A stray row — a hand-written insert, a kind that stops being optional — must not be able to
    /// stop somebody resetting their password.
    /// </remarks>
    [Fact]
    public async Task A_letter_that_cannot_be_declined_is_sent_anyway()
    {
        Assert.False(MailKinds.ResetYourPassword.CanDecline,
            "if this ever becomes declinable, somebody can lock themselves out by switching it off");

        var factory = await SeedAsync(MailKinds.ResetYourPassword.Key);

        await Outbox(factory).SendAsync(new EmailMessage(
            Address, "Reset your password", "<p>Here is the link.</p>",
            Kind: MailKinds.ResetYourPassword.Key));

        Assert.Equal(1, await QueuedAsync(factory));
    }

    /// <summary>Somebody else's refusal is not yours.</summary>
    [Fact]
    public async Task One_persons_choice_does_not_reach_another()
    {
        var factory = await SeedAsync(MailKinds.SessionCancelled.Key);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser
            {
                Id = Guid.NewGuid(), Email = "other@example.test", UserName = "other@example.test",
                DisplayName = "Other", DateCreated = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await Outbox(factory).SendAsync(new EmailMessage(
            "other@example.test", "Cancelled: Ovilus class", "<p>It is off.</p>",
            Kind: MailKinds.SessionCancelled.Key));

        Assert.Equal(1, await QueuedAsync(factory));
    }

    /// <summary>
    /// The essential letters are named, so making one optional is a decision somebody takes.
    /// </summary>
    /// <remarks>
    /// Without this, a letter becomes declinable by somebody adding one argument, and the first
    /// anybody hears of it is a person who cannot get back into their account.
    /// </remarks>
    [Fact]
    public void The_letters_that_get_you_into_your_account_are_never_optional()
    {
        foreach (var essential in new[]
                 {
                     MailKinds.ConfirmYourAddress,
                     MailKinds.ResetYourPassword,
                     MailKinds.SomebodyUsedYourAddress,
                     MailKinds.PaymentReceipt,
                 })
        {
            Assert.False(essential.CanDecline,
                $"{essential.Key} must not be something a person can switch off");
        }
    }
}
