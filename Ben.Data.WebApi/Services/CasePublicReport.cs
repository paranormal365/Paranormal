using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Redaction;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// What a case's investigation report says to the public, or nothing (site evaluation
/// 2026-09-06, W-P3).
/// </summary>
/// <remarks>
/// <para><b>The gap this closes.</b> A published report existed only on the client's own case
/// page. The group wrote it, published it, the client read it, and that was where it stopped —
/// nothing on the group's public site or in its CMS could carry any of it. A case's public page
/// offered the client's account of what happened and a timeline, and never the group's own
/// finding.</para>
///
/// <para><b>Three conditions, asked separately.</b> The case must be public, the report must be
/// Published, and the group must have switched this report's summary on. None of those implies
/// another: publishing a report delivers it to the client, and making a case public releases the
/// case. Neither is a decision to show a document written for the person whose house it is.</para>
///
/// <para><b>The summary and the conclusion, and nothing else.</b> Not the sections, the evidence
/// files or the cited field sessions — those carry the working detail of an investigation inside
/// somebody's home. The two parts released here are the ones written to be read.</para>
///
/// <para><b>Resolved on every request, never snapshotted.</b> Redaction runs against the case's
/// live roster each time, so a private engagement's names are substituted exactly as they are on
/// the title and the timeline, and a group that changes its mind takes effect immediately. One
/// resolver serves both the public case page and the CMS embed, so the two cannot disagree about
/// what was released.</para>
/// </remarks>
public static class CasePublicReport
{
    /// <summary>A report's public face: what a visitor is shown of the group's finding.</summary>
    /// <param name="Title">The report's own title.</param>
    /// <param name="Summary">The executive summary, redacted. HTML.</param>
    /// <param name="Conclusion">The conclusion, redacted. HTML.</param>
    /// <param name="PublishedAt">When the group published it to their client.</param>
    public sealed record PublicReport(
        string Title,
        string? Summary,
        string? Conclusion,
        DateTime? PublishedAt);

    /// <summary>
    /// The report this case publishes, or null.
    /// </summary>
    /// <remarks>
    /// <para>The caller has already established that the case itself is public — this is reached
    /// from endpoints that found the case under exactly that condition — so the check here is the
    /// report's own two: Published, and switched on.</para>
    ///
    /// <para>The newest wins when a group has switched on more than one, which nothing stops them
    /// doing. A page showing two conclusions about the same case would be worse than showing the
    /// later one.</para>
    /// </remarks>
    public static async Task<PublicReport?> ForCaseAsync(
        BenDataContext db, Guid caseId, RedactionRoster roster, CancellationToken ct = default)
    {
        var report = await db.CaseReports.AsNoTracking()
            .Where(r => r.CaseId == caseId
                     && r.IsPublicSummaryVisible
                     && r.Status == CaseReportStatus.Published)
            .OrderByDescending(r => r.PublishedAt ?? r.DateCreated)
            .FirstOrDefaultAsync(ct);

        if (report is null) return null;

        // A report switched on but left empty would render as a heading over nothing, which reads
        // as a fault rather than as a group that has not written it yet.
        var summary    = CaseProseRedactor.RedactHtml(report.Summary, roster);
        var conclusion = CaseProseRedactor.RedactHtml(report.Conclusion, roster);
        if (string.IsNullOrWhiteSpace(summary) && string.IsNullOrWhiteSpace(conclusion)) return null;

        return new PublicReport(
            CaseProseRedactor.Redact(report.Title, roster) ?? report.Title,
            summary,
            conclusion,
            report.PublishedAt);
    }

    /// <summary>
    /// The same, for a batch of cases — one query rather than one per case.
    /// </summary>
    /// <remarks>
    /// For the CMS embed, which resolves several cases at once. The rosters come from
    /// <see cref="CaseRedactionRoster.ForCasesAsync"/> for the same reason.
    /// </remarks>
    public static async Task<IReadOnlyDictionary<Guid, PublicReport>> ForCasesAsync(
        BenDataContext db, IReadOnlyCollection<Guid> caseIds,
        IReadOnlyDictionary<Guid, RedactionRoster> rosters, CancellationToken ct = default)
    {
        if (caseIds.Count == 0) return new Dictionary<Guid, PublicReport>();

        var reports = await db.CaseReports.AsNoTracking()
            .Where(r => caseIds.Contains(r.CaseId)
                     && r.IsPublicSummaryVisible
                     && r.Status == CaseReportStatus.Published)
            .OrderByDescending(r => r.PublishedAt ?? r.DateCreated)
            .ToListAsync(ct);

        var result = new Dictionary<Guid, PublicReport>();
        foreach (var report in reports)
        {
            if (result.ContainsKey(report.CaseId)) continue;   // newest wins — see ForCaseAsync

            var roster     = rosters.GetValueOrDefault(report.CaseId) ?? RedactionRoster.Empty;
            var summary    = CaseProseRedactor.RedactHtml(report.Summary, roster);
            var conclusion = CaseProseRedactor.RedactHtml(report.Conclusion, roster);
            if (string.IsNullOrWhiteSpace(summary) && string.IsNullOrWhiteSpace(conclusion)) continue;

            result[report.CaseId] = new PublicReport(
                CaseProseRedactor.Redact(report.Title, roster) ?? report.Title,
                summary, conclusion, report.PublishedAt);
        }

        return result;
    }
}
