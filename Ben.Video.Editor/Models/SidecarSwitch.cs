namespace Ben.Video.Editor.Models;

/// <summary>What happened when the person turned the sidecar off.</summary>
public enum SidecarStopOutcome
{
    /// <summary>It is stopping.</summary>
    Stopped,

    /// <summary>Refused: an export or render is still running, and the person should be told
    /// before it is thrown away.</summary>
    WorkInProgress,

    /// <summary>There was nothing connected to stop.</summary>
    NotConnected,

    /// <summary>It was asked and something went wrong - the process may or may not still be there.</summary>
    Failed,
}

/// <summary>
/// The rules behind the on/off switch, kept away from anything that needs a browser.
/// </summary>
/// <remarks>
/// <para><b>Turning it on is not symmetrical with turning it off.</b> Off is a request to a program
/// that is already listening. On cannot be: a web page has no way to start a program, so the editor
/// opens a registered <c>benvideo-sidecar:</c> link and the operating system starts it. That only
/// works where something registered the scheme - the Windows installer does, and the Store package
/// declares it in its manifest - so the switch is only offered where it can actually do something.</para>
///
/// <para>macOS does not need the switch at all: there the sidecar is started on demand by launchd
/// when the editor connects, and stops itself when idle. Offering an on switch there would be
/// offering to do something that already happens.</para>
/// </remarks>
public static class SidecarSwitch
{
    /// <summary>The link that asks Windows to start the sidecar.</summary>
    /// <remarks>
    /// The scheme is registered by the installer (HKCU\Software\Classes) or by the MSIX manifest.
    /// The sidecar is handed this whole string as a command-line argument and ignores it.
    /// </remarks>
    public const string LaunchUri = "benvideo-sidecar:start";

    /// <summary>
    /// Whether to offer the switch at all, given what the browser says it is running on.
    /// </summary>
    /// <param name="browserPlatform">Whatever the browser reports - a user-agent platform string, or
    /// null when it will not say.</param>
    /// <remarks>
    /// Windows only, and deliberately not "anything that is not macOS": an unknown platform gets no
    /// switch, because a switch that does nothing when pressed is worse than no switch. The person
    /// on an unrecognised system still has every other way of starting the sidecar.
    /// </remarks>
    public static bool IsOfferedOn(string? browserPlatform) =>
        browserPlatform is not null
        && browserPlatform.Contains("Win", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What the sidecar's answer to a stop request means.
    /// </summary>
    /// <remarks>
    /// 409 is the interesting one: the sidecar refuses while a job is running rather than
    /// discarding it, and the person is asked again. Anything else that is not success is a
    /// failure and says so - including 401, because a token that no longer works is not a reason
    /// to pretend the process stopped.
    /// </remarks>
    public static SidecarStopOutcome ReadStopResponse(int httpStatus) => httpStatus switch
    {
        >= 200 and < 300 => SidecarStopOutcome.Stopped,
        409 => SidecarStopOutcome.WorkInProgress,
        _ => SidecarStopOutcome.Failed,
    };

    /// <summary>
    /// What to say to the person about <paramref name="outcome"/>.
    /// </summary>
    /// <param name="activeJobs">How many jobs the sidecar said were running, when it said so.</param>
    public static string Explain(SidecarStopOutcome outcome, int activeJobs) => outcome switch
    {
        SidecarStopOutcome.Stopped => "The sidecar has stopped. Turn it back on whenever you need it.",
        SidecarStopOutcome.WorkInProgress => activeJobs == 1
            ? "Something is still rendering. Turn it off again to stop anyway and lose that work."
            : $"{activeJobs} jobs are still running. Turn it off again to stop anyway and lose that work.",
        SidecarStopOutcome.NotConnected => "There is no sidecar connected to stop.",
        _ => "The sidecar could not be stopped. It may still be running.",
    };
}
