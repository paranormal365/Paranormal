using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The <see cref="Place"/> entity and the two optional links into it.
/// </summary>
/// <remarks>
/// <para>Deliberately narrow: this covers the model, not the backfill. The
/// <c>BackfillPlacesFromCases</c> migration is raw SQL and the InMemory provider never runs it, so
/// a test asserting backfilled rows would pass here while proving nothing about the database. That
/// migration was verified against the real dev database instead.</para>
///
/// <para>What is worth pinning down in code is the shape the rest of the branch will lean on: a
/// place can exist with nothing but a name, a case and an investigation can each point at one, and
/// several visits can accumulate against a single place over time — which is the whole reason the
/// entity exists rather than folding into <see cref="Case"/>.</para>
/// </remarks>
public class PlaceEntityTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static Place NamedLandmark() => new()
    {
        Id = Guid.NewGuid(),
        Name = "The Bell Witch Cave",
        City = "Adams",
        State = "TN",
        Kind = PlaceKind.PublicLocation,
        DateCreated = DateTime.UtcNow,
        CreatedByAppUserId = UserId,
    };

    [Fact]
    public async Task A_place_can_be_a_name_and_nothing_else()
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        // Case demands a street address, city, state and ZIP. A landmark has none of those, and
        // requiring them would only produce invented ones — hence every address field nullable.
        db.Places.Add(new Place
        {
            Id = Guid.NewGuid(),
            Name = "Devil's Punchbowl",
            Kind = PlaceKind.PublicLocation,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = UserId,
        });
        await db.SaveChangesAsync();

        var saved = await db.Places.SingleAsync();
        Assert.Equal("Devil's Punchbowl", saved.Name);
        Assert.Null(saved.StreetAddress1);
        Assert.Null(saved.Latitude);
    }

    [Fact]
    public void A_new_place_defaults_to_the_cautious_kind()
    {
        // The default matters: a place created without anyone stating what it is must not start
        // out with the wider sharing scope. Getting this wrong widens the scope of somebody's home.
        Assert.Equal(PlaceKind.PrivateResidence, new Place().Kind);
    }

    [Fact]
    public void The_curation_flag_starts_false_and_nothing_reads_it_yet()
    {
        // Scaffolded, documented as inert. Recorded here so that if some future write path starts
        // hardcoding it true — the ExperienceCategory mistake — the intent is at least written down.
        Assert.False(new Place().IsApproved);
    }

    [Fact]
    public async Task Several_investigations_can_accumulate_against_one_place()
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        var place = NamedLandmark();
        db.Places.Add(place);

        var orgId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = "A case", CaseYear = 2026, OrgCaseNumber = 1,
            StreetAddress1 = "1 Somewhere Rd", City = "Adams", State = "TN", ZipCode = "37010",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        });

        for (var i = 0; i < 3; i++)
        {
            db.Investigations.Add(new Investigation
            {
                Id = Guid.NewGuid(), CaseId = caseId, PlaceId = place.Id,
                Title = $"Visit {i + 1}", ScheduledDateTime = DateTime.UtcNow.AddDays(-i * 30),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
            });
        }
        await db.SaveChangesAsync();

        // This is what makes "N investigations by M groups since Y" answerable, and it is the whole
        // argument for a Place existing separately from a Case.
        var visits = await db.Investigations.CountAsync(i => i.PlaceId == place.Id);
        Assert.Equal(3, visits);
    }

    [Fact]
    public async Task A_case_and_an_investigation_can_each_stand_without_a_place()
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        var caseId = Guid.NewGuid();
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = Guid.NewGuid(), Title = "Placeless", CaseYear = 2026,
            OrgCaseNumber = 2, StreetAddress1 = "2 Elsewhere Rd", City = "Nashville", State = "TN",
            ZipCode = "37201", DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        });
        db.Investigations.Add(new Investigation
        {
            Id = Guid.NewGuid(), CaseId = caseId, Title = "No place yet",
            ScheduledDateTime = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = UserId,
        });
        await db.SaveChangesAsync();

        // Both links are nullable on purpose. A case created before this branch existed has no
        // place until the backfill or an edit gives it one, and P2's case-less investigations
        // depend on the same nullability from the other direction.
        Assert.Null((await db.Cases.SingleAsync()).PlaceId);
        Assert.Null((await db.Investigations.SingleAsync()).PlaceId);
    }

    [Fact]
    public async Task A_case_keeps_its_own_address_when_it_gains_a_place()
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        var place = NamedLandmark();
        place.StreetAddress1 = "Corrected By Another Org";
        db.Places.Add(place);
        db.Cases.Add(new Case
        {
            Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), Title = "Reported", CaseYear = 2026,
            OrgCaseNumber = 3, StreetAddress1 = "As The Client Reported It", City = "Adams",
            State = "TN", ZipCode = "37010", PlaceId = place.Id,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        });
        await db.SaveChangesAsync();

        // The case address is the record of what the client actually said. A shared place that
        // another organization may later correct must never rewrite it.
        var saved = await db.Cases.SingleAsync();
        Assert.Equal("As The Client Reported It", saved.StreetAddress1);
        Assert.Equal(place.Id, saved.PlaceId);
    }
}
