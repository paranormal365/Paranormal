using System.Net;
using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Ben.Web.Services.WebApi;
using Ben.Web.Tests.Support;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every hosted-event booking call goes to the address it means to, and none of them can mistake a
/// refusal for an absence (item 235, phase 1).
/// </summary>
/// <remarks>
/// <para><b>Why the addresses are pinned as text.</b> A route with a typo in it is a 404, and a
/// 404 on a read here is a booking board politely reporting an empty weekend. The website slice
/// was written against controllers another agent was still adding routes to, so the contract is
/// the string, and the string is what this reads. Each address must appear <i>exactly once</i>:
/// twice means a copy-paste that will drift, zero means the door has no key.</para>
///
/// <para><b>Why <c>GetAsync&lt;</c> is banned from the file.</b> It answers a 401, a 403, a 404
/// and an empty success with the same <c>null</c>. The plan of record's rule for every hosted
/// screen is that a 403 must not read as "not found" — a member refused the dietary sheet is not
/// looking at an event with no guests — and the only way to keep that rule for every future method
/// in the slice is to make the flattening helper unwelcome in the file.</para>
///
/// <para>The last three tests send real requests through <see cref="WebApiClient"/> and a capturing
/// handler, for the three places a source scan cannot see: the verb, the escaping of the guest's
/// reason, and the three-way answer of the layout save.</para>
/// </remarks>
public sealed class EventBookingClientRouteTests
{
    private const string Adapter   = "BenAdminClientAdapter.EventBooking.cs";
    private const string Contract  = "IBenEventBookingClient.cs";
    private const string Aggregate = "IBenAdminClient.cs";

    /// <summary>
    /// Every door the slice wraps, as it is spelled in the adapter — placeholders included, closing
    /// quote included, so that <c>…/pass"</c> and <c>…/pass/revoke"</c> are different strings.
    /// </summary>
    private static readonly string[] Routes =
    [
        // the plan (GET + PUT share the helper)
        "/api/organizations/{orgId}/events/{eventId}/layout\"",
        // the board
        "/api/organizations/{orgId}/events/{eventId}/bookings\"",
        "/api/organizations/{orgId}/events/{eventId}/bookings/dietary?includeRequests={flag}\"",
        "/api/organizations/{orgId}/events/{eventId}/bookings/{bookingId}/confirm\"",
        "/api/organizations/{orgId}/events/{eventId}/bookings/{bookingId}/turn-down\"",
        "/api/organizations/{orgId}/events/{eventId}/bookings/{bookingId}/cancel\"",
        "/api/organizations/{orgId}/events/{eventId}/bookings/{bookingId}\"",
        "/api/organizations/{orgId}/events/{eventId}/bookings/on-behalf\"",
        "/api/organizations/{orgId}/events/{eventId}/bookings/on-behalf/invite\"",
        // passes and the door
        "/api/organizations/{orgId}/events/{eventId}/bookings/{bookingId}/pass\"",
        "/api/organizations/{orgId}/events/{eventId}/bookings/{bookingId}/pass/revoke\"",
        "/api/organizations/{orgId}/events/{eventId}/bookings/{bookingId}/pass/reissue\"",
        "/api/organizations/{orgId}/events/{eventId}/bookings/{bookingId}/pass/email\"",
        "/api/organizations/{orgId}/events/{eventId}/bookings/door/scan\"",
        // menus (GET + PUT share the helper)
        "/api/organizations/{orgId}/events/{eventId}/menus\"",
        // the guest
        "/api/public/hosted-events/mine\"",
        "/api/public/hosted-events/{eventId}/my-booking\"",
        "/api/public/hosted-events/{eventId}/bookings\"",
        "/api/public/hosted-events/{eventId}/my-booking/acknowledge\"",
        "/api/public/hosted-events/{eventId}/my-booking/pass\"",
        "/api/public/hosted-events/{eventId}/menus\"",
    ];

    /// <summary>The names the plan gave the doors. The compiler proves they are implemented; this proves they are spelled as planned.</summary>
    private static readonly string[] Methods =
    [
        "GetEventLayoutAsync", "SetEventLayoutAsync", "GetEventBookingBoardAsync",
        "ConfirmEventBookingAsync", "TurnDownEventBookingAsync", "CancelEventBookingAsync",
        "EditEventBookingAsync", "CreateEventBookingOnBehalfAsync", "InviteEventGuestAsync",
        "GetEventDietaryAsync", "GetEventMenusAsync", "SetEventMenusAsync",
        "IssueEventPassAsync", "RevokeEventPassAsync", "ReissueEventPassAsync", "EmailEventPassAsync",
        "ScanEventPassAsync",
        "GetMyHostedEventBookingsAsync", "GetMyHostedEventBookingAsync", "RequestHostedEventBookingAsync",
        "UpdateMyHostedEventBookingAsync", "AcknowledgeMyHostedEventBookingAsync",
        "WithdrawMyHostedEventBookingAsync", "GetMyHostedEventPassAsync", "GetMyHostedEventMenusAsync",
    ];

    private static string Source(string fileName)
    {
        var path = RepoFiles.Paths("*.cs").SingleOrDefault(p => Path.GetFileName(p) == fileName);
        Assert.NotNull(path);   // the file itself went missing, which is a different problem
        return File.ReadAllText(path!);
    }

    private static int Occurrences(string haystack, string needle)
    {
        int count = 0, at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { count++; at += needle.Length; }
        return count;
    }

    // ── the source-scan contract ─────────────────────────────────────────────

    [Fact]
    public void Every_route_appears_in_the_adapter_exactly_once()
    {
        var text = Source(Adapter);

        var wrong = Routes
            .Select(r => (Route: r, Count: Occurrences(text, r)))
            .Where(x => x.Count != 1)
            .Select(x => $"{x.Count}× {x.Route}")
            .ToList();

        Assert.True(wrong.Count == 0,
            "each address must be spelled exactly once in " + Adapter + " — zero is a door with no "
            + "key, two is a copy that will drift:\n  " + string.Join("\n  ", wrong));
    }

    [Fact]
    public void No_method_in_the_adapter_uses_the_flattening_GetAsync()
    {
        // GetItemAsync< and GetListAsync< are the honest ones; "GetAsync<" is the one that turns a
        // 403 into the same null as a 404. Matched with the angle bracket so the honest helpers'
        // names, which merely contain the letters, do not trip it.
        var text = Source(Adapter);

        Assert.DoesNotContain("GetAsync<", text);
        Assert.DoesNotContain("GetAnonymousAsync<", text);
    }

    [Fact]
    public void Every_planned_method_is_declared_on_the_contract()
    {
        var text = Source(Contract);
        var missing = Methods.Where(m => !text.Contains(m + "(")).ToList();

        Assert.True(missing.Count == 0,
            "the plan named these doors and the contract does not declare them:\n  "
            + string.Join("\n  ", missing));
    }

    [Fact]
    public void The_slice_is_part_of_the_aggregate_every_page_injects()
    {
        // A slice that is not in IBenAdminClient is a slice no page can reach.
        Assert.Contains("IBenEventBookingClient", Source(Aggregate));
    }

    [Fact]
    public void Reads_that_return_one_thing_carry_an_ItemResult()
    {
        // Every single-object read on the contract — the layout, the board, the sheet, the menus,
        // the guest's booking, pass and menus — must be able to say "refused" as distinct from
        // "absent". Counted rather than named, so a new read added later is held to the same rule.
        var text = Source(Contract);
        var singleObjectReads = Methods.Count(m => m.StartsWith("Get", StringComparison.Ordinal))
                              - 1;   // GetMyHostedEventBookingsAsync is a list and carries LoadResult

        Assert.Equal(singleObjectReads, Occurrences(text, "Task<ItemResult<"));
        Assert.Equal(1, Occurrences(text, "Task<LoadResult<"));
    }

    // ── what a source scan cannot see: the wire ──────────────────────────────

    /// <summary>Records what was asked for and answers with whatever the test needs.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Body { get; set; } = "null";
        public string MediaType { get; set; } = "application/json";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(Body, System.Text.Encoding.UTF8, MediaType),
            });
        }
    }

    private static (BenAdminClientAdapter Client, CapturingHandler Handler) Build(
        HttpStatusCode status = HttpStatusCode.OK, string body = "null", string mediaType = "application/json")
    {
        var handler = new CapturingHandler { Status = status, Body = body, MediaType = mediaType };
        var http    = new HttpClient(handler) { BaseAddress = new Uri("http://unit.test") };
        var api     = new WebApiClient(http, new WebApiTokenStore { AccessToken = "t" });
        var adapter = new BenAdminClientAdapter(api, new Mock<IWebApiAuthService>().Object,
            Microsoft.Extensions.Options.Options.Create(new WebApiOptions()));
        return (adapter, handler);
    }

    private static readonly Guid Org   = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Event = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Withdrawing_is_a_DELETE_whose_reason_travels_escaped_in_the_query()
    {
        // "can't make it, my mother's ill & the car's gone" is an ordinary reason; unescaped, the
        // ampersand would hand the server half of it and the apostrophe would be a matter of luck.
        var (client, handler) = Build();

        var (withdrawn, error) = await client.WithdrawMyHostedEventBookingAsync(Event, "mother's ill & the car's gone");

        Assert.True(withdrawn);
        Assert.Null(error);
        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
        Assert.Equal($"/api/public/hosted-events/{Event}/my-booking?reason=mother%27s%20ill%20%26%20the%20car%27s%20gone",
            handler.LastRequest.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Withdrawing_with_no_reason_sends_no_query_at_all()
    {
        var (client, handler) = Build();
        await client.WithdrawMyHostedEventBookingAsync(Event, "   ");

        Assert.Equal($"/api/public/hosted-events/{Event}/my-booking", handler.LastRequest!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Withdrawing_a_booking_already_turned_down_keeps_the_servers_sentence()
    {
        // Phase 1 turned this into a 409 with prose; the guest must read the prose, not "failed".
        var (client, _) = Build(HttpStatusCode.Conflict,
            "That booking was turned down, so there is nothing to withdraw.", "text/plain");

        var (withdrawn, error) = await client.WithdrawMyHostedEventBookingAsync(Event, null);

        Assert.False(withdrawn);
        Assert.Equal("That booking was turned down, so there is nothing to withdraw.", error);
    }

    [Fact]
    public async Task The_dietary_sheet_spells_the_flag_the_way_the_API_documents_it()
    {
        var (client, handler) = Build(body: "null");
        await client.GetEventDietaryAsync(Org, Event, includeRequests: true);

        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.EndsWith("/bookings/dietary?includeRequests=true", handler.LastRequest.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task A_refused_read_is_Failed_and_not_an_absent_item()
    {
        // The rule itself, on the wire: a 403 on the board is a refusal, not an empty weekend.
        var (client, _) = Build(HttpStatusCode.Forbidden, "", "text/plain");

        var board = await client.GetEventBookingBoardAsync(Org, Event);

        Assert.True(board.Failed);
        Assert.False(board.SessionExpired);
        Assert.Null(board.Item);
    }

    private static SetHostedEventLayoutRequest AnyLayout()
        => new(HostedEventLayoutKind.Rooms, []);

    [Fact]
    public async Task Saving_the_layout_is_a_PUT_and_a_409_comes_back_as_the_units_to_ring()
    {
        var ringMe = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var (client, handler) = Build(HttpStatusCode.Conflict,
            $$"""{"sentence":"C4 still has a confirmed booking.","unitIds":["{{ringMe}}"]}""");

        var (result, error, conflict) = await client.SetEventLayoutAsync(Org, Event, AnyLayout());

        Assert.Equal(HttpMethod.Put, handler.LastRequest!.Method);
        Assert.Null(result);
        Assert.Null(error);
        Assert.NotNull(conflict);
        Assert.Equal("C4 still has a confirmed booking.", conflict!.Sentence);
        Assert.Equal(new[] { ringMe }, conflict.UnitIds);
    }

    [Fact]
    public async Task Saving_the_layout_keeps_a_400_sentence_as_the_error()
    {
        // The other refusal: a plain sentence, no structure. PostExpectingConflictAsync would have
        // dropped this on the floor, which is why the slice has its own helper.
        var (client, _) = Build(HttpStatusCode.BadRequest,
            "That room belongs to another place.", "text/plain");

        var (result, error, conflict) = await client.SetEventLayoutAsync(Org, Event, AnyLayout());

        Assert.Null(result);
        Assert.Null(conflict);
        Assert.Equal("That room belongs to another place.", error);
    }

    [Fact]
    public async Task Saving_the_layout_treats_a_409_that_is_not_ours_as_a_plain_failure()
    {
        // A proxy's 409 is an HTML page. It must neither crash nor be shown.
        var (client, _) = Build(HttpStatusCode.Conflict, "<html><body>409 Conflict</body></html>", "text/html");

        var (result, error, conflict) = await client.SetEventLayoutAsync(Org, Event, AnyLayout());

        Assert.Null(result);
        Assert.Null(error);
        Assert.Null(conflict);
    }

    [Fact]
    public async Task Saving_the_layout_returns_the_saved_plan_on_success()
    {
        var (client, _) = Build(body:
            $$"""{"hostedEventId":"{{Event}}","kind":1,"dayPassCapacity":null,"dayPassPrice":null,"units":[]}""");

        var (result, error, conflict) = await client.SetEventLayoutAsync(Org, Event, AnyLayout());

        Assert.NotNull(result);
        Assert.Equal(Event, result!.HostedEventId);
        Assert.Null(error);
        Assert.Null(conflict);
    }
}
