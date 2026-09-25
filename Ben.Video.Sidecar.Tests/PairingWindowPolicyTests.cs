using Ben.Video.Sidecar.Lifetime;
using Ben.Video.Sidecar.Security;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// When the Windows build shows its pairing window, and the on-disk state that decides it.
/// </summary>
/// <remarks>
/// Ben, 2026-09-25: a window with the code on the first launch, then nothing on screen from then
/// on - the editor turns it on and off. The window itself is Windows-only; these are the rules it
/// runs by, which hold everywhere.
/// </remarks>
public sealed class PairingWindowPolicyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("benvideo-window-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void A_new_install_is_waiting_for_its_first_pairing_until_a_code_is_exchanged()
    {
        var store = new PairingTokenStore(_dir);
        store.LoadOrCreate();
        Assert.True(store.AwaitingFirstPairing);
        Assert.True(PairingWindowPolicy.ShowOnStart(store.AwaitingFirstPairing));

        var paired = 0;
        store.Paired += () => paired++;
        Assert.NotNull(store.TryExchangeCode(store.BeginPairing()));

        Assert.False(store.AwaitingFirstPairing);
        Assert.Equal(1, paired);
        Assert.False(PairingWindowPolicy.ShowOnStart(store.AwaitingFirstPairing));
    }

    [Fact]
    public void Closing_the_sidecar_before_pairing_shows_the_window_again_next_time()
    {
        new PairingTokenStore(_dir).LoadOrCreate();

        var restarted = new PairingTokenStore(_dir);
        restarted.LoadOrCreate();

        Assert.False(restarted.WasJustCreated);
        Assert.True(restarted.AwaitingFirstPairing);
    }

    [Fact]
    public void An_install_from_before_the_window_existed_reads_as_paired()
    {
        // 1.1.2 and earlier wrote the token and nothing else; their browsers are already paired,
        // and they must not start greeting people with a window at every sign-in.
        File.WriteAllText(Path.Combine(_dir, "pairing-token"), "an-older-token");

        var store = new PairingTokenStore(_dir);
        store.LoadOrCreate();

        Assert.False(store.AwaitingFirstPairing);
    }

    [Fact]
    public void A_wrong_code_neither_pairs_nor_raises_Paired()
    {
        var store = new PairingTokenStore(_dir);
        store.LoadOrCreate();
        var paired = 0;
        store.Paired += () => paired++;
        var code = store.BeginPairing();

        Assert.Null(store.TryExchangeCode(code == "000000" ? "000001" : "000000"));
        Assert.True(store.AwaitingFirstPairing);
        Assert.Equal(0, paired);
    }

    [Fact]
    public void Resetting_the_token_unpairs_every_browser_so_the_window_returns()
    {
        var store = new PairingTokenStore(_dir);
        store.LoadOrCreate();
        store.TryExchangeCode(store.BeginPairing());
        Assert.False(store.AwaitingFirstPairing);

        store.Generate(); // --reset-token

        Assert.True(store.AwaitingFirstPairing);
    }

    [Fact]
    public void The_window_knows_when_its_code_stops_working()
    {
        var store = new PairingTokenStore(_dir);
        store.LoadOrCreate();

        var shown = store.BeginPairing();
        Assert.True(store.IsCurrentCode(shown));

        var fromThePairPage = store.BeginPairing(); // loading /pair mints a newer one
        Assert.False(store.IsCurrentCode(shown));
        Assert.True(store.IsCurrentCode(fromThePairPage));

        store.TryExchangeCode(fromThePairPage);     // and a used code is spent
        Assert.False(store.IsCurrentCode(fromThePairPage));
    }

    [Theory]
    [InlineData("benvideo-sidecar:start", true)]   // the editor's Turn on (SidecarSwitch.LaunchUri)
    [InlineData("BenVideo-Sidecar:", true)]         // schemes are case-insensitive
    [InlineData("--reset-token", false)]
    public void A_protocol_launch_is_recognised(string arg, bool expected)
        => Assert.Equal(expected, PairingWindowPolicy.IsProtocolLaunch([arg]));

    [Fact]
    public void Starting_it_again_by_hand_shows_the_window_but_the_editors_link_does_not()
    {
        Assert.True(PairingWindowPolicy.SecondStartShowsWindow([]));                        // Start menu
        Assert.False(PairingWindowPolicy.SecondStartShowsWindow(["benvideo-sidecar:start"])); // Turn on
    }
}
