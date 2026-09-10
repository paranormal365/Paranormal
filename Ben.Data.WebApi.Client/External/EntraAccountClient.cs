using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Ben.Data.WebApi.Client.External;

/// <summary>Creating or attaching a local account for somebody Microsoft has already vouched for.</summary>
/// <remarks>
/// Both endpoints are authorised by the Microsoft token itself, and the server reads the identity
/// from that token's claims rather than from anything sent in the body. That is deliberate on the
/// server's part: trusting a caller-supplied identifier there once allowed an account to be claimed
/// by somebody who did not hold it.
/// </remarks>
public sealed class EntraAccountClient
{
    private readonly HttpClient _http;

    public EntraAccountClient(HttpClient http) => _http = http;

    /// <param name="Reason">A sentence to show, when it did not work.</param>
    /// <param name="ShouldLinkInstead">
    /// The address already has an account here, so creating a second one is not the answer — they
    /// need to prove they own the existing one instead.
    /// </param>
    /// <param name="RequiresTwoFactor">
    /// The password was right and the account has a second factor. NOT a failure to report as one.
    /// </param>
    public readonly record struct Result(
        bool Succeeded, string? Reason, bool ShouldLinkInstead = false, bool RequiresTwoFactor = false)
    {
        public static Result Ok() => new(true, null);
        public static Result Failed(string reason) => new(false, reason);
        public static Result LinkInstead(string reason) => new(false, reason, ShouldLinkInstead: true);
        public static Result NeedsCode(string reason) => new(false, reason, RequiresTwoFactor: true);
    }

    /// <summary>Creates a new local account for the Microsoft identity the caller's token carries.</summary>
    public async Task<Result> RegisterAsync(string displayName, CancellationToken token = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(
                "api/auth/entra/register", new EntraRegisterRequest(displayName), token);

            if (response.IsSuccessStatusCode) return Result.Ok();

            var message = await ReadMessageAsync(response, token);

            // 409 is not a failure to report as one: it means an account with that address already
            // exists, and the person simply needs the other door. Saying "couldn't create your
            // account" would leave them stuck in front of the wrong form.
            return response.StatusCode == HttpStatusCode.Conflict
                ? Result.LinkInstead(message ?? "An account with this email address already exists. Sign in to it once to link them.")
                : Result.Failed(message ?? Unreadable(response));
        }
        catch (HttpRequestException) { return Result.Failed("Couldn't reach the server. Nothing was created."); }
        catch (System.Text.Json.JsonException) { return Result.Failed("The server's answer couldn't be read."); }
    }

    /// <summary>
    /// Attaches the caller's Microsoft identity to an account that already exists here.
    /// </summary>
    /// <remarks>
    /// The password is what proves the existing account belongs to them. Holding a valid Microsoft
    /// token is not enough on its own, and the server is explicit about why: it would let anybody
    /// with any Microsoft account attach themselves to any address they could name.
    /// </remarks>
    public async Task<Result> LinkAsync(
        string email,
        string password,
        string? twoFactorCode = null,
        string? recoveryCode = null,
        CancellationToken token = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(
                "api/auth/entra/link",
                new EntraLinkRequest(email, password, twoFactorCode, recoveryCode),
                token);

            if (response.IsSuccessStatusCode) return Result.Ok();

            // A 401 is /login's problem-detail, the same four words every door now uses, so the
            // mapping is the one the password form uses rather than a second copy. Linking attaches
            // a Microsoft identity permanently and it signs in on its own afterwards, so a link
            // without the second factor would remove it for good - which is why the code case is
            // told apart from a wrong password.
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var detail = (await ReadRefusalAsync(response, token))?.Detail;
                var failure = Ben.Web.Services.WebApi.LoginFailureMapping.From(
                    new Ben.Web.Services.WebApi.LoginAttempt(null, 401, detail));

                return failure switch
                {
                    Ben.Web.Services.WebApi.LoginFailure.RequiresTwoFactor =>
                        Result.NeedsCode("That account uses two-step verification. Enter the code from your authenticator app."),
                    Ben.Web.Services.WebApi.LoginFailure.EmailNotConfirmed =>
                        Result.Failed("That account's email address hasn't been confirmed yet. Use the link we sent, or ask for another."),
                    Ben.Web.Services.WebApi.LoginFailure.LockedOut =>
                        Result.Failed("That account is locked after too many attempts. Waiting is the only thing that helps."),
                    Ben.Web.Services.WebApi.LoginFailure.InvalidCredentials =>
                        Result.Failed("That email address and password don't match an account."),
                    _ => Result.Failed("That sign-in was refused without a reason. Try again - and if it keeps happening, it isn't your password."),
                };
            }

            var message = await ReadMessageAsync(response, token);
            return response.StatusCode switch
            {
                HttpStatusCode.Conflict => Result.Failed(message ?? "That Microsoft account is already linked to a different account here."),
                _ => Result.Failed(message ?? Unreadable(response)),
            };
        }
        catch (HttpRequestException) { return Result.Failed("Couldn't reach the server. Nothing was linked."); }
        catch (System.Text.Json.JsonException) { return Result.Failed("The server's answer couldn't be read."); }
    }

    /// <summary>
    /// Reads the server's own sentence out of the several shapes these endpoints use.
    /// </summary>
    /// <remarks>
    /// They answer with <c>{ "message": ... }</c> in some branches and <c>{ "errors": [...] }</c>
    /// in others. Reading only one shape means half the refusals arrive as a status code, and a
    /// status code tells somebody nothing about what to change.
    /// </remarks>
    private static async Task<string?> ReadMessageAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ServerMessage>(cancellationToken: token);
            if (body is null) return null;

            if (!string.IsNullOrWhiteSpace(body.Message)) return body.Message;

            return body.Errors is { Length: > 0 } ? string.Join(" ", body.Errors) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads a refusal, which carries a flag as well as a sentence.</summary>
    private static async Task<ServerMessage?> ReadRefusalAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ServerMessage>(cancellationToken: token);
        }
        catch
        {
            return null;
        }
    }

    private static string Unreadable(HttpResponseMessage response) =>
        $"The server answered {(int)response.StatusCode} ({response.ReasonPhrase}).";

    private sealed record ServerMessage(
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("errors")] string[]? Errors,
        [property: JsonPropertyName("detail")] string? Detail = null);

    private sealed record EntraRegisterRequest(
        [property: JsonPropertyName("displayName")] string DisplayName);

    private sealed record EntraLinkRequest(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("twoFactorCode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? TwoFactorCode = null,
        [property: JsonPropertyName("twoFactorRecoveryCode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? TwoFactorRecoveryCode = null);
}
