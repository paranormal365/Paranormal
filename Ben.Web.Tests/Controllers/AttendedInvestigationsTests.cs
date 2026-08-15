using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// A person's map of where they have actually been.
/// </summary>
/// <remarks>
/// The rule is narrow on purpose: past <b>and</b> attended. Being invited is not being there, and
/// accepting an invitation is not being there either — a map that counted either would claim the
/// person had visited places they never reached. Most of these tests exist to hold that line,
/// because every one of the near-misses is a plausible thing for the query to let through.
/// </remarks>
public class AttendedInvestigationsTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid MeId = Guid.NewGuid();

    private static MyInvestigationsController Build(IDbContextFactory<BenDataContext> f, Guid? asUser = null)
        => new(f)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, (asUser ?? MeId).ToString())], "Bearer"))
                }
            }
        };

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "BenCo", UrlName = "benco", DateCreated = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return factory;
    }

    /// <summary>Adds one investigation plus my attendee row against it.</summary>
    private static async Task<Guid> AddAsync(
        IDbContextFactory<BenDataContext> factory,
        string title,
        DateTime scheduled,
        bool? didAttend,
        RsvpStatus rsvp = RsvpStatus.Invited,
        bool isLead = false,
        Guid? attendeeUserId = null,
        decimal? lat = null,
        decimal? lon = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var invId = Guid.NewGuid();

        db.Investigations.Add(new Investigation
        {
            Id = invId, OrganizationId = OrgId, Title = title, ScheduledDateTime = scheduled,
            Latitude = lat, Longitude = lon,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        db.InvestigationAttendees.Add(new InvestigationAttendee
        {
            Id = Guid.NewGuid(), InvestigationId = invId, AppUserId = attendeeUserId ?? MeId,
            DidAttend = didAttend, Rsvp = rsvp, IsLead = isLead,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return invId;
    }

    private static async Task<List<AttendedInvestigationItem>> AttendedAsync(
        IDbContextFactory<BenDataContext> factory, Guid? asUser = null)
    {
        var result = await Build(factory, asUser).GetAttended(default);
        return Assert.IsAssignableFrom<IEnumerable<AttendedInvestigationItem>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();
    }

    private static DateTime Past => DateTime.UtcNow.AddDays(-30);
    private static DateTime Future => DateTime.UtcNow.AddDays(30);

    // ── What counts ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_past_investigation_i_attended_appears()
    {
        var factory = await SeedAsync();
        await AddAsync(factory, "The one I went to", Past, didAttend: true, lat: 36.16m, lon: -86.78m);

        var attended = await AttendedAsync(factory);

        var row = Assert.Single(attended);
        Assert.Equal("The one I went to", row.Title);
        Assert.Equal(36.16m, row.Latitude);
    }

    // ── The near-misses ───────────────────────────────────────────────────────

    [Fact]
    public async Task Being_invited_is_not_being_there()
    {
        var factory = await SeedAsync();
        await AddAsync(factory, "Invited only", Past, didAttend: null);

        Assert.Empty(await AttendedAsync(factory));
    }

    [Fact]
    public async Task Accepting_an_invitation_is_not_being_there()
    {
        var factory = await SeedAsync();
        await AddAsync(factory, "Said yes, never went", Past, didAttend: null, rsvp: RsvpStatus.Accepted);

        // The most tempting shortcut, and the wrong one: an RSVP is a statement of intent made
        // beforehand, and plans change.
        Assert.Empty(await AttendedAsync(factory));
    }

    [Fact]
    public async Task Being_marked_absent_does_not_appear()
    {
        var factory = await SeedAsync();
        await AddAsync(factory, "Missed it", Past, didAttend: false);

        // DidAttend is a nullable bool, so "false" and "not yet known" are different states and a
        // truthiness check would wrongly include one of them.
        Assert.Empty(await AttendedAsync(factory));
    }

    [Fact]
    public async Task A_future_investigation_does_not_appear_even_if_marked_attended()
    {
        var factory = await SeedAsync();
        await AddAsync(factory, "Next month", Future, didAttend: true);

        // Data can say silly things. The map is a record of the past, so the date filter has to
        // hold independently of the flag rather than trusting it.
        Assert.Empty(await AttendedAsync(factory));
    }

    [Fact]
    public async Task Somebody_elses_attendance_does_not_appear_on_my_map()
    {
        var factory = await SeedAsync();
        await AddAsync(factory, "Their visit", Past, didAttend: true, attendeeUserId: Guid.NewGuid());

        Assert.Empty(await AttendedAsync(factory));
    }

    // ── What it carries ───────────────────────────────────────────────────────

    [Fact]
    public async Task Leading_a_visit_is_reported()
    {
        var factory = await SeedAsync();
        await AddAsync(factory, "I ran this one", Past, didAttend: true, isLead: true);

        Assert.True(Assert.Single(await AttendedAsync(factory)).WasLead);
    }

    [Fact]
    public async Task Newest_first()
    {
        var factory = await SeedAsync();
        await AddAsync(factory, "Older", DateTime.UtcNow.AddDays(-90), didAttend: true);
        await AddAsync(factory, "Newer", DateTime.UtcNow.AddDays(-5), didAttend: true);

        var attended = await AttendedAsync(factory);

        Assert.Equal("Newer", attended[0].Title);
        Assert.Equal("Older", attended[1].Title);
    }

    [Fact]
    public async Task A_case_less_visit_appears_with_no_case_reference()
    {
        var factory = await SeedAsync();
        await AddAsync(factory, "A landmark", Past, didAttend: true);

        var row = Assert.Single(await AttendedAsync(factory));

        // Visits with no client case are exactly the ones this map exists to remember.
        Assert.Null(row.CaseId);
        Assert.Null(row.CaseReference);
        Assert.Equal("BenCo", row.OrganizationName);
    }

    [Fact]
    public async Task Someone_with_no_attendance_gets_an_empty_map_not_an_error()
    {
        var factory = await SeedAsync();
        await AddAsync(factory, "Invited only", Past, didAttend: null);

        // The expected state for most people until arrival check-in exists. Empty and honest.
        Assert.Empty(await AttendedAsync(factory));
    }
}
