using AutoMapper;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Admin;

[ApiController]
[Route("api/admin/roles")]
[Authorize(Roles = RoleNames.SuperAdmin)]
public sealed class AdminRoleController : ControllerBase
{
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly UserManager<AppUser> _userManager;
    private readonly IMapper _mapper;

    public AdminRoleController(
        RoleManager<IdentityRole<Guid>> roleManager,
        UserManager<AppUser> userManager,
        IMapper mapper)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _mapper      = mapper;
    }

    /// <summary>Returns all site-level roles with their current user counts.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AdminRoleWithCountResponse>>> GetAll(CancellationToken ct)
    {
        var roles = _roleManager.Roles.ToList();

        var result = new List<AdminRoleWithCountResponse>(roles.Count);
        foreach (var role in roles.OrderBy(r => r.Name))
        {
            var users = await _userManager.GetUsersInRoleAsync(role.Name!);
            result.Add(new AdminRoleWithCountResponse(_mapper.Map<AppRoleAdminRecord>(role), users.Count));
        }

        return Ok(result);
    }

    /// <summary>Creates a new site-level role.</summary>
    [HttpPost]
    public async Task<ActionResult<AppRoleAdminRecord>> Create([FromBody] AdminCreateRoleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Role name is required.");

        var role = new IdentityRole<Guid>(request.Name.Trim());
        var result = await _roleManager.CreateAsync(role);

        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        return CreatedAtAction(nameof(GetAll), _mapper.Map<AppRoleAdminRecord>(role));
    }

    /// <summary>Deletes a role. Refuses if any users are currently assigned to it.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role is null) return NotFound();

        var users = await _userManager.GetUsersInRoleAsync(role.Name!);
        if (users.Count > 0)
            return Conflict($"Cannot delete '{role.Name}' — {users.Count} user(s) are assigned to it.");

        var result = await _roleManager.DeleteAsync(role);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        return NoContent();
    }
}

public sealed record AdminCreateRoleRequest(string Name);

public sealed record AdminRoleWithCountResponse(AppRoleAdminRecord Role, int UserCount);
