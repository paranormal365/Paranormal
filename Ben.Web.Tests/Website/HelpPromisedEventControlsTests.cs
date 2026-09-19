using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The two guest controls the venue's help chapter describes in detail actually exist.
/// </summary>
/// <remarks>
/// <para><b>The 2026-09-17 audit's clearest broken promise.</b>
/// <c>organization-administration.md</c> tells a venue, in its own chapter: "there is <b>Invite by
/// email</b>: you type their address and we send them a link", and for somebody on the phone,
/// "make the booking against an account instead". The endpoints shipped. The client methods
/// shipped — <c>InviteEventGuestAsync</c> and <c>CreateEventBookingOnBehalfAsync</c>, declared and
/// implemented. Neither had a single caller outside the adapter and a route test, and the bookings
/// board's only board-level buttons were "Give back N lapsed holds", "Download as a spreadsheet"
/// and "What the kitchen needs".</para>
///
/// <para>So a paying host read the help, went to the board, and there was nowhere to type an
/// address.</para>
///
/// <para><b>The trap that hid it.</b> <c>InviteEventGuestAsync</c> DOES have a caller —
/// <c>OrgScheduler.razor</c> — but that is the calendar-event overload on a different interface,
/// for ordinary events. A grep that does not tell the two apart reports this feature as built. So
/// this guard names the PAGE as well as the method.</para>
///
/// <para><b>And the sentence that must survive.</b> An emailed invitation holds no room and no day
/// pass until the person clicks it; the response record carries no booking precisely so a host is
/// never told otherwise. A dialog that omitted that would be a worse bug than the missing button,
/// so it is asserted too.</para>
/// </remarks>
public sealed class HelpPromisedEventControlsTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private static string Board()
        => File.ReadAllText(Path.Combine(
            RepoRoot().FullName, "Ben.Web.Website.Library", "Manage", "Events", "OrgEventBookings.razor"));

    private static string Help()
        => File.ReadAllText(Path.Combine(
            RepoRoot().FullName, "Ben.Web.Services", "Help", "Content", "organization-administration.md"));

    [Theory]
    [InlineData("InviteEventGuestAsync")]
    [InlineData("CreateEventBookingOnBehalfAsync")]
    public void The_bookings_board_calls_the_client_method(string method)
        => Assert.Contains(method, Board());

    [Theory]
    [InlineData("board-invite-guest")]
    [InlineData("board-book-on-behalf")]
    public void The_bookings_board_offers_the_control(string id)
        => Assert.Contains(id, Board());

    /// <summary>
    /// Both dialogs have somewhere to type the address, or the button leads nowhere.
    /// </summary>
    [Theory]
    [InlineData("invite-guest-email")]
    [InlineData("invite-guest-send")]
    [InlineData("behalf-email")]
    [InlineData("behalf-create")]
    public void The_dialog_has_its_field_and_its_commit(string id)
        => Assert.Contains(id, Board());

    /// <summary>
    /// An invitation reserves nothing, and the screen says so. The help makes this its own
    /// paragraph — "Nothing is held until they click" — because a host who believes a room is held
    /// has been misled by the software, not by the guest.
    /// </summary>
    [Fact]
    public void The_invite_dialog_says_nothing_is_held_yet()
    {
        var board = Board();

        Assert.Contains("Nothing is held until they click", board);
        // And the help still says it, so the two cannot drift apart silently.
        Assert.Contains("Nothing is held until they click", Help());
    }

    /// <summary>
    /// The invite asks only for a day pass, as the help states, and the dialog says so rather than
    /// leaving a host to discover it when somebody turns up expecting a bed.
    /// </summary>
    [Fact]
    public void The_on_behalf_dialog_says_it_is_a_day_pass()
        => Assert.Contains("day pass", Board(), StringComparison.OrdinalIgnoreCase);
}
