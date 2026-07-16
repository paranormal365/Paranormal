using Ben.Data.Common.Constants;
using System.Text;
using System.Text.Json;

namespace Ben.Web.WebApp.Services.WebApi;

/// <summary>Parses JWT claims from a bearer token without validating the signature.</summary>
public static class JwtClaimsParser
{
    /// <summary>
    /// Decodes the JWT payload and returns the user ID (sub claim) and whether
    /// the token carries the SuperAdmin role.  Returns defaults on any error.
    /// </summary>
    public static (Guid? UserId, bool IsSuperAdmin) ParseClaims(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return (null, false);

            // Convert Base64URL → standard Base64, then add padding
            var raw    = parts[1].Replace('-', '+').Replace('_', '/');
            var padded = raw.PadRight(raw.Length + (4 - raw.Length % 4) % 4, '=');
            var json   = Encoding.UTF8.GetString(Convert.FromBase64String(padded));

            using var doc = JsonDocument.Parse(json);

            Guid? userId = null;
            if (doc.RootElement.TryGetProperty("sub", out var sub)
                && Guid.TryParse(sub.GetString(), out var id))
                userId = id;

            bool isSuperAdmin = false;
            if (doc.RootElement.TryGetProperty("role", out var role))
            {
                isSuperAdmin = role.ValueKind == JsonValueKind.String
                    ? role.GetString() == RoleNames.SuperAdmin
                    : role.EnumerateArray().Any(r => r.GetString() == RoleNames.SuperAdmin);
            }

            return (userId, isSuperAdmin);
        }
        catch
        {
            return (null, false);
        }
    }
}
