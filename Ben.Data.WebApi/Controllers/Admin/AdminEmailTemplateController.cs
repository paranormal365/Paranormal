using System.Security.Claims;
using Ben.Data.Common;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Mail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// Writing the letters the site sends (item 246).
/// </summary>
/// <remarks>
/// <para><b>SuperAdmin only.</b> These letters go to everybody, carry password-reset links and
/// door passes, and a template is HTML that lands in software nobody here controls.</para>
///
/// <para><b>Nothing here can stop a letter going.</b> Every write leaves the built-in letter in
/// place until somebody publishes, and <see cref="MailComposer"/> falls back to it on any
/// failure.</para>
/// </remarks>
[ApiController]
[Route("api/admin/email-templates")]
[Authorize(Policy = RoleNames.SuperAdmin)]
public sealed class AdminEmailTemplateController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly MailComposer _composer;
    private readonly SiteIdentity _site;

    public AdminEmailTemplateController(IDbContextFactory<BenDataContext> db,
                                        MailComposer composer,
                                        IOptions<SiteIdentity> site)
    {
        _db = db;
        _composer = composer;
        _site = site.Value;
    }

    private Guid Me()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    /// <summary>Every letter the site sends, and whether somebody has written one.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EmailTemplateSummary>>> All(CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var rows = await db.EmailTemplates.AsNoTracking()
            .ToDictionaryAsync(t => t.Kind, ct);

        return Ok(MailKinds.All.Select(k =>
        {
            rows.TryGetValue(k.Key, out var row);
            return new EmailTemplateSummary(
                k.Key, k.Title, k.Description,
                IsLive: row?.IsLive ?? false,
                HasUnpublishedDraft: row?.HasUnpublishedDraft ?? false,
                PublishedUtc: row?.PublishedUtc,
                DraftSavedUtc: row?.DraftSavedUtc);
        }).ToList());
    }

    /// <summary>One letter, with everything its author may put in it.</summary>
    [HttpGet("{kindKey}")]
    public async Task<ActionResult<EmailTemplateDetail>> One(string kindKey, CancellationToken ct)
    {
        if (MailKinds.Find(kindKey) is not { } kind) return NotFound();

        await using var db = await _db.CreateDbContextAsync(ct);
        var row = await db.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Kind == kindKey, ct);

        var tables = MailTemplateSchema.For(kind, db);

        return Ok(new EmailTemplateDetail(
            kind.Key, kind.Title, kind.Description,
            Subject: row?.Subject,
            BodyHtml: row?.BodyHtml,
            DraftSubject: row?.DraftSubject ?? row?.Subject,
            DraftBodyHtml: row?.DraftBodyHtml ?? row?.BodyHtml,
            PublishedUtc: row?.PublishedUtc,
            DraftSavedUtc: row?.DraftSavedUtc,
            Tables: tables.Select(t => new EmailTemplateTable(
                t.Name, t.Columns.Select(c => new EmailTemplateColumn(c.Name, c.Type)).ToList())).ToList(),
            Common: MailTokens.Common.Select(c => new EmailTemplateToken(c.Name, c.Looks, c.What)).ToList()));
    }

    /// <summary>Keeps what the author is working on. Nobody receives it.</summary>
    [HttpPut("{kindKey}/draft")]
    public async Task<ActionResult<EmailTemplateSaved>> SaveDraft(
        string kindKey, [FromBody] SaveEmailTemplateRequest request, CancellationToken ct)
    {
        if (MailKinds.Find(kindKey) is not { } kind) return NotFound();

        // Refused at authoring time, which is the half that makes rendering a missing token as
        // nothing safe rather than silently lossy.
        var bad = MailTokens.Unresolvable(request.Subject, kind)
            .Concat(MailTokens.Unresolvable(request.BodyHtml, kind))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (bad.Count > 0)
            // A plain sentence, not a record: WebApiClient.SendExpectingReasonAsync DROPS a
            // non-2xx body that starts with "{", so that a framework ProblemDetails blob can never
            // be shown to a person. A refusal wrapped in JSON is discarded with it, and the page
            // says "couldn't save that" instead of the reason — which is what happened here first.
            return BadRequest(
                $"This letter cannot fill in {string.Join(", ", bad.Select(b => "{" + b + "}"))}. "
              + "Pick a table it carries, or one of the ready-made tokens.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var me = Me();

        var row = await db.EmailTemplates.FirstOrDefaultAsync(t => t.Kind == kindKey, ct);
        if (row is null)
        {
            row = new EmailTemplate
            {
                Id = Guid.NewGuid(), Kind = kindKey, DateCreated = now, CreatedByAppUserId = me,
            };
            db.EmailTemplates.Add(row);
        }

        row.DraftSubject = request.Subject;
        row.DraftBodyHtml = request.BodyHtml;
        row.DraftSavedUtc = now;
        row.DraftAuthorAppUserId = me;
        row.DateUpdated = now;
        row.UpdatedByAppUserId = me;

        await db.SaveChangesAsync(ct);
        return Ok(new EmailTemplateSaved(true, "Saved.", now));
    }

    /// <summary>Makes the draft the letter people receive.</summary>
    [HttpPost("{kindKey}/publish")]
    public async Task<ActionResult<EmailTemplateSaved>> Publish(string kindKey, CancellationToken ct)
    {
        if (MailKinds.Find(kindKey) is not { } kind) return NotFound();

        await using var db = await _db.CreateDbContextAsync(ct);
        var row = await db.EmailTemplates.FirstOrDefaultAsync(t => t.Kind == kindKey, ct);

        if (row is null || string.IsNullOrWhiteSpace(row.DraftSubject) || string.IsNullOrWhiteSpace(row.DraftBodyHtml))
            return BadRequest("There is nothing written to publish.");

        var now = DateTime.UtcNow;
        row.Subject = row.DraftSubject;
        row.BodyHtml = row.DraftBodyHtml;
        row.PublishedUtc = now;
        row.PublishedByAppUserId = Me();
        row.DateUpdated = now;
        row.UpdatedByAppUserId = Me();

        await db.SaveChangesAsync(ct);

        // So the next letter uses it rather than waiting out the minute.
        _composer.Forget(kindKey);

        return Ok(new EmailTemplateSaved(true, "Published. The next letter of this kind uses it.", now));
    }

    /// <summary>
    /// Goes back to the letter the code writes.
    /// </summary>
    /// <remarks>
    /// Deleting the row IS the revert: the built-in letter never left the code, so there is
    /// nothing to restore.
    /// </remarks>
    [HttpDelete("{kindKey}")]
    public async Task<ActionResult<EmailTemplateSaved>> Revert(string kindKey, CancellationToken ct)
    {
        if (MailKinds.Find(kindKey) is null) return NotFound();

        await using var db = await _db.CreateDbContextAsync(ct);
        var row = await db.EmailTemplates.FirstOrDefaultAsync(t => t.Kind == kindKey, ct);
        if (row is not null)
        {
            db.EmailTemplates.Remove(row);
            await db.SaveChangesAsync(ct);
        }

        _composer.Forget(kindKey);
        return Ok(new EmailTemplateSaved(true, "This letter is back to the one the site writes.", null));
    }

    /// <summary>What the draft would look like, against made-up rows.</summary>
    [HttpPost("{kindKey}/preview")]
    public async Task<ActionResult<EmailTemplatePreview>> Preview(
        string kindKey, [FromBody] SaveEmailTemplateRequest request, CancellationToken ct)
    {
        if (MailKinds.Find(kindKey) is not { } kind) return NotFound();

        await using var db = await _db.CreateDbContextAsync(ct);
        var tables = MailTemplateSchema.For(kind, db);

        var zone = Zone(request.TimeZoneId);
        var context = new MailTokens.Context(
            MailSampleRows.For(tables), zone, MailSampleRows.WrittenAtUtc,
            _site.Name, _site.AbsoluteUrl("/"));

        return Ok(new EmailTemplatePreview(
            MailTokens.Render(request.Subject, context),
            MailTokens.Render(request.BodyHtml, context),
            zone.Id));
    }

    /// <summary>The zone asked for, or the site's when it is not one this machine knows.</summary>
    private static TimeZoneInfo Zone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return Fallback();
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return Fallback(); }
        catch (InvalidTimeZoneException) { return Fallback(); }

        static TimeZoneInfo Fallback()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        }
    }
}

public sealed record EmailTemplateSummary(
    string Kind, string Title, string Description,
    bool IsLive, bool HasUnpublishedDraft, DateTime? PublishedUtc, DateTime? DraftSavedUtc);

public sealed record EmailTemplateColumn(string Name, string Type);
public sealed record EmailTemplateTable(string Name, IReadOnlyList<EmailTemplateColumn> Columns);
public sealed record EmailTemplateToken(string Name, string Looks, string What);

public sealed record EmailTemplateDetail(
    string Kind, string Title, string Description,
    string? Subject, string? BodyHtml,
    string? DraftSubject, string? DraftBodyHtml,
    DateTime? PublishedUtc, DateTime? DraftSavedUtc,
    IReadOnlyList<EmailTemplateTable> Tables,
    IReadOnlyList<EmailTemplateToken> Common);

public sealed record SaveEmailTemplateRequest(string? Subject, string? BodyHtml, string? TimeZoneId = null);
public sealed record EmailTemplateSaved(bool Ok, string Message, DateTime? At);
public sealed record EmailTemplatePreview(string Subject, string Html, string ZoneId);
