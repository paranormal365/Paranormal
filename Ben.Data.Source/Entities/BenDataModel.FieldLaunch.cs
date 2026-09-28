using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A lead starting everybody's Field Kit at once (item 252): the guide on a tour date, the
    /// organiser of an event, the lead of an investigation pressed Launch.
    /// </summary>
    /// <remarks>
    /// <para>Ben, 2026-09-28: "the planner wants the hunt to start at a specific time. That is what
    /// the button does." The server pushes to every registered person signed in on the app, and
    /// writes a post in the feed they can tap if they missed the push — public when the thing is
    /// public, otherwise shown only to them. The post goes six hours after the thing ends.</para>
    ///
    /// <para><b>Who it went to is kept</b> (<see cref="Recipients"/>), frozen at the moment of the
    /// launch: that list is the audience of a private notice, and who could open it. Somebody who
    /// registers afterwards is not sent a launch that happened before them.</para>
    /// </remarks>
    public partial class FieldLaunch
    {
        public Guid Id { get; set; }

        /// <summary>The group running it.</summary>
        public Guid OrganizationId { get; set; }

        public FieldLaunchTarget Target { get; set; }

        /// <summary>Set for an investigation launch.</summary>
        public Guid? InvestigationId { get; set; }

        /// <summary>Set for a tour date or a calendar event.</summary>
        public Guid? OrgCalendarEventId { get; set; }

        /// <summary>Set for a hosted event.</summary>
        public Guid? HostedEventId { get; set; }

        /// <summary>What it was called when it was launched — the push and the card say this.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Where, as a session's location label; null when the exact place is withheld.</summary>
        public string? LocationLabel { get; set; }

        public Guid LaunchedByAppUserId { get; set; }

        public DateTime LaunchedUtc { get; set; }

        /// <summary>When the thing ends.</summary>
        public DateTime EndsUtc { get; set; }

        /// <summary>Six hours after it ends: the card leaves the feed and the link stops opening it.</summary>
        public DateTime ExpiresUtc { get; set; }

        /// <summary>A public tour date, public event or public investigation: the card is for anyone.</summary>
        public bool IsPublic { get; set; }

        /// <summary>The card in the feed.</summary>
        public Guid? FeedPostId { get; set; }

        /// <summary>How many people it was for, how many had the app, and how many phones took it.</summary>
        public int PeopleCount { get; set; }
        public int PeopleWithTheApp { get; set; }
        public int PhonesReached { get; set; }

        public virtual ICollection<FieldLaunchRecipient> Recipients { get; set; } = new List<FieldLaunchRecipient>();
    }

    /// <summary>One person a launch was sent to (item 252).</summary>
    public partial class FieldLaunchRecipient
    {
        public Guid Id { get; set; }
        public Guid FieldLaunchId { get; set; }
        public Guid AppUserId { get; set; }

        public virtual FieldLaunch FieldLaunch { get; set; } = null!;
    }
}
