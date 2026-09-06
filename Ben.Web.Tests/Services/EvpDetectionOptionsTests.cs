using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The bounds on a scan's dials.
/// </summary>
/// <remarks>
/// <para>Six rules, none of which had a test. They are the server's only defence against a
/// hand-built request: the fine-tune panel keeps its sliders inside these ranges, but the endpoint
/// takes the numbers from the body, and a scan run with a merge gap of ten seconds or a minimum
/// length longer than its maximum produces a candidate list that means nothing — which on this site
/// looks exactly like a finding (2026-09-06 audio audit, phase 6).</para>
///
/// <para>Each rule is checked at both ends of its range and just outside, because an off-by-one on
/// a boundary is the mistake these are actually likely to grow.</para>
/// </remarks>
public sealed class EvpDetectionOptionsTests
{
    [Fact]
    public void The_defaults_are_valid()
        => Assert.Null(new EvpDetectionOptions().Validate());

    [Theory]
    [InlineData(EvpSensitivity.Low)]
    [InlineData(EvpSensitivity.Medium)]
    [InlineData(EvpSensitivity.High)]
    public void Every_preset_is_valid(EvpSensitivity sensitivity)
        => Assert.Null(EvpDetectionOptions.FromSensitivity(sensitivity).Validate());

    /// <summary>Low finds only the obvious; High proposes far more. That ordering is the feature.</summary>
    [Fact]
    public void A_lower_sensitivity_needs_a_louder_sound()
    {
        var low    = EvpDetectionOptions.FromSensitivity(EvpSensitivity.Low);
        var medium = EvpDetectionOptions.FromSensitivity(EvpSensitivity.Medium);
        var high   = EvpDetectionOptions.FromSensitivity(EvpSensitivity.High);

        Assert.True(low.ThresholdDb > medium.ThresholdDb);
        Assert.True(medium.ThresholdDb > high.ThresholdDb);
    }

    // ── Each dial, at its edges and just outside ──────────────────────────────

    [Theory]
    [InlineData(2.0)]    // the floor
    [InlineData(6.0)]
    [InlineData(20.0)]   // the ceiling
    public void A_threshold_inside_the_range_is_accepted(double value)
        => Assert.Null(new EvpDetectionOptions(ThresholdDb: value).Validate());

    [Theory]
    [InlineData(1.99)]
    [InlineData(20.01)]
    [InlineData(0)]
    [InlineData(-6)]
    [InlineData(double.NaN)]
    public void A_threshold_outside_the_range_is_refused(double value)
    {
        var problem = new EvpDetectionOptions(ThresholdDb: value).Validate();

        Assert.NotNull(problem);
        Assert.Contains("Threshold", problem);
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(5.0)]
    public void A_minimum_length_inside_the_range_is_accepted(double value)
        => Assert.Null(new EvpDetectionOptions(MinDurationSeconds: value, MaxEventSeconds: 10).Validate());

    [Theory]
    [InlineData(0.04)]
    [InlineData(5.01)]
    [InlineData(double.NaN)]
    public void A_minimum_length_outside_the_range_is_refused(double value)
        => Assert.Contains("Minimum length",
            new EvpDetectionOptions(MinDurationSeconds: value).Validate() ?? "");

    [Theory]
    [InlineData(0.0)]
    [InlineData(2.0)]
    public void A_merge_gap_inside_the_range_is_accepted(double value)
        => Assert.Null(new EvpDetectionOptions(MergeGapSeconds: value).Validate());

    [Theory]
    [InlineData(-0.01)]
    [InlineData(2.01)]
    [InlineData(double.NaN)]
    public void A_merge_gap_outside_the_range_is_refused(double value)
        => Assert.Contains("Merge gap",
            new EvpDetectionOptions(MergeGapSeconds: value).Validate() ?? "");

    [Theory]
    [InlineData(0.0)]
    [InlineData(3.0)]
    public void A_context_pad_inside_the_range_is_accepted(double value)
        => Assert.Null(new EvpDetectionOptions(ContextPadSeconds: value).Validate());

    [Theory]
    [InlineData(-0.01)]
    [InlineData(3.01)]
    [InlineData(double.NaN)]
    public void A_context_pad_outside_the_range_is_refused(double value)
        => Assert.Contains("Context padding",
            new EvpDetectionOptions(ContextPadSeconds: value).Validate() ?? "");

    [Theory]
    [InlineData(1.0)]
    [InlineData(30.0)]
    public void A_longest_candidate_inside_the_range_is_accepted(double value)
        => Assert.Null(new EvpDetectionOptions(MaxEventSeconds: value).Validate());

    [Theory]
    [InlineData(0.99)]
    [InlineData(30.01)]
    [InlineData(double.NaN)]
    public void A_longest_candidate_outside_the_range_is_refused(double value)
        => Assert.Contains("Longest candidate",
            new EvpDetectionOptions(MaxEventSeconds: value).Validate() ?? "");

    // ── The rule that is about two dials at once ──────────────────────────────

    /// <summary>
    /// Both numbers can be in range and still contradict each other.
    /// </summary>
    /// <remarks>
    /// A minimum longer than the maximum asks for candidates that are both longer than four seconds
    /// and shorter than two. The scan would simply return nothing, and nothing is a finding here —
    /// it is what a clean recording looks like.
    /// </remarks>
    [Fact]
    public void A_minimum_longer_than_the_maximum_is_refused()
    {
        var problem = new EvpDetectionOptions(MinDurationSeconds: 4.0, MaxEventSeconds: 2.0).Validate();

        Assert.NotNull(problem);
        Assert.Contains("greater than the minimum", problem);
    }

    [Fact]
    public void A_minimum_equal_to_the_maximum_is_refused()
        => Assert.Contains("greater than the minimum",
            new EvpDetectionOptions(MinDurationSeconds: 2.0, MaxEventSeconds: 2.0).Validate() ?? "");

    [Fact]
    public void A_minimum_just_below_the_maximum_is_accepted()
        => Assert.Null(new EvpDetectionOptions(MinDurationSeconds: 1.9, MaxEventSeconds: 2.0).Validate());

    /// <summary>
    /// NaN survives every comparison, so a range check written as two inequalities lets it through.
    /// </summary>
    /// <remarks>
    /// This is the same trap the audio edit endpoints fell into. Here it is already handled; the
    /// test is what keeps it handled through the next refactor of <c>OutOfRange</c>.
    /// </remarks>
    [Fact]
    public void NaN_would_pass_a_range_check_written_the_obvious_way()
    {
        Assert.False(double.NaN < 2.0);
        Assert.False(double.NaN > 20.0);

        Assert.NotNull(new EvpDetectionOptions(ThresholdDb: double.NaN).Validate());
    }

    [Fact]
    public void An_infinite_value_is_refused()
    {
        Assert.NotNull(new EvpDetectionOptions(ThresholdDb: double.PositiveInfinity).Validate());
        Assert.NotNull(new EvpDetectionOptions(MaxEventSeconds: double.NegativeInfinity).Validate());
    }

    /// <summary>The first thing wrong is the thing reported, so a person fixes one dial at a time.</summary>
    [Fact]
    public void The_message_names_the_dial_that_is_wrong()
    {
        var problem = new EvpDetectionOptions(ThresholdDb: 99, MergeGapSeconds: 99).Validate();

        Assert.Contains("Threshold", problem);
        Assert.DoesNotContain("Merge gap", problem);
    }
}
