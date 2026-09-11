using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Tours;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The mail one guest gets about one tour date, with the walk attached (item 233).
/// </summary>
/// <remarks>
/// The rule this suite is really about is the one that keeps the feature from spreading: the
/// mailer answers for a date that belongs to a TOUR and says nothing about anything else, so an
/// investigation group's open evening keeps the mail it has always had.
/// </remarks>
public sealed class TourGuestMailerTests
{
    private sealed class FakeEmail : IEmailService
    {
        public readonly List<EmailMessage> Sent = [];
        public bool Configured = true;
        public bool Throws;

        public bool IsConfigured => Configured;

        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
            => SendAsync(new EmailMessage(to, subject, htmlBody), ct);

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (Throws) throw new InvalidOperationException("the relay refused");
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> Db()
        => new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static TourGuestMailer Mailer(FakeEmail email)
        => new(email,
               Options.Create(new Ben.Data.Common.SiteIdentity
               {
                   Name = "IsHaunted.com", BaseUrl = "https://ishaunted.com",
               }),
               NullLogger<TourGuestMailer>.Instance);

    private sealed record World(IDbContextFactory<BenDataContext> Factory, Guid TourEventId, Guid PlainEventId);

    private static async Task<World> SeedAsync(
        string? subjectTemplate = null, string? bodyTemplate = null,
        bool guidePhoto = true, string? contact = "Cash on the night.")
    {
        var factory = Db();
        var now = DateTime.UtcNow;
        Guid orgId = Guid.NewGuid(), guideId = Guid.NewGuid(), addressId = Guid.NewGuid(),
             tourId = Guid.NewGuid(), tourEventId = Guid.NewGuid(), plainEventId = Guid.NewGuid(),
             fileId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser
        {
            Id = guideId, UserName = "gale", Email = "gale@t.com", DisplayName = "Gale", DateCreated = now,
        });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Printers Alley Walks", UrlName = "paw",
            Kind = OrganizationKind.GhostWalkingTour, PublicEmail = "walks@example.com",
            DateCreated = now, CreatedByAppUserId = guideId,
        });
        db.OrganizationAddresses.Add(new OrganizationAddress
        {
            Id = addressId, OrganizationId = orgId, OrganizationAddressTypeId = Guid.NewGuid(),
            StreetAddress1 = "1 Printers Alley", City = "Nashville", State = "TN", ZipCode = "37201",
            Country = "US", Latitude = 36.1628568m, Longitude = -86.7773829m,
            DateCreated = now, CreatedByAppUserId = guideId,
        });
        db.Tours.Add(new Tour
        {
            Id = tourId, OrganizationId = orgId, Name = "Printers Alley Ghost Walk",
            UrlName = "printers-alley-ghost-walk", StartOrganizationAddressId = addressId,
            DurationMinutes = 90, DefaultCapacity = 20, TimeZoneId = "America/Chicago",
            ContactLine = contact, MailSubjectTemplate = subjectTemplate, MailBodyTemplate = bodyTemplate,
            DateCreated = now, CreatedByAppUserId = guideId,
        });
        db.OrgCalendarEvents.AddRange(
            new OrgCalendarEvent
            {
                Id = tourEventId, OrganizationId = orgId, TourId = tourId, Title = "Saturday walk",
                StartDateTime = new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc),
                EndDateTime = new DateTime(2026, 9, 13, 1, 30, 0, DateTimeKind.Utc),
                IsPublic = true, AttendeeCapacity = 20, UrlName = "2026-09-12-saturday-walk",
                OrganizationAddressId = addressId, DateCreated = now, CreatedByAppUserId = guideId,
            },
            new OrgCalendarEvent
            {
                Id = plainEventId, OrganizationId = orgId, Title = "Members' meeting",
                StartDateTime = now.AddDays(2), EndDateTime = now.AddDays(2).AddHours(1),
                IsPublic = true, DateCreated = now, CreatedByAppUserId = guideId,
            });
        db.OrgCalendarEventGuides.Add(new OrgCalendarEventGuide
        {
            Id = Guid.NewGuid(), OrgCalendarEventId = tourEventId, AppUserId = guideId,
            SortOrder = 0, DateCreated = now, CreatedByAppUserId = guideId,
        });
        if (guidePhoto)
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), FileName = "gale.jpg",
                StoredFileName = "gale.jpg", ContentType = "image/jpeg", FileSize = 1,
                DateCreated = now, CreatedByAppUserId = guideId,
            });
            db.AppUserPhotos.Add(new AppUserPhoto
            {
                Id = Guid.NewGuid(), AppUserId = guideId, UploadFileId = fileId,
                IsPublic = true, IsActive = true, DateCreated = now, CreatedByAppUserId = guideId,
            });
        }
        await db.SaveChangesAsync();

        return new World(factory, tourEventId, plainEventId);
    }

    // ── what a guest receives ────────────────────────────────────────────────

    [Fact]
    public async Task A_sign_up_carries_the_walk_as_a_calendar_file()
    {
        var w = await SeedAsync();
        var email = new FakeEmail();
        await using var db = await w.Factory.CreateDbContextAsync();

        Assert.True(await Mailer(email).SendSignUpAsync(db, w.TourEventId, "ada@example.com", "Ada", default));

        var sent = Assert.Single(email.Sent);
        var attachment = Assert.Single(sent.Attachments!);
        Assert.Equal("tour.ics", attachment.FileName);
        Assert.Contains("text/calendar", attachment.ContentType);

        var ics = System.Text.Encoding.UTF8.GetString(attachment.Content);
        // The date's own id, so a later reminder updates the diary rather than duplicating it.
        Assert.Contains($"UID:{w.TourEventId}@ishaunted.com", ics);
        Assert.Contains("DTSTART:20260913T000000Z", ics);
        Assert.Contains(@"LOCATION:1 Printers Alley\, Nashville\, TN\, 37201", ics);
        Assert.Contains("GEO:36.162857;-86.777383", ics);
    }

    [Fact]
    public async Task It_says_where_when_who_and_how_to_pay()
    {
        var w = await SeedAsync();
        var email = new FakeEmail();
        await using var db = await w.Factory.CreateDbContextAsync();

        await Mailer(email).SendSignUpAsync(db, w.TourEventId, "ada@example.com", "Ada", default);

        var body = Assert.Single(email.Sent).HtmlBody;
        Assert.Contains("1 Printers Alley", body);                       // where
        Assert.Contains("7:00 PM CDT", body);                            // when, in the tour's zone
        Assert.Contains("Gale", body);                                   // who
        Assert.Contains("Cash on the night", body);                      // how to pay
        Assert.Contains("/media/guide-photo/", body);                    // the face, absolute
        Assert.Contains("https://ishaunted.com/media/guide-photo/", body);
    }

    [Fact]
    public async Task A_reply_reaches_the_business_not_the_site()
    {
        var w = await SeedAsync();
        var email = new FakeEmail();
        await using var db = await w.Factory.CreateDbContextAsync();

        await Mailer(email).SendSignUpAsync(db, w.TourEventId, "ada@example.com", "Ada", default);

        Assert.Equal("walks@example.com", Assert.Single(email.Sent).ReplyTo);
    }

    [Fact]
    public async Task The_business_own_wording_is_used_when_it_wrote_some()
    {
        var w = await SeedAsync(
            subjectTemplate: "Your walk with {{business.name}}",
            bodyTemplate: "<p>{{guest.name}}, meet us at {{tour.meetingPoint}} at {{date.start}}.</p>");
        var email = new FakeEmail();
        await using var db = await w.Factory.CreateDbContextAsync();

        await Mailer(email).SendSignUpAsync(db, w.TourEventId, "ada@example.com", "Ada", default);

        var sent = Assert.Single(email.Sent);
        Assert.Equal("Your walk with Printers Alley Walks", sent.Subject);
        Assert.Contains("Ada, meet us at 1 Printers Alley", sent.HtmlBody);
    }

    [Fact]
    public async Task The_reminder_is_the_same_mail_and_says_tomorrow()
    {
        var w = await SeedAsync();
        var email = new FakeEmail();
        await using var db = await w.Factory.CreateDbContextAsync();

        Assert.True(await Mailer(email).SendReminderAsync(db, w.TourEventId, "ada@example.com", "Ada", default));

        var sent = Assert.Single(email.Sent);
        Assert.StartsWith("Tomorrow: ", sent.Subject);
        Assert.Single(sent.Attachments!);
    }

    // ── what it leaves alone ─────────────────────────────────────────────────

    [Fact]
    public async Task An_event_that_is_not_a_tours_gets_nothing_from_this_mailer()
    {
        // The whole containment rule: a group's members' meeting keeps the mail it always had,
        // and the caller is told nothing was sent so it can fall back.
        var w = await SeedAsync();
        var email = new FakeEmail();
        await using var db = await w.Factory.CreateDbContextAsync();

        Assert.False(await Mailer(email).SendSignUpAsync(db, w.PlainEventId, "ada@example.com", "Ada", default));
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task Nothing_is_sent_when_email_is_not_configured()
    {
        var w = await SeedAsync();
        var email = new FakeEmail { Configured = false };
        await using var db = await w.Factory.CreateDbContextAsync();

        Assert.False(await Mailer(email).SendSignUpAsync(db, w.TourEventId, "ada@example.com", "Ada", default));
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task A_refused_relay_never_becomes_the_guests_problem()
    {
        // A guest who is on the list but whose mail bounced is a guest on the list.
        var w = await SeedAsync();
        var email = new FakeEmail { Throws = true };
        await using var db = await w.Factory.CreateDbContextAsync();

        Assert.False(await Mailer(email).SendSignUpAsync(db, w.TourEventId, "ada@example.com", "Ada", default));
    }

    [Fact]
    public async Task An_empty_address_is_not_sent_to()
    {
        var w = await SeedAsync();
        var email = new FakeEmail();
        await using var db = await w.Factory.CreateDbContextAsync();

        Assert.False(await Mailer(email).SendSignUpAsync(db, w.TourEventId, "", "Ada", default));
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task A_guide_with_no_published_photograph_is_still_named()
    {
        var w = await SeedAsync(guidePhoto: false);
        var email = new FakeEmail();
        await using var db = await w.Factory.CreateDbContextAsync();

        await Mailer(email).SendSignUpAsync(db, w.TourEventId, "ada@example.com", "Ada", default);

        var body = Assert.Single(email.Sent).HtmlBody;
        Assert.Contains("Gale", body);
        Assert.DoesNotContain("<img", body);
    }
}
