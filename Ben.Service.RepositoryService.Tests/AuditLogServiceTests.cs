using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Text.Json;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

public class AuditLogServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private sealed class TestEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Test";
        public int Count { get; set; } = 1;
        public bool IsActive { get; set; } = true;

        // Navigation — must be excluded from snapshot
        public TestEntity? Parent { get; set; }
    }

    private static readonly Guid _userId = Guid.NewGuid();
    private static readonly Guid _entityId = Guid.NewGuid();
    private const string EntityType = "Organization";

    // ── LogCreateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task LogCreate_WritesOneEntryToDatabase()
    {
        var factory = CreateFactory();
        var svc = new AuditLogService(factory);

        await svc.LogCreateAsync(EntityType, _entityId, new TestEntity(), _userId, AppSources.WebApi);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Single(db.AuditLogs);
    }

    [Fact]
    public async Task LogCreate_SetsActionToCreate()
    {
        var factory = CreateFactory();
        var svc = new AuditLogService(factory);

        await svc.LogCreateAsync(EntityType, _entityId, new TestEntity(), _userId, AppSources.WebApi);

        await using var db = await factory.CreateDbContextAsync();
        var log = Assert.Single(db.AuditLogs);
        Assert.Equal(AuditAction.Create, log.Action);
    }

    [Fact]
    public async Task LogCreate_SetsEntityTypeEntityIdUserIdSource()
    {
        var factory = CreateFactory();
        var svc = new AuditLogService(factory);

        await svc.LogCreateAsync(EntityType, _entityId, new TestEntity(), _userId, AppSources.WebApi);

        await using var db = await factory.CreateDbContextAsync();
        var log = Assert.Single(db.AuditLogs);
        Assert.Equal(EntityType,       log.EntityType);
        Assert.Equal(_entityId,         log.EntityId);
        Assert.Equal(_userId,           log.UserId);
        Assert.Equal(AppSources.WebApi, log.Source);
    }

    [Fact]
    public async Task LogCreate_ChangesJsonContainsAllScalarProperties()
    {
        var factory = CreateFactory();
        var svc = new AuditLogService(factory);
        var entity = new TestEntity { Name = "Acme", Count = 5 };

        await svc.LogCreateAsync(EntityType, _entityId, entity, _userId, AppSources.WebApi);

        await using var db = await factory.CreateDbContextAsync();
        var log = Assert.Single(db.AuditLogs);
        Assert.NotNull(log.ChangesJson);

        var doc = JsonDocument.Parse(log.ChangesJson!);
        Assert.True(doc.RootElement.TryGetProperty("Name", out var nameProp));
        Assert.Equal("Acme", nameProp.GetString());
        Assert.True(doc.RootElement.TryGetProperty("Count", out var countProp));
        Assert.Equal("5", countProp.GetString());
    }

    [Fact]
    public async Task LogCreate_ChangesJsonExcludesNavigationProperties()
    {
        var factory = CreateFactory();
        var svc = new AuditLogService(factory);
        var entity = new TestEntity { Parent = new TestEntity { Name = "ParentEntity" } };

        await svc.LogCreateAsync(EntityType, _entityId, entity, _userId, AppSources.WebApi);

        await using var db = await factory.CreateDbContextAsync();
        var log = Assert.Single(db.AuditLogs);
        var doc = JsonDocument.Parse(log.ChangesJson!);
        Assert.False(doc.RootElement.TryGetProperty("Parent", out _));
    }

    [Fact]
    public async Task LogCreate_OccurredAt_IsCloseToUtcNow()
    {
        var factory = CreateFactory();
        var svc = new AuditLogService(factory);
        var before = DateTime.UtcNow;

        await svc.LogCreateAsync(EntityType, _entityId, new TestEntity(), _userId, AppSources.WebApi);

        await using var db = await factory.CreateDbContextAsync();
        var log = Assert.Single(db.AuditLogs);
        Assert.True(log.OccurredAt >= before && log.OccurredAt <= DateTime.UtcNow.AddSeconds(2));
    }

    // ── LogUpdateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task LogUpdate_SetsActionToUpdate()
    {
        var factory = CreateFactory();
        var svc = new AuditLogService(factory);
        var before = new TestEntity { Name = "Old" };
        var after  = new TestEntity { Name = "New", Id = before.Id, Count = before.Count, IsActive = before.IsActive };

        await svc.LogUpdateAsync(EntityType, _entityId, before, after, _userId, AppSources.WebApi);

        await using var db = await factory.CreateDbContextAsync();
        var log = Assert.Single(db.AuditLogs);
        Assert.Equal(AuditAction.Update, log.Action);
    }

    [Fact]
    public async Task LogUpdate_ChangesJsonContainsDiff()
    {
        var factory = CreateFactory();
        var svc = new AuditLogService(factory);
        var before = new TestEntity { Name = "Alpha", Count = 1 };
        var after  = new TestEntity { Name = "Beta",  Count = 99, Id = before.Id, IsActive = before.IsActive };

        await svc.LogUpdateAsync(EntityType, _entityId, before, after, _userId, AppSources.WebApi);

        await using var db = await factory.CreateDbContextAsync();
        var log = Assert.Single(db.AuditLogs);
        var changes = JsonSerializer.Deserialize<List<JsonElement>>(log.ChangesJson!);
        Assert.NotNull(changes);

        var nameChange  = changes!.FirstOrDefault(c => c.GetProperty("Property").GetString() == "Name");
        var countChange = changes!.FirstOrDefault(c => c.GetProperty("Property").GetString() == "Count");

        Assert.Equal("Alpha", nameChange.GetProperty("Before").GetString());
        Assert.Equal("Beta",  nameChange.GetProperty("After").GetString());
        Assert.Equal("1",     countChange.GetProperty("Before").GetString());
        Assert.Equal("99",    countChange.GetProperty("After").GetString());
    }

    [Fact]
    public async Task LogUpdate_NoChanges_WritesEntryWithEmptyArray()
    {
        var factory = CreateFactory();
        var svc = new AuditLogService(factory);
        var entity = new TestEntity { Name = "Same" };

        // before and after are identical snapshots
        await svc.LogUpdateAsync(EntityType, _entityId, entity, entity, _userId, AppSources.WebApi);

        await using var db = await factory.CreateDbContextAsync();
        var log = Assert.Single(db.AuditLogs);
        var changes = JsonSerializer.Deserialize<List<JsonElement>>(log.ChangesJson!);
        Assert.NotNull(changes);
        Assert.Empty(changes!);
    }

    // ── LogDeleteAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task LogDelete_SetsActionToDelete()
    {
        var factory = CreateFactory();
        var svc = new AuditLogService(factory);

        await svc.LogDeleteAsync(EntityType, _entityId, new TestEntity(), _userId, AppSources.WebApi);

        await using var db = await factory.CreateDbContextAsync();
        var log = Assert.Single(db.AuditLogs);
        Assert.Equal(AuditAction.Delete, log.Action);
    }

    [Fact]
    public async Task LogDelete_ChangesJsonContainsSnapshot()
    {
        var factory = CreateFactory();
        var svc = new AuditLogService(factory);
        var entity = new TestEntity { Name = "Deleted Entity", Count = 42 };

        await svc.LogDeleteAsync(EntityType, _entityId, entity, _userId, AppSources.WebApi);

        await using var db = await factory.CreateDbContextAsync();
        var log = Assert.Single(db.AuditLogs);
        var doc = JsonDocument.Parse(log.ChangesJson!);
        Assert.Equal("Deleted Entity", doc.RootElement.GetProperty("Name").GetString());
        Assert.Equal("42", doc.RootElement.GetProperty("Count").GetString());
    }

    [Fact]
    public async Task LogDelete_SourceIsWebApi()
    {
        var factory = CreateFactory();
        var svc = new AuditLogService(factory);

        await svc.LogDeleteAsync(EntityType, _entityId, new TestEntity(), _userId, AppSources.WebApi);

        await using var db = await factory.CreateDbContextAsync();
        var log = Assert.Single(db.AuditLogs);
        Assert.Equal(AppSources.WebApi, log.Source);
    }

    // ── Multiple operations ───────────────────────────────────────────────────

    [Fact]
    public async Task MultipleOperations_EachWritesSeparateEntry()
    {
        var factory  = CreateFactory();
        var svc      = new AuditLogService(factory);
        var entity   = new TestEntity { Name = "Obj" };
        var modified = new TestEntity { Name = "Obj Modified", Id = entity.Id, Count = entity.Count, IsActive = entity.IsActive };

        await svc.LogCreateAsync(EntityType, _entityId, entity,   _userId, AppSources.WebApi);
        await svc.LogUpdateAsync(EntityType, _entityId, entity, modified, _userId, AppSources.WebApi);
        await svc.LogDeleteAsync(EntityType, _entityId, entity,   _userId, AppSources.WebApi);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(3, db.AuditLogs.Count());
        Assert.Single(db.AuditLogs, l => l.Action == AuditAction.Create);
        Assert.Single(db.AuditLogs, l => l.Action == AuditAction.Update);
        Assert.Single(db.AuditLogs, l => l.Action == AuditAction.Delete);
    }

    [Fact]
    public async Task LogCreate_IdIsUnique_EachCall()
    {
        var factory = CreateFactory();
        var svc = new AuditLogService(factory);

        await svc.LogCreateAsync(EntityType, _entityId, new TestEntity(), _userId, AppSources.WebApi);
        await svc.LogCreateAsync(EntityType, _entityId, new TestEntity(), _userId, AppSources.WebApi);

        await using var db = await factory.CreateDbContextAsync();
        var logs = db.AuditLogs.ToList();
        Assert.Equal(2, logs.Count);
        Assert.NotEqual(logs[0].Id, logs[1].Id);
    }
}
