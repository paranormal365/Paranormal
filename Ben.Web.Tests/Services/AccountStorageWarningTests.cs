using Ben.Data.WebApi.Services.Scheduling;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Which warning a level of usage earns (Ben, 2026-09-22).
/// </summary>
/// <remarks>
/// <para>The banding is the whole decision, so it is tested on its own rather than through the
/// job: the job's other half is "send it once and re-arm", which is a different question and is
/// about the stored band, not about the arithmetic.</para>
///
/// <para>The boundaries are the point. Exactly 10% left must warn — a cap you are allowed to sit
/// precisely on without hearing anything is a cap that surprises you one byte later.</para>
/// </remarks>
public sealed class AccountStorageWarningTests
{
    private const long Cap = 1000;

    [Theory]
    // Plenty of room: nothing to say.
    [InlineData(0,    null)]
    [InlineData(500,  null)]
    [InlineData(889,  null)]   // 11.1% left
    // The gentle band, from its exact boundary.
    [InlineData(900,  10)]     // exactly 10% left
    [InlineData(930,  10)]
    [InlineData(949,  10)]     // 5.1% left — still the gentle one
    // The band that matters, from its exact boundary.
    [InlineData(950,  5)]      // exactly 5% left
    [InlineData(999,  5)]
    [InlineData(1000, 5)]      // full
    [InlineData(1200, 5)]      // over, which is the most urgent state there is
    public void Usage_earns_the_right_warning(long used, int? expected)
        => Assert.Equal(expected, AccountStorageWarningJob.BandFor(used, Cap));

    /// <summary>
    /// A cap of zero produces no warning rather than a division by zero.
    /// </summary>
    /// <remarks>
    /// The setting degrades to a default rather than to zero, so this should be unreachable — but
    /// "unreachable" is what every crash was before it happened, and the alternative here is an
    /// exception inside a background job, which is the kind that gets noticed a week later.
    /// </remarks>
    [Fact]
    public void A_cap_of_nothing_warns_about_nothing()
    {
        Assert.Null(AccountStorageWarningJob.BandFor(0, 0));
        Assert.Null(AccountStorageWarningJob.BandFor(500, 0));
    }

    /// <summary>
    /// The worse band wins when both are true.
    /// </summary>
    /// <remarks>
    /// At 2% left an account is under ten percent AND under five. Reading the bands in the wrong
    /// order would tell somebody about to be refused that they have "about 90% used", which is
    /// true and useless.
    /// </remarks>
    [Fact]
    public void The_worse_band_wins()
        => Assert.Equal(5, AccountStorageWarningJob.BandFor(980, Cap));
}
