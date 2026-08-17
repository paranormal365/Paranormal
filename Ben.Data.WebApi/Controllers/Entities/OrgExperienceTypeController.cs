using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.SeedData;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Lets a group add an experience type that the shared taxonomy is missing.
/// </summary>
/// <remarks>
/// <para>The taxonomy is global, and until now only a SuperAdmin could extend it — so a group
/// tagging an occurrence had to either force it into an approximate existing type or go without.
/// The schema always had a proposal path (<c>IsApproved</c>, <c>ProposedByOrganizationId</c>) and
/// the admin screen always had Approve buttons, but nothing ever created an unapproved row, so
/// that whole half was unreachable.</para>
///
/// <para>New types go live <b>immediately</b> rather than waiting in a queue: a group tagging
/// tonight's occurrence cannot wait for someone to wake up and approve a word. Review is
/// after-the-fact — the entry is marked as unreviewed, app administrators are notified, and they
/// confirm it or reject it later.</para>
///
/// <para>"Unreviewed" needs no new column: an entry that is approved but has no
/// <c>ApprovedByAppUserId</c> is one no human has looked at. SuperAdmin-created entries stamp that
/// field on creation, so they never appear in the review queue.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/organizations/{orgId:guid}/experience-types")]
public sealed class OrgExperienceTypeController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;
    private readonly IAuditLogService _auditLog;

    public OrgExperienceTypeController(
        IDbContextFactory<BenDataContext> db, IMapper mapper, IAuditLogService auditLog)
    {
        _db = db;
        _mapper = mapper;
        _auditLog = auditLog;
    }

    /// <summary>Adds a type to an existing category on behalf of a group.</summary>
    [HttpPost]
    public async Task<ActionResult<ExperienceTypeRecord>> Add(
        Guid orgId, [FromBody] AddOrgExperienceTypeRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("A name is required.");
        if (name.Length > 100) return BadRequest("Name must be 100 characters or fewer.");

        await using var db = await _db.CreateDbContextAsync(ct);

        if (!await CanAdministerAsync(db, orgId, userId, ct)) return Forbid();

        // Categories stay SuperAdmin-only. A group filling a gap in a category is a small,
        // reversible act; inventing a whole top-level branch of the taxonomy is not.
        var category = await db.ExperienceCategories
            .FirstOrDefaultAsync(c => c.Id == request.ExperienceCategoryId && c.IsApproved && c.IsActive, ct);
        if (category is null) return NotFound("Category not found.");

        // Case-insensitive, because "Knocking" and "knocking" are the same type to everyone except
        // a database. Returns the existing row rather than erroring — the caller wanted a type
        // with this name in this category, and there is one.
        var existing = await db.ExperienceTypes.FirstOrDefaultAsync(
            t => t.ExperienceCategoryId == category.Id && t.Name.ToLower() == name.ToLower(), ct);
        if (existing is not null) return Ok(_mapper.Map<ExperienceTypeRecord>(existing));

        // Nothing matches exactly, but something close might — and this is the only cheap moment to
        // ask. Afterwards it takes an app administrator noticing, and a merge. Same treatment the
        // equipment catalog gives "Sansung": the type is a shared word, and a near-miss splits it.
        var nearMisses = await FindProbableTyposAsync(db, category.Id, name, ct);
        if (nearMisses.Count > 0 && !request.ConfirmDistinct)
            return Conflict(new ProbableDuplicateResponse(name, nearMisses));

        var entity = new ExperienceType
        {
            Id                       = Guid.NewGuid(),
            ExperienceCategoryId     = category.Id,
            Name                     = name,
            Description              = request.Description?.Trim(),
            SortOrder                = 500,
            IsActive                 = true,
            // Live immediately, but with ApprovedByAppUserId left null — that null is what marks
            // it as awaiting a human look. DateApproved stays null for the same reason.
            IsApproved               = true,
            ProposedByOrganizationId = orgId,
            DateCreated              = DateTime.UtcNow,
            CreatedByAppUserId       = userId,
        };
        db.ExperienceTypes.Add(entity);

        await NotifyAppAdministratorsAsync(db, entity, category.Name, orgId, userId, ct);

        await db.SaveChangesAsync(ct);
        await _auditLog.LogCreateAsync(nameof(ExperienceType), entity.Id, entity, userId, AppSources.WebApi);

        return Ok(_mapper.Map<ExperienceTypeRecord>(entity));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Approved types in the same category whose names look like mistypings of this one.
    /// </summary>
    /// <remarks>
    /// <para><b>Only approved types, and only within the category.</b> Suggesting somebody else's
    /// unapproved typo would spread it rather than catch it. Confining it to the category matters
    /// more here than it does for equipment brands: "Shadow" under <i>Visual</i> and "Shadow" under
    /// <i>Tactile</i> would be a strange pair of words to conflate, and across a taxonomy this
    /// broad the cross-category near-misses are mostly noise.</para>
    /// </remarks>
    private static async Task<List<string>> FindProbableTyposAsync(
        BenDataContext db, Guid categoryId, string name, CancellationToken ct)
    {
        // Length-bounded in the query so this reads a handful of rows rather than the taxonomy:
        // anything more than two characters different in length is not a typo of this.
        var lower = name.Length - 2;
        var upper = name.Length + 2;

        var candidates = await db.ExperienceTypes.AsNoTracking()
            .Where(t => t.ExperienceCategoryId == categoryId
                     && t.IsApproved && t.ApprovedByAppUserId != null
                     && t.Name.Length >= lower && t.Name.Length <= upper)
            .Select(t => t.Name)
            .ToListAsync(ct);

        return [.. candidates.Where(c => NameSimilarity.IsProbableTypo(name, c)).OrderBy(c => c)];
    }

    private static async Task<bool> CanAdministerAsync(
        BenDataContext db, Guid orgId, Guid userId, CancellationToken ct)
        => await db.OrganizationUserMemberships.AsNoTracking().AnyAsync(
            m => m.OrganizationId == orgId
              && m.AppUserId == userId
              && m.IsActive
              && (m.Role == OrganizationMemberRole.Owner
               || m.Role == OrganizationMemberRole.Administrator), ct);

    /// <summary>
    /// Tells every app administrator that a new type is live and unreviewed.
    /// </summary>
    /// <remarks>
    /// Written into the same system-message inbox the notification bell already counts, so this
    /// needs no new delivery mechanism. Rows are added to the caller's change set, not saved
    /// separately — the notice and the type it announces commit together or not at all.
    /// </remarks>
    private static async Task NotifyAppAdministratorsAsync(
        BenDataContext db, ExperienceType entity, string categoryName,
        Guid orgId, Guid userId, CancellationToken ct)
    {
        var adminIds = await db.UserRoles
            .Join(db.Roles.Where(r => r.Name == RoleNames.SuperAdmin || r.Name == RoleNames.Admin),
                  ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
            .Distinct()
            .ToListAsync(ct);

        if (adminIds.Count == 0) return;

        var orgName = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == orgId).Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "A group";

        var message = new UserMessage
        {
            Id                 = Guid.NewGuid(),
            UserMessageTypeId  = OrganizationSeeder.TaxonomyReviewMessageTypeId,
            MessageSubject     = $"New experience type: {entity.Name}",
            MessageBody        =
                $"<strong>{orgName}</strong> added the experience type <strong>{entity.Name}</strong> " +
                $"under <strong>{categoryName}</strong>. It is live now and in use. " +
                "Review it on the Experience Taxonomy page — confirm it to clear this notice, or " +
                "reject it to remove the type and strip it from any entries tagged with it.",
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.UserMessages.Add(message);

        foreach (var adminId in adminIds)
        {
            db.UserMessageTos.Add(new UserMessageTo
            {
                Id          = Guid.NewGuid(),
                MessageId   = message.Id,
                ToAppUserId = adminId,
            });
        }
    }
}

/// <summary>A group adding a missing type to an existing category.</summary>
/// <param name="ExperienceCategoryId">The approved category the new type belongs under.</param>
/// <param name="Name">The type's name, unique within the category and case-insensitively so.</param>
/// <param name="Description">Optional explanation of what the type covers.</param>
/// <param name="ConfirmDistinct">
/// Set once the person has been shown the close matches and said theirs is genuinely different.
/// Without it a probable typo is refused with the suggestions rather than silently created.
/// </param>
public sealed record AddOrgExperienceTypeRequest(
    Guid ExperienceCategoryId,
    string? Name,
    string? Description,
    bool ConfirmDistinct = false);
