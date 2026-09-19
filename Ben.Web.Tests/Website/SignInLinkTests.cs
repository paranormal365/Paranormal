using System.Text.RegularExpressions;
using Ben.Web.Tests.Support;
using Ben.Web.Website.Library.Kit;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The sign-in link brings a person back to the page they were on, in the only form the sign-in page's open-redirect guard
/// accepts (UI test pass 6.1 and 6.8, 2026-09-14).
/// </summary>
public class SignInLinkTests
{
    [Theory]
    [InlineData("organizations/afc81ae7/calendar", "/login?returnUrl=%2Forganizations%2Fafc81ae7%2Fcalendar")]
    [InlineData("organizations/x?tab=calendar", "/login?returnUrl=%2Forganizations%2Fx%3Ftab%3Dcalendar")]
    [InlineData("/tours/night-walk", "/login?returnUrl=%2Ftours%2Fnight-walk")]
    public void Carries_the_page_as_a_path_starting_with_one_slash(string here, string expected)
    {
        var link = SignInLink.For(here);
        Assert.Equal(expected, link);

        // What Login.razor accepts: starts with '/', not '//'.
        var returnUrl = Uri.UnescapeDataString(link["/login?returnUrl=".Length..]);
        Assert.True(returnUrl.StartsWith('/') && !returnUrl.StartsWith("//"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("login")]
    [InlineData("login?returnUrl=%2F")]
    [InlineData("logout")]
    [InlineData("signup")]
    [InlineData("forgot-password")]
    [InlineData("reset-password?token=abc")]
    public void From_the_home_page_or_a_sign_in_page_it_is_plain_sign_in(string here)
        => Assert.Equal("/login", SignInLink.For(here));

    [Fact]
    public void No_sign_in_link_is_built_from_the_full_address()
    {
        // NavigationManager.Uri is absolute ("http://…"); the guard refuses it and the person is signed in to the home page.
        var offenders = RepoFiles.Paths("*.razor")
            .Where(p => Regex.IsMatch(File.ReadAllText(p), @"login\?returnUrl=\{Uri\.EscapeDataString\((NavManager|Nav|Navigation)\.Uri\)"))
            .ToList();
        Assert.Empty(offenders);
    }
}
