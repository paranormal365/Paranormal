using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The Sansung problem, one taxonomy over: typos, staleness and merges in the experience taxonomy.
/// </summary>
/// <remarks>
/// <para>The equipment catalog got all three treatments — typo detection at the moment of typing,
/// orphan cleanup when the last user of a name goes away, and rename-as-merge. The experience
/// taxonomy grows exactly the same way, by groups proposing words they need tonight, and had
/// <b>none</b> of them. It was in fact worse off: a group cannot delete a type it proposed, because
/// the only delete lives behind an app-administrator screen, so a mistyping was permanent.</para>
///
/// <para><b>These tests lean positive on purpose.</b> Proving a merge is refused in the wrong
/// direction is easy and proves little. What matters is that the taggings actually arrive at the
/// surviving type, that an ordinary rename still just renames, that a genuinely new word is still
/// accepted, and that cleanup removes the mistyping <i>without</i> touching the seeded vocabulary
/// sitting next to it.</para>
/// </remarks>
public sealed class ExperienceTaxonomyStalenessTests
{
    private static readonly Guid AdminId  = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid OrgId    = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IMapper CreateMapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<ExperienceTypeRecord>(It.IsAny<object>()))
         .Returns<object>(o => o is not ExperienceType t
             ? new ExperienceTypeRecord { Name = "" }
             : new ExperienceTypeRecord
             {
                 Id = t.Id,
                 ExperienceCategoryId = t.ExperienceCategoryId,
                 Name = t.Name,
                 IsApproved = t.IsApproved,
                 ProposedByOrganizationId = t.ProposedByOrganizationId,
                 ApprovedByAppUserId = t.ApprovedByAppUserId,
                 DateCreated = t.DateCreated,
             });
        return m.Object;
    }

    private static ControllerContext AsUser(Guid userId, params string[] roles)
        => new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    .. roles.Select(r => new Claim(ClaimTypes.Role, r)),
                ], "Bearer"))
            }
        };

    private static AdminExperienceTypeController Admin(IDbContextFactory<BenDataContext> f)
        => new(f, CreateMapper()) { ControllerContext = AsUser(AdminId, RoleNames.SuperAdmin) };

    private static OrgExperienceTypeController Org(IDbContextFactory<BenDataContext> f)
        => new(f, CreateMapper(), Mock.Of<IAuditLogService>())
        { ControllerContext = AsUser(MemberId) };

    // ── The world ────────────────────────────────────────────────────────────
    // One category, one reviewed word ("Knocking"), and a group that may propose.

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory, Guid CategoryId, Guid KnockingId);

    private static async Task<World> SeedAsync()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();

        var categoryId = Guid.NewGuid();
        db.ExperienceCategories.Add(new ExperienceCategory
        {
            Id = categoryId, Name = "Auditory", IsApproved = true, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow });

        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = MemberId,
            Role = OrganizationMemberRole.Administrator, IsActive = true,
            DateCreated = DateTime.UtcNow,
        });

        var knockingId = Guid.NewGuid();
        db.ExperienceTypes.Add(new ExperienceType
        {
            Id = knockingId, ExperienceCategoryId = categoryId, Name = "Knocking",
            IsActive = true, IsApproved = true,
            // Reviewed: a human stamped it. This is what separates seeded vocabulary from a word
            // somebody proposed last night, and every rule below turns on it.
            ApprovedByAppUserId = AdminId, DateApproved = DateTime.UtcNow,
            DateCreated = DateTime.UtcNow.AddYears(-1), CreatedByAppUserId = AdminId,
        });

        await db.SaveChangesAsync();
        return new World(factory, categoryId, knockingId);
    }

    /// <summary>Adds an unreviewed type as if a group had proposed it, and tags one entry with it.</summary>
    private static async Task<(Guid TypeId, Guid EntryId)> ProposeAndTagAsync(
        World w, string name, bool tag = true)
    {
        await using var db = await w.Factory.CreateDbContextAsync();

        var typeId = Guid.NewGuid();
        db.ExperienceTypes.Add(new ExperienceType
        {
            Id = typeId, ExperienceCategoryId = w.CategoryId, Name = name,
            IsActive = true, IsApproved = true,
            ApprovedByAppUserId = null,          // live, but nobody has looked at it
            ProposedByOrganizationId = OrgId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = MemberId,
        });

        var entryId = Guid.NewGuid();
        if (tag)
        {
            db.CaseTimelineEntryExperienceTypes.Add(new CaseTimelineEntryExperienceType
            { CaseTimelineEntryId = entryId, ExperienceTypeId = typeId });
        }

        await db.SaveChangesAsync();
        return (typeId, entryId);
    }

    // ── Proposing: the word that is genuinely new still gets through ──────────

    /// <summary>
    /// The positive case, and the one that matters most. A typo check that also blocked real new
    /// vocabulary would be worse than no check at all — groups would learn to click past it.
    /// </summary>
    [Theory]
    [InlineData("Footsteps")]
    [InlineData("Whistling")]
    [InlineData("Disembodied Voices")]
    [InlineData("Growling")]
    public async Task A_genuinely_new_type_is_created(string name)
    {
        var w = await SeedAsync();

        var result = await Org(w.Factory).Add(
            OrgId, new AddOrgExperienceTypeRequest(w.CategoryId, name, null), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.True(await db.ExperienceTypes.AnyAsync(t => t.Name == name));
    }

    [Fact]
    public async Task A_probable_typo_is_offered_the_word_it_missed()
    {
        var w = await SeedAsync();

        var result = await Org(w.Factory).Add(
            OrgId, new AddOrgExperienceTypeRequest(w.CategoryId, "Knockign", null), default);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var offer = Assert.IsType<ProbableDuplicateResponse>(conflict.Value);
        Assert.Contains("Knocking", offer.DidYouMean);

        // Refused, not created — the whole point is that the mistyping never reaches the taxonomy.
        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.False(await db.ExperienceTypes.AnyAsync(t => t.Name == "Knockign"));
    }

    /// <summary>
    /// Having been shown the near-miss and said "no, mine is different", the person gets their word.
    /// Without this the check would be a wall rather than a question.
    /// </summary>
    [Fact]
    public async Task Confirming_it_is_distinct_creates_it_anyway()
    {
        var w = await SeedAsync();

        var result = await Org(w.Factory).Add(
            OrgId,
            new AddOrgExperienceTypeRequest(w.CategoryId, "Knockign", null, ConfirmDistinct: true),
            default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.True(await db.ExperienceTypes.AnyAsync(t => t.Name == "Knockign"));
    }

    /// <summary>
    /// An unreviewed word is never suggested. Offering somebody else's typo would spread it rather
    /// than catch it.
    /// </summary>
    [Fact]
    public async Task An_unreviewed_type_is_not_offered_as_a_suggestion()
    {
        var w = await SeedAsync();
        await ProposeAndTagAsync(w, "Rustling");

        var result = await Org(w.Factory).Add(
            OrgId, new AddOrgExperienceTypeRequest(w.CategoryId, "Rustlign", null), default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task The_same_name_in_a_different_case_returns_the_existing_type()
    {
        var w = await SeedAsync();

        var result = await Org(w.Factory).Add(
            OrgId, new AddOrgExperienceTypeRequest(w.CategoryId, "KNOCKING", null), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(w.KnockingId, Assert.IsType<ExperienceTypeRecord>(ok.Value).Id);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(1, await db.ExperienceTypes.CountAsync(t => t.Name.ToLower() == "knocking"));
    }

    /// <summary>
    /// The administrator's own create path never deduped at all, so it could produce the second
    /// "Knocking" that everybody else was being stopped from producing.
    /// </summary>
    [Fact]
    public async Task The_admin_create_path_dedupes_too()
    {
        var w = await SeedAsync();

        var result = await Admin(w.Factory).Create(
            w.CategoryId, new UpsertExperienceTypeRequest("knocking", null, null, 100, true), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(1, await db.ExperienceTypes.CountAsync(t => t.Name.ToLower() == "knocking"));
    }

    // ── Renaming ─────────────────────────────────────────────────────────────

    /// <summary>An ordinary rename is still an ordinary rename.</summary>
    [Fact]
    public async Task Renaming_to_a_free_name_just_renames()
    {
        var w = await SeedAsync();
        var (typoId, _) = await ProposeAndTagAsync(w, "Rustlign");

        var result = await Admin(w.Factory).Update(
            w.CategoryId, typoId,
            new UpsertExperienceTypeRequest("Rustling", null, null, 500, true), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await w.Factory.CreateDbContextAsync();
        var renamed = await db.ExperienceTypes.FirstAsync(t => t.Id == typoId);
        Assert.Equal("Rustling", renamed.Name);

        // The tagging follows the row, because nothing about the row moved.
        Assert.True(await db.CaseTimelineEntryExperienceTypes.AnyAsync(x => x.ExperienceTypeId == typoId));
    }

    [Fact]
    public async Task Renaming_onto_an_existing_name_offers_a_merge_instead()
    {
        var w = await SeedAsync();
        var (typoId, _) = await ProposeAndTagAsync(w, "Knockign");

        var result = await Admin(w.Factory).Update(
            w.CategoryId, typoId,
            new UpsertExperienceTypeRequest("Knocking", null, null, 500, true), default);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var offer = Assert.IsType<TaxonomyMergeOffer>(conflict.Value);

        Assert.Equal(typoId, offer.SourceId);
        Assert.Equal(w.KnockingId, offer.TargetId);

        // Nothing happened yet — the offer is an offer.
        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal("Knockign", (await db.ExperienceTypes.FirstAsync(t => t.Id == typoId)).Name);
    }

    /// <summary>
    /// Two categories may each have a "Shadow" — the name only has to be unique within its own
    /// branch, and refusing across the whole taxonomy would be wrong.
    /// </summary>
    [Fact]
    public async Task The_same_name_under_a_different_category_is_not_a_clash()
    {
        var w = await SeedAsync();

        var otherCategoryId = Guid.NewGuid();
        var otherTypeId = Guid.NewGuid();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.ExperienceCategories.Add(new ExperienceCategory
            {
                Id = otherCategoryId, Name = "Visual", IsApproved = true, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
            });
            db.ExperienceTypes.Add(new ExperienceType
            {
                Id = otherTypeId, ExperienceCategoryId = otherCategoryId, Name = "Tapping",
                IsActive = true, IsApproved = true, ApprovedByAppUserId = AdminId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
            });
            await db.SaveChangesAsync();
        }

        var result = await Admin(w.Factory).Update(
            otherCategoryId, otherTypeId,
            new UpsertExperienceTypeRequest("Knocking", null, null, 500, true), default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    // ── Merging ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The heart of it: the tagging has to arrive at the surviving type. A merge that removed the
    /// duplicate and lost what it was tagged on would quietly erase what somebody recorded about
    /// their own night.
    /// </summary>
    [Fact]
    public async Task Merging_moves_the_taggings_to_the_survivor()
    {
        var w = await SeedAsync();
        var (typoId, entryId) = await ProposeAndTagAsync(w, "Knockign");

        var result = await Admin(w.Factory).Merge(w.CategoryId, typoId, w.KnockingId, default);
        Assert.IsType<NoContentResult>(result);

        await using var db = await w.Factory.CreateDbContextAsync();

        Assert.False(await db.ExperienceTypes.AnyAsync(t => t.Id == typoId));
        Assert.True(await db.CaseTimelineEntryExperienceTypes.AnyAsync(
            x => x.CaseTimelineEntryId == entryId && x.ExperienceTypeId == w.KnockingId));
    }

    /// <summary>
    /// An entry tagged with both types keeps one tag, not two — and the merge does not fall over
    /// trying to write a join row that already exists.
    /// </summary>
    [Fact]
    public async Task An_entry_tagged_with_both_ends_up_tagged_once()
    {
        var w = await SeedAsync();
        var (typoId, entryId) = await ProposeAndTagAsync(w, "Knockign");

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.CaseTimelineEntryExperienceTypes.Add(new CaseTimelineEntryExperienceType
            { CaseTimelineEntryId = entryId, ExperienceTypeId = w.KnockingId });
            await db.SaveChangesAsync();
        }

        var result = await Admin(w.Factory).Merge(w.CategoryId, typoId, w.KnockingId, default);
        Assert.IsType<NoContentResult>(result);

        await using var check = await w.Factory.CreateDbContextAsync();
        Assert.Equal(1, await check.CaseTimelineEntryExperienceTypes
            .CountAsync(x => x.CaseTimelineEntryId == entryId));
    }

    /// <summary>
    /// Merging the reviewed word into the unreviewed one is the direction people get wrong, and the
    /// result would be a taxonomy where the endorsed word vanished and the slip survived.
    /// </summary>
    [Fact]
    public async Task Merging_a_reviewed_type_into_an_unreviewed_one_is_refused()
    {
        var w = await SeedAsync();
        var (typoId, _) = await ProposeAndTagAsync(w, "Knockign");

        var result = await Admin(w.Factory).Merge(w.CategoryId, w.KnockingId, typoId, default);

        Assert.IsType<ConflictObjectResult>(result);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.True(await db.ExperienceTypes.AnyAsync(t => t.Id == w.KnockingId));
    }

    [Fact]
    public async Task Merging_across_categories_is_refused()
    {
        var w = await SeedAsync();
        var (typoId, _) = await ProposeAndTagAsync(w, "Knockign");

        var otherCategoryId = Guid.NewGuid();
        var elsewhereId = Guid.NewGuid();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.ExperienceCategories.Add(new ExperienceCategory
            {
                Id = otherCategoryId, Name = "Visual", IsApproved = true, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
            });
            db.ExperienceTypes.Add(new ExperienceType
            {
                Id = elsewhereId, ExperienceCategoryId = otherCategoryId, Name = "Shadow Figure",
                IsActive = true, IsApproved = true, ApprovedByAppUserId = AdminId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
            });
            await db.SaveChangesAsync();
        }

        // Addressed through the source's category, so the target simply is not found there.
        var result = await Admin(w.Factory).Merge(w.CategoryId, typoId, elsewhereId, default);
        Assert.IsType<NotFoundResult>(result);
    }

    // ── Deleting ─────────────────────────────────────────────────────────────

    /// <summary>An unused type deletes cleanly — the ordinary case still works.</summary>
    [Fact]
    public async Task Deleting_an_unused_type_removes_it()
    {
        var w = await SeedAsync();
        var (typeId, _) = await ProposeAndTagAsync(w, "Rustling", tag: false);

        var result = await Admin(w.Factory).Delete(w.CategoryId, typeId, default);
        Assert.IsType<NoContentResult>(result);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.False(await db.ExperienceTypes.AnyAsync(t => t.Id == typeId));
    }

    /// <summary>
    /// A type in use used to be deleted blindly, which failed at the foreign key and surfaced as a
    /// 500 telling the administrator nothing. It is now refused, in words, with the alternative.
    /// </summary>
    [Fact]
    public async Task Deleting_a_type_in_use_is_refused_and_says_why()
    {
        var w = await SeedAsync();
        var (typeId, _) = await ProposeAndTagAsync(w, "Rustling");

        var result = await Admin(w.Factory).Delete(w.CategoryId, typeId, default);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var message = Assert.IsType<string>(conflict.Value);
        Assert.Contains("Rustling", message);
        Assert.Contains("Reject", message);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.True(await db.ExperienceTypes.AnyAsync(t => t.Id == typeId));
    }

    // ── Staleness: the orphan sweep ──────────────────────────────────────────

    /// <summary>
    /// Ben's case exactly. Invent a word, mistype it, untag it — and the word goes with it, because
    /// no human ever endorsed it and nothing else uses it.
    /// </summary>
    [Fact]
    public async Task Untagging_the_last_use_of_an_unreviewed_proposed_type_removes_it()
    {
        var w = await SeedAsync();
        var (typoId, _) = await ProposeAndTagAsync(w, "Knockign");

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.CaseTimelineEntryExperienceTypes.RemoveRange(
                db.CaseTimelineEntryExperienceTypes.Where(x => x.ExperienceTypeId == typoId));
            await db.SaveChangesAsync();
            await TaxonomyCleanup.RemoveOrphanedExperienceTypesAsync(db, [typoId], default);
        }

        await using var check = await w.Factory.CreateDbContextAsync();
        Assert.False(await check.ExperienceTypes.AnyAsync(t => t.Id == typoId));
    }

    /// <summary>
    /// The sweep must not touch the shared vocabulary sitting beside it. A reviewed type that
    /// nothing happens to be tagged with today is still a word the taxonomy means to offer.
    /// </summary>
    [Fact]
    public async Task The_sweep_leaves_reviewed_and_seeded_vocabulary_alone()
    {
        var w = await SeedAsync();

        await using (var db = await w.Factory.CreateDbContextAsync())
            await TaxonomyCleanup.RemoveOrphanedExperienceTypesAsync(db, [w.KnockingId], default);

        await using var check = await w.Factory.CreateDbContextAsync();
        Assert.True(await check.ExperienceTypes.AnyAsync(t => t.Id == w.KnockingId));
    }

    /// <summary>A proposed type still tagged somewhere else stays put.</summary>
    [Fact]
    public async Task The_sweep_leaves_a_type_that_is_still_in_use()
    {
        var w = await SeedAsync();
        var (typoId, _) = await ProposeAndTagAsync(w, "Knockign");

        await using (var db = await w.Factory.CreateDbContextAsync())
            await TaxonomyCleanup.RemoveOrphanedExperienceTypesAsync(db, [typoId], default);

        await using var check = await w.Factory.CreateDbContextAsync();
        Assert.True(await check.ExperienceTypes.AnyAsync(t => t.Id == typoId));
    }

    /// <summary>
    /// A type an administrator reviewed and endorsed survives losing its last tagging, even though
    /// it was originally proposed by a group. Review is the line, not origin.
    /// </summary>
    [Fact]
    public async Task A_reviewed_proposal_survives_losing_its_last_tagging()
    {
        var w = await SeedAsync();
        var (typeId, _) = await ProposeAndTagAsync(w, "Rustling", tag: false);

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var t = await db.ExperienceTypes.FirstAsync(x => x.Id == typeId);
            t.ApprovedByAppUserId = AdminId;
            t.DateApproved = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await TaxonomyCleanup.RemoveOrphanedExperienceTypesAsync(db, [typeId], default);
        }

        await using var check = await w.Factory.CreateDbContextAsync();
        Assert.True(await check.ExperienceTypes.AnyAsync(t => t.Id == typeId));
    }
}
