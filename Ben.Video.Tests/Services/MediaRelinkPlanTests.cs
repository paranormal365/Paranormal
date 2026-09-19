using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Whether to fetch a project's missing media straight away, or ask first.
/// </summary>
/// <remarks>
/// The wrong answer either interrupts somebody over four megabytes or starts moving an evening's
/// session recordings in silence (2026-09-05 audit, F14).
/// </remarks>
public sealed class MediaRelinkPlanTests
{
    private const long OneMb = 1024L * 1024;

    private static MediaRelinkCandidate Clip(long? sizeBytes) =>
        new(Guid.NewGuid(), Guid.NewGuid(), ".mp4", sizeBytes);

    [Fact]
    public void Nothing_missing_asks_nothing()
        => Assert.False(MediaRelinkPlan.ShouldAskFirst([]));

    [Fact]
    public void A_few_short_clips_are_fetched_without_asking()
    {
        var candidates = new[] { Clip(3 * OneMb), Clip(5 * OneMb), Clip(2 * OneMb) };

        Assert.False(MediaRelinkPlan.ShouldAskFirst(candidates));
    }

    [Fact]
    public void An_evenings_recordings_are_not()
    {
        var candidates = new[] { Clip(300 * OneMb), Clip(112 * OneMb) };

        Assert.True(MediaRelinkPlan.ShouldAskFirst(candidates));
    }

    /// <summary>
    /// Several small clips can add up past the limit, so the total is what decides.
    /// </summary>
    [Fact]
    public void The_total_is_what_counts_not_any_one_clip()
    {
        var candidates = Enumerable.Range(0, 12).Select(_ => Clip(5 * OneMb)).ToArray();

        Assert.True(MediaRelinkPlan.ShouldAskFirst(candidates));
    }

    /// <summary>
    /// A size that was never recorded is a reason to ask, not a reason to assume zero.
    /// </summary>
    /// <remarks>
    /// Not knowing how much is about to be downloaded is precisely the case where somebody else
    /// should decide; treating it as nothing lets an unbounded download start in silence.
    /// </remarks>
    [Fact]
    public void An_unrecorded_size_is_asked_about()
    {
        var candidates = new[] { Clip(1 * OneMb), Clip(null) };

        Assert.True(MediaRelinkPlan.ShouldAskFirst(candidates));
    }

    [Fact]
    public void The_question_says_how_much_when_it_knows()
    {
        var text = MediaRelinkPlan.Describe([Clip(300 * OneMb), Clip(112 * OneMb)]);

        Assert.Contains("2 clips", text);
        Assert.Contains("412 MB", text);
    }

    [Fact]
    public void And_says_it_does_not_when_it_does_not()
    {
        var text = MediaRelinkPlan.Describe([Clip(1 * OneMb), Clip(null)]);

        Assert.Contains("not recorded", text);
        Assert.DoesNotContain("1 MB", text);
    }

    [Fact]
    public void One_clip_is_not_described_as_one_clips()
    {
        var text = MediaRelinkPlan.Describe([Clip(200 * OneMb)]);

        Assert.Contains("1 clip ", text);
        Assert.DoesNotContain("1 clips", text);
    }

    [Fact]
    public void Only_recorded_sizes_are_totalled()
    {
        var total = MediaRelinkPlan.KnownTotalBytes([Clip(4 * OneMb), Clip(null), Clip(6 * OneMb)]);

        Assert.Equal(10 * OneMb, total);
    }
}
