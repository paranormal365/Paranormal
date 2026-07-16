using System.Text.Json.Serialization;

namespace Ben.Web.WebApp.Services.WebApi;

public sealed record WebApiLoginRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password);

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
