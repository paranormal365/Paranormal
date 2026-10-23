using Ben.Web.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Ben.Web.Website.Services;

/// <summary>
/// The groups each person was last shown in the menu, so the next page draws "Your groups" at once
/// (item 259).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The menu drew first and asked for the person's groups afterwards; when they
/// arrived, "Your groups" was inserted under Home and every entry below it moved down a row. A click
/// aimed at one of them in that moment landed on the entry above: on every full page load, for
/// anybody in a group (found 10/14/2026 by the browser run clicking Administration and landing on
/// /organizations).</para>
///
/// <para><b>What it holds.</b> Names and ids only, per user, for <see cref="Lifetime"/> after the
/// last fetch. The menu still fetches on every page and overwrites this, so a group joined or left
/// is wrong for one page at most. The very first page after signing in still shifts: there is
/// nothing to remember yet.</para>
/// </remarks>
public sealed class NavGroupsMemory
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly IMemoryCache _cache;

    public NavGroupsMemory(IMemoryCache cache) => _cache = cache;

    private static string Key(Guid userId) => $"nav-groups:{userId:N}";

    /// <summary>What this person was last shown, or nothing.</summary>
    public IReadOnlyList<MyMembershipOrgItem> Recall(Guid? userId)
        => userId is { } id && _cache.TryGetValue(Key(id), out IReadOnlyList<MyMembershipOrgItem>? groups) && groups is not null
            ? groups
            : [];

    /// <summary>What the menu was just told. A failed fetch is never remembered.</summary>
    public void Remember(Guid? userId, IReadOnlyList<MyMembershipOrgItem> groups)
    {
        if (userId is not { } id) return;
        _cache.Set(Key(id), groups, Lifetime);
    }
}
