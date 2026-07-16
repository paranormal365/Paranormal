using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

[Route("api/admin/app-users")]
public sealed class AdminAppUserController : AdminEntityControllerBase<AppUser, AppUserAdminRecord>
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IMapper _mapper;

    public AdminAppUserController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper, IAuditLogService auditLog)
        : base(dbContextFactory, mapper, auditLog)
    {
        _dbFactory = dbContextFactory;
        _mapper = mapper;
    }

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

    /// <summary>Updates editable profile fields including audit timestamps.</summary>
    [HttpPut("{id:guid}/profile")]
    public async Task<ActionResult<AppUserAdminRecord>> UpdateProfile(
        Guid id, [FromBody] AdminUpdateUserProfileRequest request, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        user.DisplayName          = request.DisplayName;
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
        return Ok(_mapper.Map<AppUserAdminRecord>(user));
    }
}

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

