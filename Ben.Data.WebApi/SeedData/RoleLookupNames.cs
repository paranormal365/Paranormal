namespace Ben.Data.WebApi.SeedData;

/// <summary>
/// Puts back the upper-case lookup name on any role row that lost it.
/// </summary>
/// <remarks>
/// <para>Identity finds a role by <c>NormalizedName</c>, never by <c>Name</c>. A row inserted by hand — which is how
/// roles were assigned on the live site before the Site Roles screen existed — has that column empty, and then
/// <c>AddToRole</c> and <c>RemoveFromRole</c> <b>throw</b> rather than fail. The page shows a refusal with no reason and
/// the log shows a 500; nothing names the column (Ben, 2026-09-15, adding his nephew as a SuperAdmin).</para>
/// <para>Run at startup because the row looks perfectly ordinary to a person, so nothing else would ever find it, and
/// because one such row breaks every future role change on that role.</para>
/// </remarks>
internal static class RoleLookupNames
{
    /// <summary>Repairs the rows that need it and returns their names.</summary>
    public static async Task<IReadOnlyList<string>> RepairAsync(BenDataContext db, CancellationToken ct = default)
    {
        var broken = await db.Roles
            .Where(r => r.Name != null && (r.NormalizedName == null || r.NormalizedName != r.Name.ToUpper()))
            .ToListAsync(ct);
        if (broken.Count == 0) return [];

        foreach (var role in broken) role.NormalizedName = role.Name!.ToUpperInvariant();
        await db.SaveChangesAsync(ct);
        return [.. broken.Select(r => r.Name!)];
    }
}
