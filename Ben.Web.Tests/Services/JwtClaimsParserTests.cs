using System.Text;
using Ben.Web.Services.WebApi;
using Xunit;

namespace Ben.Web.Tests.Services;

public class JwtClaimsParserTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal JWT with standard Base64 encoding (no signature validation).
    /// Parser converts Base64URL → Base64, so standard Base64 works here after the fix.
    /// </summary>
    private static string MakeJwt(string payloadJson)
    {
        // Use standard Base64 — parser strips URL-unsafe chars before decoding
        var header  = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson));
        return $"{header}.{payload}.fake-signature";
    }

    private static readonly Guid TestUserId = Guid.Parse("9f3821d6-4a2a-4a1e-b345-c1234abcdef0");

    // ── UserId Extraction ─────────────────────────────────────────────────────

    [Fact]
    public void ParseClaims_ValidToken_ExtractsUserIdFromSubClaim()
    {
        var token = MakeJwt($$"""{"sub":"{{TestUserId}}","role":"Member","exp":9999999999}""");

        var (userId, _, _) = JwtClaimsParser.ParseClaims(token);

        Assert.Equal(TestUserId, userId);
    }

    [Fact]
    public void ParseClaims_NoSubClaim_UserIdIsNull()
    {
        var token = MakeJwt("""{"email":"user@test.com","role":"Member"}""");

        var (userId, _, _) = JwtClaimsParser.ParseClaims(token);

        Assert.Null(userId);
    }

    // ── IsSuperAdmin ──────────────────────────────────────────────────────────

    [Fact]
    public void ParseClaims_StringRole_SuperAdmin_ReturnsSuperAdmin()
    {
        var token = MakeJwt($$"""{"sub":"{{TestUserId}}","role":"SuperAdmin","exp":9999999999}""");

        var (_, isSuperAdmin, _) = JwtClaimsParser.ParseClaims(token);

        Assert.True(isSuperAdmin);
    }

    [Fact]
    public void ParseClaims_StringRole_NonSuperAdmin_ReturnsFalse()
    {
        var token = MakeJwt($$"""{"sub":"{{TestUserId}}","role":"Editor","exp":9999999999}""");

        var (_, isSuperAdmin, _) = JwtClaimsParser.ParseClaims(token);

        Assert.False(isSuperAdmin);
    }

    [Fact]
    public void ParseClaims_ArrayRoleContainingSuperAdmin_ReturnsSuperAdmin()
    {
        var token = MakeJwt($$"""{"sub":"{{TestUserId}}","role":["Editor","SuperAdmin"],"exp":9999999999}""");

        var (_, isSuperAdmin, _) = JwtClaimsParser.ParseClaims(token);

        Assert.True(isSuperAdmin);
    }

    [Fact]
    public void ParseClaims_ArrayRoleWithoutSuperAdmin_ReturnsFalse()
    {
        var token = MakeJwt($$"""{"sub":"{{TestUserId}}","role":["Editor","Viewer"],"exp":9999999999}""");

        var (_, isSuperAdmin, _) = JwtClaimsParser.ParseClaims(token);

        Assert.False(isSuperAdmin);
    }

    [Fact]
    public void ParseClaims_NoRoleClaim_ReturnsFalse()
    {
        var token = MakeJwt($$"""{"sub":"{{TestUserId}}","exp":9999999999}""");

        var (_, isSuperAdmin, _) = JwtClaimsParser.ParseClaims(token);

        Assert.False(isSuperAdmin);
    }

    // ── Error Handling ────────────────────────────────────────────────────────

    [Fact]
    public void ParseClaims_MalformedToken_ReturnsDefaults()
    {
        var (userId, isSuperAdmin, _) = JwtClaimsParser.ParseClaims("not.a.valid.jwt.token.at.all");

        Assert.Null(userId);
        Assert.False(isSuperAdmin);
    }

    [Fact]
    public void ParseClaims_EmptyString_ReturnsDefaults()
    {
        var (userId, isSuperAdmin, _) = JwtClaimsParser.ParseClaims(string.Empty);

        Assert.Null(userId);
        Assert.False(isSuperAdmin);
    }

    [Fact]
    public void ParseClaims_TwoParts_ReturnsDefaults()
    {
        var (userId, isSuperAdmin, _) = JwtClaimsParser.ParseClaims("onlyone");

        Assert.Null(userId);
        Assert.False(isSuperAdmin);
    }

    [Fact]
    public void ParseClaims_Base64UrlEncoded_WithDashAndUnderscore_DecodesCorrectly()
    {
        // Build a token using proper Base64URL encoding (replaces + with - and / with _)
        // This tests the bug fix: parser must convert - → + and _ → / before decoding
        var payloadJson = $$"""{"sub":"{{TestUserId}}","role":"SuperAdmin","exp":9999999999}""";
        var rawBytes    = System.Text.Encoding.UTF8.GetBytes(payloadJson);
        var base64Url   = Convert.ToBase64String(rawBytes)
                              .TrimEnd('=')
                              .Replace('+', '-')
                              .Replace('/', '_');

        var header = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        var token = $"{header}.{base64Url}.sig";

        var (userId, isSuperAdmin, _) = JwtClaimsParser.ParseClaims(token);

        Assert.Equal(TestUserId, userId);
        Assert.True(isSuperAdmin);
    }

    [Fact]
    public void ParseClaims_AdminRole_IsReportedSeparatelyFromSuperAdmin()
    {
        var token = MakeJwt("""{"role":"Admin"}""");

        var (_, isSuperAdmin, isAdmin) = JwtClaimsParser.ParseClaims(token);

        // Admin must never be mistaken for SuperAdmin — it deliberately grants far less.
        Assert.False(isSuperAdmin);
        Assert.True(isAdmin);
    }

    [Fact]
    public void ParseClaims_ArrayWithBothRoles_ReportsBoth()
    {
        var token = MakeJwt("""{"role":["Admin","SuperAdmin"]}""");

        var (_, isSuperAdmin, isAdmin) = JwtClaimsParser.ParseClaims(token);

        Assert.True(isSuperAdmin);
        Assert.True(isAdmin);
    }
}
