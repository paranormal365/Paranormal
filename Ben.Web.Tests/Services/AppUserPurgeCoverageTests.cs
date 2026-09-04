using System.Text.RegularExpressions;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Deleting a person has to be honest about which of the two things it did, and the census that
/// decides that has to know about every table.
/// </summary>
/// <remarks>
/// <para><b>Why a model scan rather than a behaviour test.</b> <c>AppUserPurge</c> uses
/// <c>ExecuteDeleteAsync</c>, transactions and raw <c>COUNT</c> queries, none of which the
/// in-memory provider implements — the same wall the organization purge hit, and the reason its
/// coverage is a scan too. What can be checked without a database is the part that actually goes
/// wrong: a table added next year that nobody remembers to think about.</para>
///
/// <para><b>The failure being guarded against.</b> The purge sweeps a fixed list of tables and
/// then asks the model whether anything still points at the account, to decide whether the row can
/// go. If the sweep list and the census disagree — a table swept but still counted — every account
/// looks permanent and the row never goes. If a table is swept and NOT excluded from the census,
/// the same. Both are silent.</para>
/// </remarks>
public sealed class AppUserPurgeCoverageTests
{
    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, relative);
    }

    /// <summary>The purge's source with comments removed — prose does not delete rows.</summary>
    private static string PurgeSource()
    {
        var source = File.ReadAllText(RepoFile("Ben.Data.WebApi/Services/Admin/AppUserPurge.cs"));
        source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        source = Regex.Replace(source, @"//[^\n]*", " ");
        return Regex.Replace(source, @"\s+", " ");
    }

    private static BenDataContext Context() => new(
        new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>Entity names the purge names inside its swept-tables set.</summary>
    private static HashSet<string> SweptEntities()
    {
        var source = PurgeSource();
        var block = Regex.Match(source, @"sweptEntities = new HashSet<string>\(StringComparer\.Ordinal\) \{(.*?)\};");
        Assert.True(block.Success,
            "AppUserPurge no longer declares a sweptEntities set, so this guard cannot see what "
          + "it clears. Update the guard rather than deleting it.");

        return Regex.Matches(block.Groups[1].Value, @"nameof\((\w+)\)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void Every_table_the_purge_empties_is_also_excluded_from_the_census()
    {
        var source = PurgeSource();
        var swept = SweptEntities();

        // The DbSets the purge actually issues a delete against, read from the source.
        var deleted = Regex.Matches(source, @"db\.(\w+)\s*\.Where\([^;]*?\)\.ExecuteDeleteAsync\(")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        using var db = Context();
        var entityBySet = db.Model.GetEntityTypes()
            .Where(e => e.ClrType is not null)
            .ToDictionary(e => SetNameFor(db, e.ClrType!), e => e.ClrType!.Name, StringComparer.Ordinal);

        var missing = deleted
            .Where(set => entityBySet.TryGetValue(set, out var clr)
                       && clr != nameof(AppUser)
                       && !swept.Contains(clr))
            .OrderBy(s => s)
            .ToList();

        Assert.True(missing.Count == 0,
            "These tables are emptied by the purge but still counted by the reference census, so "
          + "every account will look permanent and no row will ever be removed:\n  "
          + string.Join("\n  ", missing)
          + "\nAdd each to the sweptEntities set in AppUserPurge.");
    }

    [Fact]
    public void The_census_never_excludes_a_table_the_purge_does_not_actually_empty()
    {
        var source = PurgeSource();
        var swept = SweptEntities();

        // Entities cleared by the shared anonymise step rather than by the purge's own deletes.
        // Named here so the two halves of "what gets emptied" stay visible in one place.
        var clearedByAnonymise = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(UserAddress), nameof(UserEmail), nameof(UserPhone), nameof(UserLink),
            nameof(AppUserPhoto),
        };

        // UploadFile rows go after the transaction, one at a time, so they are not in the
        // ExecuteDeleteAsync sweep the other test reads.
        var clearedSeparately = new HashSet<string>(StringComparer.Ordinal) { nameof(UploadFile) };

        using var db = Context();
        var setByClr = db.Model.GetEntityTypes()
            .Where(e => e.ClrType is not null)
            .ToDictionary(e => e.ClrType!.Name, e => SetNameFor(db, e.ClrType!), StringComparer.Ordinal);

        var unjustified = swept
            .Where(clr => !clearedByAnonymise.Contains(clr)
                       && !clearedSeparately.Contains(clr)
                       && !(setByClr.TryGetValue(clr, out var set)
                            && Regex.IsMatch(source, @"db\." + Regex.Escape(set) + @"\s*\.Where\([^;]*?\)\.ExecuteDeleteAsync\(")))
            .OrderBy(s => s)
            .ToList();

        // The dangerous direction: a table excluded from the census that the purge does NOT clear
        // means the row delete is attempted while rows still point at the account. The database
        // refuses it, the account is left anonymised, and the screen has already told a SuperAdmin
        // it would be removed completely.
        Assert.True(unjustified.Count == 0,
            "These entities are excluded from the reference census but nothing empties them, so "
          + "the purge may promise a row removal the database then refuses:\n  "
          + string.Join("\n  ", unjustified));
    }

    [Fact]
    public void The_census_asks_about_every_foreign_key_into_AppUsers()
    {
        using var db = Context();

        var referencing = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetForeignKeys()
                .Where(fk => fk.PrincipalEntityType.ClrType == typeof(AppUser))
                .Select(fk => (Entity: e.ClrType.Name, Properties: fk.Properties.Count)))
            .ToList();

        // The census skips composite keys, so a composite reference to AppUsers would go uncounted
        // and the row delete would be attempted with rows still pointing at it. There are none
        // today; this fails on the day somebody adds one.
        var composite = referencing.Where(r => r.Properties != 1).Select(r => r.Entity).Distinct().Order().ToList();
        Assert.True(composite.Count == 0,
            "These entities reference AppUsers with a composite key, which AppUserPurge's census "
          + "skips — so the purge would not see them:\n  " + string.Join("\n  ", composite));

        // And a sanity floor: if this ever reads zero the census is looking at the wrong thing and
        // every account would appear deletable.
        Assert.True(referencing.Count > 100,
            $"Only {referencing.Count} foreign keys into AppUsers were found. The census is "
          + "almost certainly reading the wrong model.");
    }

    private static string SetNameFor(BenDataContext db, Type clrType)
        => db.Model.FindEntityType(clrType)?.GetTableName() is { } table
            ? db.GetType().GetProperties()
                .FirstOrDefault(p => p.PropertyType.IsGenericType
                                  && p.PropertyType.GetGenericArguments()[0] == clrType)?.Name ?? table
            : clrType.Name;
}
