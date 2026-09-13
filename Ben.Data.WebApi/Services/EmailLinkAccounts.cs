using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// The account an emailed link stands for: the one the address already has, or a passwordless one
/// made at the moment the address is proved.
/// </summary>
/// <remarks>
/// <para>Lifted out of the public event sign-up link (item 199) when hosted events gained a second
/// link that proves an address — picking seats without signing in (item 235 slice 11d). Two copies of
/// "make an account for a proven address" would drift, and the one that stopped allocating a handle
/// would make guests nobody can mention.</para>
///
/// <para><b>Only ever called after the click.</b> An address somebody typed proves nothing, and an
/// account made from it would be a stranger's.</para>
/// </remarks>
public sealed class EmailLinkAccounts
{
    private readonly UserManager<AppUser> _users;
    private readonly UserHandleService _handles;
    private readonly ILogger<EmailLinkAccounts> _logger;

    public EmailLinkAccounts(
        UserManager<AppUser> users, UserHandleService handles, ILogger<EmailLinkAccounts> logger)
    {
        _users = users;
        _handles = handles;
        _logger = logger;
    }

    /// <summary>The account for this proven address, made if there is none.</summary>
    /// <returns>Null only when the account could not be created, which has been logged.</returns>
    public async Task<AppUser?> FindOrCreateAsync(
        string email, string? displayName, string? firstName = null, string? lastName = null,
        CancellationToken ct = default)
    {
        var user = await _users.FindByEmailAsync(email);
        if (user is not null) return user;

        var name = displayName
                   ?? (firstName is { Length: > 0 } && lastName is { Length: > 0 } ? $"{firstName} {lastName}" : null);

        user = new AppUser
        {
            Id                 = Guid.NewGuid(),
            Email              = email,
            UserName           = email,
            NormalizedEmail    = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            // They proved it by clicking a link sent to it, which is what confirmation means.
            EmailConfirmed     = true,
            DisplayName        = name ?? email.Split('@')[0],
            FirstName          = firstName,
            LastName           = lastName,
            // C1: allocated at creation. A guest who signs up and is then invisible to every
            // @mention is a person the group cannot talk to afterwards.
            Handle             = await _handles.AllocateAsync(name, email, ct),
            DateCreated        = DateTime.UtcNow,
        };

        // No password. They can set one whenever they want an account they sign into; until then
        // this exists so the organizer has somebody to reach and they have somewhere to look.
        var created = await _users.CreateAsync(user);
        if (created.Succeeded) return user;

        _logger.LogWarning("Could not create an account for an emailed link: {Errors}",
            string.Join("; ", created.Errors.Select(e => e.Description)));
        return null;
    }

    /// <summary>
    /// Whether to offer setting a password — asked, not assumed, because the link may have been
    /// clicked by somebody who has had one for years.
    /// </summary>
    public async Task<bool> HasNoPasswordAsync(AppUser user) => !await _users.HasPasswordAsync(user);
}
