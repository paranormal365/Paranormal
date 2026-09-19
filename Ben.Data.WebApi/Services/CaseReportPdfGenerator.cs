using Ben.Data.Source.Entities;
using Telerik.Windows.Documents.Fixed.FormatProviders.Pdf;
using Telerik.Windows.Documents.Fixed.Model;
using Telerik.Windows.Documents.Fixed.Model.Editing;

namespace Ben.Data.WebApi.Services;

/// <summary>Shared PDF generation helper for CaseReport — used by both org and client controllers.</summary>
public static class CaseReportPdfGenerator
{
    /// <summary>Without readouts: every cited session prints the "not on this server" sentence.</summary>
    public static byte[] Generate(CaseReport report) => Generate(report, new Dictionary<Guid, string?>());

    /// <summary>The downloaded file's name: the report's title as plain letters, digits and hyphens.</summary>
    /// <remarks>
    /// The title went in with only its spaces swapped, so "Initial Assessment — Belmont Blvd residence" reached a client as
    /// "report-Initial-Assessment-_-Belmont-Blvd-residence.pdf": the header's plain-text copy of a name cannot carry the dash,
    /// and that copy is the one a download reads (client test pass, 2026-09-14).
    /// </remarks>
    public static string FileName(string? title)
    {
        var decomposed = (title ?? "").Normalize(System.Text.NormalizationForm.FormD);
        var plain = new System.Text.StringBuilder();
        foreach (var c in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            plain.Append(c < 128 && char.IsLetterOrDigit(c) ? c : '-');
        }
        var slug = System.Text.RegularExpressions.Regex.Replace(plain.ToString(), "-{2,}", "-").Trim('-');
        if (slug.Length > 80) slug = slug[..80].TrimEnd('-');
        return slug.Length == 0 ? "report.pdf" : $"report-{slug}.pdf";
    }

    public static byte[] Generate(CaseReport report, IReadOnlyDictionary<Guid, string?> readouts)
    {
        var doc    = new RadFixedDocument();
        var editor = new RadFixedDocumentEditor(doc);

        editor.InsertRun(report.Title);
        editor.InsertParagraph();

        if (!string.IsNullOrWhiteSpace(report.Summary))
        {
            editor.InsertRun(StripHtml(report.Summary));
            editor.InsertParagraph();
        }

        foreach (var section in report.Sections.OrderBy(s => s.SortOrder))
        {
            editor.InsertParagraph();
            editor.InsertRun(section.Title);
            editor.InsertParagraph();

            if (!string.IsNullOrWhiteSpace(section.Body))
            {
                editor.InsertRun(StripHtml(section.Body));
                editor.InsertParagraph();
            }

            // Field sessions the section cites. The PDF states what the night ACTUALLY holds —
            // when it ran, who recorded it, how many readings and marks, and which recordings
            // belong to it — because the client reads the report, not the site, and a citation
            // that says only "field session" tells them nothing they can check.
            foreach (var link in section.FieldSessions.OrderBy(x => x.SortOrder))
            {
                var session = link.FieldSessionUpload;
                var where   = string.IsNullOrWhiteSpace(session.LocationLabel)
                    ? "Field session" : $"Field session — {session.LocationLabel}";
                editor.InsertRun(where);
                editor.InsertLineBreak();

                var ran = session.EndedAt is null
                    // An interrupted session has no honest end time. Saying so beats inventing one.
                    ? $"{session.StartedAt:yyyy-MM-dd HH:mm} UTC — interrupted, no end recorded"
                    : $"{session.StartedAt:yyyy-MM-dd HH:mm} – {session.EndedAt:HH:mm} UTC "
                      + $"({(session.EndedAt.Value - session.StartedAt).TotalMinutes:F0} min)";
                editor.InsertRun("    " + ran);
                editor.InsertLineBreak();

                var by = string.IsNullOrWhiteSpace(session.RecordedByName)
                    // Unattributed is a fact about the evidence, not a gap to paper over.
                    ? "not attributed" : session.RecordedByName;
                editor.InsertRun($"    Recorded by {by} on {session.DeviceModel} · "
                               + $"{session.ReadingCount:N0} readings · {session.MarkerCount:N0} marks");
                editor.InsertLineBreak();
                // What the night held, in one paragraph — peak field, when, in which room, what was
                // recording at that moment — so the PDF stands on its own. Read from the session's
                // own document; when this server cannot open it, the PDF says that instead.
                var readout = readouts.GetValueOrDefault(session.Id)
                              ?? "The session's readings are not on this server, so no readout can be given.";
                editor.InsertRun("    " + readout);
                editor.InsertLineBreak();

                foreach (var file in session.Files.OrderBy(x => x.RelativePath))
                {
                    var flag = file.DigestMatched ? "" : "  [checksum did not match on arrival]";
                    editor.InsertRun($"        {file.RelativePath}{flag}");
                    editor.InsertLineBreak();
                }

                if (!string.IsNullOrWhiteSpace(link.Caption))
                {
                    editor.InsertRun($"    {link.Caption}");
                    editor.InsertLineBreak();
                }
            }

            foreach (var f in section.Files.OrderBy(x => x.SortOrder))
            {
                var label = $"[{f.UploadFile.ContentType.Split('/')[0].ToUpper()}] {f.UploadFile.FileName}";
                if (f.Caption is not null) label += $" — {f.Caption}";
                editor.InsertRun(label);
                editor.InsertLineBreak();
            }
        }

        if (!string.IsNullOrWhiteSpace(report.Conclusion))
        {
            editor.InsertParagraph();
            editor.InsertRun(StripHtml(report.Conclusion));
        }

        editor.Dispose();
        return new PdfFormatProvider().Export(doc, null);
    }

    private static string StripHtml(string html)
        => System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ").Trim();
}
