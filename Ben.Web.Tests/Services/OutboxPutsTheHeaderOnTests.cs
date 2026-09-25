using Ben.Data.Common;
using Ben.Data.Common.Interfaces;
using Ben.Data.Common.Mail;
using Ben.Data.WebApi.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every letter gets the site's header on its way to the queue — and only one.
/// </summary>
/// <remarks>
/// <para>Ben asked for the header from his templates to be the standard for the letters he had not
/// written a template for (2026-09-20). About forty of those are bare <c>&lt;p&gt;</c> fragments,
/// each built in its own mailer, and they all pass through <c>OutboxEmailService.Row</c> on the way
/// to the outbox — so it goes on there rather than in forty places, one of which would be missed.</para>
///
/// <para>The risk this guards is the opposite one: a letter that already has a header getting a
/// second stacked on top of it. That is what most of these tests are about.</para>
/// </remarks>
public sealed class OutboxPutsTheHeaderOnTests
{
    private static readonly SiteIdentity Site = new()
    {
        Name = "IsHaunted.com",
        BaseUrl = "https://ishaunted.com",
        Tagline = "Find paranormal investigators near you.",
    };

    private static string BodyOf(string html) =>
        OutboxEmailService.Row(new EmailMessage("a@b.c", "Subject", html), DateTime.UtcNow, Site).HtmlBody ?? "";

    private static int HeaderCount(string html) =>
        html.Split(MailHeader.IconFileName).Length - 1;

    [Fact]
    public void A_bare_fragment_from_a_mailer_comes_out_with_the_header_on()
    {
        // What EventGuestMailer, the reminder job and about forty others actually produce.
        var body = BodyOf("<p>Your event is tomorrow at 7pm.</p>");

        Assert.Contains("""<em style="opacity:70%;">Is</em><strong>Haunted</strong>""", body);
        Assert.Contains("https://ishaunted.com/apple-touch-icon.png", body);
        Assert.Contains("<p>Your event is tomorrow at 7pm.</p>", body);
    }

    [Fact]
    public void A_letter_that_already_has_the_header_does_not_get_a_second()
    {
        // A template from the editor: the header is in the markup the author wrote.
        var fromTemplate = MailHeader.Html("https://ishaunted.com/apple-touch-icon.png", "IsHaunted.com")
                         + "<p>Here is your receipt.</p>";

        Assert.Equal(1, HeaderCount(BodyOf(fromTemplate)));
    }

    [Fact]
    public void A_letter_already_wrapped_by_its_mailer_does_not_get_a_second()
    {
        var wrapped = BenEmailLayout.Wrap(Site, "Confirm your email", "<p>Please confirm.</p>");

        var body = BodyOf(wrapped);

        Assert.Equal(1, HeaderCount(body));
        // And it is still the document its mailer built, not one nested inside another.
        Assert.Equal(1, body.Split("<!DOCTYPE").Length - 1);
    }

    /// <summary>
    /// A body with its own <c>&lt;head&gt;</c> cannot go inside another document, whoever built it.
    /// </summary>
    [Fact]
    public void A_whole_document_from_anywhere_is_left_alone()
    {
        const string document = "<!DOCTYPE html><html><head></head><body><p>Hand rolled.</p></body></html>";

        Assert.Equal(document, BodyOf(document));
    }

    [Fact]
    public void An_empty_body_is_not_given_a_header_to_wrap_nothing_in()
    {
        Assert.Equal("", BodyOf(""));
    }

    /// <summary>
    /// The row is also built in tests and tooling that have no site identity to build a header
    /// from; those must keep working and get the body unchanged.
    /// </summary>
    [Fact]
    public void Without_a_site_identity_the_body_is_untouched()
    {
        var row = OutboxEmailService.Row(
            new EmailMessage("a@b.c", "Subject", "<p>Body.</p>"), DateTime.UtcNow);

        Assert.Equal("<p>Body.</p>", row.HtmlBody);
    }

    /// <summary>
    /// The header costs about a kilobyte, and the outbox truncates a long body. Wrapping happens
    /// first, so the measurement is of what would actually be sent.
    /// </summary>
    [Fact]
    public void The_size_rule_measures_the_letter_that_would_go_out()
    {
        var big = "<p>" + new string('x', OutboxEmailService.MaximumBodyBytes) + "</p>";

        var row = OutboxEmailService.Row(
            new EmailMessage("a@b.c", "Subject", big), DateTime.UtcNow, Site);

        Assert.True((row.HtmlBody?.Length ?? 0) <= OutboxEmailService.MaximumBodyBytes + 100);
        Assert.NotNull(row.LastError);
    }
}
