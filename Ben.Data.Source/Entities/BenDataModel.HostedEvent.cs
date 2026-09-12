using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// An event somebody puts on: the product, not a date in a diary (item 235).
    /// </summary>
    /// <remarks>
    /// <para><b>Ben, 2026-09-11:</b> "The idea is to allow someone to schedule and track and
    /// organize an event that is not ghost hunting related." A weekend at a haunted hotel with
    /// classes and presentations, a writers' retreat, a monthly murder-mystery dinner — the same
    /// record with different words in the description. Nothing here says "paranormal", and nothing
    /// should.</para>
    ///
    /// <para><b>The same shape as <see cref="Tour"/>, on purpose.</b> A tour is the thing a
    /// business sells and a date is an <see cref="OrgCalendarEvent"/> that names it. An event is
    /// the same: this row is the product, and exactly one calendar row — the umbrella — carries it
    /// into every part of the site that already understands a public event. The phone in somebody's
    /// pocket, the public list, the reminder job, the calendar file and the <c>/o/{org}/events/…</c>
    /// URL all keep working with no change at all, which is the whole reason for the umbrella.</para>
    ///
    /// <para><b>A stay or a run.</b> <see cref="DatesAreSeparate"/> decides which. False is a stay:
    /// consecutive nights of one event, one booking spanning several of them — the haunted-hotel
    /// weekend. True is a run: the event is the <i>production</i> and each date is a performance of
    /// it, booked on its own, the way a resident play company sells a monthly show with dinner.
    /// Ben's case, 2026-09-11: "The Thomas House hosts a play company. They put on plays there
    /// about once a month and sell dinner with the tickets."</para>
    ///
    /// <para><b>It is billed once either way</b>, which is the point of the flag existing at all. A
    /// monthly show is one production on twelve dates, not twelve events; counting the dates would
    /// charge a company twelve times for one show.</para>
    ///
    /// <para><b>Archiving, not deleting.</b> An event that has run has bookings, a programme and
    /// people's photographs behind it. Archiving stops it counting and takes it off the list;
    /// nothing that happened is touched.</para>
    /// </remarks>
    public partial class HostedEvent : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }

        /// <summary>
        /// What the host calls it — and for a run, the name of the production rather than of any
        /// one night. Required; unique within the organization.
        /// </summary>
        /// <remarks>
        /// Unique for the reason a tour's name is: it is what tells two of them apart when
        /// everything else about them matches. "Murder at the Manor" is one production however
        /// many times it is performed, and a second event of the same name in the same venue is
        /// the same production scheduled again, not a new one.
        /// </remarks>
        public string Name { get; set; } = null!;

        /// <summary>
        /// The readable part of the public URL — <c>/o/{org}/events/{UrlName}</c>, shared with the
        /// umbrella calendar row so one address serves both.
        /// </summary>
        /// <remarks>
        /// Generated from the name at creation and then left alone. A slug that chased a renamed
        /// event would break every link already printed on a poster, and the poster is the point.
        /// </remarks>
        public string UrlName { get; set; } = null!;

        /// <summary>One line under the name: "Three nights at the Thomas House".</summary>
        public string? Tagline { get; set; }

        /// <summary>What a visitor is told, cleaned like any other public markup.</summary>
        public string? Description { get; set; }

        /// <summary>The venue. Required — an event happens somewhere.</summary>
        /// <remarks>
        /// A shared <see cref="Place"/>, not an address of the organization's own, because the
        /// host is often not the owner: an event company runs a weekend at a hotel it does not own,
        /// and the hotel may be a customer in its own right. Refused for a private residence, the
        /// same rule a public calendar event follows.
        /// </remarks>
        public Guid PlaceId { get; set; }

        /// <summary>Withhold the exact address from anybody without a place.</summary>
        public bool HideExactLocation { get; set; }

        /// <summary>The zone the event actually happens in, as an IANA id ("America/Chicago").</summary>
        /// <remarks>
        /// A guest is told "Saturday, doors at 7:00 PM", and that sentence is true in exactly one
        /// zone. Held on the event rather than read from the reader's browser for the same reason a
        /// tour holds one.
        /// </remarks>
        public string TimeZoneId { get; set; } = "America/Chicago";

        /// <summary>The first date. A date, not a moment: the times live on each night.</summary>
        public DateTime StartsOn { get; set; }

        /// <summary>The last date, on or after <see cref="StartsOn"/>.</summary>
        public DateTime EndsOn { get; set; }

        /// <summary>
        /// True when each date stands on its own — a run of performances rather than one stay.
        /// </summary>
        /// <remarks>
        /// <para>False (the default) is a weekend: the nights are consecutive, a booking may span
        /// several of them, and a room is held for each night booked.</para>
        ///
        /// <para>True is a production: every <see cref="HostedEventNight"/> is an independent date
        /// with its own capacity, its own menu and its own bookings, and nobody is staying over.
        /// The dates need not be consecutive — a monthly show is twelve of them across a year — so
        /// <see cref="StartsOn"/> and <see cref="EndsOn"/> bracket the run rather than describe
        /// it.</para>
        ///
        /// <para>It changes what the words on the screen are, and nothing about what is billed.</para>
        /// </remarks>
        public bool DatesAreSeparate { get; set; }

        /// <summary>When doors open, in the event's own zone, unless a night says otherwise.</summary>
        public TimeSpan? DefaultStartLocal { get; set; }

        /// <summary>When it ends, in the event's own zone, unless a night says otherwise.</summary>
        public TimeSpan? DefaultEndLocal { get; set; }

        /// <summary>
        /// Whether it is live: on the public site, taking bookings, and costing something.
        /// </summary>
        /// <remarks>
        /// <b>Publishing is the moment money happens</b> (Ben, 2026-09-11). It spends an event
        /// credit for a host without a plan, and starts occupying a slot for one with a plan.
        /// Everything before it — nights, rooms, programme, menus, staff, files, the page — is free
        /// and reversible, because a draft has no page, takes no bookings and sends no
        /// confirmations. Publishing is precisely the act that lets other people's answers start
        /// arriving, which is the last moment at which stopping is still free.
        /// </remarks>
        public bool IsPublished { get; set; }

        /// <summary>When it was first published, so nothing is ever charged for twice.</summary>
        /// <remarks>
        /// Re-publishing an event that has been taken down and put back up must not spend a second
        /// credit or take a second slot. One event, one charge, for the life of the event.
        /// </remarks>
        public DateTime? FirstPublishedUtc { get; set; }

        /// <summary>How many people may come who are not staying. Null = no limit, 0 = none.</summary>
        public int? DayPassCapacity { get; set; }

        /// <summary>
        /// What a day pass costs, shown to guests and <b>never charged</b>.
        /// </summary>
        /// <remarks>
        /// Ben, 2026-09-12: <i>"We can tell them the price, but we do not collect money."</i> The
        /// venue settles it with the guest. Null is "ask the venue"; zero is a day pass that is
        /// genuinely free, and the two read differently to somebody deciding whether to come.
        /// </remarks>
        public decimal? DayPassPrice { get; set; }

        /// <summary>
        /// What this event allocates to its guests — rooms to sleep in, or seats to sit in.
        /// </summary>
        /// <remarks>
        /// <para><b>One per event</b> (Ben, 2026-09-12). There is no hotel that is also a theatre
        /// on the same weekend, so an event picks a kind and gets one plan. Saying it here rather
        /// than inferring it from which fields happen to be filled in is what keeps the booking
        /// screen answerable: a guest is asked for a room, or asked for a seat, never asked which
        /// sort of thing they would like to be asked for.</para>
        ///
        /// <para>Rooms is the default, because a hosted event is an overnight stay until somebody
        /// says otherwise.</para>
        /// </remarks>
        public HostedEventLayoutKind LayoutKind { get; set; } = HostedEventLayoutKind.Rooms;

        /// <summary>
        /// When the venue stops taking requests, in UTC. Null means it never does.
        /// </summary>
        /// <remarks>
        /// A deadline the venue sets so it can cater and staff to a known number. Closing bookings
        /// does not touch the ones already made: a request still waiting on the day it closes is
        /// still the venue's to decide, because a guest who asked in time should not be refused by
        /// a clock while the host was asleep.
        /// </remarks>
        public DateTime? BookingsCloseAtUtc { get; set; }

        /// <summary>
        /// How to reach the host and how the money is settled — free text, shown to guests.
        /// </summary>
        /// <remarks>
        /// <b>The site takes nothing from a guest.</b> Confirming a booking means the host and the
        /// guest have settled it between themselves, exactly as a tour seat does. This line is
        /// where the host says how.
        /// </remarks>
        public string? ContactLine { get; set; }

        /// <summary>The picture at the top of the page.</summary>
        public Guid? CoverUploadFileId { get; set; }

        /// <summary>Subject of the mail a guest gets when their booking is confirmed.</summary>
        public string? MailSubjectTemplate { get; set; }

        /// <summary>Body of that mail, with the same placeholder grammar a tour's uses.</summary>
        public string? MailBodyTemplate { get; set; }

        /// <summary>
        /// Whether the paranormal surfaces are offered on this one.
        /// </summary>
        /// <remarks>
        /// Off by default, because most events are not ghost hunts. On, it opens the evidence queue
        /// and the archive path an attendee already knows from a public event. A venue running a
        /// play does not want an evidence queue on its page, and a lock-in does.
        /// </remarks>
        public bool CollectsEvidence { get; set; }

        /// <summary>When it stopped counting. Null while it is still on the books.</summary>
        /// <remarks>
        /// Written by the archive job fourteen days after the last date, unless a booking is still
        /// undecided. A host stops paying for last Halloween without having to remember to.
        /// </remarks>
        public DateTime? ArchivedAtUtc { get; set; }

        /// <summary>When it was called off. The row is kept so people can see that it is off.</summary>
        public DateTime? CancelledAtUtc { get; set; }

        /// <summary>Why it was called off, shown to anybody who had a place.</summary>
        public string? CancelledReason { get; set; }

        /// <summary>On the books: not archived.</summary>
        public bool IsActive => ArchivedAtUtc is null;

        /// <summary>Live: published, not archived, not called off — the state that costs.</summary>
        public bool IsLive => IsPublished && ArchivedAtUtc is null && CancelledAtUtc is null;

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual Place Place { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }

        /// <summary>The nights, or the performances — see <see cref="DatesAreSeparate"/>.</summary>
        public virtual ICollection<HostedEventNight> Nights { get; set; } = [];
        public virtual ICollection<HostedEventLayoutUnit> LayoutUnits { get; set; } = [];
        public virtual ICollection<HostedEventBooking> Bookings { get; set; } = [];

        /// <summary>
        /// The umbrella calendar row, and only ever one.
        /// </summary>
        /// <remarks>
        /// A collection because that is the shape EF wants for the inverse of a nullable foreign
        /// key; a unique index on <c>OrgCalendarEvent.HostedEventId</c> is what actually says
        /// "one".
        /// </remarks>
        public virtual ICollection<OrgCalendarEvent> CalendarRows { get; set; } = [];
    }

    /// <summary>
    /// One date of a hosted event: a night of a stay, or a performance of a run (item 235).
    /// </summary>
    /// <remarks>
    /// <para>Children rather than calendar rows of their own. Three nights of one weekend are one
    /// thing that happens, and listing them separately on the public calendar would show a visitor
    /// the same event three times and offer them three sign-ups to the same weekend.</para>
    ///
    /// <para>What a night is called depends on <see cref="HostedEvent.DatesAreSeparate"/>: a
    /// "night" of a stay, or a "date" of a production. The record does not care; the screens
    /// do.</para>
    /// </remarks>
    public partial class HostedEventNight : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid HostedEventId { get; set; }

        /// <summary>The calendar date, in the event's own zone. Unique within the event.</summary>
        public DateTime Date { get; set; }

        /// <summary>What this one is called — "Opening night", "Saturday". Optional.</summary>
        public string? Title { get; set; }

        /// <summary>When this one starts, in the event's zone. Falls back to the event's default.</summary>
        public TimeSpan? StartLocal { get; set; }

        /// <summary>When this one ends, in the event's zone. Falls back to the event's default.</summary>
        public TimeSpan? EndLocal { get; set; }

        /// <summary>Anything the host wants said about this date in particular.</summary>
        public string? Notes { get; set; }

        /// <summary>Order shown. Date order unless the host says otherwise.</summary>
        public int SortOrder { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEvent HostedEvent { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
