using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Redaction;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Item 184: display-time name redaction for private-engagement cases. Groups and clients write
/// with real names freely; the public copy substitutes. The rules that cost the most when wrong:
/// whole words only, HTML structure untouched, the ladder's fallbacks, and — Ben's scope rule —
/// no substitution at all on a case that is not designated private.
/// </summary>
public sealed class CaseProseRedactorTests
{
    private static RedactionRoster Roster(params (string[] tokens, string label)[] entries)
        => new([.. entries.Select(e => new RosterEntry(e.tokens, e.label))]);

    // ── Matching ─────────────────────────────────────────────────────────────

    [Fact]
    public void Whole_words_only_the_parkers_stay_parkers()
    {
        var roster = Roster((["Daniel", "Park"], "the client"));
        Assert.Equal("The Parker house on Old Parkway",
            CaseProseRedactor.Redact("The Parker house on Old Parkway", roster));
    }

    [Fact]
    public void Every_token_of_a_name_is_replaced_case_insensitively()
    {
        var roster = Roster((["Daniel", "Park"], "the client"));
        // Text start counts as a sentence start, so the first replacement capitalizes.
        Assert.Equal("The client — the client — met us; the client waved.",
            CaseProseRedactor.Redact("DANIEL — park — met us; Daniel waved.", roster));
    }

    [Fact]
    public void The_replacement_capitalizes_at_a_sentence_start_and_not_mid_sentence()
    {
        var roster = Roster((["Daniel"], "the client"));
        Assert.Equal("The client opened the door. We followed the client upstairs.",
            CaseProseRedactor.Redact("Daniel opened the door. We followed Daniel upstairs.", roster));
    }

    [Fact]
    public void Multiple_people_each_get_their_own_label()
    {
        var roster = Roster((["Daniel"], "the client"), (["Linda"], "the homeowner"));
        Assert.Equal("The client and the homeowner disagreed",
            CaseProseRedactor.Redact("Daniel and Linda disagreed", roster));
    }

    [Fact]
    public void A_replacement_sheds_its_article_when_the_text_already_has_one()
    {
        // "The Vexley house" must not become "The the family house". Found live: the first
        // endpoint test produced exactly that doubling.
        var roster = Roster((["Vexley"], "the family"));
        Assert.Equal("The family house on Elm",
            CaseProseRedactor.Redact("The Vexley house on Elm", roster));
    }

    [Fact]
    public void A_proper_label_keeps_its_capitals_mid_sentence()
    {
        var roster = Roster((["Daniel"], "The Hargrove Family"), (["Linda"], "Mrs H"));
        Assert.Equal("We met The Hargrove Family and Mrs H upstairs.",
            CaseProseRedactor.Redact("We met Daniel and Linda upstairs.", roster));
    }

    // ── HTML ─────────────────────────────────────────────────────────────────

    [Fact]
    public void A_client_surnamed_strong_does_not_corrupt_strong_tags()
    {
        var roster = Roster((["Strong"], "the client"));
        var html = "<p><strong>Note:</strong> Mr Strong reported knocking.</p>";
        var redacted = CaseProseRedactor.RedactHtml(html, roster);

        Assert.Contains("<strong>", redacted);
        Assert.Contains("</strong>", redacted);
        Assert.DoesNotContain("Mr Strong", redacted);
        Assert.Contains("the client reported knocking", redacted);
    }

    [Fact]
    public void Names_inside_attributes_are_markup_not_prose_and_are_left_alone()
    {
        var roster = Roster((["Park"], "the client"));
        var html = "<a href=\"/places/park-slope\" title=\"Park St\">Park told us</a>";
        var redacted = CaseProseRedactor.RedactHtml(html, roster)!;

        Assert.Contains("/places/park-slope", redacted);
        Assert.Contains("title=\"Park St\"", redacted);
        // The text node begins with the name, which reads as a sentence start — accepted artifact.
        Assert.Contains("client told us", redacted);
    }

    [Fact]
    public void Unparseable_markup_falls_back_to_raw_replacement_toward_privacy()
    {
        // Even fed garbage, the name must not survive.
        var roster = Roster((["Daniel"], "the client"));
        var redacted = CaseProseRedactor.RedactHtml("<p>Daniel <<<", roster)!;
        Assert.DoesNotContain("Daniel", redacted);
    }

    [Fact]
    public void An_empty_roster_returns_the_text_unchanged()
    {
        Assert.Equal("<p>as written</p>",
            CaseProseRedactor.RedactHtml("<p>as written</p>", RedactionRoster.Empty));
    }

    // ── The roster and its ladder ────────────────────────────────────────────

    private static BenDataContext NewDb() =>
        new(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(BenDataContext Db, Guid CaseId)> SeedAsync(
        bool isPrivate = true, string? alias = null, string? pseudonym = null,
        params (string name, string? relationship, bool lives, string? label)[] people)
    {
        var db = NewDb();
        Guid caseId = Guid.NewGuid(), userId = Guid.NewGuid(), clientId = Guid.NewGuid(), reqId = Guid.NewGuid();
        db.Users.Add(new AppUser { Id = userId, UserName = "u@t", Email = "u@t", DateCreated = DateTime.UtcNow });
        db.Users.Add(new AppUser
        {
            Id = clientId, UserName = "c@t", Email = "c@t",
            FirstName = "Daniel", LastName = "Park", DisplayName = "Daniel Park", DateCreated = DateTime.UtcNow,
        });
        db.ClientRequests.Add(new ClientRequest
        {
            Id = reqId, AppUserId = clientId, Status = ClientRequestStatus.Assigned,
            StreetAddress1 = "1 Elm", City = "N", State = "TN", ZipCode = "1",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
        });
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = Guid.NewGuid(), Title = "t", Status = CaseStatus.Active,
            ClientRequestId = reqId, IsPrivateEngagement = isPrivate,
            ClientDisplayAlias = alias, PublicPseudonym = pseudonym,
            StreetAddress1 = "1 Elm", City = "N", State = "TN", ZipCode = "1", Country = "US",
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        foreach (var (name, relationship, lives, label) in people)
            db.CaseRelatedPeople.Add(new CaseRelatedPerson
            {
                Id = Guid.NewGuid(), CaseId = caseId, Name = name, Relationship = relationship,
                LivesAtProperty = lives, PublicLabel = label,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
        await db.SaveChangesAsync();
        return (db, caseId);
    }

    [Fact]
    public async Task A_case_not_designated_private_has_no_roster_at_all()
    {
        // Ben's scope rule: public-place cases render exactly as written.
        var (db, caseId) = await SeedAsync(isPrivate: false);
        Assert.Null(await CaseRedactionRoster.ForCaseAsync(db, caseId, default));
    }

    [Fact]
    public async Task The_client_ladder_prefers_their_alias_then_the_pseudonym()
    {
        var (db1, id1) = await SeedAsync(alias: "The Night Visitors", pseudonym: "The Hargrove Family");
        var r1 = await CaseRedactionRoster.ForCaseAsync(db1, id1, default);
        Assert.Equal("The Night Visitors", r1!.Entries[0].Replacement);

        var (db2, id2) = await SeedAsync(pseudonym: "The Hargrove Family");
        var r2 = await CaseRedactionRoster.ForCaseAsync(db2, id2, default);
        Assert.Equal("The Hargrove Family", r2!.Entries[0].Replacement);
    }

    [Fact]
    public async Task With_nothing_set_two_residents_make_it_the_family_otherwise_the_client()
    {
        var (dbFam, idFam) = await SeedAsync(people:
            [("Linda Park", "spouse", true, null), ("Tommy Park", "son", true, null)]);
        var famRoster = await CaseRedactionRoster.ForCaseAsync(dbFam, idFam, default);
        Assert.Equal("the family", famRoster!.Entries[0].Replacement);

        var (dbSolo, idSolo) = await SeedAsync();
        var soloRoster = await CaseRedactionRoster.ForCaseAsync(dbSolo, idSolo, default);
        Assert.Equal("the client", soloRoster!.Entries[0].Replacement);
    }

    [Fact]
    public async Task Related_people_use_their_label_then_relationship_then_residence()
    {
        var (db, caseId) = await SeedAsync(people:
        [
            ("Linda Park", "spouse", true, "the lady of the house"),   // explicit label wins
            ("Tommy Park", "son", true, null),                          // relationship → family member
            ("Ray Holt", "neighbor", false, null),                      // relationship → neighbor
            ("Gwen Marsh", null, true, null),                           // resident
            ("Ed Boone", null, false, null),                            // witness ("Ed" < 3 chars → keyed on Boone)
        ]);
        var roster = await CaseRedactionRoster.ForCaseAsync(db, caseId, default);
        var byName = roster!.Entries.ToDictionary(e => e.Tokens[0], e => e.Replacement, StringComparer.OrdinalIgnoreCase);

        Assert.Equal("the lady of the house", byName["Linda"]);
        Assert.Equal("a family member", byName["Tommy"]);
        Assert.Equal("a neighbor", byName["Ray"]);
        Assert.Equal("a resident", byName["Gwen"]);
        Assert.Equal("a witness", byName["Boone"]);
    }

    [Fact]
    public async Task End_to_end_a_report_paragraph_comes_out_clean()
    {
        var (db, caseId) = await SeedAsync(pseudonym: "The Hargrove Family", people:
            [("Linda Park", "spouse", true, null)]);
        var roster = await CaseRedactionRoster.ForCaseAsync(db, caseId, default);

        var body = "<p>Daniel met us at the door. <em>Linda</em> stayed upstairs; "
                 + "Park said the knocking starts at 3 AM.</p>";
        var redacted = CaseProseRedactor.RedactHtml(body, roster!)!;

        Assert.DoesNotContain("Daniel", redacted);
        Assert.DoesNotContain("Linda", redacted);
        Assert.DoesNotContain("Park", redacted);
        Assert.Contains("The Hargrove Family met us at the door", redacted);
        Assert.Contains("<em>", redacted);
    }
}
