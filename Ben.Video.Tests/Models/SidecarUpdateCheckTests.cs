using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

/// <summary>
/// The update notice appears only when it is certainly true.
/// </summary>
public sealed class SidecarUpdateCheckTests
{
    [Fact]
    public void An_older_install_is_out_of_date()
    {
        Assert.True(SidecarUpdateCheck.IsOutOfDate("1.0.0.0", "1.1.0.0"));
        Assert.True(SidecarUpdateCheck.IsOutOfDate("1.0.0.0", "1.0.0.1"));
        Assert.True(SidecarUpdateCheck.IsOutOfDate("0.9", "1.0"));
    }

    [Fact]
    public void The_same_version_is_not()
    {
        Assert.False(SidecarUpdateCheck.IsOutOfDate("1.2.3.4", "1.2.3.4"));
        Assert.False(SidecarUpdateCheck.IsOutOfDate(" 1.2.3.4 ", "1.2.3.4"));
    }

    [Fact]
    public void A_newer_install_is_never_nagged()
    {
        // Every developer running the sidecar from source is here, and so is anybody who installed
        // a build before the site caught up. Telling them to downgrade would be absurd.
        Assert.False(SidecarUpdateCheck.IsOutOfDate("2.0.0.0", "1.0.0.0"));
    }

    [Theory]
    [InlineData(null, "1.0.0.0")]
    [InlineData("1.0.0.0", null)]
    [InlineData("", "1.0.0.0")]
    [InlineData("1.0.0.0", "")]
    [InlineData("not-a-version", "1.0.0.0")]
    [InlineData("1.0.0.0", "latest")]
    [InlineData(null, null)]
    public void Anything_it_cannot_be_sure_about_says_nothing(string? installed, string? published)
    {
        Assert.False(SidecarUpdateCheck.IsOutOfDate(installed, published));
    }

    [Fact]
    public void A_host_that_publishes_no_version_never_nags()
    {
        // The standalone playground and any host that ships no sidecar of its own.
        Assert.False(SidecarUpdateCheck.IsOutOfDate("1.0.0.0", publishedVersion: null));
    }
}
