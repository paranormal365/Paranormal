using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Service.Models.Admin;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The price list's verdict on itself has to come back as JSON.
/// </summary>
/// <remarks>
/// <para><b>What went wrong.</b> The endpoint returned <c>Ok(aString)</c>. MVC serves a string
/// result through its string formatter as <c>text/plain</c>, and every client in this solution
/// reads an answer as JSON — so the moment the ladder had something to say, the Price Bands screen
/// threw inside <c>OnInitializedAsync</c>, the circuit disconnected, and every button on the page
/// went dead. Not only the dialog that was reported: the whole screen, including the one that
/// would have let somebody fix the ladder.</para>
///
/// <para><b>It only broke when it mattered.</b> A healthy ladder answers "nothing to report", and
/// null came back as an empty 204 the client already handled. So the screen worked on a fresh
/// database and died on a real one. That is also why the reported symptom was a dialog and not a
/// page: nothing else on the screen is load-bearing enough to notice (item 232, 2026-09-11).</para>
///
/// <para><b>The same page had already been broken by the other half of this.</b> The 204 case was
/// fixed in <c>ApiResponseMapper</c> after the screen died on production when the list was
/// HEALTHY. The unhealthy branch was left.</para>
/// </remarks>
public sealed class TierValidationShapeTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AdminSubscriptionTierController Build(IDbContextFactory<BenDataContext> f)
        => new(f, new Mock<IAuditLogService>().Object,
               new TierChangeNotifier(f, new PlatformMessageService(f)))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, AdminId.ToString()),
                         new Claim(ClaimTypes.Role, RoleNames.SuperAdmin)], "Bearer")),
                },
            },
        };

    /// <summary>A ladder with a band priced at nothing — a list that has something to say.</summary>
    private static async Task<IDbContextFactory<BenDataContext>> SeedAFreeBandAsync()
    {
        var f = CreateFactory();
        await using var db = await f.CreateDbContextAsync();

        var free = Guid.NewGuid();
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = free, Name = "Free", MinMembers = 1, MaxMembers = 3, SortOrder = 1,
            IsActive = true, IsBandedByMembers = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = free,
            Interval = BillingInterval.Monthly, Price = 0m, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });

        var paid = Guid.NewGuid();
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = paid, Name = "Large Group", MinMembers = 4, MaxMembers = null, SortOrder = 2,
            IsActive = true, IsBandedByMembers = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = paid,
            Interval = BillingInterval.Monthly, Price = 40m, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });

        await db.SaveChangesAsync();
        return f;
    }

    [Fact]
    public async Task A_problem_comes_back_as_an_object_the_client_can_read()
    {
        var result = await Build(await SeedAFreeBandAsync()).GetValidation(default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);

        // The shape is the whole point. A bare string here is served as text/plain and kills the
        // page that reads it.
        var record = Assert.IsType<TierValidationRecord>(ok.Value);
        Assert.False(string.IsNullOrWhiteSpace(record.Problem),
            "A band priced at nothing should have been named.");

        // A free band is worth knowing about; it does not stop the list pricing anybody. Saying so
        // is what lets the screen avoid announcing that checkout is refused when it is not.
        Assert.False(record.IsBlocking);

        // And it survives the round trip the page actually makes.
        using var content = JsonContent.Create(record);
        var back = await content.ReadFromJsonAsync<TierValidationRecord>();
        Assert.Equal(record.Problem, back!.Problem);
    }

    [Fact]
    public async Task A_healthy_ladder_says_nothing_and_still_says_it_as_an_object()
    {
        var f = CreateFactory();
        await using (var db = await f.CreateDbContextAsync())
        {
            var id = Guid.NewGuid();
            db.SubscriptionTiers.Add(new SubscriptionTier
            {
                Id = id, Name = "Everyone", MinMembers = 1, MaxMembers = null, SortOrder = 1,
                IsActive = true, IsBandedByMembers = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
            });
            db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
            {
                Id = Guid.NewGuid(), SubscriptionTierId = id,
                Interval = BillingInterval.Monthly, Price = 29m, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
            });
            await db.SaveChangesAsync();
        }

        var ok = Assert.IsType<OkObjectResult>((await Build(f).GetValidation(default)).Result);
        var record = Assert.IsType<TierValidationRecord>(ok.Value);
        Assert.Null(record.Problem);
    }

    // ── The scan ─────────────────────────────────────────────────────────────

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static string StripComments(string source)
    {
        var withoutBlocks = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        return string.Join('\n', withoutBlocks.Split('\n').Select(line =>
        {
            var slashes = line.IndexOf("//", StringComparison.Ordinal);
            if (slashes >= 0) line = line[..slashes];

            var doc = line.IndexOf("///", StringComparison.Ordinal);
            return doc >= 0 ? line[..doc] : line;
        }));
    }

    [Fact]
    public void No_endpoint_answers_with_a_bare_string()
    {
        var controllers = new DirectoryInfo(Path.Combine(RepoRoot().FullName, "Ben.Data.WebApi"));
        var pattern = new Regex(@"ActionResult<\s*string\s*\??\s*>");
        var offences = new List<string>();

        foreach (var file in controllers.EnumerateFiles("*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                              && !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = StripComments(File.ReadAllText(file.FullName));
            foreach (System.Text.RegularExpressions.Match match in pattern.Matches(text))
                offences.Add($"{file.Name}:{text.Take(match.Index).Count(c => c == '\n') + 1}");
        }

        Assert.True(offences.Count == 0,
            $"""
             {offences.Count} endpoint(s) answer with a bare string.

             MVC serves a string result as text/plain through its string formatter, and every
             client here reads an answer as JSON. A sentence, a key or a name is not valid JSON, so
             the reader throws — and when that reader is a page's OnInitializedAsync, the whole
             screen dies rather than one field being wrong.

             Wrap it in a record with one property. TierValidationRecord and GiphySdkKeyRecord are
             the examples.

               {string.Join("\n  ", offences.Take(40))}
             """);
    }
}
