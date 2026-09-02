using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Deleting field sessions whose readings are not on this server.
/// </summary>
/// <remarks>
/// <para>The first version of this endpoint did nothing at all on a real database, and said
/// "Deleted 0" while doing it. A case report may cite a field session, and that foreign key is
/// <c>NoAction</c> — so one cited session threw and took the whole batch with it. Every database a
/// report test has ever run against has cited sessions, which is why the browser test on a clean
/// side database passed and production did not.</para>
///
/// <para>The delete itself cannot be exercised here: <c>ExecuteDeleteAsync</c> is relational-only
/// and the in-memory provider has neither it nor transactions. So the ORDER of the deletes — the
/// thing that was actually wrong — is guarded by a source scan in
/// <see cref="Ben.Web.Tests.Services.OrphanedSessionPurgeCoverageTests"/>, the same shape as
/// <c>OrganizationPurgeCoverageTests</c> and for the same reason.</para>
/// </remarks>
public class AdminOrphanedFieldSessionControllerTests
{
    /// <summary>Storage that has lost its files — every read fails, which is the orphan case.</summary>
    private sealed class EmptyStorage : IFileStorageService
    {
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
            => throw new FileNotFoundException(relativePath);

        public Task WriteAsync(string relativePath, Stream data, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string relativePath, CancellationToken ct = default) => Task.CompletedTask;

        public bool Exists(string relativePath) => false;
        public IReadOnlyList<string> ListFiles(string relativeDirectory) => [];
        public string UserFilePath(Guid userId, string storedFileName) => $"users/{userId}/{storedFileName}";
        public string OrgFilePath(Guid orgId, string storedFileName) => $"orgs/{orgId}/{storedFileName}";
        public string CaseFilePath(Guid caseId, string storedFileName) => $"cases/{caseId}/{storedFileName}";
    }

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // The controller wraps its deletes in a transaction, and the in-memory store has none.
            // Ignoring the warning tests the ORDER of the deletes — which is where the bug was —
            // and leaves the all-or-nothing behaviour to SQL Server, where it is real.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static AdminOrphanedFieldSessionController Build(IDbContextFactory<BenDataContext> factory)
    {
        var ctrl = new AdminOrphanedFieldSessionController(
            factory, new EmptyStorage(),
            NullLogger<AdminOrphanedFieldSessionController>.Instance);

        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                     new Claim(ClaimTypes.Role, "SuperAdmin")], "Bearer"))
            }
        };
        return ctrl;
    }

    /// <summary>A session whose document names a path the storage cannot open.</summary>
    private static FieldSessionUpload SeedOrphan(BenDataContext db, string label = "orphan")
    {
        var file = new UploadFile
        {
            Id = Guid.NewGuid(), FileName = "data.json", ContentType = "application/json",
            StoragePath = $"orgs/x/field-sessions/{Guid.NewGuid()}.json",
            DateCreated = DateTime.UtcNow,
        };
        var session = new FieldSessionUpload
        {
            Id = Guid.NewGuid(), DeviceSessionId = Guid.NewGuid(), DeviceModel = "iPhone17,1",
            LocationLabel = label, StartedAt = DateTime.UtcNow.AddHours(-1),
            DocumentUploadFileId = file.Id, DocumentUploadFile = file,
            ReadingCount = 9, MarkerCount = 2, DateCreated = DateTime.UtcNow,
        };
        db.UploadFiles.Add(file);
        db.FieldSessionUploads.Add(session);
        return session;
    }

    [Fact]
    public async Task A_session_whose_document_cannot_be_read_is_listed()
    {
        var factory = CreateFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            SeedOrphan(db);
            await db.SaveChangesAsync();
        }

        var result = await Build(factory).Preview(default);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<OrphanedFieldSessionRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Single(rows);
        Assert.Equal("orphan", rows[0].LocationLabel);
    }

    /// <summary>
    /// An id the server does not consider orphaned is refused by name rather than skipped, so a
    /// stale screen cannot quietly do three quarters of what was asked.
    /// </summary>
    [Fact]
    public async Task An_id_that_is_not_orphaned_refuses_the_whole_request()
    {
        var factory = CreateFactory();
        Guid orphan;

        await using (var db = await factory.CreateDbContextAsync())
        {
            orphan = SeedOrphan(db).Id;
            await db.SaveChangesAsync();
        }

        var result = await Build(factory)
            .Purge(new PurgeOrphanedSessionsRequest([orphan, Guid.NewGuid()]), default);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var outcome  = Assert.IsType<OrphanedFieldSessionPurgeResult>(conflict.Value);
        Assert.Equal(0, outcome.Deleted);
        Assert.NotNull(outcome.Refusal);

        await using (var db = await factory.CreateDbContextAsync())
            Assert.Single(db.FieldSessionUploads);   // nothing was deleted
    }

    [Fact]
    public async Task An_empty_request_is_refused()
    {
        var result = await Build(CreateFactory()).Purge(new PurgeOrphanedSessionsRequest([]), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
