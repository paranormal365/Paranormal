using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Text.Json;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The case-bound slot — photos chosen from a case, published on a group's own page
/// (backlog item #80, part 2b, second half).
/// </summary>
/// <remarks>
/// <para>What separates this from every other section type is that its stored content is a set of
/// <b>references</b>. The interesting assertions are therefore not about what an author picked but
/// about what happens <i>afterwards</i>: a timeline entry narrowed next month, a case unpublished,
/// a file unlinked. Each of those must take the photo off pages published today without anybody
/// editing them, and the tests below are mostly about that.</para>
///
/// <para>The positive cases come first for the same reason as
/// <see cref="CaseMediaPublicationTests"/>: a section that resolved to nothing would pass every
/// safety assertion here and be worthless.</para>
/// </remarks>
public sealed class CmsCaseMediaTests
{
    private static readonly Guid OrgId      = Guid.NewGuid();
    private static readonly Guid OtherOrgId = Guid.NewGuid();
    private static readonly Guid UserId     = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory,
        Guid CaseId,
        Guid OtherOrgCaseId,
        Guid FirstPublicFileId,
        Guid SecondPublicFileId,
        Guid OrgOnlyFileId,
        Guid OtherOrgFileId,
        Guid PublicEntryId);

    private static async Task<World> SeedAsync(bool casePublic = true)
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();

        Guid AddCase(Guid orgId, string title, bool isPublic)
        {
            var id = Guid.NewGuid();
            db.Cases.Add(new Case
            {
                Id = id, OrganizationId = orgId, Title = title,
                StreetAddress1 = "42 Elm Street", City = "Nashville", State = "TN", ZipCode = "37201",
                IsPublic = isPublic, Status = CaseStatus.Public,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
            });
            return id;
        }

        var caseId = AddCase(OrgId, "The Knocking", casePublic);
        var otherCaseId = AddCase(OtherOrgId, "Someone Elses Case", isPublic: true);

        (Guid FileId, Guid EntryId) AddEntryWithFile(
            Guid onCase, CaseTimelineVisibility visibility, string title, int daysAgo)
        {
            var entryId = Guid.NewGuid();
            var fileId  = Guid.NewGuid();

            db.CaseTimelineEntries.Add(new CaseTimelineEntry
            {
                Id = entryId, CaseId = onCase, Title = title, Visibility = visibility,
                EntryType = CaseTimelineEntryType.Evidence,
                EventDateTime = DateTime.UtcNow.AddDays(-daysAgo),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
            });
            db.CaseTimelineEntryFiles.Add(new CaseTimelineEntryFile
            {
                Id = Guid.NewGuid(), CaseTimelineEntryId = entryId, UploadFileId = fileId,
            });
            return (fileId, entryId);
        }

        var first  = AddEntryWithFile(caseId, CaseTimelineVisibility.Public,  "The hallway photo", 1);
        var second = AddEntryWithFile(caseId, CaseTimelineVisibility.Public,  "The stairwell",     2);
        var hidden = AddEntryWithFile(caseId, CaseTimelineVisibility.OrgOnly, "Working file",      3);
        var other  = AddEntryWithFile(otherCaseId, CaseTimelineVisibility.Public, "Their photo",   1);

        await db.SaveChangesAsync();

        return new World(factory, caseId, otherCaseId,
                         first.FileId, second.FileId, hidden.FileId, other.FileId, first.EntryId);
    }

    private static async Task<JsonElement[]> ResolveAsync(
        World w, CmsEmbed.CaseMediaSettings settings, Guid? asOrg = null)
    {
        await using var db = await w.Factory.CreateDbContextAsync();

        var json = await CmsEmbed.ResolveAsync(
            db, asOrg ?? OrgId, CmsSectionType.CaseMedia,
            CmsEmbed.WriteCaseMediaSettings(settings), default);

        using var doc = JsonDocument.Parse(json);
        return [.. doc.RootElement.EnumerateArray().Select(e => e.Clone())];
    }

    private static string? Text(JsonElement row, string property)
        => row.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    // ── What an author actually gets ─────────────────────────────────────────

    /// <summary>
    /// The chosen files come back, in the order the author arranged them rather than by date.
    /// </summary>
    /// <remarks>
    /// The ordering half is the point: a write-up is a sequence somebody composed, and re-sorting it
    /// newest-first would silently rearrange their argument. The two files are seeded a day apart so
    /// asking for the older one first would fail if anything sorted them.
    /// </remarks>
    [Fact]
    public async Task Chosen_files_resolve_in_the_authors_order()
    {
        var w = await SeedAsync();

        var rows = await ResolveAsync(w, new CmsEmbed.CaseMediaSettings(
            w.CaseId, [w.SecondPublicFileId, w.FirstPublicFileId]));

        Assert.Equal(2, rows.Length);
        Assert.Equal(w.SecondPublicFileId.ToString(), Text(rows[0], "uploadFileId"));
        Assert.Equal(w.FirstPublicFileId.ToString(),  Text(rows[1], "uploadFileId"));
    }

    /// <summary>
    /// The property names the renderer reads are the ones the server writes.
    /// </summary>
    /// <remarks>
    /// Not pedantry. This JSON is a string carried inside the response, so the outer serializer never
    /// touches its casing — and part 4 shipped a version where the renderer looked for <c>title</c>
    /// while the server wrote <c>Title</c>, which would have blanked every card on a live page. That
    /// was caught by an assertion like this one, not by any of the safety tests.
    /// </remarks>
    [Fact]
    public async Task The_projection_uses_the_property_names_the_renderer_reads()
    {
        var w = await SeedAsync();

        var rows = await ResolveAsync(w, new CmsEmbed.CaseMediaSettings(
            w.CaseId, [w.FirstPublicFileId], ShowCaptions: true));

        var row = Assert.Single(rows);
        Assert.True(row.TryGetProperty("caseId",       out _));
        Assert.True(row.TryGetProperty("uploadFileId", out _));
        Assert.True(row.TryGetProperty("caption",      out _));

        // The case id must survive into the projection: the renderer builds the media URL from it,
        // and a row without it cannot be displayed at all.
        Assert.Equal(w.CaseId.ToString(), Text(row, "caseId"));
    }

    // ── The rule, re-asked on every read ─────────────────────────────────────

    /// <summary>
    /// A file that is not publishable is dropped even though the section stored it.
    /// </summary>
    /// <remarks>
    /// The editor never offers this file, so the only way it reaches storage is a hand-made request
    /// or an entry narrowed after the fact. Both must fail the same way — this is the assertion that
    /// makes the picker a convenience rather than the control.
    /// </remarks>
    [Fact]
    public async Task A_file_that_may_not_be_published_is_dropped()
    {
        var w = await SeedAsync();

        var rows = await ResolveAsync(w, new CmsEmbed.CaseMediaSettings(
            w.CaseId, [w.FirstPublicFileId, w.OrgOnlyFileId]));

        var row = Assert.Single(rows);
        Assert.Equal(w.FirstPublicFileId.ToString(), Text(row, "uploadFileId"));
    }

    /// <summary>
    /// Narrowing a timeline entry's visibility removes its photo from pages already published.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the section stores references instead of copying the file in at
    /// fill-in time. Nobody edits the page; nobody has to remember which pages used the photo. If
    /// this test ever fails, the feature has become a way to publish something permanently.
    /// </remarks>
    [Fact]
    public async Task Narrowing_the_entry_afterwards_takes_the_photo_off_the_page()
    {
        var w = await SeedAsync();
        var settings = new CmsEmbed.CaseMediaSettings(w.CaseId, [w.FirstPublicFileId]);

        Assert.Single(await ResolveAsync(w, settings));

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var entry = await db.CaseTimelineEntries.FirstAsync(e => e.Id == w.PublicEntryId);
            entry.Visibility = CaseTimelineVisibility.OrgOnly;
            await db.SaveChangesAsync();
        }

        Assert.Empty(await ResolveAsync(w, settings));
    }

    /// <summary>Unpublishing the case takes everything with it, without the page being touched.</summary>
    [Fact]
    public async Task Unpublishing_the_case_empties_the_section()
    {
        var w = await SeedAsync();
        var settings = new CmsEmbed.CaseMediaSettings(
            w.CaseId, [w.FirstPublicFileId, w.SecondPublicFileId]);

        Assert.Equal(2, (await ResolveAsync(w, settings)).Length);

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var c = await db.Cases.FirstAsync(x => x.Id == w.CaseId);
            c.IsPublic = false;
            await db.SaveChangesAsync();
        }

        Assert.Empty(await ResolveAsync(w, settings));
    }

    /// <summary>
    /// A group cannot show another group's case media, even when that case is public.
    /// </summary>
    /// <remarks>
    /// Publishability alone would have allowed this — the other group's file passes every check in
    /// <see cref="CaseMediaPublication"/>. Ownership is a second, independent gate, and it exists
    /// because a page decorated with somebody else's investigation reads as a claim about who did
    /// the work.
    /// </remarks>
    [Fact]
    public async Task Another_groups_case_resolves_to_nothing()
    {
        var w = await SeedAsync();

        Assert.Empty(await ResolveAsync(w, new CmsEmbed.CaseMediaSettings(
            w.OtherOrgCaseId, [w.OtherOrgFileId])));
    }

    // ── Captions ─────────────────────────────────────────────────────────────

    /// <summary>
    /// With captions off, the timeline entry's title is absent rather than sent-and-ignored.
    /// </summary>
    /// <remarks>
    /// The entry title is the group's own working description — "Shared with the family", "Working
    /// file" — and it can say more than a group intends. Leaving it out of the payload rather than
    /// hiding it in the renderer means a careless later edit to the markup cannot surface it.
    /// </remarks>
    [Fact]
    public async Task Captions_off_means_the_entry_title_never_leaves_the_server()
    {
        var w = await SeedAsync();

        var rows = await ResolveAsync(w, new CmsEmbed.CaseMediaSettings(
            w.CaseId, [w.FirstPublicFileId], ShowCaptions: false));

        var row = Assert.Single(rows);
        Assert.Null(Text(row, "caption"));
        Assert.DoesNotContain("hallway", row.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>With captions on, it is there — otherwise the switch does nothing.</summary>
    [Fact]
    public async Task Captions_on_publishes_the_entry_title()
    {
        var w = await SeedAsync();

        var rows = await ResolveAsync(w, new CmsEmbed.CaseMediaSettings(
            w.CaseId, [w.FirstPublicFileId], ShowCaptions: true));

        Assert.Equal("The hallway photo", Text(Assert.Single(rows), "caption"));
    }

    // ── Failing closed ───────────────────────────────────────────────────────

    /// <summary>
    /// A half-filled section — files chosen, no case — publishes nothing.
    /// </summary>
    /// <remarks>
    /// Reachable in the editor: pick a case, tick photos, then clear the case. The file ids are real
    /// and publishable, so anything that resolved them without the case id would publish them under
    /// no case at all, and the media endpoint could not have gated them.
    /// </remarks>
    [Fact]
    public async Task File_ids_without_a_case_publish_nothing()
    {
        var w = await SeedAsync();

        Assert.Empty(await ResolveAsync(w, new CmsEmbed.CaseMediaSettings(
            Guid.Empty, [w.FirstPublicFileId, w.SecondPublicFileId])));
    }

    /// <summary>Content that will not parse shows nothing, never a permissive default.</summary>
    [Fact]
    public async Task Unparseable_content_publishes_nothing()
    {
        var w = await SeedAsync();
        await using var db = await w.Factory.CreateDbContextAsync();

        foreach (var junk in new[] { "not json at all", "[]", "", "{\"caseId\":\"nonsense\"}" })
        {
            var json = await CmsEmbed.ResolveAsync(db, OrgId, CmsSectionType.CaseMedia, junk, default);
            using var doc = JsonDocument.Parse(json);
            Assert.Empty(doc.RootElement.EnumerateArray());
        }
    }

    /// <summary>
    /// The same file picked twice appears once.
    /// </summary>
    /// <remarks>
    /// Reachable through the stored JSON rather than the editor, whose checkboxes cannot tick twice.
    /// Worth pinning because the fix — a <c>Distinct</c> — is one word and silently removable.
    /// </remarks>
    [Fact]
    public async Task A_file_listed_twice_is_shown_once()
    {
        var w = await SeedAsync();

        var rows = await ResolveAsync(w, new CmsEmbed.CaseMediaSettings(
            w.CaseId, [w.FirstPublicFileId, w.FirstPublicFileId]));

        Assert.Single(rows);
    }

    /// <summary>
    /// A case-media section is one of the types whose stored content is replaced on read.
    /// </summary>
    /// <remarks>
    /// The switch that routes a section through resolution at all. Left off it, the public endpoint
    /// would hand a visitor the raw stored ids — including ones the rule would have dropped — and
    /// every test above would still pass, because they call the resolver directly.
    /// </remarks>
    [Fact]
    public void Case_media_sections_are_resolved_rather_than_served_as_stored()
        => Assert.True(CmsEmbed.IsEmbed(CmsSectionType.CaseMedia));
}
