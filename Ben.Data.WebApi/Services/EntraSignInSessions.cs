namespace Ben.Data.WebApi.Services;

/// <summary>
/// When an Entra request counts as somebody arriving, rather than as somebody still here.
/// </summary>
/// <remarks>
/// <para><b>Entra has no moment that is "the sign-in".</b> A password sign-in, Sign in with Apple
/// and the editor handoff each have one instant where a session is minted, and each writes one
/// row there. An Entra session is a bearer token validated on every single request: the person
/// signed in to Microsoft, possibly days ago, and the token simply keeps arriving. Until now the
/// honest answer was to record nothing and say so, which is what the dashboard did.</para>
///
/// <para><b>Ben chose a visit, 2026-09-19:</b> "I want to see who is logging in, where and how
/// often as close as possible... but not needed every time they change a page... more like true
/// visits as close as possible." So the first request in a twelve-hour window is one arrival and
/// everything after it is the same visit. Somebody who works a morning and comes back in the
/// evening counts twice, which is what a password user's two sign-ins would also count.</para>
///
/// <para><b>Why not the token.</b> Deduplicating on the token's own id would be precise about
/// something nobody asked: Entra refreshes tokens roughly hourly in the background, so a person
/// working one day would show eight arrivals where a password user shows one, and the count
/// column would quietly mean two different things in two different rows.</para>
///
/// <para>Pure, and with the clock passed in, so the window can be tested without waiting twelve
/// hours or pretending to.</para>
/// </remarks>
public static class EntraSignInSessions
{
    /// <summary>
    /// How long one arrival covers.
    /// </summary>
    /// <remarks>
    /// Twelve hours, so a working day is one visit and an evening is another. Long enough that a
    /// laptop left open all afternoon is not counted again, short enough that "how often" still
    /// means something.
    /// </remarks>
    public static readonly TimeSpan Visit = TimeSpan.FromHours(12);

    /// <summary>
    /// Whether this request begins a new visit.
    /// </summary>
    /// <param name="lastRecordedUtc">
    /// When this person's last Entra arrival was recorded, or null when there has never been one.
    /// </param>
    /// <param name="nowUtc">Passed in rather than read, so the rule keeps no clock of its own.</param>
    public static bool IsANewVisit(DateTime? lastRecordedUtc, DateTime nowUtc)
        => lastRecordedUtc is not { } last || nowUtc - last >= Visit;
}
