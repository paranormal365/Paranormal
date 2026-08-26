namespace Ben.Web.Services;

/// <summary>
/// Whether the browser should keep a copy of the signed-in session, and why.
/// </summary>
/// <remarks>
/// <para>This lived as two lines inside <c>MainLayout</c> and got one case wrong for months. It is
/// a rule, not an implementation detail, so it is written down here where it can be read and
/// tested.</para>
///
/// <para><b>IH-01, Ben's production sweep of 2026-08-26.</b> An Entra session is carried by the
/// OIDC cookie, so persisting its token buys nothing and risks restoring a stale one — hence the
/// original rule, "never persist an Entra session". But an admin who is IMPERSONATING somebody is
/// no longer the identity that cookie describes. Nothing was written down, so a reload restored
/// nothing, the Entra bridge re-established the SuperAdmin, and the page came back with the badge
/// gone and Administration restored, saying nothing at all. An admin who reloads mid-check
/// believes they are still viewing as a member and keeps clicking — with full privileges, against
/// real records.</para>
///
/// <para>The existing Playwright coverage could not see it: it signs in with a password, which
/// takes the other branch. A rule with two inputs deserves four tests, not one path exercised by
/// accident.</para>
/// </remarks>
public static class AuthStatePersistence
{
    /// <summary>What the layout should do with the browser's copy of the session.</summary>
    public enum Action
    {
        /// <summary>Write the current session down, so a reload restores it.</summary>
        Persist,

        /// <summary>
        /// Write nothing, and remove anything already stored.
        /// </summary>
        /// <remarks>
        /// The removal is the half that is easy to forget: an admin who impersonates from an
        /// Entra session and then stops must not leave a record behind, or the next reload would
        /// silently put them back INTO the impersonation they had just ended.
        /// </remarks>
        Clear,
    }

    /// <summary>
    /// Decides whether this session is the browser's to remember.
    /// </summary>
    /// <param name="isEntraSession">The session came from Microsoft sign-in, carried by a cookie.</param>
    /// <param name="isImpersonating">The admin is currently viewing as somebody else.</param>
    public static Action For(bool isEntraSession, bool isImpersonating)
        // Impersonation always wins: it is the one identity no cookie can restore.
        => !isEntraSession || isImpersonating ? Action.Persist : Action.Clear;
}
