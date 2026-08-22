using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Services.Billing;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A tier edit's changes classify per field, in the direction the resolver actually applies them.
/// </summary>
/// <remarks>
/// The classification decides <b>delivery</b>, not just wording: an improvement is announced
/// immediately and a reduction waits for the pre-renewal notice. Misclassifying one direction
/// either buries bad news in a cheerful message or sits on good news for two weeks — and if the
/// analyzer and <see cref="EffectiveTermsResolver"/> ever disagree, a group is told about a change
/// that is not applied to them, or has one applied unannounced.
/// </remarks>
public sealed class TierChangeAnalyzerTests
{
    private static Dictionary<SubscriptionLimit, int?> Limits(params (SubscriptionLimit, int?)[] caps) =>
        caps.ToDictionary(c => c.Item1, c => c.Item2);

    private static Dictionary<BillingInterval, decimal> Prices(params (BillingInterval, decimal)[] prices) =>
        prices.ToDictionary(p => p.Item1, p => p.Item2);

    private static TierChange Single(IReadOnlyList<TierChange> changes) => Assert.Single(changes);

    // ── limits, in the four directions ────────────────────────────────────────

    [Fact]
    public void A_raised_cap_is_an_improvement_and_a_lowered_one_is_a_reduction()
    {
        var up = Single(TierChangeAnalyzer.Analyze(
            Limits((SubscriptionLimit.EquipmentItems, 25)),
            Limits((SubscriptionLimit.EquipmentItems, 50)),
            Prices(), Prices()));
        Assert.True(up.IsImprovement);
        Assert.Contains("increased", up.Sentence);

        var down = Single(TierChangeAnalyzer.Analyze(
            Limits((SubscriptionLimit.EquipmentItems, 50)),
            Limits((SubscriptionLimit.EquipmentItems, 25)),
            Prices(), Prices()));
        Assert.False(down.IsImprovement);
    }

    [Fact]
    public void A_removed_cap_is_an_improvement_and_a_new_cap_is_a_reduction()
    {
        var removed = Single(TierChangeAnalyzer.Analyze(
            Limits((SubscriptionLimit.OpenCases, 5)), Limits(),
            Prices(), Prices()));
        Assert.True(removed.IsImprovement);
        Assert.Contains("removed", removed.Sentence);

        var added = Single(TierChangeAnalyzer.Analyze(
            Limits(), Limits((SubscriptionLimit.OpenCases, 5)),
            Prices(), Prices()));
        Assert.False(added.IsImprovement);
    }

    /// <summary>Null is unlimited: moving to it improves, moving off it reduces.</summary>
    [Fact]
    public void Unlimited_counts_as_the_top_of_the_scale()
    {
        var toUnlimited = Single(TierChangeAnalyzer.Analyze(
            Limits((SubscriptionLimit.StorageMegabytes, 500)),
            Limits((SubscriptionLimit.StorageMegabytes, null)),
            Prices(), Prices()));
        Assert.True(toUnlimited.IsImprovement);

        var fromUnlimited = Single(TierChangeAnalyzer.Analyze(
            Limits((SubscriptionLimit.StorageMegabytes, null)),
            Limits((SubscriptionLimit.StorageMegabytes, 500)),
            Prices(), Prices()));
        Assert.False(fromUnlimited.IsImprovement);
    }

    /// <summary>Zero is feature-off, so 0 → n is turning a feature ON.</summary>
    [Fact]
    public void Turning_a_feature_on_is_an_improvement_and_off_is_a_reduction()
    {
        var on = Single(TierChangeAnalyzer.Analyze(
            Limits((SubscriptionLimit.PublishedPages, 0)),
            Limits((SubscriptionLimit.PublishedPages, 3)),
            Prices(), Prices()));
        Assert.True(on.IsImprovement);

        var off = Single(TierChangeAnalyzer.Analyze(
            Limits((SubscriptionLimit.PublishedPages, 3)),
            Limits((SubscriptionLimit.PublishedPages, 0)),
            Prices(), Prices()));
        Assert.False(off.IsImprovement);
        Assert.Contains("not included", off.Sentence);
    }

    // ── prices ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_price_cut_is_an_improvement_and_a_rise_is_a_reduction()
    {
        var cut = Single(TierChangeAnalyzer.Analyze(
            Limits(), Limits(),
            Prices((BillingInterval.Monthly, 15m)), Prices((BillingInterval.Monthly, 12m))));
        Assert.True(cut.IsImprovement);

        var rise = Single(TierChangeAnalyzer.Analyze(
            Limits(), Limits(),
            Prices((BillingInterval.Monthly, 15m)), Prices((BillingInterval.Monthly, 18m))));
        Assert.False(rise.IsImprovement);
        Assert.Contains("$15.00", rise.Sentence);
        Assert.Contains("$18.00", rise.Sentence);
    }

    [Fact]
    public void A_new_cadence_is_an_improvement_and_a_withdrawn_one_is_a_reduction()
    {
        var added = Single(TierChangeAnalyzer.Analyze(
            Limits(), Limits(),
            Prices((BillingInterval.Monthly, 15m)),
            Prices((BillingInterval.Monthly, 15m), (BillingInterval.Yearly, 150m))));
        Assert.True(added.IsImprovement);
        Assert.Contains("yearly", added.Sentence);

        var withdrawn = Single(TierChangeAnalyzer.Analyze(
            Limits(), Limits(),
            Prices((BillingInterval.Monthly, 15m), (BillingInterval.Yearly, 150m)),
            Prices((BillingInterval.Monthly, 15m))));
        Assert.False(withdrawn.IsImprovement);
    }

    // ── the shape of the whole answer ─────────────────────────────────────────

    /// <summary>
    /// One edit can improve one thing and reduce another; each travels under its own flag.
    /// </summary>
    /// <remarks>
    /// This is why the classification is per field and not per save: wholesale, the good news
    /// buries the bad or the bad sits on the good — both wrong, in different directions.
    /// </remarks>
    [Fact]
    public void A_mixed_edit_classifies_each_change_separately()
    {
        var changes = TierChangeAnalyzer.Analyze(
            Limits((SubscriptionLimit.EquipmentItems, 25), (SubscriptionLimit.StorageMegabytes, 2048)),
            Limits((SubscriptionLimit.EquipmentItems, 50), (SubscriptionLimit.StorageMegabytes, 1024)),
            Prices(), Prices());

        Assert.Equal(2, changes.Count);
        Assert.True(changes.Single(c => c.Sentence.Contains("equipment")).IsImprovement);
        Assert.False(changes.Single(c => c.Sentence.Contains("storage")).IsImprovement);
    }

    [Fact]
    public void An_edit_that_changes_no_terms_reports_nothing()
    {
        Assert.Empty(TierChangeAnalyzer.Analyze(
            Limits((SubscriptionLimit.OpenCases, 5)),
            Limits((SubscriptionLimit.OpenCases, 5)),
            Prices((BillingInterval.Monthly, 15m)),
            Prices((BillingInterval.Monthly, 15m))));
    }

    /// <summary>Storage sentences speak GB when they can — 2048 MB is a number nobody budgets in.</summary>
    [Fact]
    public void Storage_amounts_read_in_gigabytes_when_whole()
    {
        var change = Single(TierChangeAnalyzer.Analyze(
            Limits((SubscriptionLimit.StorageMegabytes, 2048)),
            Limits((SubscriptionLimit.StorageMegabytes, 5120)),
            Prices(), Prices()));

        Assert.Contains("2 GB", change.Sentence);
        Assert.Contains("5 GB", change.Sentence);
    }

    /// <summary>
    /// The analyzer's direction agrees with the resolver's — the pairing that keeps message and
    /// experience in step.
    /// </summary>
    /// <remarks>
    /// For every shape of cap movement, "the analyzer calls it an improvement" must equal "the
    /// resolver lets it through immediately". Tested against the shared primitive both sides use.
    /// </remarks>
    [Theory]
    [InlineData(25, 50)]
    [InlineData(50, 25)]
    [InlineData(500, null)]
    [InlineData(null, 500)]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    public void The_analyzer_and_the_resolver_agree_on_direction(int? oldMax, int? newMax)
    {
        var change = Single(TierChangeAnalyzer.Analyze(
            Limits((SubscriptionLimit.OpenCases, oldMax)),
            Limits((SubscriptionLimit.OpenCases, newMax)),
            Prices(), Prices()));

        Assert.Equal(EffectiveTermsResolver.IsAtLeastAsGood(newMax, oldMax), change.IsImprovement);
    }
}
