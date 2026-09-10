using System.Net;
using System.Net.Http.Json;
using Ben.Data.WebApi.Client.Auth;
using Ben.Web.Services.WebApi;

namespace Ben.Data.WebApi.Client.External;

/// <summary>
/// Everything after Apple's own sheet closes: handing the identity token to our API and doing
/// whatever it answers.
/// </summary>
/// <remarks>
/// <para>Unlike the Microsoft path, this ends with one of <b>our</b> sessions. The endpoint signs
/// the person in under our own bearer scheme and returns a body byte-identical to <c>/login</c>'s,
/// so there is nothing external about the session that comes out of it.</para>
///
/// <para>Three outcomes, and the middle one is the interesting one: Apple authenticated somebody
/// we have never seen, and the account cannot be created until they choose a display name and a
/// handle. Apple gives a real name on the <b>first</b> authorization only, so a suggestion that
/// arrives must be used then or it is gone for good.</para>
/// </remarks>
public sealed class AppleSignInClient
{
    private readonly HttpClient _http;
    private readonly SessionStore _store;

    public AppleSignInClient(HttpClient http, SessionStore store)
    {
        _http = http;
        _store = store;
    }

    /// <param name="NeedsProfile">Apple vouched for them; the account still has to be created.</param>
    /// <param name="Reason">A sentence to show, when it did not work.</param>
    public readonly record struct Outcome(
        bool Succeeded,
        AppleNeedsProfileResponse? NeedsProfile = null,
        string? Reason = null)
    {
        public bool RequiresProfile => NeedsProfile is not null;

        public static Outcome Ok() => new(true);
        public static Outcome Profile(AppleNeedsProfileResponse p) => new(false, p);
        public static Outcome Failed(string reason) => new(false, null, reason);
    }

    /// <summary>
    /// Presents Apple's identity token, and signs in when the server accepts it.
    /// </summary>
    /// <param name="displayName">
    /// Only present on a first authorization, and only worth sending then. Apple never gives it
    /// again, so an account created without it has no name it did not invent.
    /// </param>
    /// <param name="handle">The chosen @name, on the second attempt after a needs-profile answer.</param>
    public async Task<Outcome> SignInAsync(
        string identityToken,
        string? displayName = null,
        string? handle = null,
        CancellationToken token = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(
                "api/auth/apple", new AppleSignInRequest(identityToken, displayName, handle), token);
        }
        catch (HttpRequestException)
        {
            return Outcome.Failed("Couldn't reach the server to finish signing in.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                WebApiTokenResponse? body;
                try
                {
                    body = await response.Content.ReadFromJsonAsync<WebApiTokenResponse>(cancellationToken: token);
                }
                catch (System.Text.Json.JsonException)
                {
                    return Outcome.Failed("The server's answer couldn't be read.");
                }

                if (body is null || string.IsNullOrWhiteSpace(body.AccessToken))
                    return Outcome.Failed("The server answered without a session.");

                await _store.AdoptExternalSignInAsync(body, token);
                return Outcome.Ok();
            }

            // 409 carries what is missing, plus whatever Apple was willing to tell us about them.
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                AppleNeedsProfileResponse? profile;
                try
                {
                    profile = await response.Content.ReadFromJsonAsync<AppleNeedsProfileResponse>(cancellationToken: token);
                }
                catch (System.Text.Json.JsonException)
                {
                    return Outcome.Failed("The server's answer couldn't be read.");
                }

                return profile is null
                    ? Outcome.Failed("The server needs more details, but didn't say which.")
                    : Outcome.Profile(profile);
            }

            // This endpoint writes its refusals as sentences meant for a person, so prefer them
            // over anything invented here. Verified against the running API: a token it cannot
            // validate comes back 401 with "That Apple sign-in couldn't be verified. Try again."
            var prose = await ReadProseAsync(response, token);

            return response.StatusCode switch
            {
                // The server has no Apple audience configured, so this door was never open here.
                // Distinct from a rejected token: nothing the person does will change it, and
                // "try again" would have them retype something that cannot work.
                HttpStatusCode.ServiceUnavailable =>
                    Outcome.Failed(prose ?? "Signing in with Apple isn't switched on for this server."),

                HttpStatusCode.Unauthorized =>
                    Outcome.Failed(prose ?? "Apple's sign-in couldn't be verified. Try again."),

                HttpStatusCode.TooManyRequests =>
                    Outcome.Failed("Too many attempts. Give it a minute before trying again."),

                _ => Outcome.Failed(prose ?? $"The server answered {(int)response.StatusCode} ({response.ReasonPhrase})."),
            };
        }
    }

    /// <summary>
    /// Claims an account that already exists here for this Apple identity, and signs in.
    /// </summary>
    /// <param name="email">
    /// The account being claimed. NOT necessarily the address Apple gave — that is the entire
    /// point of this call.
    /// </param>
    /// <remarks>
    /// <para>Sign-in only joins an Apple identity to an existing account when Apple's verified
    /// email happens to match one. A Hide My Email relay address never will, and plenty of people
    /// have an Apple ID at one address and an account at another. Without this, both end at
    /// "create an account" and quietly get a SECOND one holding none of their history.</para>
    ///
    /// <para>Succeeds into a full session, unlike the Microsoft equivalent: an Apple identity token
    /// is not a credential the API accepts on ordinary requests, so there is no standing session to
    /// fall back on.</para>
    /// </remarks>
    public async Task<Outcome> LinkAsync(
        string identityToken, string email, string password, CancellationToken token = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(
                "api/auth/apple/link", new AppleLinkRequest(identityToken, email, password), token);
        }
        catch (HttpRequestException)
        {
            return Outcome.Failed("Couldn't reach the server. Nothing was linked.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                WebApiTokenResponse? body;
                try
                {
                    body = await response.Content.ReadFromJsonAsync<WebApiTokenResponse>(cancellationToken: token);
                }
                catch (System.Text.Json.JsonException)
                {
                    return Outcome.Failed("The server's answer couldn't be read.");
                }

                if (body is null || string.IsNullOrWhiteSpace(body.AccessToken))
                    return Outcome.Failed("The server answered without a session.");

                await _store.AdoptExternalSignInAsync(body, token);
                return Outcome.Ok();
            }

            var prose = await ReadProseAsync(response, token);

            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized =>
                    Outcome.Failed(prose ?? "That email address and password don't match an account."),

                HttpStatusCode.Conflict =>
                    Outcome.Failed(await ReadConflictAsync(response, token)
                                   ?? "That Apple account is already linked to a different account here."),

                HttpStatusCode.ServiceUnavailable =>
                    Outcome.Failed(prose ?? "Signing in with Apple isn't switched on for this server."),

                HttpStatusCode.TooManyRequests =>
                    Outcome.Failed("Too many attempts. Give it a minute before trying again."),

                _ => Outcome.Failed(prose ?? $"The server answered {(int)response.StatusCode} ({response.ReasonPhrase})."),
            };
        }
    }

    /// <summary>The conflict body here is an object with a message, not a sentence.</summary>
    private static async Task<string?> ReadConflictAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ConflictBody>(cancellationToken: token);
            return string.IsNullOrWhiteSpace(body?.Message) ? null : body!.Message;
        }
        catch
        {
            return null;
        }
    }

    private sealed record ConflictBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("message")] string? Message);

    /// <summary>
    /// The server's own sentence, when it wrote one rather than a machine-shaped body.
    /// </summary>
    /// <remarks>
    /// Same rule the rest of this client uses: a refusal we wrote is a sentence, and a body that
    /// starts with a brace or an angle bracket is a problem-details blob or an error page. Showing
    /// either of those to a person is worse than saying nothing useful.
    /// </remarks>
    private static async Task<string?> ReadProseAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(token);
            return ApiResponseMapper.LooksLikeProse(body) ? body.Trim('"', ' ', '\n') : null;
        }
        catch
        {
            return null;
        }
    }
}
