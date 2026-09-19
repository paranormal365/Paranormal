using Ben.Data.Common.Constants;
using System.Text;
using System.Text.Json;

namespace Ben.Web.Services.WebApi;

/// <summary>Parses JWT claims from a bearer token without validating the signature.</summary>
public static class JwtClaimsParser
{
    /// <summary>
    /// Decodes the JWT payload and returns the user ID (sub claim) plus which app-wide roles
    /// the token carries. Returns defaults on any error.
    /// </summary>
    public static (Guid? UserId, bool IsSuperAdmin, bool IsAdmin, bool IsModerator) ParseClaims(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return (null, false, false, false);

            // Convert Base64URL → standard Base64, then add padding
            var raw    = parts[1].Replace('-', '+').Replace('_', '/');
            var padded = raw.PadRight(raw.Length + (4 - raw.Length % 4) % 4, '=');
            var json   = Encoding.UTF8.GetString(Convert.FromBase64String(padded));

            using var doc = JsonDocument.Parse(json);

            Guid? userId = null;
            if (doc.RootElement.TryGetProperty("sub", out var sub)
                && Guid.TryParse(sub.GetString(), out var id))
                userId = id;

            var roles = new List<string?>();
            if (doc.RootElement.TryGetProperty("role", out var role))
            {
                if (role.ValueKind == JsonValueKind.String) roles.Add(role.GetString());
                else roles.AddRange(role.EnumerateArray().Select(r => r.GetString()));
            }

            return (userId,
                    roles.Contains(RoleNames.SuperAdmin),
                    roles.Contains(RoleNames.Admin),
                    // SuperAdmin moderates implicitly, matching ModeratorHandler on the server —
                    // two answers to "may this person moderate" would eventually disagree, and
                    // the visible symptom would be a menu item that leads to a 403.
                    roles.Contains(RoleNames.Moderator) || roles.Contains(RoleNames.SuperAdmin));
        }
        catch
        {
            return (null, false, false, false);
        }
    }
}
