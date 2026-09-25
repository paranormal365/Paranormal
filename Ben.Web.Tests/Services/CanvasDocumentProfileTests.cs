using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The real <see cref="CanvasDocumentProfile"/>, not a mocked mapper.
/// </summary>
/// <remarks>
/// Every controller test in this project mocks <see cref="IMapper"/>, so a profile that silently
/// maps <c>OrganizationId</c> as null — the one value a WebAssembly client needs to know where a
/// case's files live — would pass all of them. This builds the configuration the API builds.
/// </remarks>
public sealed class CanvasDocumentProfileTests
{
    private static IMapper Mapper()
    {
        var config = new MapperConfiguration(cfg => cfg.AddProfile<CanvasDocumentProfile>(), NullLoggerFactory.Instance);
        config.AssertConfigurationIsValid();
        return config.CreateMapper();
    }

    private static CanvasDocument Board(Guid? orgId) => new()
    {
        Id = Guid.NewGuid(),
        CaseId = orgId is null ? null : Guid.NewGuid(),
        Case = orgId is null ? null : new Case { OrganizationId = orgId.Value, Title = "Case" },
        Name = "Board", DocumentJson = "{\"title\":\"Board\"}", Revision = 7,
        DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
    };

    [Fact]
    public void A_case_board_carries_its_groups_id_and_revision()
    {
        var orgId = Guid.NewGuid();
        var board = Board(orgId);

        var record = Mapper().Map<CanvasDocumentRecord>(board);

        Assert.Equal(orgId, record.OrganizationId);
        Assert.Equal(7, record.Revision);
        Assert.Equal(board.DocumentJson, record.DocumentJson);
        Assert.Null(record.CreatedByName);
    }

    [Fact]
    public void A_summary_has_no_document_and_a_personal_board_has_no_group()
    {
        var summary = Mapper().Map<CanvasDocumentSummaryRecord>(Board(null));

        Assert.Null(summary.OrganizationId);
        Assert.Null(typeof(CanvasDocumentSummaryRecord).GetProperty("DocumentJson"));
    }
}
