using Ben.Data.WebApi.Services.Tours;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The mail a tour guest is sent, from the business's own wording (item 233).
/// </summary>
/// <remarks>
/// Two things carry real weight here. The <b>time</b>, because a guest reads one sentence and
/// decides when to leave the house — and the reminder job used to print raw UTC with a "UTC"
/// suffix. And the <b>encoding</b>, because these values come from guests and businesses, and a
/// mail body is markup.
/// </remarks>
public sealed class TourMailRendererTests
{
    private static TourMailRenderer.TourMailFacts Facts(
        string? guestName = "Ada",
        IReadOnlyList<string>? guideNames = null,
        IReadOnlyList<(string, string)>? guidePhotos = null,
        string? description = null,
        string zone = "America/Chicago") =>
        new(
            TourName: "Printers Alley Ghost Walk",
            TourDescriptionHtml: description,
            MeetingPoint: "1 Printers Alley, Nashville, TN, 37201",
            MeetingPointMapUrl: "https://maps.apple.com/?ll=36.16,-86.77",
            DurationMinutes: 90,
            // 7pm Central on a Saturday in September — daylight saving is in force.
            StartUtc: new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc),
            EndUtc: new DateTime(2026, 9, 13, 1, 30, 0, DateTimeKind.Utc),
            TimeZoneId: zone,
            Capacity: 20,
            SpacesLeft: 6,
            DateTitle: "Saturday walk",
            DateUrl: "https://ishaunted.com/o/paw/events/2026-09-12-saturday-walk",
            GuideNames: guideNames ?? ["Gale"],
            GuidePhotos: guidePhotos ?? [],
            GuestName: guestName,
            BusinessName: "Printers Alley Walks",
            BusinessUrl: "https://ishaunted.com/o/paw",
            ContactLine: "Cash on the night, or call 555-0100.",
            SiteName: "IsHaunted.com");

    /// <summary>
    /// What a template produced, without the meeting point the renderer appends.
    /// </summary>
    /// <remarks>
    /// Every one-placeholder body here gains a "Where you meet" line, because Ben's rule is that
    /// the start address is always in the mail. That rule has its own tests below; these are
    /// about the placeholder, so they read the part the template produced.
    /// </remarks>
    private static string Body(string? template, TourMailRenderer.TourMailFacts facts)
    {
        var body = TourMailRenderer.Render(null, template, facts).HtmlBody;
        // Matched at the END and on the whole line, so a template that mentions the meeting point
        // itself keeps what it wrote.
        var appended = $"<p><strong>Where you meet:</strong> "
                     + System.Net.WebUtility.HtmlEncode(facts.MeetingPoint) + "</p>";
        return (body.EndsWith(appended, StringComparison.Ordinal)
                    ? body[..^appended.Length]
                    : body).Trim();
    }

    // ── the time, in the tour's own zone ─────────────────────────────────────

    [Fact]
    public void The_start_is_written_in_the_tours_zone_with_the_zone_named()
    {
        // Midnight UTC is seven the previous evening in Nashville, in September.
        Assert.Equal("Saturday, 09/12/2026 at 7:00 PM CDT", Body("{{date.start}}", Facts()));
    }

    [Fact]
    public void A_different_zone_gives_a_different_sentence_for_the_same_instant()
    {
        var central = Body("{{date.start}}", Facts());
        var eastern = Body("{{date.start}}", Facts(zone: "America/New_York"));

        Assert.Equal("Saturday, 09/12/2026 at 7:00 PM CDT", central);
        Assert.Equal("Saturday, 09/12/2026 at 8:00 PM EDT", eastern);
    }

    [Fact]
    public void A_zone_this_server_has_never_heard_of_still_sends_the_mail()
    {
        // Wrong-by-an-hour beats not-sent. Nobody's walk is saved by an exception.
        Assert.Contains("09/13/2026", Body("{{date.start}}", Facts(zone: "Mars/Olympus")));
    }

    // ── the facts a guest needs ──────────────────────────────────────────────

    [Fact]
    public void Every_placeholder_the_editor_offers_actually_resolves()
    {
        // The list somebody reads and the list that works are one list, or a business writes a
        // mail full of blanks.
        foreach (var (token, _) in TourMailRenderer.Tokens)
        {
            var rendered = TourMailRenderer.Render(null, token,
                Facts(guidePhotos: [("Gale", "https://ishaunted.com/media/guide-photo/x")],
                      description: "<p>An evening walk.</p>"));

            Assert.False(string.IsNullOrWhiteSpace(rendered.HtmlBody),
                $"{token} rendered nothing");
            Assert.DoesNotContain("{{", rendered.HtmlBody);
        }
    }

    [Fact]
    public void An_unknown_placeholder_renders_as_nothing_rather_than_as_machinery()
    {
        Assert.Equal("Hello .", Body("Hello {{tour.nmae}}.", Facts()));
    }

    [Fact]
    public void Guides_are_named_the_way_a_person_would_say_it()
    {
        string Names(params string[] names) => Body("{{guide.names}}", Facts(guideNames: names));

        Assert.Equal("Gale", Names("Gale"));
        Assert.Equal("Gale and Marcus", Names("Gale", "Marcus"));
        Assert.Equal("Gale, Marcus and Ada", Names("Gale", "Marcus", "Ada"));
        Assert.Equal("your guide", Body("{{guide.names}}", Facts(guideNames: [])));
    }

    [Fact]
    public void A_guide_with_no_published_photograph_leaves_no_gap()
    {
        // The photograph was always optional — Ben said so when he asked for it.
        Assert.Equal(string.Empty, Body("{{guide.photos}}", Facts(guidePhotos: [])));
    }

    [Fact]
    public void A_guides_photograph_carries_their_name_for_a_client_that_blocks_images()
    {
        var body = Body("{{guide.photos}}",
            Facts(guidePhotos: [("Gale", "https://ishaunted.com/media/guide-photo/x")]));

        Assert.Contains("alt=\"Gale\"", body);
        Assert.Contains("https://ishaunted.com/media/guide-photo/x", body);
    }

    [Fact]
    public void A_guest_with_no_name_is_greeted_rather_than_left_blank()
        => Assert.Equal("Hello there,", Body("Hello {{guest.name}},", Facts(guestName: null)));

    // ── encoding ─────────────────────────────────────────────────────────────

    [Fact]
    public void A_hostile_guest_name_is_a_name_not_an_instruction()
    {
        var body = Body("Hello {{guest.name}}.", Facts(guestName: "<script>alert(1)</script>"));

        Assert.DoesNotContain("<script>", body);
        Assert.Contains("&lt;script&gt;", body);
    }

    [Fact]
    public void The_description_the_business_wrote_keeps_its_formatting()
    {
        // It came through the markup sanitizer when it was saved; encoding it twice would show a
        // guest their own paragraph tags.
        Assert.Equal("<p>An evening walk.</p>",
            Body("{{tour.description}}", Facts(description: "<p>An evening walk.</p>")));
    }

    // ── Ben's rule about the meeting point ───────────────────────────────────

    [Fact]
    public void A_template_that_leaves_out_the_meeting_point_still_carries_it()
    {
        // Ben, 2026-09-10: "The address of the tour start would be in the e-mail."
        var rendered = TourMailRenderer.Render(null, "<p>See you Saturday!</p>", Facts());

        Assert.Contains("1 Printers Alley", rendered.HtmlBody);
        Assert.Contains("Where you meet", rendered.HtmlBody);
    }

    [Fact]
    public void A_template_that_already_names_it_is_left_as_written()
    {
        var rendered = TourMailRenderer.Render(null, "<p>Meet at {{tour.meetingPoint}}.</p>", Facts());

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(
            rendered.HtmlBody, "1 Printers Alley"));
    }

    // ── the built-in wording, and the reminder ───────────────────────────────

    // ── blocks vanish; values do not ─────────────────────────────────────────

    [Fact]
    public void The_guide_block_disappears_when_nobody_is_on_the_date_yet()
    {
        // Seen in a real mail: "Your guide: your guide" — a label with its own fallback inside it.
        // A block is a whole paragraph and can be absent; a value has to say something because it
        // is sitting inside somebody else's sentence.
        Assert.Equal(string.Empty, Body("{{guide.block}}", Facts(guideNames: [])));
        Assert.Equal("your guide", Body("{{guide.names}}", Facts(guideNames: [])));
    }

    [Fact]
    public void The_guide_block_names_them_and_pluralises_the_label()
    {
        Assert.Contains("<strong>Your guide:</strong> Gale",
            Body("{{guide.block}}", Facts(guideNames: ["Gale"])));
        Assert.Contains("<strong>Your guides:</strong> Gale and Marcus",
            Body("{{guide.block}}", Facts(guideNames: ["Gale", "Marcus"])));
    }

    [Fact]
    public void The_contact_block_disappears_when_the_business_wrote_no_contact_line()
    {
        // The other empty paragraph the first real mail carried: <p></p>.
        var facts = Facts() with { ContactLine = null };
        Assert.Equal(string.Empty, Body("{{business.contactBlock}}", facts));
        Assert.Contains("Cash on the night", Body("{{business.contactBlock}}", Facts()));
    }

    [Fact]
    public void The_built_in_mail_has_no_empty_paragraphs_in_it()
    {
        var bare = TourMailRenderer.Render(null, null,
            Facts(guideNames: [], guidePhotos: []) with { ContactLine = null });

        Assert.DoesNotContain("<p></p>", bare.HtmlBody);
        Assert.DoesNotContain("your guide", bare.HtmlBody);
        // And it still says the three things a guest cannot do without.
        Assert.Contains("1 Printers Alley", bare.HtmlBody);
        Assert.Contains("7:00 PM CDT", bare.HtmlBody);
        Assert.Contains("Printers Alley Ghost Walk", bare.HtmlBody);
    }

    [Fact]
    public void A_business_that_never_opens_the_editor_still_sends_a_complete_mail()
    {
        var rendered = TourMailRenderer.Render(null, null,
            Facts(guidePhotos: [("Gale", "https://ishaunted.com/media/guide-photo/x")]));

        Assert.Equal("You're coming on Printers Alley Ghost Walk", rendered.Subject);
        Assert.Contains("1 Printers Alley", rendered.HtmlBody);          // where
        Assert.Contains("7:00 PM CDT", rendered.HtmlBody);               // when
        Assert.Contains("Gale", rendered.HtmlBody);                      // who
        Assert.Contains("Cash on the night", rendered.HtmlBody);         // how to pay
        Assert.Contains("alt=\"Gale\"", rendered.HtmlBody);              // the face
    }

    [Fact]
    public void The_reminder_says_tomorrow_once_and_only_once()
    {
        var plain = TourMailRenderer.RenderReminder("Your walk with {{business.name}}", null, Facts());
        Assert.Equal("Tomorrow: Your walk with Printers Alley Walks", plain.Subject);

        var already = TourMailRenderer.RenderReminder("Tomorrow night: {{tour.name}}", null, Facts());
        Assert.Equal("Tomorrow night: Printers Alley Ghost Walk", already.Subject);
    }

    [Fact]
    public void The_reminder_is_the_same_body_the_sign_up_used()
    {
        // One template, not two: what a guest needs the night before is what they needed when
        // they signed up, plus the fact that it is tomorrow.
        var facts = Facts();
        Assert.Equal(
            TourMailRenderer.Render(null, null, facts).HtmlBody,
            TourMailRenderer.RenderReminder(null, null, facts).HtmlBody);
    }
}
