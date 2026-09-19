using System.Reflection;
using System.Text.RegularExpressions;
using Ben.Data.Source.Entities;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every table that points at a field session must be cleared by the orphan purge, or the purge
/// deletes nothing at all.
/// </summary>
/// <remarks>
/// <para><b>The bug this exists to stop happening twice.</b> Shipped 2026-09-02, the purge deleted
/// the session's own file rows and the session, and nothing else. <c>CaseReportSectionFieldSession</c>
/// also points at a session, with <c>OnDelete(NoAction)</c> — so on any database where a report had
/// ever cited a session, the delete threw a constraint violation, the transaction rolled back, and
/// the screen reported "Deleted 0". Ben found it on production within an hour.</para>
///
/// <para><b>Why a source scan rather than a behaviour test.</b> The purge uses
/// <c>ExecuteDeleteAsync</c>, which the in-memory provider does not implement, and transactions,
/// which it also does not have. A behaviour test would need a real SQL Server. This instead finds
/// every entity carrying a <c>FieldSessionUploadId</c> and asserts the purge's source names it —
/// crude, and aimed squarely at the failure that actually happens: a table added later whose author
/// never heard of this endpoint.</para>
///
/// <para><b>Comments are stripped before scanning.</b> The controller's own prose explains why it
/// clears citations and names the table while doing so; without stripping, this test would pass on
/// a version that only talks about the table and never deletes from it. That trap has caught guards
/// in this repository before.</para>
/// </remarks>
public sealed class OrphanedSessionPurgeCoverageTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("Could not find the repository root.");
    }

    /// <summary>The purge's source with every comment removed, so only real code counts.</summary>
    private static string PurgeCode()
    {
        var path = Path.Combine(RepoRoot().FullName,
            "Ben.Data.WebApi", "Controllers", "Admin", "AdminOrphanedFieldSessionController.cs");
        Assert.True(File.Exists(path), $"The purge controller has moved: {path}");

        var source = File.ReadAllText(path);
        source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);   // block comments
        source = Regex.Replace(source, @"//[^\n]*", " ");                             // line and doc comments
        return source;
    }

    /// <summary>
    /// Entity types with a <c>FieldSessionUploadId</c>: everything a session delete must get past.
    /// </summary>
    /// <summary>Entity names carrying a <c>FieldSessionUploadId</c>, as plain strings.</summary>
    private static List<string> ReferencingEntityNames() =>
        [.. typeof(FieldSessionUpload).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t != typeof(FieldSessionUpload))
            // Real entities only. LINQ projections in this assembly compile to anonymous types
            // that also carry a FieldSessionUploadId, and a purge cannot delete from one of those.
            .Where(t => t.Namespace == typeof(FieldSessionUpload).Namespace)
            .Where(t => !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
            .Where(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Any(p => p.Name == "FieldSessionUploadId"))
            .Select(t => t.Name)
            .Distinct()
            .OrderBy(n => n)];

    /// <summary>Everything a session delete must get past.</summary>
    public static TheoryData<string> ReferencingEntities()
    {
        var data = new TheoryData<string>();
        foreach (var name in ReferencingEntityNames()) data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(ReferencingEntities))]
    public void The_purge_clears_every_table_that_points_at_a_session(string entityName)
    {
        var code = PurgeCode();

        // The DbSet is named for the entity's plural; both spellings are accepted because the
        // point is that the table was not forgotten, not how the delete was written.
        var mentioned = code.Contains(entityName + "s", StringComparison.Ordinal)
                     || code.Contains(entityName, StringComparison.Ordinal);

        Assert.True(mentioned,
            $"{entityName} points at a field session, and the orphan purge never mentions it. "
            + "A foreign key that is not cleared first makes the whole delete fail and report "
            + "\"Deleted 0\" — see this fixture's remarks.");
    }

    /// <summary>
    /// The scan is only worth anything if it finds the table that broke it. If this ever fails,
    /// the reflection above has stopped seeing the citation entity and every other case is vacuous.
    /// </summary>
    [Fact]
    public void The_scan_actually_covers_the_citation_table()
    {
        Assert.Contains(nameof(CaseReportSectionFieldSession), ReferencingEntityNames());
    }
}
