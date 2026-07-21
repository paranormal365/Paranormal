using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Admin;

[ApiController]
[Route("api/admin/impersonate")]
[Authorize(Policy = RoleNames.SuperAdmin)]
public sealed class ImpersonateController : BenControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;

    public ImpersonateController(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    /// <summary>
    /// Issues a bearer token for the target user without requiring their password.
    /// SuperAdmin only.
    /// </summary>
    [HttpPost("{targetUserId:guid}")]
    public async Task<IActionResult> ImpersonateUser(Guid targetUserId, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(targetUserId.ToString());
        if (user is null)
            return NotFound($"User {targetUserId} not found.");

        var principal = await _signInManager.CreateUserPrincipalAsync(user);
        // Use the scheme registered by AddIdentityApiEndpoints (Identity.Bearer)
        return SignIn(principal, IdentityConstants.BearerScheme);
    }
}
