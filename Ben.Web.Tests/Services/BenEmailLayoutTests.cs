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

    /// <summary>
    /// The shell now opens with the header Ben wrote for his templates, so a letter built in code
    /// and one written in the template editor look the same (2026-09-20). It used to be a dark band
    /// with a square <c>icon-192.png</c> — a second design that only the handful of wrapped letters
    /// ever showed.
    /// </summary>
    [Fact]
    public void The_logo_is_an_absolute_url_to_a_png()
    {
        var html = BenEmailLayout.Wrap(Site, "Confirm your email", "<p>Body.</p>");

        // Absolute, because the reader's mail client resolves nothing relative; PNG, because
        // most clients strip SVG entirely and the logo would simply vanish.
        Assert.Contains("https://ishaunted.com/apple-touch-icon.png", html);
        Assert.DoesNotContain(".svg", html);
    }

    [Fact]
    public void The_shell_opens_with_the_header_from_the_templates()
    {
        var html = BenEmailLayout.Wrap(Site, "Confirm your email", "<p>Body.</p>");

        Assert.Contains("""<em style="opacity:70%;">Is</em><strong>Haunted</strong>""", html);
        Assert.Contains("border-radius:10px", html);
    }

    /// <summary>
    /// A letter wrapped at the outbox has no separate title — its body was written as a whole
    /// letter and opens with its own first line, so a heading taken from the subject would say the
    /// same thing twice.
    /// </summary>
    [Fact]
    public void An_empty_title_leaves_no_empty_heading_behind()
    {
        var html = BenEmailLayout.Wrap(Site, title: "", bodyHtml: "<p>Your event is tomorrow.</p>");

        Assert.Contains("<p>Your event is tomorrow.</p>", html);
        Assert.Contains("apple-touch-icon.png", html);
        Assert.DoesNotContain("font-size:20px;font-weight:bold;color:#111827;", html);
    }

    /// <summary>
    /// Ben, 2026-09-20: say so when the letter only reports UTC and not the reader's local time.
    /// </summary>
    [Fact]
    public void A_letter_written_in_UTC_says_so_in_the_footer()
    {
        var html = BenEmailLayout.Wrap(Site, "T", "<p>7pm.</p>", timesShownInZone: "UTC");

        Assert.Contains("Times above are UTC, not your local time.", html);
    }

    [Fact]
    public void A_letter_with_no_times_in_it_says_nothing_about_zones()
    {
        Assert.DoesNotContain("local time", BenEmailLayout.Wrap(Site, "T", "<p>B.</p>"));
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
