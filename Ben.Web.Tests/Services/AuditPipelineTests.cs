using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Service.RepositoryService.GenericInterfaces;
using Ben.Service.RepositoryService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The three ways the audit pipeline used to lose records, each pinned by a test that fails
/// against the old behaviour.
/// </summary>
public class AuditPipelineTests
{
    // ── 1. An audit write is not tied to the caller's request ────────────────

    [Fact]
    public async Task Audit_is_written_even_when_the_caller_has_gone_away()
    {
        // The bug: audit tasks were handed the REQUEST's CancellationToken and fired without
        // being awaited. A client that disconnected right after its mutation committed cancelled
        // the audit write for a change that had already happened.
        //
        // This test cancels a token and then audits. It could only pass by the audit write
        // ignoring it — which is now structural: IAuditLogService takes no token at all, so this
        // test would not have compiled against the old signature.
        var factory = TestDbFactory.Create();
        var service = new AuditLogService(factory);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var entity = new UserAddressType { Id = Guid.NewGuid(), Name = "Home" };
        await service.LogCreateAsync(nameof(UserAddressType), entity.Id, entity,
            Guid.NewGuid(), AppSources.WebApi);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Single(await db.AuditLogs.Where(a => a.EntityId == entity.Id).ToListAsync());
    }

    [Fact]
    public void IAuditLogService_exposes_no_CancellationToken()
    {
        // Guards the fix itself rather than a symptom: re-adding a token parameter would let the
        // request-scoped-token bug back in at 133 call sites, one careless `, ct` at a time.
        var methods = typeof(IAuditLogService).GetMethods()
            .Where(m => m.Name.StartsWith("Log"));

        foreach (var method in methods)
        {
            Assert.DoesNotContain(method.GetParameters(),
                p => p.ParameterType == typeof(CancellationToken));
        }
    }

    // ── 2. A failed delete does not leave a "deleted" audit row ──────────────

    [Fact]
    public async Task Delete_that_fails_writes_no_audit_row()
    {
        // The bug: AdminEntityControllerBase.Delete audited BEFORE SaveChangesAsync, so a delete
        // that threw (an FK still pointing at the row, a concurrency conflict) still recorded
        // that the entity had been deleted.
        var audit = new Mock<IAuditLogService>();
        audit.Setup(a => a.LogDeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(),
                It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var entity = new UserAddressType { Id = Guid.NewGuid(), Name = "Doomed" };
        var factory = new ThrowOnSaveFactory(entity);
        var controller = BuildController(factory, audit.Object);

        await Assert.ThrowsAsync<DbUpdateException>(() => controller.Delete(entity.Id, default));

        audit.Verify(a => a.LogDeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(),
            It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    // ── 3. An audit failure is absorbed, but never silently ──────────────────

    [Fact]
    public async Task A_failing_audit_write_is_logged_and_does_not_surface()
    {
        // The bug: TryAuditAsync swallowed every exception with a bare `catch { }`, so a
        // systemically broken audit table produced a perfectly quiet application writing no
        // audit rows — discoverable only by noticing their absence.
        var logger = new Mock<ILogger<BenControllerBase>>();
        var services = new ServiceCollection();
        services.AddSingleton(logger.Object);

        var probe = new AuditProbeController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() }
            }
        };

        // Absorbed — the caller's operation already succeeded and must not be failed by this.
        await probe.RunAsync(Task.FromException(new InvalidOperationException("audit table is gone")));

        logger.Verify(l => l.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Reaches the protected TryAuditAsync without standing up a real controller.</summary>
    private sealed class AuditProbeController : BenControllerBase
    {
        public Task RunAsync(Task auditTask) => TryAuditAsync(auditTask);
    }

    private static Ben.Data.WebApi.Controllers.Admin.AdminUserAddressTypeController BuildController(
        IDbContextFactory<BenDataContext> factory, IAuditLogService audit)
    {
        var mapper = new Mock<AutoMapper.IMapper>().Object;
        return new Ben.Data.WebApi.Controllers.Admin.AdminUserAddressTypeController(factory, mapper, audit)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    /// <summary>
    /// A context factory whose SaveChanges always fails, standing in for the real reasons a
    /// delete gets refused (FK constraint, concurrency).
    /// </summary>
    private sealed class ThrowOnSaveFactory : IDbContextFactory<BenDataContext>
    {
        private readonly string _dbName = Guid.NewGuid().ToString();

        public ThrowOnSaveFactory(UserAddressType seed)
        {
            // Seeded through a normal context so the row exists; only the contexts the controller
            // gets back refuse to save.
            using var db = new BenDataContext(Options(_dbName));
            db.UserAddressTypes.Add(seed);
            db.SaveChanges();
        }

        public BenDataContext CreateDbContext() => new ThrowOnSaveContext(Options(_dbName));

        private static DbContextOptions<BenDataContext> Options(string name) =>
            new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(name).Options;
    }

    private sealed class ThrowOnSaveContext(DbContextOptions<BenDataContext> options)
        : BenDataContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => throw new DbUpdateException("delete refused");
    }
}
