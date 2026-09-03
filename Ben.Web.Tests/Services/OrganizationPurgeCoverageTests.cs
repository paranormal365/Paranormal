using System.Text.RegularExpressions;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Deleting a group must delete everything the group owns, and the database must never be the one
/// to say no.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> On 2026-09-03 Ben deleted Music City Spirit Seekers on production
/// and got: <i>The DELETE statement conflicted with the REFERENCE constraint
/// FK_InvestigationDutyAssignments_InvestigationAttendees_InvestigationAttendeeId.</i> The purge is
/// a hand-kept list of tables, duty assignments were added to the schema after it was written, and
/// nothing noticed. A SuperAdmin deleting a whole group has a reason; the answer must be yes.</para>
///
/// <para><b>What it checks</b> is derived from the model rather than from anybody's memory: for
/// every relationship whose principal is a table the purge deletes from, and whose delete behaviour
/// leaves the row for the database to refuse (anything but Cascade or SetNull), the dependent
/// table must be one the purge deletes from too. The next table anyone hangs off an attendee, a
/// case or an investigation fails this test the day it is added.</para>
/// </remarks>
public sealed class OrganizationPurgeCoverageTests
{
    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, relative);
    }

    private static string PurgeSource()
    {
        var source = File.ReadAllText(RepoFile("Ben.Data.WebApi/Services/Admin/OrganizationPurge.cs"));
        source = Regex.Replace(source, @"//[^\n]*", "");            // comments do not delete rows
        return Regex.Replace(source, @"\s+", " ");                   // a chained call may span lines
    }

    /// <summary>The DbSets the purge issues a delete against, read from the source.</summary>
    private static HashSet<string> PurgedSets()
        => Regex.Matches(PurgeSource(), @"db\.(\w+)\s*\.Where\([^;]*?\)\s*\.ExecuteDeleteAsync\(")
                .Select(m => m.Groups[1].Value)
                .ToHashSet();

    /// <summary>
    /// The DbSets the purge updates rather than deletes — a proposal to the shared equipment or
    /// experience catalogue outlives the group that made it, so the purge clears the reference
    /// instead. Accepted only for a nullable reference; a required one cannot be cleared.
    /// </summary>
    private static HashSet<string> ClearedSets()
        => Regex.Matches(PurgeSource(), @"db\.(\w+)\s*\.Where\([^;]*?\)\s*\.ExecuteUpdateAsync\(")
                .Select(m => m.Groups[1].Value)
                .ToHashSet();

    [Fact]
    public void Every_table_that_would_block_the_purge_is_purged()
    {
        var purged  = PurgedSets();
        var cleared = ClearedSets();
        Assert.Contains("Organizations", purged);   // the scan must at least see the root delete

        using var db = new BenDataContext(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        var setByClr = db.Model.GetEntityTypes()
            .Where(e => e.ClrType is not null)
            .ToDictionary(e => e.ClrType!, e => SetName(db, e.ClrType!));

        var blockers = new List<string>();

        foreach (var entity in db.Model.GetEntityTypes())
        {
            foreach (var fk in entity.GetForeignKeys())
            {
                var principalSet = setByClr.GetValueOrDefault(fk.PrincipalEntityType.ClrType);
                var dependentSet = setByClr.GetValueOrDefault(entity.ClrType);
                if (principalSet is null || dependentSet is null) continue;
                if (!purged.Contains(principalSet)) continue;

                // Cascade: SQL removes the dependent itself. SetNull: SQL clears the reference.
                // Anything else leaves the row standing, and the database refuses the delete.
                if (fk.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.SetNull) continue;
                if (purged.Contains(dependentSet)) continue;
                if (cleared.Contains(dependentSet) && fk.Properties.All(p => p.IsNullable)) continue;
                if (principalSet == dependentSet) continue;   // a self-reference dies with its table

                blockers.Add($"{dependentSet}.{string.Join("+", fk.Properties.Select(p => p.Name))} -> {principalSet} ({fk.DeleteBehavior})");
            }
        }

        Assert.True(blockers.Count == 0,
            "Deleting a group would be refused by the database because these tables reference a "
            + "purged table and the purge never deletes from them:\n  "
            + string.Join("\n  ", blockers.Distinct().Order())
            + "\nAdd each to OrganizationPurge.PurgeAsync, before the table it points at.");
    }

    /// <summary>
    /// The earlier guard, kept: every entity that carries an <c>OrganizationId</c> must be named
    /// in the purge. The model-driven test above is the one that caught duty assignments — a
    /// table keyed on an attendee, with no OrganizationId to find — but this one reads faster
    /// when it fails, so both stay.
    /// </summary>
    [Fact]
    public void Every_table_that_belongs_to_a_group_is_named_in_the_purge()
    {
        var source = PurgeSource();
        var entities = typeof(Ben.Data.Source.Entities.Organization).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                     && t.Namespace == typeof(Ben.Data.Source.Entities.Organization).Namespace
                     && t.GetProperty("OrganizationId") is not null)
            .Select(t => t.Name).Distinct().Order().ToList();
        Assert.NotEmpty(entities);

        var missing = entities.Where(name => !Mentions(source, name)).ToList();
        Assert.True(missing.Count == 0,
            "These belong to a group and the purge never mentions them:\n  " + string.Join("\n  ", missing));
    }

    private static bool Mentions(string source, string entityName)
        => source.Contains(entityName, StringComparison.Ordinal)
        || (entityName.EndsWith('y') && source.Contains($"{entityName[..^1]}ies", StringComparison.Ordinal));

    /// <summary>
    /// And the purge must not reach past what a group owns. Checked as an actual delete against
    /// the set — the catalogue rows are <i>updated</i> to forget who proposed them, which is the
    /// opposite of deleting them and must not trip this.
    /// </summary>
    [Theory]
    [InlineData("AppUsers", "a person is not a group's property and may belong to other groups")]
    [InlineData("Places", "a place is shared, and its public archive is built from many people's visits")]
    [InlineData("UploadFileTypes", "site-wide lookup data that no group owns")]
    [InlineData("EquipmentModels", "the equipment catalogue is site-wide reference data")]
    [InlineData("EquipmentBrands", "the equipment catalogue is site-wide reference data")]
    [InlineData("ExperienceTypes", "the experience taxonomy is site-wide reference data")]
    [InlineData("UploadFiles", "a file belongs to the person who uploaded it; the purge removes shares into the group, not the file")]
    public void The_purge_never_deletes_what_a_group_does_not_own(string forbidden, string why)
    {
        Assert.False(PurgedSets().Contains(forbidden), $"The purge deletes from {forbidden}, but {why}.");
    }

    [Fact]
    public void Personal_field_sessions_are_not_swept_up_with_the_group()
    {
        var source = PurgeSource();
        Assert.Contains("InvestigationId", source);
        Assert.DoesNotContain("db.FieldSessionUploads.Where(x => x.SubmittedByAppUserId", source);
    }

    private static string? SetName(BenDataContext db, Type clr)
        => typeof(BenDataContext).GetProperties()
            .FirstOrDefault(p => p.PropertyType.IsGenericType
                              && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                              && p.PropertyType.GetGenericArguments()[0] == clr)?.Name;
}
