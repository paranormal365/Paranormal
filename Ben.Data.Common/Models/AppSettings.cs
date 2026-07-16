using Microsoft.Extensions.Configuration;

namespace Ben.Data.Common.Models;

public class AppSettings
{
    public const string SectionName = nameof(AppSettings);
    public const string CorsPolicyName = "AppCorsPolicy";

    public AppSettings(IConfiguration configuration)
    {
        configuration.GetSection(SectionName).Bind(this);
    }

    public string? ErrorUrl { get; set; } = null;
    public string? OpenIdAuthority { get; set; } = null;
    public string? OpenIdClientId { get; set; } = null;
    public string? OpenIdUserName { get; set; } = null;
    public string? LogoutUrl { get; set; } = null;
    public string? UnAuthorizedUrl { get; set; } = null;
    public int? SessionTimeout { get; set; } = null;
    public int? ClientTimeout { get; set; } = null;
    public string? BasePath { get; set; } = null;
}
