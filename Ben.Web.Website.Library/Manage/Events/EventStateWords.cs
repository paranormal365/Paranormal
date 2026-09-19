using Ben.Data.Common.Enums;

namespace Ben.Web.Website.Library.Manage.Events;

/// <summary>
/// A hosted event's state in words, for lists and sentences (item 235 phase 17b). The badge has its own shorter words.
/// </summary>
public static class EventStateWords
{
    /// <summary>A label for a filter or a chart: "Called off".</summary>
    public static string Label(HostedEventLifecycleState state) => state switch
    {
        HostedEventLifecycleState.Draft => "Draft",
        HostedEventLifecycleState.Published => "Published",
        HostedEventLifecycleState.Live => "On now",
        HostedEventLifecycleState.Ended => "Over",
        HostedEventLifecycleState.Archived => "Archived",
        HostedEventLifecycleState.Cancelled => "Called off",
        HostedEventLifecycleState.VenueWithdrawn => "Venue withdrew",
        HostedEventLifecycleState.Removed => "Removed",
        _ => state.ToString(),
    };

    /// <summary>For the middle of a sentence: "when it was published".</summary>
    public static string Of(HostedEventLifecycleState state) => state switch
    {
        HostedEventLifecycleState.Draft => "a draft",
        HostedEventLifecycleState.Published => "published",
        HostedEventLifecycleState.Live => "on",
        HostedEventLifecycleState.Ended => "over",
        HostedEventLifecycleState.Archived => "archived",
        HostedEventLifecycleState.Cancelled => "called off",
        HostedEventLifecycleState.VenueWithdrawn => "withdrawn by the venue",
        HostedEventLifecycleState.Removed => "removed",
        _ => state.ToString(),
    };
}
