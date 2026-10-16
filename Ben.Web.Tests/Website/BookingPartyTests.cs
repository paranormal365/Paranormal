using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Ben.Web.Website.Library.Organization.Public;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// A guest naming their party and saying what each person can't eat (10/13/2026).
/// </summary>
/// <remarks>
/// <para><b>The draft's rules are the ones that lose a note without a word if they're wrong.</b>
/// The server keeps no row without a name and no more rows than the party has people, so the
/// draft has to match both or a guest's typing goes nowhere.</para>
///
/// <para>The panel is rendered for real, because what matters is which states offer a change: a
/// turned-down booking has nobody for a kitchen to cook for, and the server refuses it.</para>
/// </remarks>
public sealed class BookingPartyTests
{
    private static MyHostedEventBookingRecord ABooking(
        HostedEventBookingStatus status, int partySize = 2,
        params HostedEventBookingGuestRecord[] guests) => new(
        Guid.NewGuid(), Guid.NewGuid(), "An Evening of Evidence", "an-evening-of-evidence",
        "Paranormal365", "paranormal365", "The Thomas House Hotel",
        new DateTime(2026, 10, 30), new DateTime(2026, 10, 30),
        PartySize: partySize, Kind: HostedEventBookingKind.Overnight, Status: status,
        DecisionNote: null, GuestAcknowledgedUtc: null, CancellationRequestedUtc: null,
        Note: null, Nights: [], Guests: guests);

    private static HostedEventBookingGuestRecord AGuest(string name, string? notes, int order, Guid? account = null)
        => new(Guid.NewGuid(), name, account, notes, order);

    // ── the draft ────────────────────────────────────────────────────────────

    [Fact]
    public void A_row_per_person_keeping_what_was_typed_into_the_rows_that_stay()
    {
        var draft = new BookingPartyDraft();
        draft.Resize(3);
        draft.People[0].Name = "Ruth";
        draft.People[2].Name = "Sam";

        draft.Resize(2);
        Assert.Equal(2, draft.People.Count);
        Assert.Equal("Ruth", draft.People[0].Name);

        draft.Resize(0);
        Assert.Single(draft.People);   // never fewer than the person booking
    }

    [Fact]
    public void The_first_row_is_the_person_booking_unless_they_typed_something_else()
    {
        var filled = new BookingPartyDraft();
        filled.FillLead("Daniel", "Park");
        Assert.Equal("Daniel Park", filled.People[0].Name);

        var typed = new BookingPartyDraft();
        typed.People[0].Name = "Maya Park";
        typed.FillLead("Daniel", "Park");
        Assert.Equal("Maya Park", typed.People[0].Name);

        var cleared = new BookingPartyDraft();
        cleared.Touch();   // they emptied it on purpose
        cleared.FillLead("Daniel", "Park");
        Assert.Equal("", cleared.People[0].Name);
    }

    [Fact]
    public void A_note_with_no_name_is_caught_rather_than_dropped()
    {
        var draft = new BookingPartyDraft();
        draft.Resize(2);
        draft.People[1].Notes = "nut allergy";

        Assert.True(draft.HasANoteWithoutAName);

        draft.People[1].Name = "Sam";
        Assert.False(draft.HasANoteWithoutAName);
    }

    [Fact]
    public void A_note_with_no_name_is_said_in_one_plain_sentence()
    {
        var draft = new BookingPartyDraft();
        draft.Resize(2);
        Assert.Null(draft.WhatIsMissing);

        draft.People[1].Notes = "nut allergy";
        Assert.Equal("Give each dietary note a name.", draft.WhatIsMissing);
    }

    // ── who it is for: the reason beside a grey button (10/16/2026) ──────────

    private static BookingContactDraft AContact(string first = "Ruth", string last = "Okafor",
        string email = "ruth@example.com", string phone = "(615) 555-0100")
        => new() { FirstName = first, LastName = last, Email = email, Phone = phone };

    [Theory]
    [InlineData("", "", "(615) 555-0100", "Add your first and last name.")]
    [InlineData(" ", "Okafor", "(615) 555-0100", "Add your first name.")]
    [InlineData("Ruth", "", "(615) 555-0100", "Add your last name.")]
    [InlineData("Ruth", "Okafor", "", "Add a phone number so Apple-Beta can reach you.")]
    [InlineData("Ruth", "Okafor", "555-01", "Check your phone number. It needs 7 to 15 digits.")]
    [InlineData("Ruth", "Okafor", "+1 615 555 0100 0100 99", "Check your phone number. It needs 7 to 15 digits.")]
    public void What_is_missing_names_the_next_thing_to_do(string first, string last, string phone, string expected)
    {
        var contact = AContact(first, last, phone: phone);

        Assert.Equal(expected, contact.WhatIsMissing(withEmail: false, "Apple-Beta"));
        Assert.False(contact.IsEnough(withEmail: false));
    }

    [Fact]
    public void The_production_case_a_name_and_no_phone_asks_for_the_phone()
    {
        // A signed-in guest whose account carried a name and no phone: the whole form looked filled in.
        var contact = new BookingContactDraft();
        contact.Fill(new BookingContactRecord("Ruth", "Okafor", Phone: null, Email: "ruth@example.com"));

        Assert.Equal("Add a phone number so Apple-Beta can reach you.", contact.WhatIsMissing(false, "Apple-Beta"));
    }

    [Fact]
    public void An_email_is_asked_for_only_when_the_form_needs_one()
    {
        Assert.Null(AContact(email: "").WhatIsMissing(withEmail: false, "Apple-Beta"));
        Assert.Equal("Add your email address so we can send you the link.",
            AContact(email: "").WhatIsMissing(withEmail: true, "Apple-Beta"));
        Assert.Equal("Check your email address. It needs an @.",
            AContact(email: "ruth.example.com").WhatIsMissing(withEmail: true, "Apple-Beta"));
    }

    [Fact]
    public void Enough_is_nothing_missing()
    {
        var contact = AContact();
        Assert.Null(contact.WhatIsMissing(withEmail: true, "Apple-Beta"));
        Assert.True(contact.IsEnough(withEmail: true));
    }

    [Fact]
    public void Without_a_name_for_the_group_the_sentence_says_the_organizer()
        => Assert.Equal("Add a phone number so the organizer can reach you.",
            AContact(phone: "").WhatIsMissing(withEmail: false, null));

    [Fact]
    public async Task The_ask_form_says_why_its_button_is_grey()
    {
        var html = await RenderAskFormAsync();

        // One night, so it is ticked already; nothing typed, so the name is what is missing.
        Assert.Matches(@"<button[^>]*id=""ask-send""[^>]*disabled", html);
        Assert.Matches(@"<span[^>]*id=""ask-missing""[^>]*>Add your first and last name\.</span>", html);
        Assert.Contains(@"aria-describedby=""ask-missing""", html);
        Assert.Contains("Phone number <span class=\"text-secondary\">(required)</span>", html);
    }

    private static async Task<string> RenderAskFormAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IBenAdminClient>().Object);

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);

        var hosted = new PublicHostedEventRecord(
            Guid.NewGuid(), Guid.NewGuid(), "Apple-Beta", "apple-beta", "App Review Night", "app-review-night",
            Tagline: null, Description: null, TimeZoneId: "America/Chicago",
            StartsOn: new DateTime(2026, 10, 30), EndsOn: new DateTime(2026, 10, 30),
            DatesAreSeparate: false, DateNoun: "night", VenueName: null, City: null, State: null,
            ExactAddress: null, IsExactAddressHidden: false, Latitude: null, Longitude: null,
            DayPassCapacity: null, ContactLine: null, CoverUploadFileId: null,
            IsCancelled: false, CancelledReason: null, CollectsEvidence: false,
            Nights: [new HostedEventNightRecord(Guid.NewGuid(), new DateTime(2026, 10, 30), "Friday, October 30",
                null, new TimeSpan(18, 0, 0), new TimeSpan(23, 0, 0), null, 0)]);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<AskForPlaceForm>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(AskForPlaceForm.Hosted)] = hosted,
                }));

            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    [Fact]
    public void Only_named_rows_are_sent_trimmed_with_empty_notes_as_null()
    {
        var draft = new BookingPartyDraft();
        draft.Resize(3);
        draft.People[0].Name = "  Ruth  ";
        draft.People[0].Notes = "   ";
        draft.People[2].Name = "Sam";
        draft.People[2].Notes = " vegan ";

        var sent = draft.Sent();

        Assert.Equal(2, sent.Count);
        Assert.Equal("Ruth", sent[0].DisplayName);
        Assert.Null(sent[0].DietaryNotes);
        Assert.Equal("vegan", sent[1].DietaryNotes);
    }

    [Fact]
    public void Changing_a_booking_keeps_the_lead_linked_to_their_account_and_sizes_to_the_party()
    {
        var lead = Guid.NewGuid();
        var draft = BookingPartyDraft.From(ABooking(HostedEventBookingStatus.Confirmed, partySize: 3,
            AGuest("Sam", "vegan", 1),
            AGuest("Ruth", null, 0, lead)));

        Assert.Equal(3, draft.People.Count);
        Assert.Equal("Ruth", draft.People[0].Name);   // in their order, not the list's
        Assert.Equal(lead, draft.Sent()[0].AppUserId);
        Assert.Equal("vegan", draft.People[1].Notes);
    }

    [Fact]
    public void Clearing_every_name_sends_an_empty_list_so_the_old_names_go()
    {
        // Null on a change means "leave the party as it was"; an empty list means "nobody named".
        var draft = BookingPartyDraft.From(ABooking(HostedEventBookingStatus.Requested, 1, AGuest("Ruth", null, 0)));
        draft.People[0].Name = "";

        Assert.NotNull(draft.Sent());
        Assert.Empty(draft.Sent());
    }

    // ── the panel ────────────────────────────────────────────────────────────

    private static async Task<string> RenderAsync(MyHostedEventBookingRecord booking)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IBenAdminClient>().Object);

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<BookingPartyPanel>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(BookingPartyPanel.Booking)] = booking,
                    [nameof(BookingPartyPanel.IdPrefix)] = "t",
                }));

            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    [Fact]
    public async Task A_confirmed_party_is_listed_with_its_notes_and_can_be_changed()
    {
        var html = await RenderAsync(ABooking(HostedEventBookingStatus.Confirmed, 2,
            AGuest("Ruth Okafor", null, 0), AGuest("Sam Okafor", "no shellfish", 1)));

        Assert.Contains("Sam Okafor", html);
        Assert.Contains("no shellfish", html);
        Assert.Contains("Change names and dietary needs", html);
    }

    [Fact]
    public async Task Nobody_named_says_so_and_offers_to_add_them()
    {
        var html = await RenderAsync(ABooking(HostedEventBookingStatus.Held));

        Assert.Contains("Nobody in your party is named yet.", html);
        Assert.Contains("Add names and dietary needs", html);
    }

    [Theory]
    [InlineData(HostedEventBookingStatus.TurnedDown)]
    [InlineData(HostedEventBookingStatus.Expired)]
    [InlineData(HostedEventBookingStatus.Cancelled)]
    public async Task A_booking_that_is_over_offers_no_change(HostedEventBookingStatus status)
    {
        var html = await RenderAsync(ABooking(status, 1, AGuest("Ruth", "vegan", 0)));

        Assert.DoesNotContain("names and dietary needs", html);
    }
}
