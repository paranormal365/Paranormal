using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Service.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Which of a case's files may go on a public page (backlog item #80, prerequisite for part 2b).
/// </summary>
/// <remarks>
/// <para>A page template offering "a photo from this case" is a way to publish the investigators'
/// working files unless something decides which files qualify. This is that decision, and it
/// deliberately grants nothing new: the rule is the one the public case page already follows, so a
/// template can publish exactly what a visitor could already reach and not one file more.</para>
///
/// <para>The positive cases come first and matter most — a rule that published nothing would be
/// perfectly safe and perfectly useless, and 2b's whole value is the author picking real material.
/// </para>
/// </remarks>
public sealed class CaseMediaPublicationTests
{
    private static readonly Guid OrgId  = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory,
        Guid CaseId,
        Guid PublicEvidenceFileId,
        Guid PublicNoteFileId,
        Guid ClientOnlyFileId,
        Guid OrgOnlyFileId,
        Guid GeneralTabFileId);

    private static async Task<World> SeedAsync(
        bool casePublic = true, CaseStatus status = CaseStatus.Public)
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();

        var caseId = Guid.NewGuid();
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = OrgId, Title = "The Knocking",
            StreetAddress1 = "42 Elm Street", City = "Nashville", State = "TN", ZipCode = "37201",
            IsPublic = casePublic, Status = status,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        });

        Guid AddEntryWithFile(CaseTimelineVisibility visibility, CaseTimelineEntryType type, string title)
        {
            var entryId = Guid.NewGuid();
            var fileId = Guid.NewGuid();

            db.CaseTimelineEntries.Add(new CaseTimelineEntry
            {
                Id = entryId, CaseId = caseId, Title = title, Visibility = visibility,
                EntryType = type, EventDateTime = DateTime.UtcNow.AddDays(-1),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
            });
            db.CaseTimelineEntryFiles.Add(new CaseTimelineEntryFile
            {
                Id = Guid.NewGuid(), CaseTimelineEntryId = entryId, UploadFileId = fileId,
            });
            return fileId;
        }

        var world = new World(
            factory, caseId,
            AddEntryWithFile(CaseTimelineVisibility.Public, CaseTimelineEntryType.Evidence, "The hallway photo"),
            AddEntryWithFile(CaseTimelineVisibility.Public, CaseTimelineEntryType.InvestigatorNote, "Site notes"),
            AddEntryWithFile(CaseTimelineVisibility.Client, CaseTimelineEntryType.Evidence, "Shared with the family"),
            AddEntryWithFile(CaseTimelineVisibility.OrgOnly, CaseTimelineEntryType.Evidence, "Working file"),
            Guid.NewGuid());

        // A file on the case's general Files tab, which carries no visibility of any kind.
        db.CaseFiles.Add(new CaseFile
        {
            Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = world.GeneralTabFileId,
            Description = "Scanned floor plan",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        });

        await db.SaveChangesAsync();
        return world;
    }

    private static async Task<IReadOnlyList<PublishableCaseFile>> PublishableAsync(World w)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        return await CaseMediaPublication.PublishableAsync(db, w.CaseId, default);
    }

    private static async Task<bool> MayPublishAsync(World w, Guid fileId)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        return await CaseMediaPublication.MayPublishAsync(db, w.CaseId, fileId, default);
    }

    // ── What an author can actually use ──────────────────────────────────────

    /// <summary>
    /// A file on a public timeline entry is offered. Without this the feature has no content and
    /// the safest possible rule would also be a useless one.
    /// </summary>
    [Fact]
    public async Task A_file_on_a_public_entry_can_be_published()
    {
        var w = await SeedAsync();

        Assert.True(await MayPublishAsync(w, w.PublicEvidenceFileId));
        Assert.Contains(await PublishableAsync(w), f => f.UploadFileId == w.PublicEvidenceFileId);
    }

    /// <summary>
    /// Any public entry qualifies, not only Evidence ones. The public case page happens to show
    /// files for Evidence entries alone, but that is a choice about that page — a photo the group
    /// marked Public is Public whatever kind of entry it hangs off.
    /// </summary>
    [Fact]
    public async Task A_file_on_a_public_note_can_also_be_published()
    {
        var w = await SeedAsync();

        Assert.True(await MayPublishAsync(w, w.PublicNoteFileId));
        Assert.Contains(await PublishableAsync(w), f => f.UploadFileId == w.PublicNoteFileId);
    }

    /// <summary>The picker carries enough context to choose between two photos.</summary>
    [Fact]
    public async Task Each_offered_file_says_what_it_was_of()
    {
        var w = await SeedAsync();

        var file = Assert.Single(
            (await PublishableAsync(w)).Where(f => f.UploadFileId == w.PublicEvidenceFileId));

        Assert.Equal("The hallway photo", file.Context);
        Assert.NotNull(file.When);
    }

    /// <summary>Filtering a set keeps the author's order and drops what does not qualify.</summary>
    [Fact]
    public async Task Filtering_keeps_the_order_and_drops_the_rest()
    {
        var w = await SeedAsync();

        await using var db = await w.Factory.CreateDbContextAsync();
        var kept = await CaseMediaPublication.FilterPublishableAsync(
            db, w.CaseId,
            [w.PublicNoteFileId, w.OrgOnlyFileId, w.PublicEvidenceFileId, w.ClientOnlyFileId],
            default);

        Assert.Equal([w.PublicNoteFileId, w.PublicEvidenceFileId], kept);
    }

    // ── What it refuses ──────────────────────────────────────────────────────

    [Fact]
    public async Task An_internal_file_cannot_be_published()
    {
        var w = await SeedAsync();

        Assert.False(await MayPublishAsync(w, w.OrgOnlyFileId));
        Assert.DoesNotContain(await PublishableAsync(w), f => f.UploadFileId == w.OrgOnlyFileId);
    }

    /// <summary>
    /// Shared with the client is not shared with the world. This is the one somebody would get
    /// wrong by reading "shared" as "not internal".
    /// </summary>
    [Fact]
    public async Task A_file_shared_only_with_the_client_cannot_be_published()
    {
        var w = await SeedAsync();

        Assert.False(await MayPublishAsync(w, w.ClientOnlyFileId));
        Assert.DoesNotContain(await PublishableAsync(w), f => f.UploadFileId == w.ClientOnlyFileId);
    }

    /// <summary>
    /// Files on the general Files tab are never publishable, because <c>CaseFile</c> has no
    /// visibility column and so nobody has ever agreed to any of them being public.
    /// </summary>
    [Fact]
    public async Task A_file_from_the_general_files_tab_cannot_be_published()
    {
        var w = await SeedAsync();

        Assert.False(await MayPublishAsync(w, w.GeneralTabFileId));
        Assert.DoesNotContain(await PublishableAsync(w), f => f.UploadFileId == w.GeneralTabFileId);
    }

    /// <summary>
    /// Nothing is publishable from a case that is not itself public — the entry's own visibility is
    /// not enough. A public entry on a private case has never been seen by anybody.
    /// </summary>
    [Fact]
    public async Task Nothing_is_publishable_from_a_case_that_is_not_public()
    {
        var w = await SeedAsync(casePublic: false);

        Assert.False(await MayPublishAsync(w, w.PublicEvidenceFileId));
        Assert.Empty(await PublishableAsync(w));
    }

    /// <summary>
    /// The case's status matters as well as its flag, matching the public page's own two-part test.
    /// </summary>
    [Fact]
    public async Task Nothing_is_publishable_from_a_case_whose_status_is_not_public()
    {
        var w = await SeedAsync(casePublic: true, status: CaseStatus.Active);

        Assert.False(await MayPublishAsync(w, w.PublicEvidenceFileId));
        Assert.Empty(await PublishableAsync(w));
    }

    /// <summary>
    /// A file belonging to no case at all is refused rather than defaulting to allowed.
    /// </summary>
    [Fact]
    public async Task An_unknown_file_cannot_be_published()
        => Assert.False(await MayPublishAsync(await SeedAsync(), Guid.NewGuid()));

    // ── Binding, not copying ─────────────────────────────────────────────────

    /// <summary>
    /// The behaviour the whole design exists for: narrowing an entry's visibility later removes its
    /// photo from pages already published. A slot that had copied the file would be immune to
    /// somebody changing their mind, which is exactly the wrong way round.
    /// </summary>
    [Fact]
    public async Task Making_an_entry_private_later_withdraws_its_file()
    {
        var w = await SeedAsync();
        Assert.True(await MayPublishAsync(w, w.PublicEvidenceFileId));

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var entry = await db.CaseTimelineEntries
                .FirstAsync(e => e.Files.Any(f => f.UploadFileId == w.PublicEvidenceFileId));
            entry.Visibility = CaseTimelineVisibility.OrgOnly;
            await db.SaveChangesAsync();
        }

        Assert.False(await MayPublishAsync(w, w.PublicEvidenceFileId));
    }

    /// <summary>Unpublishing the case withdraws everything, in one move.</summary>
    [Fact]
    public async Task Unpublishing_the_case_withdraws_every_file()
    {
        var w = await SeedAsync();
        Assert.NotEmpty(await PublishableAsync(w));

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var c = await db.Cases.FirstAsync(x => x.Id == w.CaseId);
            c.IsPublic = false;
            await db.SaveChangesAsync();
        }

        Assert.Empty(await PublishableAsync(w));
    }
}
