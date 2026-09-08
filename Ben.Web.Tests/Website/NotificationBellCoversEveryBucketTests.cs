using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Every notification bucket that is counted is a bucket that can be explained.
/// </summary>
/// <remarks>
/// <para>The badge sums <b>every</b> bucket on <see cref="NotificationSummaryResponse"/>. The rows
/// beneath it are hand-written, one per bucket somebody remembered. Nothing connected the two, so
/// adding a bucket added it to the number and not to the list — which is exactly what happened to
/// feed mentions: the bell read "3 items waiting" and then accounted for two of them.</para>
///
/// <para>That failure is invisible to every other test. The count is right, the rows that exist
/// are right, and only somebody who both has a mention and bothers to add up the popover would
/// notice. It is also silent at compile time, because a missing row is a branch nobody wrote.</para>
///
/// <para><b>This used to read the bell's source with a regex</b>, looking for each bucket's name
/// inside its <c>Rows()</c> method. It worked, and it was aimed at one of the three surfaces that
/// each built their own list — so when the sidebar and the notifications page picked different
/// subsets (W-CL3, 2026-09-06), the guard was green throughout. It now asks the question at
/// runtime, of the one shared list every surface reads: build a summary with every bucket
/// occupied, and check the number equals what the rows account for. A ninth bucket added to the
/// DTO and forgotten in <see cref="NotificationRows"/> fails this the day it is added, without
/// anyone updating a list here.</para>
/// </remarks>
public sealed class NotificationBellCoversEveryBucketTests
{
    /// <summary>
    /// A summary with every one of its buckets holding exactly one waiting item.
    /// </summary>
    /// <remarks>
    /// Built by reflection over the record's own constructor rather than by naming the eight
    /// buckets here. Naming them is the mistake being guarded against.
    /// </remarks>
    private static (NotificationSummaryResponse Summary, int BucketCount) OneOfEverything()
    {
        var ctor = typeof(NotificationSummaryResponse).GetConstructors().Single();
        var parameters = ctor.GetParameters();
        var occupied = new NotificationBucket(1, new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));

        var args = new object?[parameters.Length];
        var buckets = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType == typeof(NotificationBucket))
            {
                args[i] = occupied;
                buckets++;
            }
            else
            {
                // Everything else takes its default — the per-org and per-case slices stay null,
                // which is the shape of a payload from before item 173 and a case the rows have
                // to handle anyway.
                args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            }
        }

        return ((NotificationSummaryResponse)ctor.Invoke(args), buckets);
    }

    [Fact]
    public void Every_bucket_counted_by_the_badge_has_a_row_beneath_it()
    {
        var (summary, bucketCount) = OneOfEverything();

        var rows  = NotificationRows.For(summary);
        var shown = rows.Sum(r => r.Bucket.Count);

        Assert.True(shown == summary.TotalCount,
            $"The badge would read {summary.TotalCount} over {rows.Count} rows accounting for "
            + $"{shown}. A bucket on NotificationSummaryResponse has no row in NotificationRows, "
            + "so the number will not add up to what the list explains.");

        Assert.Equal(bucketCount, shown);
    }

    [Fact]
    public void The_bucket_list_is_not_empty()
    {
        // Without this, a rename of NotificationBucket would leave the test above passing over an
        // empty list — green, and checking nothing at all.
        var (_, bucketCount) = OneOfEverything();
        Assert.True(bucketCount >= 5,
            $"Only {bucketCount} buckets were found on NotificationSummaryResponse. "
            + "Has the type changed shape?");
    }
}
