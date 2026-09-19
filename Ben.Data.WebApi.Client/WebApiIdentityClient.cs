using System.Net.Http.Json;

namespace Ben.Web.Services.WebApi;

public sealed class WebApiIdentityClient : IWebApiIdentityClient
{
    private readonly HttpClient _httpClient;

    public WebApiIdentityClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<WebApiTokenResponse?> LoginAsync(string email, string password, CancellationToken token = default)
        => (await TryLoginAsync(email, password, token: token)).Token;

    public async Task<LoginAttempt> TryLoginAsync(
        string email, string password,
        string? twoFactorCode = null, string? recoveryCode = null,
        CancellationToken token = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/login",
            new WebApiLoginRequest(email, password, twoFactorCode, recoveryCode),
            token);

        if (!response.IsSuccessStatusCode)
        {
            // The problem-detail carries the reason, and "RequiresTwoFactor" arrives as a 401 —
            // the same status as a wrong password. Reading it is what lets the sign-in page ask
            // for a code instead of telling somebody their password is wrong.
            return new LoginAttempt(
                null,
                (int)response.StatusCode,
                await ReadDetailAsync(response, token),
                ReadRetryAfter(response));
        }

        return new LoginAttempt(
            await response.Content.ReadFromJsonAsync<WebApiTokenResponse>(cancellationToken: token),
            (int)response.StatusCode);
    }

    /// <summary>
    /// How long the server said to wait, when it said so.
    /// </summary>
    /// <remarks>
    /// The header comes in two shapes and both are legal: a number of seconds, or an HTTP date.
    /// Ours sends seconds, but reading only that form would break silently behind any proxy that
    /// rewrites it. A date already in the past yields null rather than a negative wait.
    /// </remarks>
    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null) return null;

        if (retryAfter.Delta is { } delta)
            return delta > TimeSpan.Zero ? delta : null;

        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : null;
        }

        return null;
    }

    private static async Task<string?> ReadDetailAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetail>(cancellationToken: token);
            return problem?.Detail;
        }
        catch
        {
            // Not every refusal is a problem-details document — a rate-limit response, a proxy
            // page. No detail simply means no special case applies.
            return null;
        }
    }

    private sealed record ProblemDetail(string? Detail);

    public async Task<bool> ForgotPasswordAsync(string email, CancellationToken token = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/forgotPassword", new { email }, token);

        return response.IsSuccessStatusCode;
    }

    public async Task<string?> ResetPasswordAsync(
        string email, string resetCode, string newPassword, CancellationToken token = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/resetPassword", new { email, resetCode, newPassword }, token);

        if (response.IsSuccessStatusCode) return null;

        // Identity answers a bad code and a bad address identically (InvalidToken), on purpose —
        // and a policy violation with the policy's own sentences. Both are worth showing; the raw
        // validation-problem JSON is not.
        var detail = await ReadValidationDetailAsync(response, token);
        return detail ?? "That reset link is invalid or has expired. Request a new one.";
    }

    /// <summary>
    /// The sentences inside a 400, whichever of Identity's two problem shapes it used.
    /// </summary>
    /// <remarks>
    /// <c>/resetPassword</c> failures arrive as <c>HttpValidationProblemDetails</c> — errors keyed
    /// by code, values are the sentences — while other endpoints use plain problem-details with a
    /// <c>detail</c>. <c>InvalidToken</c>'s sentence ("Invalid token.") is unhelpfully terse, so
    /// it is rewritten; the password-policy sentences are shown as they are.
    /// </remarks>
    private static async Task<string?> ReadValidationDetailAsync(
        HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>(cancellationToken: token);
            if (problem?.Errors is { Count: > 0 } errors)
            {
                if (errors.ContainsKey("InvalidToken"))
                    return "That reset link is invalid or has expired. Request a new one.";

                return string.Join(" ", errors.Values.SelectMany(v => v));
            }

            return problem?.Detail;
        }
        catch
        {
            return null;
        }
    }

    private sealed record ValidationProblem(string? Detail, Dictionary<string, string[]>? Errors);

    public async Task<WebApiTokenResponse?> RefreshAsync(string refreshToken, CancellationToken token = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/refresh",
            new WebApiRefreshRequest(refreshToken),
            token);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<WebApiTokenResponse>(cancellationToken: token);
    }
}
