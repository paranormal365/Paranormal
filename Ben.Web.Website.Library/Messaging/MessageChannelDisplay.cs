using Ben.Data.Common.Enums;

namespace Ben.Web.Website.Library.Messaging;

/// <summary>
/// How a message channel is named and coloured, in one place.
/// </summary>
/// <remarks>
/// These were private to the old MessageList component. The mail layout needs them in the row, in
/// the reading pane and in the compose form, and three private copies of a switch over the same
/// enum is how "Case Team" ends up written three different ways.
/// </remarks>
public static class MessageChannelDisplay
{
    /// <summary>The channel's name, as a reader should see it.</summary>
    public static string Label(OrgMessageChannel channel) => channel switch
    {
        OrgMessageChannel.OrgBroadcast  => "Broadcast",
        OrgMessageChannel.DirectMessage => "Direct",
        OrgMessageChannel.CaseTeam      => "Case Team",
        OrgMessageChannel.PublicFeed    => "Public",
        _                               => channel.ToString(),
    };

    /// <summary>Bootstrap badge classes for the channel pill.</summary>
    public static string Badge(OrgMessageChannel channel) => channel switch
    {
        OrgMessageChannel.OrgBroadcast  => "bg-primary",
        OrgMessageChannel.DirectMessage => "bg-info text-dark",
        OrgMessageChannel.CaseTeam      => "bg-warning text-dark",
        OrgMessageChannel.PublicFeed    => "bg-success",
        _                               => "bg-secondary",
    };

    /// <summary>
    /// Whether a channel is addressed to particular people rather than to everyone.
    /// </summary>
    /// <remarks>
    /// The compose form shows its recipient picker for exactly these. Before the picker existed,
    /// both were offered and neither could be addressed — the request was sent with an empty
    /// recipient list every time.
    /// </remarks>
    public static bool NeedsRecipients(OrgMessageChannel channel)
        => channel is OrgMessageChannel.DirectMessage or OrgMessageChannel.CaseTeam;
}
