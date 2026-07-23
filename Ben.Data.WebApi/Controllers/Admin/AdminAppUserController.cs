using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

[Route("api/admin/app-users")]
public sealed class AdminAppUserController : AdminEntityControllerBase<AppUser, AppUserAdminRecord>
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IMapper _mapper;
    private readonly UserManager<AppUser> _userManager;
    private readonly IAuditLogService _auditLog;

    public AdminAppUserController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper,
        IAuditLogService auditLog, UserManager<AppUser> userManager)
        : base(dbContextFactory, mapper, auditLog)
    {
        _dbFactory   = dbContextFactory;
        _mapper      = mapper;
        _userManager = userManager;
        _auditLog    = auditLog;
    }

    /// <summary>Suppresses the base Create(TEntity) route — use CreateUser instead.</summary>
    [NonAction]
    public override Task<ActionResult<AppUserAdminRecord>> Create(
        [FromBody] AppUser entity, CancellationToken cancellationToken)
        => throw new NotSupportedException("Use POST /api/admin/app-users with AdminCreateUserRequest.");

    /// <summary>Returns the full user aggregate including all related records.</summary>
    [HttpGet("{id:guid}/detail")]
    public async Task<ActionResult<AppUserDetailAdminRecord>> GetDetail(Guid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        var addresses   = await db.UserAddresses.AsNoTracking().Where(x => x.AppUserId == id).ToListAsync(ct);
        var emails      = await db.UserEmails.AsNoTracking().Where(x => x.AppUserId == id).ToListAsync(ct);
        var phones      = await db.UserPhones.AsNoTracking().Where(x => x.AppUserId == id).ToListAsync(ct);
        var links       = await db.UserLinks.AsNoTracking().Where(x => x.AppUserId == id).ToListAsync(ct);
        var notes       = await db.UserNotes.AsNoTracking().Where(x => x.CreatedByAppUserId == id).ToListAsync(ct);
        var messages    = await db.UserMessages.AsNoTracking().Where(x => x.CreatedByAppUserId == id).ToListAsync(ct);
        var memberships = await db.OrganizationUserMemberships.AsNoTracking().Where(x => x.AppUserId == id).ToListAsync(ct);
        var files       = await db.UploadFiles.AsNoTracking().Where(x => x.AppUserId == id).ToListAsync(ct);

        return Ok(new AppUserDetailAdminRecord
        {
            User        = _mapper.Map<AppUserAdminRecord>(user),
            Addresses   = _mapper.Map<IReadOnlyList<UserAddressAdminRecord>>(addresses),
            Emails      = _mapper.Map<IReadOnlyList<UserEmailAdminRecord>>(emails),
            Phones      = _mapper.Map<IReadOnlyList<UserPhoneAdminRecord>>(phones),
            Links       = _mapper.Map<IReadOnlyList<UserLinkAdminRecord>>(links),
            Notes       = _mapper.Map<IReadOnlyList<UserNoteAdminRecord>>(notes),
            Messages    = _mapper.Map<IReadOnlyList<UserMessageAdminRecord>>(messages),
            Memberships = _mapper.Map<IReadOnlyList<OrganizationUserMembershipAdminRecord>>(memberships),
            UploadFiles = _mapper.Map<IReadOnlyList<UploadFileAdminRecord>>(files)
        });
    }

    /// <summary>Creates a new application user with an initial password.</summary>
    [HttpPost]
    public async Task<ActionResult<AppUserAdminRecord>> CreateUser(
        [FromBody] AdminCreateUserRequest request, CancellationToken ct)
    {
        var user = new AppUser
        {
            UserName       = string.IsNullOrWhiteSpace(request.UserName) ? request.Email : request.UserName,
            Email          = request.Email,
            DisplayName    = request.DisplayName,
            EmailConfirmed = request.IsEmailConfirmed,
            DateCreated    = DateTime.UtcNow
        };
        user.NormalizedUserName = user.UserName?.ToUpperInvariant();
        user.NormalizedEmail    = user.Email?.ToUpperInvariant();

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        if (request.IsSuperAdmin)
            await _userManager.AddToRoleAsync(user, RoleNames.SuperAdmin);

        return CreatedAtAction(nameof(GetDetail), new { id = user.Id }, _mapper.Map<AppUserAdminRecord>(user));
    }

    /// <summary>Updates editable profile fields including audit timestamps.</summary>
    [HttpPut("{id:guid}/profile")]
    public async Task<ActionResult<AppUserAdminRecord>> UpdateProfile(
        Guid id, [FromBody] AdminUpdateUserProfileRequest request, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var before = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (before is null) return NotFound();
        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == id, ct);

        user!.DisplayName          = request.DisplayName;
        user.UserName             = request.UserName ?? user.UserName;
        user.NormalizedUserName   = request.UserName?.ToUpperInvariant() ?? user.NormalizedUserName;
        user.Email                = request.Email ?? user.Email;
        user.NormalizedEmail      = request.Email?.ToUpperInvariant() ?? user.NormalizedEmail;
        user.PhoneNumber          = request.PhoneNumber;
        user.EmailConfirmed       = request.IsEmailConfirmed;
        user.TwoFactorEnabled     = request.IsTwoFactorEnabled;
        user.LockoutEnabled       = request.IsLockoutEnabled;
        user.LockoutEnd           = request.LockoutEnd;
        user.DateCreated          = request.DateCreated;
        user.DateUpdated          = request.DateUpdated;

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(AppUser), id, before, user!, GetCurrentUserId(), AppSources.WebApi, ct));
        return Ok(_mapper.Map<AppUserAdminRecord>(user));
    }
}

public sealed record AdminCreateUserRequest(
    string Email,
    string Password,
    string? DisplayName,
    string? UserName,
    bool IsEmailConfirmed,
    bool IsSuperAdmin);

public sealed record AdminUpdateUserProfileRequest(
    string? DisplayName,
    string? UserName,
    string? Email,
    string? PhoneNumber,
    bool IsEmailConfirmed,
    bool IsTwoFactorEnabled,
    bool IsLockoutEnabled,
    DateTimeOffset? LockoutEnd,
    DateTime DateCreated,
    DateTime? DateUpdated);

