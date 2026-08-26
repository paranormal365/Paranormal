using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// A grant to READ a case does not permit changing or destroying what hangs off it.
/// </summary>
/// <remarks>
/// <para>Found doing IH-03 step 2, 2026-08-26. Case notes, case files and case research all gated
/// create, update AND delete on <c>Case.Read</c> — through helpers named <c>IsOrgMember</c>, which
/// was neither the question asked nor the one meant. Anybody who could see a case could rewrite
/// and delete its notes, files and research.</para>
///
/// <para>It was survivable while the seeder auto-granted case read to every member: nearly
/// everyone had it, so nobody noticed it was doing more than it said. It stopped being survivable
/// the moment Ben ended the grandfathering, because a read grant became a deliberate act and has
/// to mean read. Ben's instruction, same day: "They will need the permission to delete."</para>
///
/// <para>These tests assert the RULE rather than any one controller's plumbing, so the next
/// surface that hangs something off a case has an example to copy.</para>
/// </remarks>
public class ReadDoesNotGrantDestructionTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>A member holding exactly Case.Read, and nothing else.</summary>
    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid UserId)> ReaderAsync()
    {
        var factory = CreateFactory();
        var orgId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var readerId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "G", UrlName = $"g-{orgId:N}",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = readerId,
            Role = OrganizationMemberRole.Member, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.OrganizationAccessGrants.Add(new OrganizationAccessGrant
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = readerId,
            TableName = OrganizationSecurityTable.Case,
            Actions = OrganizationSecurityAction.Read,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        await TestSeeds.BridgeAsync(factory, orgId);
        return (factory, orgId, readerId);
    }

    [Fact]
    public async Task AReadGrant_AllowsReading()
    {
        var (factory, orgId, userId) = await ReaderAsync();
        var security = new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory);

        Assert.True(await security.MayAsync(
            userId, orgId, OrganizationPermissionArea.Cases, OrganizationSecurityAction.Read));
    }

    /// <summary>The three that used to be allowed by a read grant.</summary>
    [Theory]
    [InlineData(OrganizationSecurityAction.Create)]
    [InlineData(OrganizationSecurityAction.Update)]
    [InlineData(OrganizationSecurityAction.Delete)]
    public async Task AReadGrant_PermitsNothingElse(OrganizationSecurityAction action)
    {
        var (factory, orgId, userId) = await ReaderAsync();
        var security = new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory);

        Assert.False(await security.MayAsync(
            userId, orgId, OrganizationPermissionArea.Cases, action),
            $"a Case.Read grant must not permit {action} — that is how a reader deleted a case's notes");
    }

    /// <summary>
    /// No write endpoint on a case's sub-surfaces may be gated on Read.
    /// </summary>
    /// <remarks>
    /// The ratchet. The defect was not one bad line — it was the same mistake copied into three
    /// controllers, each spelling it slightly differently, and it survived because nothing looked
    /// at the pairing of VERB and ACTION. This reads the sources and fails if a POST, PUT or
    /// DELETE is guarded by a check that asks for Read.
    /// </remarks>
    [Fact]
    public void NoWriteEndpoint_IsGatedOnRead()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var root = Path.Combine(dir!.FullName, "Ben.Data.WebApi", "Controllers", "Entities");
        string[] guarded =
        [
            "CaseNoteController.cs", "CaseFileController.cs", "CaseResearchController.cs",
            // Added when the sweep of 2026-08-26 reached the client-facing surfaces. The report
            // controller is the one that mattered most: sixteen endpoints on Case.Read, including
            // publishing a report to the client and deleting a published one.
            "CaseReportController.cs", "CaseMessageController.cs",
            "ScheduleProposalController.cs", "CaseAudioMixController.cs",
            // The verb the sweep missed: Create here had no per-row manage gate to save it, so
            // Investigation.Read scheduled visits until the read-only-member e2e caught the button.
            "OrgInvestigationsController.cs",
        ];

        var offenders = new List<string>();
        foreach (var file in guarded)
        {
            var lines = File.ReadAllLines(Path.Combine(root, file));
            string? verb = null;
            for (var i = 0; i < lines.Length; i++)
            {
                // Comments are stripped, or a remark ABOUT the old behaviour would trip this.
                var line = lines[i].Split("//")[0];

                if (line.Contains("[HttpPost")) verb = "POST";
                else if (line.Contains("[HttpPut")) verb = "PUT";
                else if (line.Contains("[HttpDelete")) verb = "DELETE";
                else if (line.Contains("[HttpGet")) verb = null;

                // Both refusal spellings. The first pass of this guard looked for Forbid() only,
                // and CaseMessageController refuses with NotFound() — so reverting its POST to a
                // Read gate passed the ratchet cleanly. Caught by sabotaging the fix and watching
                // the test stay green, which is the only way that class of hole shows itself.
                var refuses = line.Contains("Forbid") || line.Contains("NotFound");
                if (verb is not null && line.Contains("SecurityAction.Read") && refuses)
                    offenders.Add($"{file}:{i + 1} — a {verb} gated on Read");
            }
        }

        Assert.True(offenders.Count == 0,
            "a write is guarded by a read grant:\n  " + string.Join("\n  ", offenders));
    }
}
