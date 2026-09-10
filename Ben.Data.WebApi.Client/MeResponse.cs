namespace Ben.Web.Services.WebApi;

/// <summary>
/// What <c>GET api/me</c> answers: who the bearer token belongs to and what site-wide roles they
/// hold.
/// </summary>
/// <remarks>
/// <para>This is the only way a client learns its own roles. Identity's bearer tokens are opaque
/// and data-protected, not JWTs, so there is nothing in the token to decode — a client that tries
/// gets nothing and silently believes the person is an ordinary member.</para>
///
/// <para>Ask again after every token acquisition, including a refresh that followed an
/// impersonation change. Matches <c>MeController.MeResponse</c> on the server.</para>
/// </remarks>
public sealed record MeResponse(
    Guid UserId,
    string Email,
    bool IsSuperAdmin,
    bool IsAdmin,
    bool IsModerator = false);
