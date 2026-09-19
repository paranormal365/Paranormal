using Ben.Data.Common.Enums;

namespace Ben.Web.Services;

/// <summary>
/// What the group's PLAN allows on a case transfer, answered before the button is pressed.
/// </summary>
/// <remarks>
/// <para><b>Item 193, at both doors.</b> The transfer panel rendered blind: a free-tier group
/// could open the propose dialog, choose a destination, write a reason and submit, only to collect
/// the sending gate's 400 at the end of it — and a group receiving a case could press Accept and
/// be refused for the same reason. Both answers were knowable before the click.</para>
///
/// <para><b>The rule mirrors the server, it does not replace it.</b> `CaseTransferController`
/// re-checks at the moment the case actually moves, because a plan can change while a proposal
/// waits. This only moves the answer earlier; it never grants anything the server would refuse.
/// </para>
///
/// <para><b>Rejecting is deliberately not gated.</b> Declining work must never require a plan —
/// the server says so in as many words, and a UI that disabled Reject would trap a group with an
/// incoming case it cannot accept and cannot turn down.</para>
///
/// <para><b>Everything here fails OPEN</b>, matching <see cref="MyOrgPermissionsItem.PlanIncludes"/>:
/// null permissions, a failed fetch or a capability an older server never mentioned all leave the
/// control working. An unknown plan feature must not punish.</para>
/// </remarks>
public static class CaseTransferPlanGate
{
    /// <summary>Whether the group may propose sending a case away.</summary>
    public static bool MayPropose(MyOrgPermissionsItem? permissions)
        => permissions?.PlanIncludes(TierCapability.CaseTransfers) ?? true;

    /// <summary>
    /// Why accepting an incoming case is refused, or null when it is available.
    /// </summary>
    /// <param name="isPrivateEngagement">
    /// A private case needs the private lane on top of transfers — taking one on is taking on
    /// private-residence work, which is the second thing the server re-checks on the way in.
    /// </param>
    /// <returns>Wording for the person looking at the button, not the server's own message: they
    /// have not made a request yet, so there is no refusal to report — only a reason it is off.
    /// </returns>
    public static string? AcceptRefusal(MyOrgPermissionsItem? permissions, bool isPrivateEngagement)
    {
        if (!MayPropose(permissions))
            return "Your group's plan does not include case transfers.";

        if (isPrivateEngagement && !(permissions?.PlanIncludes(TierCapability.PrivateResidenceCases) ?? true))
            return "This is a private engagement, and your group's plan does not include private-residence work.";

        return null;
    }
}
