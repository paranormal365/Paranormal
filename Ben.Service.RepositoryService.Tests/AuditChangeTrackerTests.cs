using Ben.Data.Common.Helpers;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

public class AuditChangeTrackerTests
{
    // ── Test fixture ──────────────────────────────────────────────────────────

    private sealed record SampleEntity
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public int Count { get; init; }
        public bool IsActive { get; init; }
        public DateTime DateCreated { get; init; }
        public decimal Price { get; init; }

        // Navigation property — must be excluded
        public SampleEntity? Parent { get; init; }
        public List<SampleEntity> Children { get; init; } = [];
    }

    private static SampleEntity Sample(string name = "Test", int count = 1, bool active = true,
        string? description = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Description = description,
        Count = count,
        IsActive = active,
        DateCreated = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
        Price = 9.99m
    };

    // ── GetChanges ────────────────────────────────────────────────────────────

    [Fact]
    public void GetChanges_IdenticalObjects_ReturnsEmpty()
    {
        var a = Sample();
        var b = Sample(a.Name, a.Count, a.IsActive) with { Id = a.Id, DateCreated = a.DateCreated, Price = a.Price };

        var changes = AuditChangeTracker.GetChanges(a, b);

        Assert.Empty(changes);
    }

    [Fact]
    public void GetChanges_NameChanged_ReturnsNameChange()
    {
        var before = Sample("Alpha");
        var after = Sample("Beta") with { Id = before.Id, Count = before.Count, IsActive = before.IsActive, DateCreated = before.DateCreated, Price = before.Price };

        var changes = AuditChangeTracker.GetChanges(before, after);

        var nameChange = Assert.Single(changes, c => c.Property == "Name");
        Assert.Equal("Alpha", nameChange.Before);
        Assert.Equal("Beta", nameChange.After);
    }

    [Fact]
    public void GetChanges_IntChanged_ReturnsIntChange()
    {
        var before = Sample(count: 5);
        var after = Sample(count: 10) with { Id = before.Id, Name = before.Name, IsActive = before.IsActive, DateCreated = before.DateCreated, Price = before.Price };

        var changes = AuditChangeTracker.GetChanges(before, after);

        var countChange = Assert.Single(changes, c => c.Property == "Count");
        Assert.Equal("5", countChange.Before);
        Assert.Equal("10", countChange.After);
    }

    [Fact]
    public void GetChanges_BoolFlipped_ReturnsBoolChange()
    {
        var before = Sample(active: true);
        var after = Sample(active: false) with { Id = before.Id, Name = before.Name, Count = before.Count, DateCreated = before.DateCreated, Price = before.Price };

        var changes = AuditChangeTracker.GetChanges(before, after);

        var activeChange = Assert.Single(changes, c => c.Property == "IsActive");
        Assert.Equal("True", activeChange.Before);
        Assert.Equal("False", activeChange.After);
    }

    [Fact]
    public void GetChanges_NullableFromNullToValue_ReturnsChange()
    {
        var before = Sample(description: null);
        var after  = Sample(description: "hello") with { Id = before.Id, Name = before.Name, Count = before.Count, IsActive = before.IsActive, DateCreated = before.DateCreated, Price = before.Price };

        var changes = AuditChangeTracker.GetChanges(before, after);

        var descChange = Assert.Single(changes, c => c.Property == "Description");
        Assert.Null(descChange.Before);
        Assert.Equal("hello", descChange.After);
    }

    [Fact]
    public void GetChanges_NullableFromValueToNull_ReturnsChange()
    {
        var before = Sample(description: "hello");
        var after  = Sample(description: null) with { Id = before.Id, Name = before.Name, Count = before.Count, IsActive = before.IsActive, DateCreated = before.DateCreated, Price = before.Price };

        var changes = AuditChangeTracker.GetChanges(before, after);

        var descChange = Assert.Single(changes, c => c.Property == "Description");
        Assert.Equal("hello", descChange.Before);
        Assert.Null(descChange.After);
    }

    [Fact]
    public void GetChanges_MultiplePropertiesChanged_ReturnsAll()
    {
        var before = Sample("Alpha", count: 1);
        var after  = Sample("Beta", count: 99) with { Id = before.Id, IsActive = before.IsActive, DateCreated = before.DateCreated, Price = before.Price };

        var changes = AuditChangeTracker.GetChanges(before, after);

        Assert.Contains(changes, c => c.Property == "Name");
        Assert.Contains(changes, c => c.Property == "Count");
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void GetChanges_SkipsNavigationProperties()
    {
        var parent1 = Sample("parent");
        var parent2 = Sample("different parent");
        var before = Sample() with { Parent = parent1 };
        var after  = before  with { Parent = parent2 };

        // Attach nav props — they must not appear in changes
        var changes = AuditChangeTracker.GetChanges(before, after);

        Assert.DoesNotContain(changes, c => c.Property == "Parent");
        Assert.DoesNotContain(changes, c => c.Property == "Children");
    }

    [Fact]
    public void GetChanges_DifferentTypes_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AuditChangeTracker.GetChanges(Sample(), new { Name = "x" }));
    }

    [Fact]
    public void GetChanges_NullBefore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AuditChangeTracker.GetChanges(null!, Sample()));
    }

    [Fact]
    public void GetChanges_DateTimeFormatted_UsesExpectedFormat()
    {
        var dt = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
        var before = Sample() with { DateCreated = dt };
        var after  = Sample() with { Id = before.Id, Name = before.Name, Count = before.Count, IsActive = before.IsActive, Price = before.Price, DateCreated = dt.AddDays(1) };

        var changes = AuditChangeTracker.GetChanges(before, after);

        var dtChange = Assert.Single(changes, c => c.Property == "DateCreated");
        Assert.Equal("2026-07-14 12:00:00", dtChange.Before);
        Assert.Equal("2026-07-15 12:00:00", dtChange.After);
    }

    // ── ToPropertySnapshot ────────────────────────────────────────────────────

    [Fact]
    public void ToPropertySnapshot_IncludesAllScalarProperties()
    {
        var entity = Sample("TestEntity", count: 7);

        var snapshot = AuditChangeTracker.ToPropertySnapshot(entity);

        Assert.True(snapshot.ContainsKey("Id"));
        Assert.True(snapshot.ContainsKey("Name"));
        Assert.True(snapshot.ContainsKey("Count"));
        Assert.True(snapshot.ContainsKey("IsActive"));
        Assert.True(snapshot.ContainsKey("DateCreated"));
        Assert.True(snapshot.ContainsKey("Price"));
    }

    [Fact]
    public void ToPropertySnapshot_ExcludesNavigationProperties()
    {
        var entity = Sample() with { Parent = Sample("parent") };

        var snapshot = AuditChangeTracker.ToPropertySnapshot(entity);

        Assert.False(snapshot.ContainsKey("Parent"));
        Assert.False(snapshot.ContainsKey("Children"));
    }

    [Fact]
    public void ToPropertySnapshot_NullPropertyValue_StoredAsNull()
    {
        var entity = Sample(description: null);

        var snapshot = AuditChangeTracker.ToPropertySnapshot(entity);

        Assert.True(snapshot.ContainsKey("Description"));
        Assert.Null(snapshot["Description"]);
    }

    [Fact]
    public void ToPropertySnapshot_NullEntity_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AuditChangeTracker.ToPropertySnapshot(null!));
    }

    // ── Scalar type coverage (via ToPropertySnapshot) ───────────────────────

    [Fact]
    public void ToPropertySnapshot_StringGuidDateTimeBoolIntDecimal_AllCaptured()
    {
        var id = Guid.NewGuid();
        var dt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var entity = new SampleEntity { Id = id, Name = "x", Count = 3, IsActive = true, DateCreated = dt, Price = 1.5m };

        var snap = AuditChangeTracker.ToPropertySnapshot(entity);

        Assert.Equal(id.ToString(),         snap["Id"]);
        Assert.Equal("x",                   snap["Name"]);
        Assert.Equal("3",                   snap["Count"]);
        Assert.Equal("True",                snap["IsActive"]);
        Assert.Equal("2026-01-01 00:00:00", snap["DateCreated"]);
        Assert.Equal("1.5",                 snap["Price"]);
    }

    [Fact]
    public void ToPropertySnapshot_CollectionProperty_NotCaptured()
    {
        var entity = new SampleEntity { Children = [new SampleEntity { Name = "child" }] };

        var snap = AuditChangeTracker.ToPropertySnapshot(entity);

        Assert.False(snap.ContainsKey("Children"));
    }
}
