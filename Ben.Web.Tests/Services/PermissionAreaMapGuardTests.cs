using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The table→area map must stay total (item 156 Phase A). A table missing from it would be a
/// permission no tier could ever include — invisible rather than broken, the failure class this
/// codebase keeps finding one level down.
/// </summary>
public sealed class PermissionAreaMapGuardTests
{
    [Fact]
    public void Every_table_is_mapped_or_deliberately_excluded_never_neither_never_both()
    {
        var all = Enum.GetValues<OrganizationSecurityTable>();

        var unaccounted = all.Where(t =>
            !PermissionAreas.Map.ContainsKey(t) && !PermissionAreas.UserScopedTables.Contains(t)).ToList();
        Assert.True(unaccounted.Count == 0,
            "These OrganizationSecurityTable values are neither mapped to an area nor in the "
            + "declared user-scoped exclusion list. A new value must go in exactly one of the "
            + "two the moment it is added:\n  " + string.Join("\n  ", unaccounted));

        var both = all.Where(t =>
            PermissionAreas.Map.ContainsKey(t) && PermissionAreas.UserScopedTables.Contains(t)).ToList();
        Assert.True(both.Count == 0,
            "These values are both mapped AND excluded, which makes the map lie about one of "
            + "them:\n  " + string.Join("\n  ", both));
    }

    [Fact]
    public void Every_area_has_at_least_one_table()
    {
        // An empty area would be a checkbox on the tier checklist that includes nothing —
        // a control that lies, the item-152 class.
        var covered = PermissionAreas.Map.Values.ToHashSet();
        var empty = Enum.GetValues<OrganizationPermissionArea>().Where(a => !covered.Contains(a)).ToList();
        Assert.True(empty.Count == 0,
            "These areas contain no tables, so including them on a tier includes nothing:\n  "
            + string.Join("\n  ", empty));
    }

    [Fact]
    public void The_tier_admin_endpoint_is_superadmin_only()
    {
        // The checklist decides what every paying group may do; the gate is the class-level
        // policy, asserted here so a refactor that drops the attribute fails a test instead of
        // opening the pricing model to every signed-in user.
        var attr = typeof(Ben.Data.WebApi.Controllers.Admin.AdminSubscriptionTierController)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .FirstOrDefault();
        Assert.NotNull(attr);
        Assert.Equal(RoleNames.SuperAdmin, attr!.Policy);
    }
}
