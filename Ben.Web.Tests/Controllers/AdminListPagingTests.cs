using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The admin lists accepted <c>page</c>/<c>pageSize</c> and returned everything regardless.
/// </summary>
public class AdminListPagingTests
{
    [Fact]
    public void No_paging_asked_for_returns_everything_and_still_says_how_many()
    {
        var response = new DefaultHttpContext().Response;
        var all = Enumerable.Range(1, 7).ToList();

        var got = ListPaging.Apply(all, page: null, pageSize: null, response);

        Assert.Equal(7, got.Count);
        Assert.Equal("7", response.Headers["X-Total-Count"]);
    }

    [Fact]
    public void A_page_is_the_slice_asked_for_and_the_total_is_the_whole()
    {
        var response = new DefaultHttpContext().Response;
        var all = Enumerable.Range(1, 7).ToList();

        var got = ListPaging.Apply(all, page: 2, pageSize: 3, response);

        Assert.Equal([4, 5, 6], got);
        Assert.Equal("7", response.Headers["X-Total-Count"]);
    }

    [Fact]
    public void Nonsense_paging_is_clamped_rather_than_thrown_or_ignored()
    {
        var response = new DefaultHttpContext().Response;
        var all = Enumerable.Range(1, 1000).ToList();

        Assert.Equal(ListPaging.MaxPageSize, ListPaging.Apply(all, 1, 99_999, response).Count);
        Assert.Equal([1], ListPaging.Apply(all, 0, 1, response));
        Assert.Empty(ListPaging.Apply(all, 500, 10, response));
    }

    /// <summary>The cases list, the one hand-written endpoint, actually goes through it.</summary>
    [Fact]
    public async Task The_cases_list_pages_when_asked()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var factory = new PooledDbContextFactory<BenDataContext>(opts);
        var org = new Organization { Id = Guid.NewGuid(), Name = "Paging Group", UrlName = "paging-group" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            for (var i = 0; i < 5; i++)
                db.Cases.Add(new Case
                {
                    Id = Guid.NewGuid(), OrganizationId = org.Id, Title = $"Case {i}",
                    CaseYear = 2026, OrgCaseNumber = i + 1, DateCaseOpened = DateTime.UtcNow.AddDays(-i),
                });
            await db.SaveChangesAsync();
        }

        var ctrl = new AdminCaseController(factory)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var whole = await ctrl.GetAll(CancellationToken.None);
        Assert.Equal(5, Assert.IsAssignableFrom<IEnumerable<AdminCaseSummaryRecord>>(
            Assert.IsType<OkObjectResult>(whole.Result).Value).Count());

        var page = await ctrl.GetAll(CancellationToken.None, page: 2, pageSize: 2);
        var rows = Assert.IsAssignableFrom<IEnumerable<AdminCaseSummaryRecord>>(
            Assert.IsType<OkObjectResult>(page.Result).Value).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("5", ctrl.Response.Headers["X-Total-Count"]);
    }
}
