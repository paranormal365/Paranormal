using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers;

[ApiController]
[Route("api/auth/entra")]
public sealed class EntraAuthController : BenControllerBase
{
    private readonly UserManager<AppUser> _userManager;

    public EntraAuthController(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>
    /// Creates a new local AppUser from an Entra identity and links it.
    /// Called when an Entra user arrives with no matching local account.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<EntraRegisterResult>> Register(
        [FromBody] EntraRegisterRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EntraEmail))
            return BadRequest("Email is required.");

        if (!Guid.TryParse(request.EntraOid, out _))
            return BadRequest("Invalid Entra OID.");

        // Guard: if email already exists, the user should use the link flow instead
        var existing = await _userManager.FindByEmailAsync(request.EntraEmail);
        if (existing is not null)
            return Conflict(new { Message = "An account with this email already exists. Please use the 'Link existing account' option." });

        // Guard: if this OID is already linked (e.g., duplicate register attempt), return that user
        var alreadyLinked = await _userManager.FindByLoginAsync("Microsoft", request.EntraOid);
        if (alreadyLinked is not null)
            return Ok(new EntraRegisterResult(alreadyLinked.Id, alreadyLinked.Email ?? string.Empty));

        var user = new AppUser
        {
            Id             = Guid.NewGuid(),
            Email          = request.EntraEmail,
            UserName       = request.EntraEmail,
            NormalizedEmail = request.EntraEmail.ToUpperInvariant(),
            NormalizedUserName = request.EntraEmail.ToUpperInvariant(),
            EmailConfirmed = true,  // Entra has verified the email
            DisplayName    = request.DisplayName,
            DateCreated    = DateTime.UtcNow
        };

        var createResult = await _userManager.CreateAsync(user);
        if (!createResult.Succeeded)
            return BadRequest(new { Errors = createResult.Errors.Select(e => e.Description) });

        var loginInfo = new UserLoginInfo("Microsoft", request.EntraOid, request.ProviderDisplayName ?? request.EntraEmail);
        var linkResult = await _userManager.AddLoginAsync(user, loginInfo);
        if (!linkResult.Succeeded)
        {
            await _userManager.DeleteAsync(user); // rollback
            return BadRequest(new { Errors = linkResult.Errors.Select(e => e.Description) });
        }

        return Ok(new EntraRegisterResult(user.Id, user.Email ?? string.Empty));
    }

    /// <summary>
    /// Links a Microsoft Entra identity to the currently authenticated local user.
    /// Called when an Entra user confirms they already have an account and logs in locally.
    /// </summary>
    [HttpPost("link")]
    [Authorize]
    public async Task<IActionResult> Link(
        [FromBody] EntraLinkRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.EntraOid, out _))
            return BadRequest("Invalid Entra OID.");

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        // Check if this OID is already linked
        var existingOwner = await _userManager.FindByLoginAsync("Microsoft", request.EntraOid);
        if (existingOwner is not null)
        {
            if (existingOwner.Id == user.Id)
                return Ok(new { Message = "This Microsoft account is already linked to your account." });

            return Conflict(new { Message = "This Microsoft account is already linked to a different local account." });
        }

        var loginInfo = new UserLoginInfo("Microsoft", request.EntraOid, request.ProviderDisplayName ?? request.EntraEmail);
        var result = await _userManager.AddLoginAsync(user, loginInfo);

        return result.Succeeded
            ? Ok(new { Message = "Microsoft account linked successfully." })
            : BadRequest(new { Errors = result.Errors.Select(e => e.Description) });
    }
}

public record EntraRegisterRequest(
    string EntraOid,
    string EntraEmail,
    string DisplayName,
    string? ProviderDisplayName = null);

public record EntraRegisterResult(Guid UserId, string Email);

public record EntraLinkRequest(
    string EntraOid,
    string EntraEmail,
    string? ProviderDisplayName = null);
