using Ben.Data.Common.Enums;
using Ben.Web.Website.Library.Messaging;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every message channel has a name a reader should see (item 238C).
/// </summary>
/// <remarks>
/// <para>The label switch ends in <c>channel.ToString()</c>, which is a sensible fallback and a
/// silent one: an appended channel renders its own enum spelling in a badge — "EventStaffRoom" —
/// and nothing fails. <c>EventRoom</c> had been doing exactly that since item 235 shipped it.</para>
///
/// <para>So the rule is asserted over the enum rather than over a list somebody keeps in step: a
/// new channel fails this the moment it is added, which is the moment it is cheapest to name.</para>
/// </remarks>
public sealed class MessageChannelDisplayTests
{
    public static TheoryData<OrgMessageChannel> EveryChannel()
    {
        var data = new TheoryData<OrgMessageChannel>();
        foreach (var c in Enum.GetValues<OrgMessageChannel>()) data.Add(c);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryChannel))]
    public void Is_named_for_a_reader_not_for_the_compiler(OrgMessageChannel channel)
    {
        var label = MessageChannelDisplay.Label(channel);

        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.NotEqual(channel.ToString(), label);
    }

    [Theory]
    [MemberData(nameof(EveryChannel))]
    public void Has_a_badge(OrgMessageChannel channel)
        => Assert.False(string.IsNullOrWhiteSpace(MessageChannelDisplay.Badge(channel)));

    /// <summary>
    /// The staff room and the attendee room must never read as the same thing.
    /// </summary>
    /// <remarks>
    /// They are the two audiences a hosted event has, and the badge is the only thing on the row
    /// that tells them apart. Two channels sharing a label is how somebody posts to the wrong one.
    /// </remarks>
    [Fact]
    public void The_two_event_channels_do_not_read_alike()
        => Assert.NotEqual(MessageChannelDisplay.Label(OrgMessageChannel.EventRoom),
                           MessageChannelDisplay.Label(OrgMessageChannel.EventStaffRoom));
}
