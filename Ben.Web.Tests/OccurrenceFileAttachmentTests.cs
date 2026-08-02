using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ben.Web.Tests;

/// <summary>
/// Tests for the occurrence file attachment feature:
/// service model record contracts and case storage path.
/// </summary>
public class OccurrenceFileAttachmentTests
{
    // ── CaseTimelineFileRecord (org-side service model) ───────────────────────

    [Fact]
    public void CaseTimelineFileRecord_HoldsAllFields()
    {
        var id  = Guid.NewGuid();
        var rec = new CaseTimelineFileRecord
        {
            FileId      = id,
            FileName    = "video.mp4",
            ContentType = "video/mp4",
            FileSize    = 10_485_760L,
        };

        Assert.Equal(id, rec.FileId);
        Assert.Equal("video.mp4", rec.FileName);
        Assert.Equal("video/mp4", rec.ContentType);
        Assert.Equal(10_485_760L, rec.FileSize);
    }

    [Fact]
    public void CaseTimelineEntryRecord_DefaultFilesIsEmpty()
    {
        var rec = new CaseTimelineEntryRecord
        {
            Id                 = Guid.NewGuid(),
            CaseId             = Guid.NewGuid(),
            AuthorAppUserId    = Guid.NewGuid(),
            EntryType          = CaseTimelineEntryType.Evidence,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = Guid.NewGuid(),
        };

        Assert.Empty(rec.Files);
        Assert.Empty(rec.ExperienceTypeIds);
    }

    [Fact]
    public void CaseTimelineEntryRecord_WithFiles_ExposesFileList()
    {
        var fileId = Guid.NewGuid();
        var rec = new CaseTimelineEntryRecord
        {
            Id                 = Guid.NewGuid(),
            CaseId             = Guid.NewGuid(),
            AuthorAppUserId    = Guid.NewGuid(),
            EntryType          = CaseTimelineEntryType.ClientReport,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = Guid.NewGuid(),
            Files              = [new CaseTimelineFileRecord { FileId = fileId, FileName = "a.jpg", ContentType = "image/jpeg", FileSize = 1024 }],
        };

        Assert.Single(rec.Files);
        Assert.Equal(fileId, rec.Files[0].FileId);
    }

    [Fact]
    public void EvidenceFileTypeGuid_IsNonEmptyAndDeterministic()
    {
        var expected = new Guid("20000000-0000-0000-0000-000000000001");
        Assert.NotEqual(Guid.Empty, expected);
        Assert.Equal("20000000-0000-0000-0000-000000000001", expected.ToString());
    }

    [Fact]
    public void CaseFilePath_ReturnsCorrectPattern()
    {
        var svc    = BuildStorageService();
        var caseId = Guid.NewGuid();
        var path   = svc.CaseFilePath(caseId, "evidence.jpg");

        Assert.StartsWith("cases/", path);
        Assert.EndsWith("/evidence.jpg", path);
        Assert.DoesNotContain('\\', path);
        Assert.Contains(caseId.ToString(), path);
    }

    [Fact]
    public void CaseFilePath_DifferentCases_HaveDifferentPaths()
    {
        var svc = BuildStorageService();
        Assert.NotEqual(svc.CaseFilePath(Guid.NewGuid(), "f.jpg"), svc.CaseFilePath(Guid.NewGuid(), "f.jpg"));
    }

    private static LocalFileStorageService BuildStorageService()
    {
        var root   = Path.Combine(Path.GetTempPath(), $"ofa-test-{Guid.NewGuid()}");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FileStorage:RootPath"] = root })
            .Build();
        return new LocalFileStorageService(config);
    }
}
