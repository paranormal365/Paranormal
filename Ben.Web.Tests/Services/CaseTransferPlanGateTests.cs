using Ben.Data.Common.Enums;
using Ben.Web.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// What the transfer buttons are allowed to offer, before anybody presses one.
/// </summary>
/// <remarks>
/// Item 193's second half. The panel rendered blind: a free-tier group could fill in the whole
/// propose dialog to collect a 400, and a group receiving a case could press Accept and be
/// refused. These pin the three things that are easy to get backwards — that Reject is never
/// gated, that a private case needs both capabilities, and that not knowing leaves the buttons
/// alone.
/// </remarks>
public class CaseTransferPlanGateTests
{
    private static MyOrgPermissionsItem With(params (TierCapability Capability, bool Included)[] caps)
        => new(false, false, null, caps.ToDictionary(c => c.Capability, c => c.Included));

    // ── Proposing ─────────────────────────────────────────────────────────────

    [Fact]
    public void APlanWithoutTransfers_CannotPropose()
        => Assert.False(CaseTransferPlanGate.MayPropose(With((TierCapability.CaseTransfers, false))));

    [Fact]
    public void APlanWithTransfers_CanPropose()
        => Assert.True(CaseTransferPlanGate.MayPropose(With((TierCapability.CaseTransfers, true))));

    // ── Accepting ─────────────────────────────────────────────────────────────

    [Fact]
    public void APlanWithoutTransfers_CannotAccept()
        => Assert.Contains("case transfers",
            CaseTransferPlanGate.AcceptRefusal(With((TierCapability.CaseTransfers, false)), false));

    [Fact]
    public void APlanWithTransfers_CanAcceptAnOrdinaryCase()
        => Assert.Null(CaseTransferPlanGate.AcceptRefusal(
            With((TierCapability.CaseTransfers, true), (TierCapability.PrivateResidenceCases, false)), false));

    /// <summary>
    /// Transfers alone are not enough for a private case: accepting one is taking on
    /// private-residence work, and the server re-checks exactly this at the moment it moves.
    /// </summary>
    [Fact]
    public void APrivateCase_AlsoNeedsThePrivateLane()
    {
        var perms = With((TierCapability.CaseTransfers, true), (TierCapability.PrivateResidenceCases, false));

        Assert.Null(CaseTransferPlanGate.AcceptRefusal(perms, isPrivateEngagement: false));
        Assert.Contains("private-residence",
            CaseTransferPlanGate.AcceptRefusal(perms, isPrivateEngagement: true));
    }

    [Fact]
    public void APlanWithBoth_CanAcceptAPrivateCase()
        => Assert.Null(CaseTransferPlanGate.AcceptRefusal(
            With((TierCapability.CaseTransfers, true), (TierCapability.PrivateResidenceCases, true)), true));

    /// <summary>
    /// The transfers refusal wins when both are missing — it is the one that explains why the
    /// whole panel is inert, rather than pointing at the narrower private-lane rule.
    /// </summary>
    [Fact]
    public void WithNeitherCapability_TheTransfersReasonIsGiven()
        => Assert.Contains("case transfers", CaseTransferPlanGate.AcceptRefusal(
            With((TierCapability.CaseTransfers, false), (TierCapability.PrivateResidenceCases, false)), true));

    // ── Not knowing ───────────────────────────────────────────────────────────

    /// <summary>
    /// A failed permissions fetch leaves both buttons working. The server is still the authority,
    /// and disabling a control somebody is entitled to — with no way to find out why — is worse
    /// than letting them meet the refusal they would have met before this existed.
    /// </summary>
    [Fact]
    public void UnknownPermissions_LeaveEverythingWorking()
    {
        Assert.True(CaseTransferPlanGate.MayPropose(null));
        Assert.Null(CaseTransferPlanGate.AcceptRefusal(null, isPrivateEngagement: true));
    }

    [Fact]
    public void AnOlderServerThatMentionsNothing_LeavesEverythingWorking()
    {
        var silent = new MyOrgPermissionsItem(false, false);

        Assert.True(CaseTransferPlanGate.MayPropose(silent));
        Assert.Null(CaseTransferPlanGate.AcceptRefusal(silent, isPrivateEngagement: true));
    }
}
