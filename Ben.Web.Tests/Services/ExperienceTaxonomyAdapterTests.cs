using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Tests for the experience taxonomy methods in BenAdminClientAdapter.
/// Verifies correct API delegation, URL construction, and null-safe returns.
/// </summary>
public class ExperienceTaxonomyAdapterTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Mock<IWebApiClient> ApiMock() => new();
    private static Mock<IWebApiAuthService> AuthMock() => new();

    private static BenAdminClientAdapter Build(Mock<IWebApiClient> api)
        => new BenAdminClientAdapter(api.Object, AuthMock().Object,
            Microsoft.Extensions.Options.Options.Create(new WebApiOptions()));

    // ── GetExperienceTaxonomyAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetExperienceTaxonomyAsync_ReturnsResultFromApi()
    {
        var api  = ApiMock();
        var data = new List<ExperienceCategoryWithTypesResponse>
        {
            new(new ExperienceCategoryRecord { Name = "Audible" }, [])
        };
        api.Setup(x => x.GetListAsync<ExperienceCategoryWithTypesResponse>(
                "/api/experience-categories/with-types", It.IsAny<CancellationToken>()))
           .ReturnsAsync(LoadResult<ExperienceCategoryWithTypesResponse>.Ok(data));

        var result = await Build(api).GetExperienceTaxonomyAsync();

        Assert.False(result.Failed);
        Assert.Single(result.Items);
        Assert.Equal("Audible", result.Items[0].Category.Name);
    }

    [Fact]
    public async Task GetExperienceTaxonomyAsync_WhenTheApiRefuses_SaysSoRatherThanReturningEmpty()
    {
        var api = ApiMock();
        api.Setup(x => x.GetListAsync<ExperienceCategoryWithTypesResponse>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(LoadResult<ExperienceCategoryWithTypesResponse>.Failure("The server answered 403 (Forbidden)."));

        var result = await Build(api).GetExperienceTaxonomyAsync();

        Assert.True(result.Failed);
        Assert.False(result.IsEmpty);
        Assert.Empty(result.Items);
    }

    // ── GetAllExperienceCategoriesAsync ───────────────────────────────────────

    [Fact]
    public async Task GetAllExperienceCategoriesAsync_GetsToCategoriesAdminUrl()
    {
        var api  = ApiMock();
        var data = new List<ExperienceCategoryRecord> { new() { Name = "Visual" } };
        api.Setup(x => x.GetListAsync<ExperienceCategoryRecord>(
                "/api/admin/experience-categories", It.IsAny<CancellationToken>()))
           .ReturnsAsync(LoadResult<ExperienceCategoryRecord>.Ok(data));

        var result = await Build(api).GetAllExperienceCategoriesAsync();

        Assert.False(result.Failed);
        Assert.Single(result.Items);
        Assert.Equal("Visual", result.Items[0].Name);
        api.Verify(x => x.GetListAsync<ExperienceCategoryRecord>(
            "/api/admin/experience-categories", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── CreateExperienceCategoryAsync ─────────────────────────────────────────

    [Fact]
    public async Task CreateExperienceCategoryAsync_PostsToCorrectUrl()
    {
        var api     = ApiMock();
        var created = new ExperienceCategoryRecord { Name = "Physical" };
        api.Setup(x => x.PostAsync<UpsertExperienceCategoryRequest, ExperienceCategoryRecord>(
                "/api/admin/experience-categories",
                It.IsAny<UpsertExperienceCategoryRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(created);

        var req    = new UpsertExperienceCategoryRequest("Physical", null, null, "text-danger", 3, true);
        var result = await Build(api).CreateExperienceCategoryAsync(req);

        Assert.Equal("Physical", result!.Name);
        api.Verify(x => x.PostAsync<UpsertExperienceCategoryRequest, ExperienceCategoryRecord>(
            "/api/admin/experience-categories", req, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── UpdateExperienceCategoryAsync ─────────────────────────────────────────

    [Fact]
    public async Task UpdateExperienceCategoryAsync_PutsToCorrectUrl()
    {
        var id  = Guid.NewGuid();
        var api = ApiMock();
        api.Setup(x => x.PutAsync<UpsertExperienceCategoryRequest, ExperienceCategoryRecord>(
                $"/api/admin/experience-categories/{id}",
                It.IsAny<UpsertExperienceCategoryRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(new ExperienceCategoryRecord { Name = "Updated" });

        var req = new UpsertExperienceCategoryRequest("Updated", null, null, null, 1, true);
        await Build(api).UpdateExperienceCategoryAsync(id, req);

        api.Verify(x => x.PutAsync<UpsertExperienceCategoryRequest, ExperienceCategoryRecord>(
            $"/api/admin/experience-categories/{id}", req, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── DeleteExperienceCategoryAsync ─────────────────────────────────────────

    [Fact]
    public async Task DeleteExperienceCategoryAsync_DeletesCorrectUrl()
    {
        var id  = Guid.NewGuid();
        var api = ApiMock();
        api.Setup(x => x.DeleteAsync($"/api/admin/experience-categories/{id}",
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(true);

        await Build(api).DeleteExperienceCategoryAsync(id);

        api.Verify(x => x.DeleteAsync(
            $"/api/admin/experience-categories/{id}", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── ApproveExperienceCategoryAsync ────────────────────────────────────────

    [Fact]
    public async Task ApproveExperienceCategoryAsync_PutsToApproveUrl()
    {
        var id  = Guid.NewGuid();
        var api = ApiMock();
        api.Setup(x => x.PutAsync<object, ExperienceCategoryRecord>(
                $"/api/admin/experience-categories/{id}/approve",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(new ExperienceCategoryRecord { Name = "Approved" });

        await Build(api).ApproveExperienceCategoryAsync(id);

        api.Verify(x => x.PutAsync<object, ExperienceCategoryRecord>(
            $"/api/admin/experience-categories/{id}/approve",
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── CreateExperienceTypeAsync ─────────────────────────────────────────────

    [Fact]
    public async Task CreateExperienceTypeAsync_PostsToCorrectUrl()
    {
        var catId   = Guid.NewGuid();
        var api     = ApiMock();
        var created = new ExperienceTypeRecord { Name = "Knocking", ExperienceCategoryId = catId };
        api.Setup(x => x.PostAsync<UpsertExperienceTypeRequest, ExperienceTypeRecord>(
                $"/api/admin/experience-categories/{catId}/types",
                It.IsAny<UpsertExperienceTypeRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(created);

        var req    = new UpsertExperienceTypeRequest("Knocking", null, null, 1, true);
        var result = await Build(api).CreateExperienceTypeAsync(catId, req);

        Assert.Equal("Knocking", result!.Name);
        api.Verify(x => x.PostAsync<UpsertExperienceTypeRequest, ExperienceTypeRecord>(
            $"/api/admin/experience-categories/{catId}/types", req,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── DeleteExperienceTypeAsync ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteExperienceTypeAsync_DeletesCorrectUrl()
    {
        var catId  = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.DeleteAsync(
                $"/api/admin/experience-categories/{catId}/types/{typeId}",
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(true);

        await Build(api).DeleteExperienceTypeAsync(catId, typeId);

        api.Verify(x => x.DeleteAsync(
            $"/api/admin/experience-categories/{catId}/types/{typeId}",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── ApproveExperienceTypeAsync ────────────────────────────────────────────

    [Fact]
    public async Task ApproveExperienceTypeAsync_PutsToApproveUrl()
    {
        var catId  = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.PutAsync<object, ExperienceTypeRecord>(
                $"/api/admin/experience-categories/{catId}/types/{typeId}/approve",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(new ExperienceTypeRecord { Name = "Approved", ExperienceCategoryId = catId });

        await Build(api).ApproveExperienceTypeAsync(catId, typeId);

        api.Verify(x => x.PutAsync<object, ExperienceTypeRecord>(
            $"/api/admin/experience-categories/{catId}/types/{typeId}/approve",
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
