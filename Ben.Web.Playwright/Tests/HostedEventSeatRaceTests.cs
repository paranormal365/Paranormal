using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Two guests reaching for the same seat in the same moment (item 235 phase 4).
/// </summary>
/// <remarks>
/// <para><b>Why this is not a click-through.</b> The claim being tested is that the DATABASE settles
/// the race, and a browser cannot press two buttons in the same millisecond. Two API clients firing
/// concurrently at the running site can, and that is the shape the plan's own "verified by" asks
/// for: two clients hold one seat, one gets a 409 naming it. The guest's picker is phase 6 and will
/// be walked in a browser when it exists.</para>
///
/// <para><b>Against the real stack.</b> A real Kestrel, a real SQL Server, the real filtered unique
/// index. None of the in-memory tests can prove this: the whole point is that two connections
/// interleave, and a single-threaded test double never does.</para>
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class HostedEventSeatRaceTests : BenTestBase
{
    /// <summary>The seeded 260-seat evening, which is a Pick event.</summary>
    private const string SeatsEventId = "40000002-0000-0000-0000-000000000003";

    private IAPIRequestContext _api = null!;

    [SetUp]
    public async Task OpenAnApiContext()
        => _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });

    [TearDown]
    public async Task CloseIt() => await _api.DisposeAsync();

    /// <summary>Signs in over the API and returns a context that carries the token.</summary>
    private async Task<IAPIRequestContext> AsAsync(string email, string password)
    {
        var login = await _api.PostAsync("/login", new()
        {
            DataObject = new { email, password },
        });
        Assert.That(login.Ok, Is.True, $"{email} could not sign in: {await login.TextAsync()}");

        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();

        return await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {token}",
            },
        });
    }

    /// <summary>
    /// Publishes the seeded evening if it is still a draft, and returns a seat and a night.
    /// </summary>
    /// <remarks>
    /// The seeder leaves it a draft on purpose — publishing spends a credit, and doing that on
    /// every database build would be spending the group's money to make a demo. So this test
    /// publishes it once and leaves it published, which is idempotent on a re-run.
    /// </remarks>
    private async Task<(string NightId, string UnitId, string UnitName)> ReadyTheHouseAsync(
        params IAPIRequestContext[] guests)
    {
        var orgId = await OrgIdBySlugAsync("paranormal365");
        var admin = await AsAsync(SuperAdminEmail, SuperAdminPassword);

        // Nobody left holding anything from a previous run, or the mode switch below is refused —
        // and refused for a good reason, since changing it would alter what their booking means.
        foreach (var guest in guests) await LetGoAsync(guest);

        // AND THE EVENT PICKS RATHER THAN ASKS.
        //
        // The seeder says so, but a database built before that line was written still has the
        // evening on Ask, and this test would then be proving something about a screen nobody is
        // testing. A fixture that only works on a database seeded the same week is not a fixture.
        var mode = await admin.PostAsync(
            $"/api/organizations/{orgId}/events/{SeatsEventId}/booking-mode",
            new() { DataObject = new { mode = 1 } });
        Assert.That(mode.Ok, Is.True,
            $"the evening could not be put on picking: {await mode.TextAsync()}");

        var publish = await admin.PostAsync(
            $"/api/organizations/{orgId}/events/{SeatsEventId}/publish", new() { DataObject = new { } });
        Assert.That(publish.Ok, Is.True,
            $"the evening could not be published: {await publish.TextAsync()}");

        var layout = await admin.GetAsync($"/api/organizations/{orgId}/events/{SeatsEventId}/layout");
        Assert.That(layout.Ok, Is.True, await layout.TextAsync());

        var units = (await layout.JsonAsync())!.Value.GetProperty("units");
        Assert.That(units.GetArrayLength(), Is.GreaterThan(0), "the evening has no seats on its plan");

        var seat = units.EnumerateArray().First();

        var ev = await admin.GetAsync($"/api/organizations/{orgId}/events/{SeatsEventId}");
        Assert.That(ev.Ok, Is.True, await ev.TextAsync());
        var night = (await ev.JsonAsync())!.Value.GetProperty("nights").EnumerateArray().First();

        await admin.DisposeAsync();

        return (night.GetProperty("id").GetString()!,
                seat.GetProperty("id").GetString()!,
                seat.GetProperty("name").GetString()!);
    }

    /// <summary>Lets go of whatever this person is holding, so the test can run again.</summary>
    private static async Task LetGoAsync(IAPIRequestContext who)
    {
        var mine = await who.GetAsync("/api/public/hosted-events/mine");
        if (!mine.Ok) return;

        foreach (var booking in (await mine.JsonAsync())!.Value.EnumerateArray())
        {
            var eventId = booking.GetProperty("hostedEventId").GetString();
            if (eventId is null) continue;
            await who.DeleteAsync($"/api/public/hosted-events/{eventId}/my-booking");
        }
    }

    [Test]
    public async Task Two_guests_reaching_for_one_seat_get_one_yes_and_one_named_no()
    {
        var first = await AsAsync(UserEmail, UserPassword);
        var second = await AsAsync(MemberEmail, MemberPassword);

        var (nightId, unitId, unitName) = await ReadyTheHouseAsync(first, second);

        var body = new
        {
            nights = new[] { new { hostedEventNightId = nightId, hostedEventLayoutUnitId = unitId } },
            partySize = 1,
            // The organizer has to be able to reach whoever holds (slice 11d).
            firstName = "Test", lastName = "Guest", phone = "615-555-0100",
        };

        // Fired together, not one after the other. Awaiting the first would test nothing: the
        // interesting case is two connections inside the same instant, which is exactly what no
        // check written in C# can settle.
        var attempts = await Task.WhenAll(
            first.PostAsync($"/api/public/hosted-events/{SeatsEventId}/holds", new() { DataObject = body }),
            second.PostAsync($"/api/public/hosted-events/{SeatsEventId}/holds", new() { DataObject = body }));

        var won = attempts.Where(a => a.Status == 200).ToList();
        var lost = attempts.Where(a => a.Status == 409).ToList();

        Assert.That(won, Has.Count.EqualTo(1),
            "exactly one of the two should have got the seat; got "
            + string.Join(", ", attempts.Select(a => a.Status)));
        Assert.That(lost, Has.Count.EqualTo(1), "the other should have been told the seat went");

        // And told WHICH seat, because "that seat is taken" in front of 260 of them is not
        // something a guest can act on.
        var refusal = JsonDocument.Parse(await lost[0].TextAsync()).RootElement;
        Assert.That(refusal.GetProperty("sentence").GetString(), Does.Contain(unitName));
        Assert.That(
            refusal.GetProperty("takenUnitIds").EnumerateArray().Select(x => x.GetString()),
            Does.Contain(unitId));

        await LetGoAsync(first);
        await LetGoAsync(second);
        await first.DisposeAsync();
        await second.DisposeAsync();
    }

    [Test]
    public async Task A_seat_somebody_is_holding_is_not_offered_to_the_next_person()
    {
        // The race settled once, the seat stays settled. This is the ordinary case the race is the
        // extreme of, and it fails differently: not a lost race but a seat that is simply gone.
        var holder = await AsAsync(UserEmail, UserPassword);
        var latecomer = await AsAsync(MemberEmail, MemberPassword);

        var (nightId, unitId, unitName) = await ReadyTheHouseAsync(holder, latecomer);

        var body = new
        {
            nights = new[] { new { hostedEventNightId = nightId, hostedEventLayoutUnitId = unitId } },
            partySize = 1,
            // The organizer has to be able to reach whoever holds (slice 11d).
            firstName = "Test", lastName = "Guest", phone = "615-555-0100",
        };

        var taken = await holder.PostAsync(
            $"/api/public/hosted-events/{SeatsEventId}/holds", new() { DataObject = body });
        Assert.That(taken.Status, Is.EqualTo(200), await taken.TextAsync());

        var refused = await latecomer.PostAsync(
            $"/api/public/hosted-events/{SeatsEventId}/holds", new() { DataObject = body });
        Assert.That(refused.Status, Is.EqualTo(409), await refused.TextAsync());

        var refusal = JsonDocument.Parse(await refused.TextAsync()).RootElement;
        Assert.That(refusal.GetProperty("sentence").GetString(), Does.Contain(unitName));

        await LetGoAsync(holder);
        await LetGoAsync(latecomer);
        await holder.DisposeAsync();
        await latecomer.DisposeAsync();
    }

    [Test]
    public async Task Letting_a_seat_go_puts_it_back_for_somebody_else()
    {
        // The other half of the rule, and the one that makes the index's filter earn its place: a
        // released night is history, so the seat is free again rather than held for ever.
        var first = await AsAsync(UserEmail, UserPassword);
        var second = await AsAsync(MemberEmail, MemberPassword);

        var (nightId, unitId, _) = await ReadyTheHouseAsync(first, second);

        var body = new
        {
            nights = new[] { new { hostedEventNightId = nightId, hostedEventLayoutUnitId = unitId } },
            partySize = 1,
            // The organizer has to be able to reach whoever holds (slice 11d).
            firstName = "Test", lastName = "Guest", phone = "615-555-0100",
        };

        Assert.That(
            (await first.PostAsync($"/api/public/hosted-events/{SeatsEventId}/holds",
                new() { DataObject = body })).Status,
            Is.EqualTo(200));

        await LetGoAsync(first);

        var again = await second.PostAsync(
            $"/api/public/hosted-events/{SeatsEventId}/holds", new() { DataObject = body });
        Assert.That(again.Status, Is.EqualTo(200), await again.TextAsync());

        await LetGoAsync(second);
        await first.DisposeAsync();
        await second.DisposeAsync();
    }
}
