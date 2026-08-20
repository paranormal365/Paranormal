using System.Text.Json.Serialization;

namespace Ben.Web.Services.WebApi;

/// <summary>
/// A sign-in attempt, in the shape <c>MapIdentityApi</c>'s <c>/login</c> expects.
/// </summary>
/// <remarks>
/// The two-factor fields are Identity's own, not ours: sending either one alongside the password
/// completes a sign-in that would otherwise be refused with <c>RequiresTwoFactor</c>. They are
/// omitted from the JSON when null, because Identity treats an empty string as an attempt with a
/// wrong code rather than as no attempt at all — which would consume a failure against an account
/// that never had 2FA on.
/// </remarks>
public sealed record WebApiLoginRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("twoFactorCode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TwoFactorCode = null,
    [property: JsonPropertyName("twoFactorRecoveryCode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TwoFactorRecoveryCode = null);

public sealed record WebApiRefreshRequest(
    [property: JsonPropertyName("refreshToken")] string RefreshToken);

public sealed class WebApiTokenResponse
{
    [JsonPropertyName("tokenType")]
    public string TokenType { get; set; } = string.Empty;

    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }
}
