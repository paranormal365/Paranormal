using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Controllers;
using System.Reflection;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The permissions endpoint answers for every area the role editor can grant.
/// </summary>
/// <remarks>
/// IH-03's shape was a UI that could not see what a grant had unlocked. The fix only works while
/// the endpoint keeps answering for EVERY area — a tenth area added to the role editor, with
/// nothing here to report it, would put the next feature straight back in the same hole, silently.
/// </remarks>
public class MyOrgPermissionsAreaCoverageTests
{
    private static (OrganizationPermissionArea Area, OrganizationSecurityTable Table)[] ProbeTables()
    {
        var field = typeof(OrganizationMembershipController)
            .GetField("AreaProbeTables", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return ((OrganizationPermissionArea, OrganizationSecurityTable)[])field!.GetValue(null)!;
    }

    [Fact]
    public void Every_permission_area_is_answered_for()
    {
        var covered = ProbeTables().Select(p => p.Area).ToHashSet();
        var missing = Enum.GetValues<OrganizationPermissionArea>().Where(a => !covered.Contains(a)).ToList();

        Assert.True(missing.Count == 0,
            "the permissions endpoint says nothing about: " + string.Join(", ", missing)
            + " — a UI cannot offer what it is never told about");
    }

    /// <summary>
    /// And each area is probed through a table that genuinely belongs to it.
    /// </summary>
    /// <remarks>
    /// Probing the wrong table would answer confidently about the wrong thing — worse than not
    /// answering, because a button would appear on somebody else's permission.
    /// </remarks>
    [Fact]
    public void Each_area_is_probed_through_one_of_its_own_tables()
    {
        foreach (var (area, table) in ProbeTables())
            Assert.Equal(area, PermissionAreas.AreaFor(table));
    }

    [Fact]
    public void No_area_is_probed_twice()
    {
        var areas = ProbeTables().Select(p => p.Area).ToList();
        Assert.Equal(areas.Count, areas.Distinct().Count());
    }
}
