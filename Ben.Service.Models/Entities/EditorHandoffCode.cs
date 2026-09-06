namespace Ben.Service.Models.Entities;

/// <summary>
/// A one-minute, one-use code that signs the person holding it into the standalone video editor.
/// </summary>
/// <param name="Code">
/// Travels in a link's fragment, never its query string: browsers do not send a fragment to a
/// server, so it stays out of access logs and out of <c>Referer</c>.
/// </param>
/// <param name="ExpiresInSeconds">How long the code is worth anything, as the API reported it.</param>
/// <remarks>
/// The site holds a person's tokens in their circuit, where the browser cannot read them. This is
/// what crosses to the other origin instead — something that proves nothing on its own and is
/// worthless a minute later (phase 12).
/// </remarks>
public sealed record EditorHandoffCode(string Code, int ExpiresInSeconds);
