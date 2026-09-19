using Ben.Data.Common;
using Ben.Data.WebApi.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The branded shell every outgoing email is wrapped in.
/// </summary>
public class BenEmailLayoutTests
{
    private static readonly SiteIdentity Site = new()
    {
        Name = "IsHaunted.com",
        BaseUrl = "https://ishaunted.com",
        Tagline = "Find paranormal investigators near you.",
    };

    [Fact]
    public void The_logo_is_an_absolute_url_to_a_png()
    {
        var html = BenEmailLayout.Wrap(Site, "Confirm your email", "<p>Body.</p>");

        // Absolute, because the reader's mail client resolves nothing relative; PNG, because
        // most clients strip SVG entirely and the logo would simply vanish.
        Assert.Contains("https://ishaunted.com/icon-192.png", html);
        Assert.DoesNotContain(".svg", html);
    }

    [Fact]
    public void The_button_link_is_repeated_as_visible_text()
    {
        var html = BenEmailLayout.Wrap(Site, "T", "<p>B.</p>",
            buttonText: "Confirm my email", buttonUrl: "https://ishaunted.com/confirm-email?x=1");

        // Twice: once as the button's href, once as text a reader can inspect before clicking —
        // and can still use when images and styling are stripped.
        var occurrences = html.Split("https://ishaunted.com/confirm-email?x=1").Length - 1;
        Assert.True(occurrences >= 3, $"The link appears {occurrences} time(s); the button, its "
            + "fallback href and its visible text should each carry it.");
    }

    [Fact]
    public void A_hostile_title_or_button_text_is_escaped()
    {
        var html = BenEmailLayout.Wrap(Site, "<script>alert(1)</script>", "<p>B.</p>",
            buttonText: "<img onerror=x>", buttonUrl: "https://ishaunted.com/a");

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img onerror", html);
    }

    [Fact]
    public void No_style_block_and_no_external_stylesheet()
    {
        // Gmail strips <style> in enough contexts that anything depending on one falls apart;
        // everything must be inline.
        var html = BenEmailLayout.Wrap(Site, "T", "<p>B.</p>");
        Assert.DoesNotContain("<style", html);
        Assert.DoesNotContain("<link", html);
    }
}
