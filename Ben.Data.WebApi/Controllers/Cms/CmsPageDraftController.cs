using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Cms;

/// <summary>
/// Working on a live page without the public watching.
/// </summary>
/// <remarks>
/// <para>Ben's *"make them live when they are ready"*. Until now editing a published page edited the
/// live page immediately — the publish flag said whether a page was visible, never whether the
/// version being typed was.</para>
///
/// <para>A draft is a whole <see cref="OrganizationPage"/> row with its own sections, pointing at
/// the page it will replace. That shape is what lets the public read path stay untouched: every
/// public query already filters <c>IsPublished &amp;&amp; IsPublic</c>, and a draft is created with
/// both false, so it is invisible by construction rather than by every future query remembering to
/// exclude it.</para>
///
/// <para><b>Copy-on-write, and only for published pages.</b> Nobody can see an unpublished page, so
/// editing one directly is already safe and a draft would just be ceremony. A draft appears the
/// first time somebody edits a page that is actually live.</para>
///
/// <para><b>Publishing copies onto the live row and deletes the draft</b> rather than swapping ids.
/// The live page keeps its id, so every link to it, every permission row and every case attached to
/// it survives — swapping would quietly break all of them.</para>
///
/// <para>Ben chose this over version history (2026-08-17); that would also have given rollback, at
/// noticeably more cost.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/pages/{pageId:guid}/draft")]
public sealed class CmsPageDraftController : OrgCmsControllerBase
{
    private readonly IAuditLogService _auditLog;

    public CmsPageDraftController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper,
        IOrganizationSecurityService security, IAuditLogService auditLog)
        : base(dbFactory, mapper, security) => _auditLog = auditLog;

    /// <summary>Whether this page has a draft waiting, and whether it needs one.</summary>
    [HttpGet]
    public async Task<ActionResult<CmsDraftStateResponse>> GetState(Guid orgId, Guid pageId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage,
                                        OrganizationSecurityAction.Read, ct))
            return NotFound();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        // Answers for either id. The editor is routed by whichever page is open, and it should not
        // have to know in advance whether that is the live page or its draft — asking is the whole
        // point of this call.
        var asked = await db.OrganizationPages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pageId && p.OrganizationId == orgId, ct);
        if (asked is null) return NotFound();

        var live = asked.DraftOfOrganizationPageId is Guid liveId
            ? await db.OrganizationPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == liveId, ct)
            : asked;
        if (live is null) return NotFound();

        var draft = await db.OrganizationPages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.DraftOfOrganizationPageId == live.Id, ct);

        return Ok(new CmsDraftStateResponse(
            LivePageId:   live.Id,
            DraftPageId:  draft?.Id,
            NeedsDraft:   live.IsPublished,
            DraftStarted: draft?.DateCreated));
    }

    /// <summary>
    /// Starts a draft of this page, or returns the one already open.
    /// </summary>
    /// <remarks>
    /// Idempotent on purpose. Two editors opening the page at once, or one double-clicking, must not
    /// produce two drafts — and the unique filtered index behind this would otherwise turn the
    /// second into a 500. Refused on a page that is not published: there is nothing to protect the
    /// public from, and a draft of an invisible page is just a second place to lose work.
    /// </remarks>
    [HttpPost]
    public async Task<ActionResult<CmsDraftStateResponse>> StartDraft(Guid orgId, Guid pageId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage,
                                        OrganizationSecurityAction.Update, ct))
            return NotFound();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        var page = await db.OrganizationPages.AsNoTracking()
            .Include(p => p.CmsSections)
            .FirstOrDefaultAsync(p => p.Id == pageId && p.OrganizationId == orgId
                                   && p.DraftOfOrganizationPageId == null, ct);
        if (page is null) return NotFound();

        if (!page.IsPublished)
            return Conflict("That page isn't published, so it can be edited directly.");

        var existing = await db.OrganizationPages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.DraftOfOrganizationPageId == pageId, ct);
        if (existing is not null)
            return Ok(new CmsDraftStateResponse(pageId, existing.Id, true, existing.DateCreated));

        var draftId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.OrganizationPages.Add(new OrganizationPage
        {
            Id                        = draftId,
            OrganizationId            = orgId,
            DraftOfOrganizationPageId = pageId,
            ParentPageId              = page.ParentPageId,
            PageTitle                 = page.PageTitle,
            UrlName                   = page.UrlName,
            PageHtml                  = page.PageHtml,
            // Never published, never public — this is what keeps a draft out of every public query
            // without any of them knowing drafts exist.
            IsPublished               = false,
            IsPublic                  = false,
            // IsHome stays false: two rows claiming to be the home page is a state no query expects,
            // and publishing copies the live page's own flag back over anyway.
            IsHome                    = false,
            SortOrder                 = page.SortOrder,
            CaseId                    = page.CaseId,
            DateCreated               = now,
            CreatedByAppUserId        = userId.Value,
        });

        foreach (var section in page.CmsSections.OrderBy(s => s.SortOrder))
            db.CmsSections.Add(new CmsSection
            {
                Id                 = Guid.NewGuid(),
                OrganizationPageId = draftId,
                SectionType        = section.SectionType,
                Title              = section.Title,
                ContentJson        = section.ContentJson,
                SortOrder          = section.SortOrder,
                IsActive           = section.IsActive,
                DateCreated        = now,
                CreatedByAppUserId = userId.Value,
            });

        await db.SaveChangesAsync(ct);
        return Ok(new CmsDraftStateResponse(pageId, draftId, true, now));
    }

    /// <summary>
    /// Makes the draft the live page.
    /// </summary>
    /// <remarks>
    /// The draft's content is copied onto the live row inside one save, then the draft and its
    /// sections are removed. One transaction, so a half-published page cannot survive a failure
    /// midway — the state that would be hardest to notice and worst to be in.
    /// </remarks>
    [HttpPost("publish")]
    public async Task<IActionResult> Publish(Guid orgId, Guid pageId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage,
                                        OrganizationSecurityAction.Update, ct))
            return NotFound();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        var live = await db.OrganizationPages
            .Include(p => p.CmsSections)
            .FirstOrDefaultAsync(p => p.Id == pageId && p.OrganizationId == orgId
                                   && p.DraftOfOrganizationPageId == null, ct);
        if (live is null) return NotFound();

        var draft = await db.OrganizationPages
            .Include(p => p.CmsSections)
            .FirstOrDefaultAsync(p => p.DraftOfOrganizationPageId == pageId, ct);
        if (draft is null) return Conflict("There is no draft to publish.");

        var now = DateTime.UtcNow;

        // Captured before the copy: an update audit with identical before/after says nothing.
        var before = new { live.PageTitle, live.UrlName, live.PageHtml, live.SortOrder, live.ParentPageId };

        live.PageTitle          = draft.PageTitle;
        live.UrlName            = draft.UrlName;
        live.PageHtml           = draft.PageHtml;
        live.SortOrder          = draft.SortOrder;
        live.ParentPageId       = draft.ParentPageId;
        live.DateUpdated        = now;
        live.UpdatedByAppUserId = userId.Value;
        // IsHome, IsPublished, IsPublic and CaseId are deliberately not copied. They are properties
        // of the page's place in the site, decided on the page itself, not things a draft is for.

        db.CmsSections.RemoveRange(live.CmsSections);
        foreach (var section in draft.CmsSections.OrderBy(s => s.SortOrder))
            db.CmsSections.Add(new CmsSection
            {
                Id                 = Guid.NewGuid(),
                OrganizationPageId = live.Id,
                SectionType        = section.SectionType,
                Title              = section.Title,
                ContentJson        = section.ContentJson,
                SortOrder          = section.SortOrder,
                IsActive           = section.IsActive,
                DateCreated        = now,
                CreatedByAppUserId = userId.Value,
            });

        db.CmsSections.RemoveRange(draft.CmsSections);
        db.OrganizationPages.Remove(draft);

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(
            nameof(OrganizationPage), live.Id, before, live, userId.Value,
            Data.Common.Constants.AppSources.WebApi));

        return NoContent();
    }

    /// <summary>Throws the draft away, leaving the live page exactly as it was.</summary>
    [HttpDelete]
    public async Task<IActionResult> Discard(Guid orgId, Guid pageId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage,
                                        OrganizationSecurityAction.Update, ct))
            return NotFound();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        // Scoped through the live page's org, so knowing a draft id is not enough to delete it.
        var liveExists = await db.OrganizationPages.AsNoTracking()
            .AnyAsync(p => p.Id == pageId && p.OrganizationId == orgId, ct);
        if (!liveExists) return NotFound();

        var draft = await db.OrganizationPages
            .Include(p => p.CmsSections)
            .FirstOrDefaultAsync(p => p.DraftOfOrganizationPageId == pageId, ct);
        if (draft is null) return NotFound();

        db.CmsSections.RemoveRange(draft.CmsSections);
        db.OrganizationPages.Remove(draft);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }
}
