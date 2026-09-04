using System.Text.RegularExpressions;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Deleting a case must delete everything that exists only because of it, and the database must
/// never be the one to say no (item 183).
/// </summary>
/// <remarks>
/// <para><b>Why this shape of test.</b> The group purge was refused twice on production because it
/// was a hand-kept list of tables and the schema moved underneath it. This one is derived from the
/// model instead: for every relationship whose principal is a table the case purge deletes from,
/// and whose delete behaviour leaves the row for the database to refuse, the dependent table has
/// to be one the purge deletes from or clears too. The next table anyone hangs off a case, a
/// report or an investigation fails this test the day it is added.</para>
///
/// <para><b>It also guards the other direction.</b> A case purge that reached into a person's
/// field sessions or their files would be destroying somebody else's property to tidy up a
/// duplicate, so the sets it must never delete from are named here with the reason.</para>
///
/// <para>The scan strips comments first. A guard that can be satisfied by a sentence in a comment
/// is not a guard.</para>
/// </remarks>
public sealed class CasePurgeCoverageTests
{
    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, relative);
    }

    private static string PurgeSource()
    {
        var source = File.ReadAllText(RepoFile("Ben.Data.WebApi/Services/Admin/CasePurge.cs"));
        source = Regex.Replace(source, @"//[^\n]*", "");            // comments do not delete rows
        return Regex.Replace(source, @"\s+", " ");                   // a chained call may span lines
    }

    /// <summary>
    /// Whether any statement on <paramref name="set"/> mentions every one of these properties —
    /// the difference between sweeping a table and sweeping the right column of it.
    /// </summary>
    private static bool Names(string set, IEnumerable<string> properties)
    {
        var statements = Regex.Matches(PurgeSource(), @"db\." + set + @"\s*\.Where\([^;]*;")
            .Select(m => m.Value).ToList();
        return statements.Any(s => properties.All(p => Regex.IsMatch(s, @"\b" + p + @"\b")));
    }

    private static HashSet<string> PurgedSets()
        => Regex.Matches(PurgeSource(), @"db\.(\w+)\s*\.Where\([^;]*?\)\s*\.ExecuteDeleteAsync\(")
                .Select(m => m.Groups[1].Value)
                .ToHashSet();

    private static HashSet<string> ClearedSets()
        => Regex.Matches(PurgeSource(), @"db\.(\w+)\s*\.Where\([^;]*?\)\s*\.ExecuteUpdateAsync\(")
                .Select(m => m.Groups[1].Value)
                .ToHashSet();

    private static string? SetName(Type clr)
        => typeof(BenDataContext).GetProperties()
            .FirstOrDefault(p => p.PropertyType.IsGenericType
                              && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                              && p.PropertyType.GetGenericArguments()[0] == clr)?.Name;

    [Fact]
    public void Every_table_that_would_block_the_purge_is_purged()
    {
        var purged  = PurgedSets();
        var cleared = ClearedSets();
        Assert.Contains("Cases", purged);   // the scan must at least see the root delete

        using var db = new BenDataContext(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        var blockers = new List<string>();

        foreach (var entity in db.Model.GetEntityTypes())
        {
            foreach (var fk in entity.GetForeignKeys())
            {
                var principalSet = SetName(fk.PrincipalEntityType.ClrType);
                var dependentSet = SetName(entity.ClrType);
                if (principalSet is null || dependentSet is null) continue;
                if (!purged.Contains(principalSet)) continue;

                // Cascade: SQL removes the dependent itself. SetNull: SQL clears the reference.
                // Anything else leaves the row standing and the database refuses the delete.
                if (fk.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.SetNull) continue;
                if (principalSet == dependentSet) continue;   // a self-reference dies with its table

                // A table swept by one column can still block on an optional reference it carries
                // to another of the purged rows, so a purged or cleared set counts as handled for
                // THIS reference only when a statement on it names the reference's property.
                var optional = fk.Properties.All(p => p.IsNullable);
                if (purged.Contains(dependentSet) && (!optional || Names(dependentSet, fk.Properties.Select(p => p.Name)))) continue;
                if (cleared.Contains(dependentSet) && optional && Names(dependentSet, fk.Properties.Select(p => p.Name))) continue;

                blockers.Add($"{dependentSet}.{string.Join("+", fk.Properties.Select(p => p.Name))} -> {principalSet} ({fk.DeleteBehavior})");
            }
        }

        Assert.True(blockers.Count == 0,
            "Deleting a case would be refused by the database because these tables reference a "
            + "purged table and the purge never deletes from or clears them:\n  "
            + string.Join("\n  ", blockers.Distinct().Order())
            + "\nAdd each to CasePurge.PurgeAsync, before the table it points at.");
    }

    /// <summary>
    /// The readable companion: everything carrying a <c>CaseId</c> has to appear in the purge by
    /// name. The model-driven test above is the thorough one; this one says which table is missing
    /// in one line when it fails.
    /// </summary>
    [Fact]
    public void Every_table_that_carries_a_case_reference_is_named_in_the_purge()
    {
        var source = PurgeSource();
        var entities = typeof(Ben.Data.Source.Entities.Case).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                     && t.Namespace == typeof(Ben.Data.Source.Entities.Case).Namespace
                     && t.Name != nameof(Ben.Data.Source.Entities.Case)
                     && t.GetProperty("CaseId") is not null)
            .Select(t => t.Name).Distinct().Order().ToList();
        Assert.NotEmpty(entities);

        var missing = entities.Where(name => !Mentions(source, name)).ToList();
        Assert.True(missing.Count == 0,
            "These carry a CaseId and the purge never mentions them:\n  " + string.Join("\n  ", missing));
    }

    private static bool Mentions(string source, string entityName)
        => source.Contains(entityName, StringComparison.Ordinal)
        || (entityName.EndsWith('y') && source.Contains($"{entityName[..^1]}ies", StringComparison.Ordinal))
        || source.Contains($"{entityName}s", StringComparison.Ordinal)
        || (entityName.EndsWith("Person") && source.Contains($"{entityName[..^6]}People", StringComparison.Ordinal));

    /// <summary>
    /// And the purge must not reach past the case. Checked as an actual delete against the set —
    /// several of these are deliberately <i>updated</i> to forget the case, which is the opposite
    /// of deleting them and must not trip this.
    /// </summary>
    [Theory]
    [InlineData("FieldSessionUploads", "a recording belongs to the person who made it; the purge detaches it from the investigation so it returns to them as a personal session")]
    [InlineData("UploadFiles", "a file belongs to its owner; only the case's own copy-on-attach copies go, one at a time through UploadFileRows, so a file something else still holds stays")]
    [InlineData("ClientRequests", "the request is the client's own record and outlives the case that was opened from it")]
    [InlineData("AppUsers", "a person is not a case's property")]
    [InlineData("Organizations", "the group outlives its cases")]
    [InlineData("OrgMessages", "a feed post is its author's; the purge clears its case reference instead")]
    [InlineData("VideoProjects", "an edit is the editor's work; it loses the case link, not its existence")]
    [InlineData("OrgCalendarEvents", "an event on the calendar happened; it loses the case link only")]
    [InlineData("OrganizationPages", "a public page is the group's; it loses the case link only")]
    [InlineData("EquipmentCheckouts", "a loan is the equipment's history and must survive the investigation it was for")]
    [InlineData("Places", "a place is shared, and its public archive is built from many people's visits")]
    public void The_purge_never_deletes_what_the_case_does_not_own(string forbidden, string why)
    {
        Assert.False(PurgedSets().Contains(forbidden), $"The case purge deletes from {forbidden}, but {why}.");
    }

    /// <summary>
    /// The positive half of the rule above: the sessions are actually detached, not merely left
    /// alone. Leaving them attached to an investigation row that no longer exists would be worse
    /// than deleting them.
    /// </summary>
    [Fact]
    public void A_field_session_is_detached_from_the_investigation_rather_than_deleted()
    {
        var source = PurgeSource();
        Assert.Contains("db.FieldSessionUploads", source);
        Assert.True(ClearedSets().Contains("FieldSessionUploads"),
            "The purge must clear FieldSessionUploads.InvestigationId, or the sessions are left "
            + "pointing at an investigation that no longer exists.");
        Assert.True(Names("FieldSessionUploads", ["InvestigationId"]),
            "The statement on FieldSessionUploads must be the one that clears InvestigationId.");
    }
}
