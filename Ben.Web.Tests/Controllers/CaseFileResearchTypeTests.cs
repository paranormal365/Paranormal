using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.SeedData;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Which kind of upload a file posted to a case is filed as.
/// </summary>
/// <remarks>
/// Ben, 2026-09-17: a researcher needs to tell which of a case's files a board is built from, so the
/// ones a camera named can be found and renamed. The board says where its files come from; nothing
/// else does, and nothing else should be able to claim it.
/// </remarks>
public sealed class CaseFileResearchTypeTests
{
    [Theory]
    [InlineData("research")]
    [InlineData("Research")]
    [InlineData("RESEARCH")]
    public void A_file_from_a_research_board_is_filed_as_research(string origin) =>
        Assert.Equal(UploadFileTypeSeeder.ResearchFileTypeId, CaseFileController.FileTypeFor(origin));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("evidence")]
    [InlineData("board-snapshot")]
    [InlineData("80000000-0000-0000-0000-000000000001")]
    public void Everything_else_is_case_evidence_exactly_as_it_was(string? origin) =>
        Assert.Equal(CaseFileController.CaseEvidenceFileTypeId, CaseFileController.FileTypeFor(origin));

    /// <summary>
    /// The word is the whole vocabulary. An endpoint that took a file-type id from the form would let
    /// a caller file anything as anything, including as a type meant to be refused publication.
    /// </summary>
    [Fact]
    public void A_file_type_id_in_the_form_names_nothing() =>
        Assert.Equal(CaseFileController.CaseEvidenceFileTypeId,
                     CaseFileController.FileTypeFor(UploadFileTypeSeeder.ResearchFileTypeId.ToString()));
}
