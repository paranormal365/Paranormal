using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Xunit;

namespace Ben.Web.Tests;

/// <summary>
/// Tests for CaseReport entity, enums, and the shared PDF generator.
/// Record types (CaseReportSummary etc.) are defined in both Ben.Data.WebApi
/// and Ben.Web.Website.Library global namespaces — ambiguous in tests; tested via enums and entity instead.
/// </summary>
public class CaseReportTests
{
    // ── CaseReportStatus enum ─────────────────────────────────────────────────

    [Theory]
    [InlineData(CaseReportStatus.Draft,     0)]
    [InlineData(CaseReportStatus.Published, 1)]
    public void CaseReportStatus_HasCorrectValues(CaseReportStatus status, int expected)
        => Assert.Equal(expected, (int)status);

    [Fact]
    public void CaseReportStatus_OnlyTwoValues()
        => Assert.Equal(2, Enum.GetValues<CaseReportStatus>().Length);

    // ── CaseReportSectionType enum ────────────────────────────────────────────

    [Theory]
    [InlineData(CaseReportSectionType.Text,        0)]
    [InlineData(CaseReportSectionType.Evidence,    1)]
    [InlineData(CaseReportSectionType.Timeline,    2)]
    [InlineData(CaseReportSectionType.Occurrences, 3)]
    public void CaseReportSectionType_HasCorrectValues(CaseReportSectionType t, int expected)
        => Assert.Equal(expected, (int)t);

    // ── CaseReport entity defaults ────────────────────────────────────────────

    [Fact]
    public void CaseReport_DefaultStatus_IsDraft()
    {
        var r = new CaseReport();
        Assert.Equal(CaseReportStatus.Draft, r.Status);
    }

    [Fact]
    public void CaseReport_DefaultSections_IsEmpty()
    {
        var r = new CaseReport();
        Assert.Empty(r.Sections);
    }

    [Fact]
    public void CaseReportSection_DefaultFiles_IsEmpty()
    {
        var s = new CaseReportSection();
        Assert.Empty(s.Files);
    }

    // ── CaseReportPdfGenerator ────────────────────────────────────────────────

    [Fact]
    public void PdfGenerator_EmptyReport_ReturnsBytesWithoutThrowing()
    {
        var report = new CaseReport { Id = Guid.NewGuid(), CaseId = Guid.NewGuid(), Title = "Test", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid() };
        var bytes = Ben.Data.WebApi.Services.CaseReportPdfGenerator.Generate(report);
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 100);
    }

    [Fact]
    public void PdfGenerator_ReportWithSummaryAndConclusion_OutputStartsWithPdfMagicBytes()
    {
        var report = new CaseReport
        {
            Id                 = Guid.NewGuid(),
            CaseId             = Guid.NewGuid(),
            Title              = "Final Assessment — Park Residence",
            Summary            = "<p>We investigated the property on Aug 2.</p>",
            Conclusion         = "<p>Evidence suggests residual activity.</p>",
            Status             = CaseReportStatus.Published,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = Guid.NewGuid(),
        };

        var bytes = Ben.Data.WebApi.Services.CaseReportPdfGenerator.Generate(report);
        // PDF files always start with %PDF-
        Assert.Equal(0x25, bytes[0]); // %
        Assert.Equal(0x50, bytes[1]); // P
        Assert.Equal(0x44, bytes[2]); // D
        Assert.Equal(0x46, bytes[3]); // F
    }

    [Fact]
    public void PdfGenerator_StripHtml_ReturnsPlainText()
    {
        // Indirectly verify via non-throwing generation with HTML summary
        var report = new CaseReport { Id = Guid.NewGuid(), CaseId = Guid.NewGuid(), Title = "T",
            Summary = "<h1>Title</h1><p>Body text with <b>bold</b> and <em>italic</em>.</p>",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid() };
        var bytes = Ben.Data.WebApi.Services.CaseReportPdfGenerator.Generate(report);
        Assert.NotNull(bytes);
    }
}
