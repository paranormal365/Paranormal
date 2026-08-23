using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.RepositoryService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Item 156 Phase D: bare membership no longer reads cases. The bridge (a role grant) does.
/// The old member-access tests were all updated to model bridged members, so THIS file is where
/// the flip itself stays pinned — and the source scan keeps a converted helper from quietly
/// regressing to a membership query.
/// </summary>
public sealed class PhaseDFlipTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    [Fact]
    public async Task A_member_with_no_role_no_longer_reads_case_surfaces()
    {
        var factory = CreateFactory();
        var orgId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = orgId, Name = "G", UrlName = "g", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = memberId,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl = new CaseFileController(factory,
            new Mock<Ben.Data.Common.Interfaces.IFileStorageService>().Object,
            new Mock<Ben.Service.RepositoryService.GenericInterfaces.IAuditLogService>().Object,
            new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(factory),
            new OrganizationSecurityService(factory));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, memberId.ToString())], "Bearer"))
            }
        };

        // The flip: membership alone is refused …
        Assert.IsType<ForbidResult>((await ctrl.GetAll(orgId, Guid.NewGuid(), default)).Result);

        // … and the bridge opens it.
        await TestSeeds.BridgeAsync(factory, orgId);
        Assert.IsNotType<ForbidResult>((await ctrl.GetAll(orgId, Guid.NewGuid(), default)).Result);
    }

    /// <summary>The ten converted files must keep delegating to HasAccessAsync.</summary>
    [Fact]
    public void No_converted_helper_regresses_to_a_membership_query()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var root = Path.Combine(dir!.FullName, "Ben.Data.WebApi", "Controllers", "Entities");

        string[] converted =
        [
            "CaseFileController.cs", "CaseAudioMixController.cs", "CaseReportController.cs",
            "CaseResearchController.cs", "ScheduleProposalController.cs", "CaseNoteController.cs",
            "CaseTransferController.cs", "InvestigationController.cs", "OrgInvestigationsController.cs",
        ];

        var offenders = new List<string>();
        foreach (var file in converted)
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            // The member-read helper must reference HasAccessAsync…
            if (!text.Contains("_security.HasAccessAsync"))
                offenders.Add($"{file}: helper no longer delegates to HasAccessAsync");
            // …and must not have re-grown a bare-membership read gate.
            if (Regex.IsMatch(text, @"private async Task<bool> Is(?:Org)?Member\w*\([^)]*\)[\s\S]{0,400}?OrganizationUserMemberships"))
                offenders.Add($"{file}: a membership query came back inside a member-gate helper");
        }

        Assert.True(offenders.Count == 0,
            "Phase D converted these gates to HasAccessAsync(table, Read); a regression here "
            + "silently reopens case data to every member and bypasses the tier area gate:\n  "
            + string.Join("\n  ", offenders));
    }
}
