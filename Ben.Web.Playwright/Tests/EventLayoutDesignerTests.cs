using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The layout designer, on a desktop, an iPad and an iPhone (item 235 phase 2).
/// </summary>
/// <remarks>
/// <para><b>Three widths, because decision 13 says so.</b> "Remember it has to work on computer,
/// iPhone and iPad-sized screens so it has to be able to have a compact view as well as desktop
/// view." A screen that has only ever been opened at 1280 is a screen nobody has checked, and this
/// is the first deliberately compact one on the site — so the widths are part of the test rather
/// than a thing to remember to look at.</para>
///
/// <para><b>It runs on the seeded plans</b> from <c>HostedEventDemoSeeder</c>: the Thomas House
/// weekend, six bookable rooms with four of them placed, and An Evening of Evidence, 260 seats in
/// two sections with a centre aisle. Building a 260-seat house through the UI in every test would
/// be slower than the thing it tests, and the seeded pair is also what a person opening the dev
/// site sees — so a failure here is a failure they would have met.</para>
///
/// <para>The plans are seeded with STABLE ids, which is what lets this navigate straight to the
/// page. The organization's id is resolved by slug because organizations are not seeded with fixed
/// ids.</para>
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class EventLayoutDesignerTests : BenTestBase
{
    /// <summary>The seeded weekend in the hotel's rooms.</summary>
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";

    /// <summary>The seeded 260-seat evening.</summary>
    private const string SeatsEventId = "40000002-0000-0000-0000-000000000003";

    private static readonly (string Name, int Width, int Height) Desktop = ("desktop", 1280, 800);
    private static readonly (string Name, int Width, int Height) Tablet = ("ipad", 768, 1024);
    private static readonly (string Name, int Width, int Height) Phone = ("iphone", 375, 812);

    [SetUp]
    public async Task SignIn() => await LoginAsync(SuperAdminEmail, SuperAdminPassword);

    /// <summary>Opens one seeded plan at one width and waits for the grid to be drawn.</summary>
    private async Task OpenAsync(string eventId, (string Name, int Width, int Height) size)
    {
        await Page.SetViewportSizeAsync(size.Width, size.Height);

        var orgId = await OrgIdBySlugAsync("paranormal365");
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/events/{eventId}/layout");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        // The grid itself, not NetworkIdle: a Blazor Server circuit fetches the plan after the
        // network goes quiet, so NetworkIdle is not a drawn plan.
        await Expect(Page.Locator(".plan__grid")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    /// <summary>
    /// Makes sure at least one room is waiting in the list, whatever the database has been through.
    /// </summary>
    /// <remarks>
    /// <para><b>Written after this fixture broke itself.</b> An earlier version placed a room and
    /// saved without putting it back, so two runs against one database left every bookable room on
    /// the plan and every later run failed waiting for a tray button that could not exist. The save
    /// test restores what it takes now, but a fixture that only works on a database no earlier
    /// version of itself has touched is not a fixture anybody can trust.</para>
    ///
    /// <para>So it establishes its own precondition rather than assuming the seed: takes one room
    /// off the plan and saves, but only when the list is empty. On a fresh database it does
    /// nothing at all.</para>
    /// </remarks>
    private async Task EnsureARoomIsWaitingAsync()
    {
        if (await Page.Locator("#plan-tray button").CountAsync() > 0) return;

        await Page.Locator(".plan__unit").First.ClickAsync();
        await Page.Locator("#plan-unplace").ClickAsync();
        await Expect(Page.Locator("#plan-tray button").First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await SaveAsync();
        await ReopenAsync();
    }

    // ── the seating plan ─────────────────────────────────────────────────────

    [Test]
    public async Task A_seated_house_draws_every_seat_and_leaves_the_aisle_empty()
    {
        await OpenAsync(SeatsEventId, Desktop);

        await Expect(Page.Locator(".plan__unit")).ToHaveCountAsync(260);

        // Column 10 is the gangway. Grid column 12 — the first two belong to the row letters and
        // the numbering — must hold no seat at all, or the aisle reads as a row of missing seats.
        await Expect(Page.Locator(".plan__unit[style*='grid-column:12']")).ToHaveCountAsync(0);

        // Rows are lettered without I, so the thirteenth row is N and there is no row I.
        await Expect(Page.GetByText("N20", new() { Exact = true })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Every_row_of_the_house_is_actually_on_the_screen()
    {
        // THE ONE THIS FIXTURE EXISTS FOR. The first build had all 260 seats in the DOM, passing
        // every count and visibility assertion, while six of the thirteen rows were cut off the
        // bottom by the grid's own overflow — a percentage in a row track resolving against a
        // height that was not there. Counting elements cannot see that; measuring the box can.
        await OpenAsync(SeatsEventId, Desktop);

        var clipped = await Page.EvaluateAsync<int>(
            "(() => { const s = document.querySelector('.plan__scroll');"
            + " return s.scrollHeight - s.clientHeight; })()");
        Assert.That(clipped, Is.LessThanOrEqualTo(1),
            $"{clipped}px of the plan is cut off the bottom of its own box");

        // And the last seat sits inside the grid's own box rather than past its bottom edge.
        // Deliberately not "in the viewport": a thirteen-row house is taller than an 800-pixel
        // window and scrolling the page to it is the reader's job, not a defect.
        var lastSeatFits = await Page.EvaluateAsync<bool>(
            "(() => { const g = document.querySelector('.plan__grid').getBoundingClientRect();"
            + " const last = [...document.querySelectorAll('.plan__unit')].pop().getBoundingClientRect();"
            + " return last.bottom <= g.bottom + 1 && last.right <= g.right + 1; })()");
        Assert.That(lastSeatFits, Is.True, "the last seat is drawn outside the grid it belongs to");
    }

    [Test]
    public async Task The_legend_prices_each_section_once()
    {
        await OpenAsync(SeatsEventId, Desktop);

        var legend = Page.GetByTestId("plan-legend");
        await Expect(legend).ToContainTextAsync("Stalls");
        await Expect(legend).ToContainTextAsync("Balcony");
        // Once per section, not once per seat: 260 squares each printing a price is 260 things to
        // read, and the legend is where a venue actually checks it.
        await Expect(legend.Locator(".plan__legend-item")).ToHaveCountAsync(2);
    }

    [Test]
    public async Task Choosing_a_seat_offers_what_can_be_done_with_it()
    {
        await OpenAsync(SeatsEventId, Desktop);

        await Page.Locator(".plan__unit").First.ClickAsync();

        await Expect(Page.Locator("#plan-selection-bar")).ToBeVisibleAsync();
        await Expect(Page.Locator("#plan-selection-bar")).ToContainTextAsync("1 chosen");
        await Expect(Page.Locator("#plan-relabel")).ToBeVisibleAsync();
    }

    [Test]
    public async Task A_quick_mouse_sweep_chooses_every_seat_it_crosses()
    {
        await OpenAsync(SeatsEventId, Desktop);

        // Six seats side by side in the first row, swept in one jump — as a quick hand does, and as the pointer reports a
        // fast drag: a move every few seats. Asking only where each move landed chose the ends and missed the middle
        // (UI test pass 6.12, 2026-09-14).
        var seats = await Page.Locator(".plan__unit").EvaluateAllAsync<float[][]>(@"els => {
            const boxes = els.map(e => e.getBoundingClientRect()).map(r => [r.left, r.top, r.width, r.height]);
            const top = Math.min(...boxes.map(b => b[1]));
            return boxes.filter(b => Math.abs(b[1] - top) < 2).sort((a, b) => a[0] - b[0]);
        }");
        Assert.That(seats.Length, Is.GreaterThanOrEqualTo(6), "the seeded house's first row has fewer than six seats side by side");
        var first = seats[0];
        var sixth = seats[5];
        var midY = first[1] + first[3] / 2;

        await Page.Mouse.MoveAsync(first[0] + first[2] / 2, midY);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(sixth[0] + sixth[2] / 2, midY, new() { Steps = 1 });
        await Page.Mouse.UpAsync();

        await Expect(Page.Locator("#plan-selection-bar")).ToContainTextAsync("6 chosen", new() { Timeout = 10_000 });
    }

    [Test]
    public async Task A_seating_plan_never_offers_the_tray_of_rooms()
    {
        await OpenAsync(SeatsEventId, Desktop);

        await Expect(Page.Locator("#plan-add-block")).ToBeVisibleAsync();
        await Expect(Page.Locator("#plan-open-tray")).ToHaveCountAsync(0);
    }

    // ── the floor plan ───────────────────────────────────────────────────────

    [Test]
    public async Task A_floor_plan_shows_the_rooms_still_waiting_to_be_placed()
    {
        await OpenAsync(RoomsEventId, Desktop);
        await EnsureARoomIsWaitingAsync();

        // Every bookable room is either on the plan or in the tray, and never both nor neither.
        // Asserted as the invariant rather than as "two are waiting", so this does not quietly
        // depend on whether another test in this fixture has already placed one.
        var placed = await Page.Locator(".plan__unit").CountAsync();
        var waiting = await Page.Locator("#plan-tray button").CountAsync();
        Assert.That(placed + waiting, Is.EqualTo(6),
            "the venue has six bookable rooms; each should be on the plan or in the list, once");

        await Expect(Page.Locator("#plan-tray")).ToContainTextAsync("sleeps");
        // The attic is deliberately not bookable, and a plan must not be able to offer it.
        await Expect(Page.Locator("#plan-tray")).Not.ToContainTextAsync("The Attic");
    }

    [Test]
    public async Task A_placed_room_carries_the_venues_own_name_for_it()
    {
        await OpenAsync(RoomsEventId, Desktop);

        await Expect(Page.GetByText("The Blue Room", new() { Exact = false }).First)
            .ToBeVisibleAsync();
    }

    [Test]
    public async Task Placing_a_room_is_choose_it_then_press_a_square()
    {
        await OpenAsync(RoomsEventId, Desktop);
        await EnsureARoomIsWaitingAsync();

        var before = await Page.Locator(".plan__unit").CountAsync();

        await Page.Locator("#plan-tray button").First.ClickAsync();
        await Expect(Page.Locator("#plan-selection-bar")).ToContainTextAsync("1 chosen");

        await Page.Locator(".plan__empty").First.ClickAsync();

        await Expect(Page.Locator(".plan__unit")).ToHaveCountAsync(before + 1);
        await Expect(Page.Locator("#plan-unsaved")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Saving_a_plan_survives_a_reload()
    {
        await OpenAsync(RoomsEventId, Desktop);
        await EnsureARoomIsWaitingAsync();

        var before = await Page.Locator(".plan__unit").CountAsync();
        var namesBefore = await Page.Locator(".plan__unit-name").AllInnerTextsAsync();

        await Page.Locator("#plan-tray button").First.ClickAsync();
        await Page.Locator(".plan__empty").First.ClickAsync();
        // Waited for, not read: the count immediately after a click is one render behind, which
        // is how this test first "proved" that saving added a room it had not been given.
        await Expect(Page.Locator(".plan__unit")).ToHaveCountAsync(before + 1);

        var placed = (await Page.Locator(".plan__unit-name").AllInnerTextsAsync())
            .Except(namesBefore).Single();

        await SaveAsync();
        await ReopenAsync();
        await Expect(Page.Locator(".plan__unit")).ToHaveCountAsync(before + 1);

        // Saving again must be a no-op. A new unit comes back from the server with an id, and a
        // screen that kept its own copy would send a create for the same room a second time —
        // which on a Rooms plan is a duplicate the venue then has to find and delete.
        await SaveAsync();
        await ReopenAsync();
        await Expect(Page.Locator(".plan__unit")).ToHaveCountAsync(before + 1);

        // Put it back. A fixture that permanently consumes the seeded tray passes once and then
        // fails on every later run against the same database — which is exactly what happened,
        // two runs in. Removing it also happens to be the only test of removing anything.
        await Page.Locator(".plan__unit", new() { HasText = placed }).First.ClickAsync();
        await Page.Locator("#plan-remove").ClickAsync();
        await Page.Locator(".modal-footer .btn-danger").ClickAsync();
        await Expect(Page.Locator(".plan__unit")).ToHaveCountAsync(before);

        await SaveAsync();
        await ReopenAsync();
        await Expect(Page.Locator(".plan__unit")).ToHaveCountAsync(before);
    }

    /// <summary>Reloads and waits for the plan to be drawn again.</summary>
    private async Task ReopenAsync()
    {
        await Page.ReloadAsync();
        await Expect(Page.Locator(".plan__grid")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    /// <summary>Presses Save and waits for the server to have answered.</summary>
    private async Task SaveAsync()
    {
        await Page.Locator("#plan-save").ClickAsync();
        await Expect(Page.Locator("#layout-note")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        // Saved means saved: the unsaved line goes, which is also what stops the leaving guard.
        await Expect(Page.Locator("#plan-unsaved")).ToHaveCountAsync(0);
    }

    // ── the three widths ─────────────────────────────────────────────────────

    [Test]
    [TestCase(1280, 800)]
    [TestCase(768, 1024)]
    [TestCase(375, 812)]
    public async Task The_page_never_scrolls_sideways_however_wide_the_house_is(int width, int height)
    {
        // 260 seats across 21 columns is far wider than any of these. Whether it fits by the
        // squares shrinking or has to scroll, the PAGE must never slide sideways under a thumb —
        // that is the single thing that makes a site feel broken on a phone.
        await OpenAsync(SeatsEventId, ("size", width, height));

        var pageOverflows = await Page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.That(pageOverflows, Is.False,
            $"the page scrolls sideways at {width}px — the plan's own scroller should be taking it");
    }

    [Test]
    public async Task On_a_phone_the_grid_takes_the_scrolling_rather_than_the_squares_shrinking()
    {
        // Above the phone breakpoint the squares shrink to fit, down to a floor. Below it they
        // stop shrinking at a size a finger can hit and the grid scrolls instead — which is the
        // trade, and the half of it a wide screen never exercises.
        await OpenAsync(SeatsEventId, Phone);

        var gridScrolls = await Page.EvaluateAsync<bool>(
            "(() => { const s = document.querySelector('.plan__scroll');"
            + " return !!s && s.scrollWidth > s.clientWidth; })()");
        Assert.That(gridScrolls, Is.True,
            "a 260-seat house cannot fit 375px with tappable squares — the scroller should say so");
    }

    [Test]
    public async Task Saving_stays_reachable_on_a_phone()
    {
        // The one control nobody may lose off the right-hand edge.
        await OpenAsync(SeatsEventId, Phone);

        await Expect(Page.Locator("#plan-save")).ToBeInViewportAsync();
        await Expect(Page.Locator("#plan-phone-note")).ToBeVisibleAsync();
    }

    [Test]
    public async Task A_phone_says_that_arranging_a_house_is_a_bigger_screens_job()
    {
        await OpenAsync(SeatsEventId, Phone);
        await Expect(Page.Locator("#plan-phone-note"))
            .ToContainTextAsync("easier on a computer or an iPad");

        // And says nothing of the sort on a desktop, where it is simply noise.
        await OpenAsync(SeatsEventId, Desktop);
        await Expect(Page.Locator("#plan-phone-note")).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task An_iPad_arranges_a_house_the_way_a_computer_does()
    {
        // 768 is where the compact rules stop. An iPad is a real working surface for this — Ben
        // said so — so it gets the whole toolbar and not the apology.
        await OpenAsync(SeatsEventId, Tablet);

        await Expect(Page.Locator("#plan-phone-note")).Not.ToBeVisibleAsync();
        await Expect(Page.Locator("#plan-add-block")).ToBeInViewportAsync();
        await Expect(Page.Locator("#plan-insert-aisle")).ToBeInViewportAsync();
        await Expect(Page.Locator("#plan-save")).ToBeInViewportAsync();
    }

    [Test]
    public async Task Every_square_stays_big_enough_to_hit_with_a_finger()
    {
        await OpenAsync(SeatsEventId, Phone);

        var box = await Page.Locator(".plan__unit").First.BoundingBoxAsync();
        Assert.That(box, Is.Not.Null);
        // 44 CSS pixels is the smallest square a finger hits reliably. A plan that fits the screen
        // and cannot be tapped is not a plan that works on a phone.
        Assert.That(box!.Width, Is.GreaterThanOrEqualTo(43.5f), "seats are too small to tap");
        Assert.That(box.Height, Is.GreaterThanOrEqualTo(43.5f), "seats are too small to tap");
    }

    [Test]
    public async Task A_price_can_be_fixed_from_a_phone_through_the_sheet()
    {
        // The whole compact promise: read-mostly, but the things somebody does on the night —
        // fix a price, block a seat — go through the per-unit sheet and work.
        await OpenAsync(RoomsEventId, Phone);

        await Page.Locator(".plan__unit").First.ClickAsync();
        await Page.Locator("#plan-set-details").ClickAsync();

        await Expect(Page.Locator("#sheet-price")).ToBeVisibleAsync();
        await Page.Locator("#sheet-price").FillAsync("195");
        await Page.Locator("#sheet-apply").ClickAsync();

        await Expect(Page.Locator("#plan-unsaved")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Nothing_offers_to_change_what_a_plan_holds_once_it_holds_something()
    {
        await OpenAsync(SeatsEventId, Desktop);

        // Both events already have units, and the server refuses a kind change on a plan with
        // anything in it. A control that is going to bounce should not be drawn.
        await Expect(Page.Locator("#plan-kind-rooms")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#plan-kind-seats")).ToHaveCountAsync(0);
    }
}
