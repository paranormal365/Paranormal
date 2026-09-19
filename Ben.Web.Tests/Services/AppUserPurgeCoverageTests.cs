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
/// <para><b>The failure being guarded against.</b> The purge empties some tables and then asks the
/// model whether anything still points at the account, to decide whether the row can go. There are
/// two ways a table can be accounted for: named in <c>sweptEntities</c>, which means it is emptied
/// of this person entirely, or given a precise set of doomed row ids in <c>GoingRowsAsync</c>,
/// which is how the partly-emptied ones are handled. A table the purge deletes from and neither
/// mechanism covers makes every account look permanent, so the row never goes.</para>
///
/// <para><b>And the dangerous direction, which actually happened.</b> Field sessions and upload
/// files were once listed in <c>sweptEntities</c>, but the purge empties them only partly — a
/// session recorded for an investigation stays, and so does a file something else holds. The
/// census therefore reported "nothing points at this account" about an account two of its own
/// tables still pointed at, the row delete was attempted, and the database refused it after the
/// anonymise had already been committed. <c>AppUserPurgeBehaviourTests</c> caught it; the third
/// test below is what stops it coming back.</para>
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

    /// <summary>
    /// Entities the purge excludes from the census row by row, read from <c>GoingRowsAsync</c> —
    /// the other half of "accounted for", used for the tables it empties only partly.
    /// </summary>
    private static HashSet<string> RowByRowEntities()
    {
        var source = PurgeSource();
        // Anchored on the DECLARATION, not the call: a looser match starts at the call site and
        // swallows everything up to the method body, sweptEntities included.
        var block = Regex.Match(source,
            @"Task<Dictionary<string, HashSet<Guid>>> GoingRowsAsync\(.*?\) \{(.*?)return going;");
        Assert.True(block.Success,
            "AppUserPurge no longer declares GoingRowsAsync, so this guard cannot see which "
          + "partly-emptied tables it excludes row by row. Update the guard rather than deleting it.");

        return Regex.Matches(block.Groups[1].Value, @"nameof\((\w+)\)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void Every_table_the_purge_empties_is_also_excluded_from_the_census()
    {
        var source = PurgeSource();
        var swept = SweptEntities();
        var rowByRow = RowByRowEntities();

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
                       && !swept.Contains(clr)
                       && !rowByRow.Contains(clr))
            .OrderBy(s => s)
            .ToList();

        Assert.True(missing.Count == 0,
            "These tables are emptied by the purge but still counted by the reference census, so "
          + "every account will look permanent and no row will ever be removed:\n  "
          + string.Join("\n  ", missing)
          + "\nAdd each to sweptEntities in AppUserPurge if it is emptied entirely, or to "
          + "GoingRowsAsync if only some of its rows go.");
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


        using var db = Context();
        var setByClr = db.Model.GetEntityTypes()
            .Where(e => e.ClrType is not null)
            .ToDictionary(e => e.ClrType!.Name, e => SetNameFor(db, e.ClrType!), StringComparer.Ordinal);

        var unjustified = swept
            .Where(clr => !clearedByAnonymise.Contains(clr)
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

    /// <summary>
    /// The two mechanisms are alternatives, never both. A table listed in <c>sweptEntities</c> is
    /// skipped outright, so also giving it a doomed-row set would be a contradiction — and it
    /// would mean somebody had described a partly-emptied table as an entirely-emptied one, which
    /// is the mistake that let the purge promise a row removal the database refused.
    /// </summary>
    [Fact]
    public void A_table_is_either_emptied_entirely_or_excluded_row_by_row_never_both()
    {
        var overlap = SweptEntities().Intersect(RowByRowEntities(), StringComparer.Ordinal)
            .OrderBy(s => s).ToList();

        Assert.True(overlap.Count == 0,
            "These entities are both skipped wholesale by the census and given a doomed-row set:\n  "
          + string.Join("\n  ", overlap)
          + "\nPick one. Wholesale means the purge empties the table of this person completely; "
          + "a doomed-row set means only some of its rows go and the rest must still be counted.");
    }

    /// <summary>
    /// The partly-emptied tables, named. This is the specific regression: each of these was once
    /// in <c>sweptEntities</c>, and each holds rows that survive the purge — a session recorded
    /// for an investigation, a file something else still references.
    /// </summary>
    [Theory]
    [InlineData(nameof(FieldSessionUpload), "a session recorded for an investigation is the group's and stays")]
    [InlineData(nameof(UploadFile), "a file something else still holds is left standing")]
    public void A_partly_emptied_table_is_never_skipped_wholesale(string entity, string why)
    {
        Assert.False(SweptEntities().Contains(entity),
            $"{entity} is skipped by the census as though the purge emptied it, but {why}. "
          + "The census would then report nothing pointing at the account, the row delete would be "
          + "attempted, and the database would refuse it after the anonymise had been committed.");
        Assert.True(RowByRowEntities().Contains(entity),
            $"{entity} has to name its doomed rows in GoingRowsAsync, or the rows that are about "
          + "to go will be counted as reasons to keep the account.");
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
