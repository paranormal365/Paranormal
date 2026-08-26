using Ben.Data.Common.Enums;
using Ben.Web.Services;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// What the browser is told about the group's PLAN, and which way it fails.
/// </summary>
/// <remarks>
/// Item 193: the private-engagement toggle rendered for every group, so a free-tier group could
/// tick it, save, and collect a 400 from PrivateCaseGate. The UI could not see the plan. It can
/// now — and the direction of failure is the part worth pinning down, because getting it backwards
/// either disables controls people are entitled to, or offers ones that will be refused.
/// </remarks>
public class PlanCapabilityReportingTests
{
    private static MyOrgPermissionsItem With(params (TierCapability Capability, bool Included)[] caps)
        => new(false, false, null, caps.ToDictionary(c => c.Capability, c => c.Included));

    [Fact]
    public void AnIncludedCapability_IsReportedAsIncluded()
        => Assert.True(With((TierCapability.PrivateResidenceCases, true))
            .PlanIncludes(TierCapability.PrivateResidenceCases));

    [Fact]
    public void AnExcludedCapability_IsReportedAsExcluded()
        => Assert.False(With((TierCapability.PrivateResidenceCases, false))
            .PlanIncludes(TierCapability.PrivateResidenceCases));

    /// <summary>
    /// Silence means INCLUDED — the opposite default from a permission, and deliberately so.
    /// </summary>
    /// <remarks>
    /// The server treats capabilities as fail-open: only a tier with an explicit exclusion row
    /// refuses. The browser has to agree, or an older server, a failed lookup or a capability
    /// added later would quietly disable a control the group is entitled to use — and the group
    /// would have no idea why. An unknown PERMISSION should refuse; an unknown PLAN FEATURE
    /// should not punish.
    /// </remarks>
    [Fact]
    public void AnUnmentionedCapability_IsAssumedIncluded()
        => Assert.True(With((TierCapability.CaseTransfers, false))
            .PlanIncludes(TierCapability.PrivateResidenceCases));

    [Fact]
    public void NoCapabilitiesAtAll_LeavesEverythingWorking()
        => Assert.True(new MyOrgPermissionsItem(false, false)
            .PlanIncludes(TierCapability.PrivateResidenceCases));

    /// <summary>
    /// And the other half of the pair still fails CLOSED: an unknown permission refuses.
    /// </summary>
    /// <remarks>
    /// The two defaults sit next to each other in one record and point opposite ways, which is
    /// exactly the sort of thing that gets "tidied" into consistency by someone who has not read
    /// why. Asserted together so the pairing is visible.
    /// </remarks>
    [Fact]
    public void AnUnknownPermission_StillRefuses()
        => Assert.False(new MyOrgPermissionsItem(false, false)
            .May(OrganizationPermissionArea.Cases, OrganizationSecurityAction.Create));
}
