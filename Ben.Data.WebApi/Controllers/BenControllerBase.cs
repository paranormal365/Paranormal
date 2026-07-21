using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Shared base for all Ben WebApi controllers.
/// Centralises identity resolution so no controller needs its own copy.
/// </summary>
[ApiController]
public abstract class BenControllerBase : ControllerBase
{
    /// <summary>
    /// Returns the current user's AppUser Guid.
    /// Prefers the <c>app_user_id</c> claim injected by EntraClaimsTransformation;
    /// falls back to the standard <c>NameIdentifier</c> / <c>sub</c> claim.
    /// Returns <see cref="Guid.Empty"/> when neither claim is present or parseable.
    /// </summary>
    protected Guid GetCurrentUserId()
    {
        var value = User.FindFirstValue(EntraClaimsTransformation.AppUserIdClaimType)
                 ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// Returns the current user's AppUser Guid, or throws <see cref="UnauthorizedAccessException"/>
    /// when the claim is absent or invalid.
    /// </summary>
    protected Guid GetCurrentUserIdOrThrow()
    {
        var id = GetCurrentUserId();
        if (id == Guid.Empty)
            throw new UnauthorizedAccessException("Authenticated user id claim is missing or invalid.");
        return id;
    }

    /// <summary>
    /// Returns the current user's AppUser Guid as nullable.
    /// Returns <c>null</c> when the claim is absent or unparseable.
    /// </summary>
    protected Guid? GetCurrentUserIdOrNull()
    {
        var id = GetCurrentUserId();
        return id == Guid.Empty ? null : id;
    }
}
