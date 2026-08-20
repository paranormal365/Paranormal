using Ben.Data.Common.Helpers;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Allocates and checks <c>@names</c>.
/// </summary>
/// <remarks>
/// <para>Two jobs, and they are different. <see cref="IsAvailableAsync"/> answers a person's
/// question as they type. <see cref="AllocateAsync"/> is for the creation paths where nobody is
/// present to be asked — an Entra sign-in linking a new account, an event magic link, the seeders,
/// an administrator creating a user — and must always produce something, because an account with
/// no handle cannot be mentioned and is invisible to the feed.</para>
///
/// <para><b>The unique index is the real guard, not these checks.</b> Two people can pass
/// <see cref="IsAvailableAsync"/> for the same name a millisecond apart; the database refuses the
/// second, and the caller reports the collision. Checking first is for the message, not the
/// guarantee.</para>
/// </remarks>
public sealed class UserHandleService
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public UserHandleService(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>
    /// Whether a handle is legal and nobody has it. The second element of the result says why not
    /// when the answer is false, and is null when it is available.
    /// </summary>
    public async Task<(bool Available, string? Reason)> IsAvailableAsync(
        string? candidate, CancellationToken ct = default)
    {
        if (!UserHandle.IsValid(candidate, out var error)) return (false, error);

        var handle = UserHandle.Normalize(candidate);
        await using var db = await _db.CreateDbContextAsync(ct);

        var taken = await db.AppUsers.AsNoTracking().AnyAsync(u => u.Handle == handle, ct);
        return taken ? (false, "That name is taken.") : (true, null);
    }

    /// <summary>
    /// A free handle derived from whatever the account already has.
    /// </summary>
    /// <remarks>
    /// <para>Suffixes with a number on collision — <c>sarahmitchell</c>, then
    /// <c>sarahmitchell2</c>. The suffix is appended to a truncated stem when the name is already
    /// at the length limit, so the result stays legal rather than being silently rejected by the
    /// column.</para>
    ///
    /// <para>Gives up after a bounded number of attempts and falls back to a random suffix, which
    /// cannot realistically collide. An unbounded loop here would be a way to hang account
    /// creation by registering a few thousand similar names.</para>
    /// </remarks>
    public async Task<string> AllocateAsync(
        string? displayName, string? email, CancellationToken ct = default)
    {
        var stem = UserHandle.Suggest(displayName, email);

        await using var db = await _db.CreateDbContextAsync(ct);

        // One query rather than one per attempt: everything that starts with the stem, so the
        // loop below is arithmetic instead of round trips.
        var taken = await db.AppUsers.AsNoTracking()
            .Where(u => u.Handle != null && u.Handle.StartsWith(stem))
            .Select(u => u.Handle!)
            .ToListAsync(ct);

        var used = new HashSet<string>(taken, StringComparer.Ordinal);

        if (!used.Contains(stem)) return stem;

        for (var suffix = 2; suffix <= 1000; suffix++)
        {
            var candidate = WithSuffix(stem, suffix.ToString());
            if (!used.Contains(candidate)) return candidate;
        }

        // A thousand people called the same thing. Vanishingly unlikely, and a random tail is a
        // better answer than a loop that never ends.
        return WithSuffix(stem, Guid.NewGuid().ToString("N")[..6]);
    }

    /// <summary>Appends a suffix, trimming the stem so the result stays within the length limit.</summary>
    private static string WithSuffix(string stem, string suffix)
    {
        var room = UserHandle.MaxLength - suffix.Length;
        var trimmed = stem.Length <= room ? stem : stem[..room];
        return trimmed + suffix;
    }
}
