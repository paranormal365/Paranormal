using Ben.Data.Common;
using Xunit;

namespace Ben.Web.Tests;

/// <summary>
/// Covers the one rule that decides whether Entra sign-in is on, and whether issuer validation is.
/// </summary>
/// <remarks>
/// <para>Both questions used to be answered twice, once per host, and the answers diverged. The API
/// asked whether <c>ClientId</c> parsed as a GUID. The website asked whether it differed from one
/// literal it had been handed as an "unset" sentinel — and that literal turned out to be the real
/// registration id, so the website hid the Microsoft button while the API stood a JWT bearer
/// handler up against the same, perfectly valid, app.</para>
///
/// <para>These tests pin the shared rule. The single-tenant issuer case is the one that would be a
/// security hole rather than an outage: with validation off, a token minted in any Microsoft
/// directory anywhere clears every remaining check.</para>
/// </remarks>
public class EntraConfigTests
{
    /// <summary>The real production registration, as of 2026-08-21.</summary>
    private const string RealClientId = "3e37e6d7-13ea-4b94-b271-618267256d8b";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("YOUR_WEBAPP_CLIENT_ID")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void IsConfigured_is_false_for_absent_or_malformed_ids(string? clientId)
    {
        Assert.False(EntraConfig.IsConfigured(clientId));
    }

    [Theory]
    [InlineData("3e37e6d7-13ea-4b94-b271-618267256d8b")]
    [InlineData("3E37E6D7-13EA-4B94-B271-618267256D8B")]                 // upper case
    [InlineData("{3e37e6d7-13ea-4b94-b271-618267256d8b}")]               // braces
    [InlineData("3e37e6d713ea4b94b271618267256d8b")]                     // no hyphens
    public void IsConfigured_accepts_a_real_id_however_it_is_written(string clientId)
    {
        // Parsed as a Guid rather than compared as text, so formatting cannot change the answer.
        Assert.True(EntraConfig.IsConfigured(clientId));
    }

    [Fact]
    public void IsConfigured_accepts_the_current_production_registration()
    {
        // Guards the specific regression: this id was once treated as a sentinel meaning "unset",
        // which silently switched Entra off on the website only.
        Assert.True(EntraConfig.IsConfigured(RealClientId));
    }

    [Theory]
    [InlineData("common")]
    [InlineData("organizations")]
    [InlineData("consumers")]
    [InlineData("COMMON")]
    [InlineData(null)]
    [InlineData("")]
    public void IsMultiTenant_is_true_for_the_shared_authorities_and_for_absent(string? tenantId)
    {
        // Absent counts as multi-tenant because both hosts fall back to "common" when the setting
        // is missing. Turning issuer validation ON against /common locks everyone out.
        Assert.True(EntraConfig.IsMultiTenant(tenantId));
    }

    [Theory]
    [InlineData("72f988bf-86f1-41af-91ab-2d7cd011db47")]
    [InlineData("contoso.onmicrosoft.com")]
    public void IsMultiTenant_is_false_once_a_single_tenant_is_named(string tenantId)
    {
        // This is what turns ValidateIssuer on in both hosts. If it ever returns true for a named
        // tenant, tokens from every other Microsoft directory become acceptable.
        Assert.False(EntraConfig.IsMultiTenant(tenantId));
    }

    [Theory]
    [InlineData(null, "common")]
    [InlineData("", "common")]
    [InlineData("   ", "common")]
    [InlineData("contoso.onmicrosoft.com", "contoso.onmicrosoft.com")]
    public void TenantOrCommon_defaults_only_when_nothing_is_set(string? tenantId, string expected)
    {
        Assert.Equal(expected, EntraConfig.TenantOrCommon(tenantId));
    }
}
