namespace Ben.Web.Website.Library.Manage;

/// <summary>
/// Where each first-run answer sends a new person (item 166 W2) — a plain map, tested in
/// xUnit, so the wizard's most consequential decision never hides inside a render tree.
/// </summary>
public static class OnboardingRouting
{
    public enum Intent
    {
        /// <summary>"Something is happening at my place" — straight to the request door.</summary>
        RequestInvestigation = 1,
        /// <summary>"I want to investigate with a group" — the group finder.</summary>
        JoinGroup = 2,
        /// <summary>"I run a group" — the founder's wizard (item 166 W1).</summary>
        RunGroup = 3,
        /// <summary>"Just looking around" — the front door.</summary>
        JustLooking = 4,
    }

    public static string DestinationFor(Intent intent) => intent switch
    {
        Intent.RequestInvestigation => "/my-requests/new",
        Intent.JoinGroup            => "/find",
        Intent.RunGroup             => "/organizations/new",
        _                           => "/",
    };
}
