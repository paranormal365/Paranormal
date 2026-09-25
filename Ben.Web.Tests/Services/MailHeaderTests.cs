using Ben.Data.Common.Mail;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The one email header, and the rule for whether a body already has one.
/// </summary>
/// <remarks>
/// Ben wrote sixteen templates by hand, all opening with the same block, and asked for it to become
/// the standard for the letters he had not written a template for (2026-09-20). Roughly forty of
/// those were bare <c>&lt;p&gt;</c> fragments with no header at all, and nine carried a different
/// one, so the risk this class carries is giving somebody TWO headers stacked up.
/// </remarks>
public sealed class MailHeaderTests
{
    private const string Icon = "https://ishaunted.com/apple-touch-icon.png";

    [Fact]
    public void The_header_is_the_one_Ben_wrote()
    {
        var html = MailHeader.Html(Icon, "IsHaunted.com");

        // The wordmark, in its three weights - this is the design, and it is literal markup
        // rather than a token on purpose.
        Assert.Contains("""<em style="opacity:70%;">Is</em><strong>Haunted</strong><span style="opacity:40%;">.com</span>""", html);

        // The dark icon, at the size and with the corners he chose.
        Assert.Contains("apple-touch-icon.png", html);
        Assert.Contains("""width="64" height="64" """.TrimEnd(), html);
        Assert.Contains("border-radius:10px", html);
    }

    /// <summary>
    /// Many clients block remote images until the reader allows them, and a header that is nothing
    /// but a blocked image leaves the letter opening with a blank rectangle.
    /// </summary>
    [Fact]
    public void The_icon_says_what_it_is_when_a_client_will_not_load_it()
    {
        Assert.Contains("""alt="IsHaunted.com" """.TrimEnd(), MailHeader.Html(Icon, "IsHaunted.com"));
    }

    [Fact]
    public void A_site_name_with_markup_in_it_cannot_break_out_of_the_alt_attribute()
    {
        var html = MailHeader.Html(Icon, """Evil" onerror="alert(1)""");

        Assert.DoesNotContain("onerror=\"alert(1)\"", html);
        Assert.Contains("&quot;", html);
    }

    // ── has this body got one already? ───────────────────────────────────────

    [Fact]
    public void A_template_Ben_wrote_is_recognised_as_already_headed()
    {
        // What a body from the EmailTemplates table looks like: the same block, with the token
        // still spelled as a token because the renderer substitutes it later.
        const string fromTheEditor =
            """<img src="{SiteUrl}apple-touch-icon.png" width="64" height="64" alt="{SiteName}" />""";

        Assert.True(MailHeader.IsAlreadyHeaded(fromTheEditor));
    }

    [Fact]
    public void A_letter_already_wrapped_in_the_shell_is_recognised_too()
    {
        Assert.True(MailHeader.IsAlreadyHeaded(MailHeader.Html(Icon, "IsHaunted.com")));
    }

    [Fact]
    public void A_bare_fragment_is_not()
    {
        Assert.False(MailHeader.IsAlreadyHeaded("<p>Your event is tomorrow.</p>"));
        Assert.False(MailHeader.IsAlreadyHeaded(""));
        Assert.False(MailHeader.IsAlreadyHeaded(null));
    }

    /// <summary>
    /// A letter carrying its own <c>&lt;head&gt;</c> cannot be dropped inside another document.
    /// </summary>
    [Fact]
    public void A_whole_document_is_told_apart_from_a_fragment()
    {
        Assert.True(MailHeader.IsWholeDocument("<!DOCTYPE html><html><body>hi</body></html>"));
        Assert.True(MailHeader.IsWholeDocument("\n  <!doctype html>\n<html>"));
        Assert.False(MailHeader.IsWholeDocument("<p>hi</p>"));
        Assert.False(MailHeader.IsWholeDocument(null));
    }

    // ── the timezone footnote ────────────────────────────────────────────────

    /// <summary>
    /// Ben, 2026-09-20: say so when the letter only reports UTC. A time with no zone beside it is
    /// read as local by everybody, which is how somebody arrives hours late.
    /// </summary>
    [Fact]
    public void A_letter_written_in_UTC_says_so()
    {
        var note = MailHeader.TimeZoneNote("UTC");

        Assert.NotNull(note);
        Assert.Contains("UTC", note);
        Assert.Contains("not your local time", note);
    }

    [Fact]
    public void A_real_zone_is_named_rather_than_warned_about()
    {
        var note = MailHeader.TimeZoneNote("CDT");

        Assert.NotNull(note);
        Assert.Contains("CDT", note);
        // Quieter wording: it does say which clock it means, unlike a bare UTC time.
        Assert.Contains("may not be your local time", note);
    }

    [Fact]
    public void A_letter_with_no_times_in_it_gets_no_note()
    {
        Assert.Null(MailHeader.TimeZoneNote(null));
        Assert.Null(MailHeader.TimeZoneNote(""));
        Assert.Null(MailHeader.TimeZoneNote("   "));
    }

    [Fact]
    public void The_zone_label_cannot_smuggle_markup_into_the_footer()
    {
        var note = MailHeader.TimeZoneNote("<script>alert(1)</script>");

        Assert.DoesNotContain("<script>", note);
    }
}
