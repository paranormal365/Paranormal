using System.Text.RegularExpressions;
using Ben.Service.Models.Entities;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Every notification bucket the bell counts is a bucket the bell can explain.
/// </summary>
/// <remarks>
/// <para>The bell's badge is <c>TotalCount</c>, which sums <b>every</b> bucket. Its dropdown is a
/// hand-written list of rows. Nothing connected the two, so adding a bucket added it to the number
/// and not to the list — which is exactly what happened to feed mentions: the bell read "3 items
/// waiting" and then accounted for two of them.</para>
///
/// <para>That failure is invisible to every other test. The count is right, the rows that exist
/// are right, and only somebody who both has a mention and bothers to add up the popover would
/// notice. It is also silent at compile time, because a missing row is a branch nobody wrote.</para>
///
/// <para>Source-scanned rather than rendered: the check is "did someone remember", and reading the
/// file is the most direct way to ask that. A rendering test would need a live summary with every
/// bucket populated, which is more machinery for a weaker answer.</para>
/// </remarks>
public sealed class NotificationBellCoversEveryBucketTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private static string BellSource() => File.ReadAllText(Path.Combine(
        RepoRoot().FullName, "Ben.Web.Website", "Components", "Layout", "BenNotificationBell.razor"));

    /// <summary>The bucket names, taken from the record itself rather than a list kept in step by hand.</summary>
    private static IReadOnlyList<string> BucketNames() =>
        typeof(NotificationSummaryResponse)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(NotificationBucket))
            .Select(p => p.Name)
            .ToList();

    [Fact]
    public void Every_bucket_counted_by_the_badge_has_a_row_in_the_popover()
    {
        var source = BellSource();

        // Only the row-building method: a bucket named in a comment elsewhere in the file would
        // otherwise satisfy this without producing a row anybody can see.
        var rows = Regex.Match(source, @"private IEnumerable<Row> Rows\(\)(.*?)\n    }", RegexOptions.Singleline);
        Assert.True(rows.Success, "Could not find Rows() in BenNotificationBell.razor — has it been renamed?");

        var missing = BucketNames()
            .Where(name => !rows.Groups[1].Value.Contains($"s.{name}", StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0,
            "These notification buckets are counted in the bell's badge but have no row in its "
            + "dropdown, so the number will not add up to what the list explains:\n  "
            + string.Join("\n  ", missing));
    }

    [Fact]
    public void The_bucket_list_is_not_empty()
    {
        // Without this, a rename of NotificationBucket would leave the test above passing over an
        // empty list — green, and checking nothing at all.
        Assert.True(BucketNames().Count >= 5,
            $"Only {BucketNames().Count} buckets were found on NotificationSummaryResponse. "
            + "Has the type changed shape?");
    }
}
