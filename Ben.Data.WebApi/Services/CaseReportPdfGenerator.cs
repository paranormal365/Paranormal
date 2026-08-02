using Ben.Data.Source.Entities;
using Telerik.Windows.Documents.Fixed.FormatProviders.Pdf;
using Telerik.Windows.Documents.Fixed.Model;
using Telerik.Windows.Documents.Fixed.Model.Editing;

namespace Ben.Data.WebApi.Services;

/// <summary>Shared PDF generation helper for CaseReport — used by both org and client controllers.</summary>
public static class CaseReportPdfGenerator
{
    public static byte[] Generate(CaseReport report)
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
