namespace Ben.Video.Sidecar.Lifetime;

/// <summary>
/// When the Windows build shows its pairing window. Kept apart from the window itself so the rules
/// are tested on every platform; the window is Windows-only (Desktop/).
/// </summary>
public static class PairingWindowPolicy
{
    /// <summary>The scheme the editor's Turn on opens. Windows passes the whole URI as an argument.</summary>
    public const string ProtocolScheme = "benvideo-sidecar:";

    /// <summary>Started by the editor's Turn on link rather than by a person.</summary>
    public static bool IsProtocolLaunch(IEnumerable<string> args)
        => args.Any(a => a.StartsWith(ProtocolScheme, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether a starting sidecar opens the window: only while no browser has paired with it yet,
    /// however it was started. Once paired it runs with nothing on screen.
    /// </summary>
    public static bool ShowOnStart(bool awaitingFirstPairing) => awaitingFirstPairing;

    /// <summary>
    /// Whether a second start, which finds a copy already running and ends, asks that copy to show
    /// its window. A person starting it again from the Start menu wants to see something; the
    /// editor's link only arrives here by racing a start already under way.
    /// </summary>
    public static bool SecondStartShowsWindow(IEnumerable<string> args) => !IsProtocolLaunch(args);
}
