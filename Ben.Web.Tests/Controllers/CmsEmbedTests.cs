using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Embedding a group's own cases and investigations in its public pages (backlog item #80, part 4).
/// </summary>
/// <remarks>
/// <para>Ben's requirement: both safeguards enforced <b>server-side, before the data leaves the
/// WebApi</b>. A case is somebody's home, so this is the point in the CMS where a mistake publishes
/// an address rather than an ugly page.</para>
///
/// <para>The tests lean positive, as they should: the interesting question is not whether a bad
/// request is refused, but whether a group publishing its own work in good faith gets a page that
/// says what they meant and nothing more. The structural checks at the end are the strongest thing
/// here — a projection with no field for an address cannot leak one however the code around it is
/// rewritten.</para>
/// </remarks>
public sealed class CmsEmbedTests
{
    private static readonly Guid OrgId      = Guid.NewGuid();
    private static readonly Guid OtherOrgId = Guid.NewGuid();
    private static readonly Guid UserId     = Guid.NewGuid();

    // A real address, so a leak would be unmistakable in an assertion.
    private const decimal TrueLatitude  = 36.1627m;
    private const decimal TrueLongitude = -86.7816m;

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory,
        Guid PublicInvestigationId,
        Guid PrivateInvestigationId,
        Guid OtherOrgInvestigationId,
        Guid PublicCaseId,
        Guid PrivateCaseId);

    private static async Task<World> SeedAsync()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();

        var placeId = Guid.NewGuid();
        db.Places.Add(new Place
        {
            Id = placeId, Name = "The Old Mill", Kind = PlaceKind.PrivateResidence,
            StreetAddress1 = "42 Elm Street", City = "Nashville", State = "TN",
            Latitude = TrueLatitude, Longitude = TrueLongitude,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        });

        Guid AddInvestigation(Guid orgId, string title, InvestigationVisibility visibility)
        {
            var id = Guid.NewGuid();
            db.Investigations.Add(new Investigation
            {
                Id = id, OrganizationId = orgId, Title = title, PlaceId = placeId,
                Visibility = visibility, Notes = "What we found.",
                ScheduledDateTime = DateTime.UtcNow.AddDays(-30),
                UrlName = "2026-07-18-" + title.ToLowerInvariant().Replace(' ', '-'),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
            });
            return id;
        }

        Guid AddCase(Guid orgId, string title, bool isPublic)
        {
            var id = Guid.NewGuid();
            db.Cases.Add(new Case
            {
                Id = id, OrganizationId = orgId, Title = title,
                Description = "A summary.",
                StreetAddress1 = "42 Elm Street", City = "Nashville", State = "TN",
                ZipCode = "37201", Latitude = TrueLatitude, Longitude = TrueLongitude,
                IsPublic = isPublic,
                Status = isPublic ? CaseStatus.Public : CaseStatus.Active,
                ClientDisplayAlias = "The Hollow Oak Family",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
            });
            return id;
        }

        var world = new World(
            factory,
            AddInvestigation(OrgId, "The Mill Vigil", InvestigationVisibility.Public),
            AddInvestigation(OrgId, "A Quiet Night", InvestigationVisibility.GroupOnly),
            AddInvestigation(OtherOrgId, "Someone Elses Work", InvestigationVisibility.Public),
            AddCase(OrgId, "The Knocking", isPublic: true),
            AddCase(OrgId, "An Active Case", isPublic: false));

        await db.SaveChangesAsync();
        return world;
    }

    private static async Task<JsonElement[]> ResolveAsync(
        World w, CmsSectionType type, CmsEmbed.Settings settings)
    {
        await using var db = await w.Factory.CreateDbContextAsync();

        var json = await CmsEmbed.ResolveAsync(
            db, OrgId, type, CmsEmbed.WriteSettings(settings), default);

        using var doc = JsonDocument.Parse(json);
        return [.. doc.RootElement.EnumerateArray().Select(e => e.Clone())];
    }

    private static string? Text(JsonElement row, string property)
        => row.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    // ── The ordinary case: a group publishes its own public work ─────────────

    [Fact]
    public async Task A_public_investigation_is_published_with_its_title_and_write_up()
    {
        var w = await SeedAsync();

        var rows = await ResolveAsync(w, CmsSectionType.EmbeddedInvestigations,
            new CmsEmbed.Settings([w.PublicInvestigationId]));

        var row = Assert.Single(rows);
        Assert.Equal("The Mill Vigil", Text(row, "title"));
        Assert.Equal("What we found.", Text(row, "summary"));
    }

    [Fact]
    public async Task A_public_case_is_published_with_its_title()
    {
        var w = await SeedAsync();

        var rows = await ResolveAsync(w, CmsSectionType.EmbeddedCases,
            new CmsEmbed.Settings([w.PublicCaseId]));

        Assert.Equal("The Knocking", Text(Assert.Single(rows), "title"));
    }

    /// <summary>
    /// The group's chosen sequence is kept. They arranged the section; a helpful re-sort by date
    /// would silently overrule an editorial decision.
    /// </summary>
    [Fact]
    public async Task The_order_the_group_chose_is_the_order_published()
    {
        var w = await SeedAsync();

        var rows = await ResolveAsync(w, CmsSectionType.EmbeddedInvestigations,
            new CmsEmbed.Settings([w.PrivateInvestigationId, w.PublicInvestigationId],
                                  IncludeNonPublic: true));

        Assert.Equal(2, rows.Length);
        Assert.Equal("A Quiet Night", Text(rows[0], "title"));
        Assert.Equal("The Mill Vigil", Text(rows[1], "title"));
    }

    /// <summary>A slug travels with each row, so the card can link to the full page.</summary>
    [Fact]
    public async Task Each_row_carries_the_link_to_its_own_page()
    {
        var w = await SeedAsync();

        var rows = await ResolveAsync(w, CmsSectionType.EmbeddedInvestigations,
            new CmsEmbed.Settings([w.PublicInvestigationId]));

        Assert.False(string.IsNullOrWhiteSpace(Text(Assert.Single(rows), "urlName")));
    }

    // ── Location ─────────────────────────────────────────────────────────────

    /// <summary>
    /// With the area switched on, what is published is the grid cell centre — never the real point.
    /// </summary>
    [Fact]
    public async Task Showing_the_area_publishes_an_approximate_point_not_the_real_one()
    {
        var w = await SeedAsync();

        var rows = await ResolveAsync(w, CmsSectionType.EmbeddedInvestigations,
            new CmsEmbed.Settings([w.PublicInvestigationId], ShowApproximateLocation: true));

        var row = Assert.Single(rows);

        var lat = row.GetProperty("latitude").GetDecimal();
        var lon = row.GetProperty("longitude").GetDecimal();

        Assert.NotEqual(TrueLatitude, lat);
        Assert.NotEqual(TrueLongitude, lon);

        // And it is genuinely nearby rather than nonsense — an approximation that lands in another
        // state is not a redaction, it is a bug.
        Assert.True(Math.Abs(lat - TrueLatitude) < 1m);
        Assert.True(Math.Abs(lon - TrueLongitude) < 1m);

        Assert.True(row.GetProperty("locationIsApproximate").GetBoolean());
    }

    /// <summary>
    /// The city is shown alongside the rough point, because "somewhere near Nashville" is what the
    /// group is actually trying to say.
    /// </summary>
    [Fact]
    public async Task Showing_the_area_publishes_the_town_and_state()
    {
        var w = await SeedAsync();

        var rows = await ResolveAsync(w, CmsSectionType.EmbeddedCases,
            new CmsEmbed.Settings([w.PublicCaseId], ShowApproximateLocation: true));

        var row = Assert.Single(rows);
        Assert.Equal("Nashville", Text(row, "city"));
        Assert.Equal("TN", Text(row, "state"));
    }

    /// <summary>With the area switched off, nothing about where it happened is published at all.</summary>
    [Fact]
    public async Task Hiding_the_area_publishes_no_location_at_all()
    {
        var w = await SeedAsync();

        var rows = await ResolveAsync(w, CmsSectionType.EmbeddedCases,
            new CmsEmbed.Settings([w.PublicCaseId], ShowApproximateLocation: false));

        var row = Assert.Single(rows);
        Assert.Null(Text(row, "city"));
        Assert.Null(Text(row, "state"));

        // Omitted from the payload rather than serialized as null — better still, since a key that
        // is not there cannot be read at all by a careless consumer.
        Assert.False(row.TryGetProperty("latitude", out var lat) && lat.ValueKind != JsonValueKind.Null);
        Assert.False(row.TryGetProperty("longitude", out var lon) && lon.ValueKind != JsonValueKind.Null);
    }

    /// <summary>
    /// The street address is never in the payload under any combination of switches. This is the
    /// assertion that matters most in the file.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task The_street_address_is_never_published(bool showLocation, bool showName)
    {
        var w = await SeedAsync();

        await using var db = await w.Factory.CreateDbContextAsync();
        var json = await CmsEmbed.ResolveAsync(
            db, OrgId, CmsSectionType.EmbeddedCases,
            CmsEmbed.WriteSettings(new CmsEmbed.Settings(
                [w.PublicCaseId, w.PrivateCaseId],
                IncludeNonPublic: true,
                ShowApproximateLocation: showLocation,
                ShowClientName: showName)),
            default);

        // The street the client actually lives on, in both the forms it is stored in.
        Assert.DoesNotContain("42 Elm", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Elm Street", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("37201", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TrueLatitude.ToString(System.Globalization.CultureInfo.InvariantCulture), json);
        Assert.DoesNotContain(TrueLongitude.ToString(System.Globalization.CultureInfo.InvariantCulture), json);
    }

    // ── Client identity ──────────────────────────────────────────────────────

    /// <summary>With names on, the client's own chosen alias is what appears.</summary>
    [Fact]
    public async Task Showing_the_client_publishes_the_alias_they_chose()
    {
        var w = await SeedAsync();

        var rows = await ResolveAsync(w, CmsSectionType.EmbeddedCases,
            new CmsEmbed.Settings([w.PublicCaseId], ShowClientName: true));

        Assert.Equal("The Hollow Oak Family", Text(Assert.Single(rows), "clientName"));
    }

    [Fact]
    public async Task Hiding_the_client_publishes_no_name()
    {
        var w = await SeedAsync();

        var rows = await ResolveAsync(w, CmsSectionType.EmbeddedCases,
            new CmsEmbed.Settings([w.PublicCaseId], ShowClientName: false));

        Assert.Null(Text(Assert.Single(rows), "clientName"));
    }

    /// <summary>
    /// A client who set no alias publishes anonymously rather than under their real name — the
    /// pre-existing rule in <c>PublicClientName</c>, which this must not have quietly bypassed.
    /// </summary>
    [Fact]
    public async Task A_client_with_no_alias_is_published_anonymously()
    {
        var w = await SeedAsync();

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var c = await db.Cases.FirstAsync(x => x.Id == w.PublicCaseId);
            c.ClientDisplayAlias = null;
            c.PublicPseudonym = null;
            await db.SaveChangesAsync();
        }

        var rows = await ResolveAsync(w, CmsSectionType.EmbeddedCases,
            new CmsEmbed.Settings([w.PublicCaseId], ShowClientName: true));

        Assert.Null(Text(Assert.Single(rows), "clientName"));
    }

    // ── Who may be embedded ──────────────────────────────────────────────────

    /// <summary>
    /// Another organization's investigation does not resolve, whatever the stored section says.
    /// The picker never offers it, but the picker is a convenience and this is the rule.
    /// </summary>
    [Fact]
    public async Task Another_organizations_work_cannot_be_embedded()
    {
        var w = await SeedAsync();

        var rows = await ResolveAsync(w, CmsSectionType.EmbeddedInvestigations,
            new CmsEmbed.Settings([w.OtherOrgInvestigationId], IncludeNonPublic: true));

        Assert.Empty(rows);
    }

    /// <summary>
    /// Work that is not already public needs the group to have said so deliberately. Without the
    /// acknowledgement it does not resolve — so a section saved by an older editor cannot publish
    /// something by omission.
    /// </summary>
    [Fact]
    public async Task Non_public_work_needs_a_deliberate_acknowledgement()
    {
        var w = await SeedAsync();

        var withoutAck = await ResolveAsync(w, CmsSectionType.EmbeddedInvestigations,
            new CmsEmbed.Settings([w.PrivateInvestigationId]));
        Assert.Empty(withoutAck);

        var withAck = await ResolveAsync(w, CmsSectionType.EmbeddedInvestigations,
            new CmsEmbed.Settings([w.PrivateInvestigationId], IncludeNonPublic: true));
        Assert.Single(withAck);
    }

    [Fact]
    public async Task A_non_public_case_needs_the_same_acknowledgement()
    {
        var w = await SeedAsync();

        Assert.Empty(await ResolveAsync(w, CmsSectionType.EmbeddedCases,
            new CmsEmbed.Settings([w.PrivateCaseId])));

        Assert.Single(await ResolveAsync(w, CmsSectionType.EmbeddedCases,
            new CmsEmbed.Settings([w.PrivateCaseId], IncludeNonPublic: true)));
    }

    /// <summary>
    /// A mixed selection publishes the public half and withholds the rest, rather than failing
    /// whole. Ids that resolve to nothing vanish quietly — saying "that one exists but is hidden"
    /// on a public page is itself a disclosure.
    /// </summary>
    [Fact]
    public async Task A_mixed_selection_publishes_only_what_it_may()
    {
        var w = await SeedAsync();

        var rows = await ResolveAsync(w, CmsSectionType.EmbeddedInvestigations,
            new CmsEmbed.Settings([w.PublicInvestigationId, w.PrivateInvestigationId, w.OtherOrgInvestigationId]));

        Assert.Equal("The Mill Vigil", Text(Assert.Single(rows), "title"));
    }

    // ── Failing closed ───────────────────────────────────────────────────────

    /// <summary>
    /// Unparseable content publishes nothing. Elsewhere in the CMS a malformed section renders an
    /// empty box; here it would be deciding whether an address is published, so the default has to
    /// be silence rather than whatever a permissive parse happened to produce.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"ids\": \"not-an-array\"}")]
    [InlineData("{}")]
    public void Malformed_settings_show_nothing(string contentJson)
    {
        var settings = CmsEmbed.ParseSettings(contentJson);

        Assert.Empty(settings.Ids);
        Assert.False(settings.IncludeNonPublic);
        Assert.False(settings.ShowApproximateLocation);
        Assert.False(settings.ShowClientName);
    }

    /// <summary>Settings survive a round trip, or the editor's choices would silently reset.</summary>
    [Fact]
    public void Settings_round_trip()
    {
        var id = Guid.NewGuid();
        var original = new CmsEmbed.Settings([id], true, true, true);

        var restored = CmsEmbed.ParseSettings(CmsEmbed.WriteSettings(original));

        Assert.Equal([id], restored.Ids);
        Assert.True(restored.IncludeNonPublic);
        Assert.True(restored.ShowApproximateLocation);
        Assert.True(restored.ShowClientName);
    }

    // ── Structural: a shape that cannot carry it cannot leak it ──────────────

    /// <summary>
    /// The published shapes have no field for an exact location or a real name.
    /// </summary>
    /// <remarks>
    /// The strongest guard here, and the cheapest. Every test above checks what the current code
    /// puts in the payload; this checks what the payload is <i>able</i> to hold, so a future mapping
    /// change cannot reintroduce a field that redaction then has to remember to blank.
    /// </remarks>
    [Theory]
    [InlineData(typeof(CmsEmbed.EmbeddedInvestigation))]
    [InlineData(typeof(CmsEmbed.EmbeddedCase))]
    public void The_published_shape_has_no_room_for_an_address_or_a_real_name(Type shape)
    {
        var forbidden = new[]
        {
            "street", "address", "postcode", "zip", "clientappuserid", "appuserid",
            "exactlatitude", "exactlongitude", "realname", "clientrealname", "email", "phone",
        };

        var offenders = shape
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{shape.Name} has fields that could carry data this section exists to withhold: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// The only coordinate fields present are the approximate ones, and they travel with the flag
    /// that says so — a bare point on a map reads as the place.
    /// </summary>
    [Theory]
    [InlineData(typeof(CmsEmbed.EmbeddedInvestigation))]
    [InlineData(typeof(CmsEmbed.EmbeddedCase))]
    public void Coordinates_always_travel_with_the_approximate_flag(Type shape)
    {
        var names = shape.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        Assert.Contains("Latitude", names);
        Assert.Contains("LocationIsApproximate", names);
    }
}
