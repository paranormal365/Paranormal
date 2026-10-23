using Ben.Web.Website.Library.Shared;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The home page's "Public Investigations" count says public cases only (10/23/2026: signed in, it read
/// "2 cases worldwide" over two cases marked "Not public", and 0 signed out).
/// </summary>
public sealed class PublicCaseCountTests
{
    [Theory]
    [InlineData(0, 0, "0 public cases worldwide")]
    [InlineData(1, 0, "1 public case worldwide")]
    [InlineData(2, 2, "0 public cases worldwide · 2 of yours, not public")]
    [InlineData(5, 1, "4 public cases worldwide · 1 of yours, not public")]
    public void Counts_only_the_public_cases_and_says_the_members_own_apart(int total, int own, string expected)
        => Assert.Equal(expected, PublicCaseDiscovery.CountLine(total, own));
}
