using System.Net;
using System.Net.Http.Json;
using System.Web;
using Ben.Web.Services.WebApi;

namespace Ben.Data.WebApi.Client.Auth;

/// <summary>
/// The self-service account endpoints: creating one, confirming the address, and checking a handle
/// before somebody commits to it.
/// </summary>
/// <remarks>
/// Separate from <see cref="IWebApiIdentityClient"/>, which wraps the endpoints ASP.NET Identity
/// maps for itself. These are ours, and they behave differently — most importantly, registration
/// answers a refusal as a JSON document rather than as a sentence.
/// </remarks>
public sealed class AccountClient
{
    private readonly HttpClient _http;

    public AccountClient(HttpClient http) => _http = http;

    /// <summary>
    /// Creates an account. Answers the server's own message whether it worked or not.
    /// </summary>
    /// <remarks>
    /// Reads the body on failure as well as success. The generic refusal handling would throw away
    /// "That name is already taken" because the body is JSON rather than prose, and substitute a
    /// paraphrase of the status code — which tells the person nothing about what to change.
    /// </remarks>
    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken token = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync("api/account/register", request, token);

            var body = await response.Content.ReadFromJsonAsync<RegisterResponse>(cancellationToken: token);
            if (body is not null) return body;

            return new RegisterResponse(false, Unreadable(response), null);
        }
        catch (HttpRequestException)
        {
            return new RegisterResponse(false, "Couldn't reach the server. Check your connection and try again.", null);
        }
        catch (System.Text.Json.JsonException)
        {
            return new RegisterResponse(false, "The server's answer couldn't be read. Nothing was created.", null);
        }
    }

    /// <summary>
    /// Whether a handle can be taken.
    /// </summary>
    /// <remarks>
    /// Null when the question could not be asked. A caller must not render that as available: a
    /// green tick that means "we could not check" invites somebody to commit to a name that is
    /// already gone, and handles are permanent.
    /// </remarks>
    public async Task<HandleAvailabilityResponse?> CheckHandleAsync(string handle, CancellationToken token = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<HandleAvailabilityResponse>(
                $"api/account/handle-available?handle={HttpUtility.UrlEncode(handle)}", token);
        }
        catch (HttpRequestException) { return null; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>Confirms an address with the identifier and code from the emailed link.</summary>
    public async Task<ConfirmEmailResponse> ConfirmEmailAsync(
        Guid userId, string code, CancellationToken token = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(
                "api/account/confirm-email", new ConfirmEmailRequest(userId, code), token);

            var body = await response.Content.ReadFromJsonAsync<ConfirmEmailResponse>(cancellationToken: token);
            if (body is not null) return body;

            return new ConfirmEmailResponse(false, Unreadable(response));
        }
        catch (HttpRequestException)
        {
            // A link that was never delivered to the server is not a spent link, and the difference
            // decides whether "try again" is useful advice.
            return new ConfirmEmailResponse(false, "Couldn't reach the server. Your link is still good — try again.");
        }
        catch (System.Text.Json.JsonException)
        {
            return new ConfirmEmailResponse(false, "The server's answer couldn't be read.");
        }
    }

    /// <summary>Asks for another confirmation email. The server rate-limits this to one a minute.</summary>
    public async Task<string> ResendConfirmationAsync(string email, CancellationToken token = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(
                "api/account/resend-confirmation", new ResendConfirmationRequest(email), token);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return "Another email was sent very recently. Give it a minute before asking again.";

            var body = await response.Content.ReadFromJsonAsync<ResendConfirmationResponse>(cancellationToken: token);
            return body?.Message ?? Unreadable(response);
        }
        catch (HttpRequestException)
        {
            return "Couldn't reach the server. Nothing was sent.";
        }
        catch (System.Text.Json.JsonException)
        {
            return "The server's answer couldn't be read.";
        }
    }

    private static string Unreadable(HttpResponseMessage response) =>
        $"The server answered {(int)response.StatusCode} ({response.ReasonPhrase}).";
}
