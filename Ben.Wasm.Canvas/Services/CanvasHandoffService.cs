using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Ben.Wasm.Canvas.Services;

// Copied from Ben.Wasm.Video/Services/EditorHandoffService.cs. Differences: the fragment keys are
// doc, case and org (not project), ApplyAsync returns all of them, and the exchange path is relative so
// the /webapi mount survives.

/// <summary>
/// What arrived in the editor's own URL: a handoff code and the board, case and organisation to open.
/// </summary>
/// <param name="Code">The one-use code, or null.</param>
/// <param name="DocumentId">The server board to open once signed in, or null.</param>
/// <param name="CaseId">The case new boards belong to, or null.</param>
/// <param name="OrganizationId">The organisation that owns the case, or null.</param>
public readonly record struct CanvasHandoff(string? Code, Guid? DocumentId, Guid? CaseId, Guid? OrganizationId)
{
    /// <summary>Whether the URL carried anything worth acting on.</summary>
    public bool IsPresent => Code is not null || DocumentId is not null || CaseId is not null || OrganizationId is not null;

    /// <summary>Nothing arrived; an ordinary visit.</summary>
    public static CanvasHandoff None => new(null, null, null, null);

    /// <summary>
    /// Reads a handoff out of a URL fragment.
    /// </summary>
    /// <param name="fragment">The fragment, or a whole URL containing one, as it appears in the address bar.</param>
    /// <remarks>
    /// The fragment and not the query string, because browsers never send a fragment to a server: the
    /// code stays out of access logs, out of <c>Referer</c>, and out of anything in between.
    /// </remarks>
    public static CanvasHandoff Parse(string? fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment)) return None;

        var hash = fragment.IndexOf('#');
        if (hash >= 0) fragment = fragment[(hash + 1)..];

        string? code = null;
        Guid? doc = null, @case = null, org = null;

        foreach (var part in fragment.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.Split('=', 2);
            if (split.Length != 2) continue;

            // A half-escaped fragment comes back as it was typed rather than throwing, so a mangled
            // paste travels on and is refused by the server like any other wrong code.
            string value;
            try { value = Uri.UnescapeDataString(split[1]).Trim(); }
            catch (UriFormatException) { value = split[1].Trim(); }

            if (value.Length == 0) continue;

            switch (split[0].Trim().ToLowerInvariant())
            {
                case "handoff":
                    code = value;
                    break;

                // An id that will not parse is simply not an id. The sign-in still stands: signing
                // somebody in and opening nothing beats refusing both.
                case "doc" when Guid.TryParse(value, out var d):
                    doc = d;
                    break;

                case "case" when Guid.TryParse(value, out var c):
                    @case = c;
                    break;

                case "org" when Guid.TryParse(value, out var o):
                    org = o;
                    break;
            }
        }

        return new(code, doc, @case, org);
    }
}

/// <summary>
/// Signs this host in from a code the site put in the link, and reports what the link asked to open.
/// </summary>
/// <remarks>
/// <para>The site asks the API for a one-minute, one-use code and puts it in the link's fragment; this
/// exchanges it for tokens of this host's own. The site's tokens never leave the site.</para>
///
/// <para><b>The fragment is erased either way.</b> Whether the exchange succeeds, fails, or was never
/// possible, the code comes out of the address bar, so a reload does not replay it and a copied URL
/// carries nothing.</para>
///
/// <para>Nothing here throws. A handoff is a convenience on top of a sign-in page that still works.</para>
/// </remarks>
public sealed class CanvasHandoffService(
    HttpClient http,
    TokenStore tokens,
    NavigationManager navigation,
    IJSRuntime js)
{
    /// <summary>
    /// Reads the URL, exchanges any code it carries, and clears the fragment.
    /// </summary>
    /// <returns>
    /// What the link asked for - whether or not the sign-in worked, so somebody whose code expired still
    /// lands on that board once they sign in themselves.
    /// </returns>
    public async Task<CanvasHandoff> ApplyAsync(CancellationToken ct = default)
    {
        var handoff = CanvasHandoff.Parse(navigation.Uri);

        if (!handoff.IsPresent) return handoff;

        await ClearFragmentAsync();

        if (handoff.Code is not null) await ExchangeAsync(handoff.Code, ct);

        return handoff;
    }

    /// <summary>Exchanges a code for tokens.</summary>
    /// <returns>False for a refused code, an unreachable API, or an answer that made no sense.</returns>
    public async Task<bool> ExchangeAsync(string code, CancellationToken ct = default)
    {
        try
        {
            // Relative, on a BaseAddress that ends in "/" (Program.cs): a leading slash replaces the
            // /webapi base path and posts to the website instead of the API.
            var response = await http.PostAsJsonAsync(
                "api/auth/editor-handoff/exchange", new { code }, ct);

            if (!response.IsSuccessStatusCode) return false;

            var minted = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);

            if (minted?.AccessToken is null || minted.RefreshToken is null) return false;

            await tokens.SetAsync(minted.AccessToken, minted.RefreshToken, minted.ExpiresIn);
            return true;
        }
        catch
        {
            // An unreachable API, a refused code and a mangled body all mean one thing to the person:
            // they are not signed in yet, and there is a link that says so.
            return false;
        }
    }

    /// <summary>
    /// Takes the handoff out of the address bar without reloading the app.
    /// </summary>
    /// <remarks>
    /// <c>history.replaceState</c>, called as a direct global: routing treats a URL that differs only
    /// by its fragment as the URL it is already on, so NavigateTo would change nothing. No eval.
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
            // A URL that cannot be rewritten is cosmetic: the code is single-use.
        }
    }

    /// <summary>Identity's own login body, which the exchange writes verbatim.</summary>
    private sealed record TokenResponse(string? AccessToken, string? RefreshToken, int ExpiresIn);
}
