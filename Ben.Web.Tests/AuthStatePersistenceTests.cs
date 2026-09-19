using Ben.Web.Services;
using Xunit;

namespace Ben.Web.Tests;

/// <summary>
/// Whether the browser keeps a copy of the session.
/// </summary>
/// <remarks>
/// Four cases for a rule with two inputs. The one that was wrong in production — an Entra session
/// that is impersonating — had no coverage at all, because the Playwright test that exercises
/// impersonation signs in with a password and so only ever took the other branch.
/// </remarks>
public class AuthStatePersistenceTests
{
    [Fact]
    public void A_password_session_is_remembered()
        => Assert.Equal(AuthStatePersistence.Action.Persist,
                        AuthStatePersistence.For(isEntraSession: false, isImpersonating: false));

    [Fact]
    public void A_password_session_that_is_impersonating_is_remembered()
        => Assert.Equal(AuthStatePersistence.Action.Persist,
                        AuthStatePersistence.For(isEntraSession: false, isImpersonating: true));

    /// <summary>The original rule, and still right: the OIDC cookie carries this one.</summary>
    [Fact]
    public void An_entra_session_is_left_to_its_cookie()
        => Assert.Equal(AuthStatePersistence.Action.Clear,
                        AuthStatePersistence.For(isEntraSession: true, isImpersonating: false));

    /// <summary>
    /// IH-01. The cookie describes the ADMIN, not the person being viewed as — so if this is not
    /// written down, a reload silently restores full SuperAdmin privileges while the admin
    /// believes they are still viewing as a member.
    /// </summary>
    [Fact]
    public void An_entra_session_that_is_impersonating_is_remembered()
        => Assert.Equal(AuthStatePersistence.Action.Persist,
                        AuthStatePersistence.For(isEntraSession: true, isImpersonating: true));
}
