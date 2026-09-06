using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Ben.Wasm.Video.Services;

/// <summary>
/// What arrived in the editor's own URL: a handoff code, a project to open, or neither.
/// </summary>
/// <param name="Code">The one-use code, or null.</param>
/// <param name="ProjectId">The server project to open once signed in, or null.</param>
public readonly record struct EditorHandoff(string? Code, Guid? ProjectId)
{
    /// <summary>Whether the URL carried anything worth acting on.</summary>
    public bool IsPresent => Code is not null || ProjectId is not null;

    /// <summary>Nothing arrived; an ordinary visit.</summary>
    public static EditorHandoff None => new(null, null);

    /// <summary>
    /// Reads a handoff out of a URL fragment.
    /// </summary>
    /// <param name="fragment">
    /// The fragment, with or without its leading <c>#</c>, as it appears in the address bar.
    /// </param>
    /// <remarks>
    /// The fragment and not the query string, because browsers never send a fragment to a server:
    /// the code stays out of access logs, out of <c>Referer</c>, and out of anything in between.
    /// </remarks>
    public static EditorHandoff Parse(string? fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment)) return None;

        var hash = fragment.IndexOf('#');
        if (hash >= 0) fragment = fragment[(hash + 1)..];

        string? code    = null;
        Guid?   project = null;

        foreach (var part in fragment.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.Split('=', 2);
            if (split.Length != 2) continue;

            // A half-escaped fragment comes back as it was typed rather than throwing, so a
            // mangled paste travels on and is refused by the server like any other wrong code.
            var value = Uri.UnescapeDataString(split[1]).Trim();

            if (value.Length == 0) continue;

            switch (split[0].Trim().ToLowerInvariant())
            {
                case "handoff":
                    code = value;
                    break;

                // A project id that will not parse is simply not a project id. The sign-in still
                // stands: signing somebody in and opening nothing beats refusing both.
                case "project" when Guid.TryParse(value, out var id):
                    project = id;
                    break;
            }
        }

        return new(code, project);
    }
}

/// <summary>
/// Signs this host in from a code the site put in the link.
/// </summary>
/// <remarks>
/// <para>Somebody already signed in on the site who followed the link to the standalone editor
/// arrived signed out, and was asked for their password at a second door. The site now asks the
/// API for a one-minute, one-use code and puts it in the link's fragment; this exchanges it for
/// tokens of this host's own — never the site's, which stay on the site's server where they
/// belong (2026-09-05 audit, phase 12).</para>
///
/// <para><b>The fragment is erased either way.</b> Whether the exchange succeeds, fails, or was
/// never possible, the code comes out of the address bar, so a reload does not replay it, a
/// screenshot does not contain it and a copied URL carries nothing. It is dead within the minute
/// regardless; this is about not leaving it lying around.</para>
///
/// <para>Nothing here throws. A handoff is a convenience on top of a sign-in page that still
/// works, so every failure ends with the editor open and a sign-in link visible.</para>
/// </remarks>
public sealed class EditorHandoffService(
    HttpClient http,
    TokenStore tokens,
    NavigationManager navigation,
    IJSRuntime js)
{
    /// <summary>
    /// Reads the URL, exchanges any code it carries, and clears the fragment.
    /// </summary>
    /// <returns>
    /// The project the link asked for, when it named one — whether or not the sign-in worked, so
    /// somebody whose code expired still lands on that project once they sign in themselves.
    /// </returns>
    public async Task<Guid?> ApplyAsync(CancellationToken ct = default)
    {
        var handoff = EditorHandoff.Parse(navigation.Uri);

        if (!handoff.IsPresent) return null;

        await ClearFragmentAsync();

        if (handoff.Code is not null) await ExchangeAsync(handoff.Code, ct);

        return handoff.ProjectId;
    }

    /// <summary>Exchanges a code for tokens.</summary>
    /// <returns>False for a refused code, an unreachable API, or an answer that made no sense.</returns>
    public async Task<bool> ExchangeAsync(string code, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync(
                "/api/auth/editor-handoff/exchange", new { code }, ct);

            if (!response.IsSuccessStatusCode) return false;

            var minted = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);

            if (minted?.AccessToken is null || minted.RefreshToken is null) return false;

            await tokens.SetAsync(minted.AccessToken, minted.RefreshToken, minted.ExpiresIn);
            return true;
        }
        catch
        {
            // An unreachable API, a refused code and a mangled body all mean one thing to the
            // person: they are not signed in yet, and there is a link that says so.
            return false;
        }
    }

    /// <summary>
    /// Takes the handoff out of the address bar without reloading the app.
    /// </summary>
    /// <remarks>
    /// <para><c>history.replaceState</c> and not <c>NavigationManager.NavigateTo</c>: routing
    /// treats a URL that differs only by its fragment as the URL it is already on, so the navigate
    /// returned having changed nothing and the code stayed in the address bar. Found by opening
    /// the page.</para>
    ///
    /// <para>The global path is called directly rather than through a script of our own, because
    /// the editor's no-eval posture rules out the string form and this needs no module.
    /// <c>replaceState</c> rather than <c>pushState</c>, so Back does not return to the URL that
    /// still has the code in it.</para>
    /// </remarks>
    private async Task ClearFragmentAsync()
    {
        try
        {
            var uri = new Uri(navigation.Uri);

            await js.InvokeVoidAsync(
                "history.replaceState", null, "", uri.GetLeftPart(UriPartial.Query));
        }
        catch
        {
            // A URL that cannot be rewritten is cosmetic: the code is single-use and already spent.
        }
    }

    /// <summary>Identity's own <c>/login</c> body, which the exchange writes verbatim.</summary>
    private sealed record TokenResponse(string? AccessToken, string? RefreshToken, int ExpiresIn);
}
