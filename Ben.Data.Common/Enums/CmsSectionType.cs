namespace Ben.Data.Common.Enums;

/// <summary>Identifies the kind of content stored in a <c>CmsSection</c>.</summary>
public enum CmsSectionType
{
    /// <summary>Rich-text content authored with the Telerik Editor. ContentJson contains the HTML string.</summary>
    RichText = 1,

    /// <summary>Image or banner section. ContentJson contains image URL/file reference, alt text, and link.</summary>
    ImageBanner = 2,

    /// <summary>Gallery of org-uploaded files. ContentJson contains an array of UploadFile IDs to display.</summary>
    FileGallery = 3,

    /// <summary>Contact info block surfacing org phones, emails, links, and addresses.</summary>
    ContactInfo = 4,

    /// <summary>Roster of org members with optional display fields (name, role, bio).</summary>
    MemberRoster = 5,

    /// <summary>Free-form HTML block. ContentJson contains raw HTML authored by the user.</summary>
    CustomHtml = 6,

    /// <summary>
    /// A selection of the group's own investigations, resolved and redacted by the server.
    /// </summary>
    /// <remarks>
    /// Unlike every type above, the stored <c>ContentJson</c> is <b>not</b> what a visitor receives.
    /// It holds ids and switches; the public endpoint replaces it with a projection built from the
    /// live records, so redaction runs on every request and a later privacy change takes effect
    /// immediately. Storing a snapshot would freeze whatever was true on the day it was embedded.
    /// </remarks>
    EmbeddedInvestigations = 7,

    /// <summary>A selection of the group's own cases, resolved and redacted like <see cref="EmbeddedInvestigations"/>.</summary>
    EmbeddedCases = 8,

    /// <summary>
    /// Photos and files chosen from one of the group's own cases — the case-bound slot item #80's
    /// page templates were for.
    /// </summary>
    /// <remarks>
    /// <para>Resolved like the two above, and for a sharper reason. The stored content holds a case
    /// id and file ids; whether each of those files may still be shown is asked again on every
    /// request, through <c>CaseMediaPublication</c>. A timeline entry pulled back from Public next
    /// month takes its photo off this page without anybody remembering which pages used it.</para>
    ///
    /// <para>Copying the file into the section at fill-in time would have been far simpler and is
    /// exactly wrong: it would make a published page immune to somebody changing their mind.</para>
    /// </remarks>
    CaseMedia = 9,


    /// <summary>An event's programme — its sessions, by night, with rooms and places left.</summary>
    /// <remarks>
    /// Resolved on every request like <see cref="EmbeddedInvestigations"/> and
    /// <see cref="CaseMedia"/>, and for the same reason twice over: a session cancelled this
    /// morning must not still be advertised this afternoon, and "3 of 15 left" is a number that
    /// is wrong the moment it is written down.
    /// </remarks>
    EventProgramme = 10,

    /// <summary>The card a visitor asks for a place through.</summary>
    /// <remarks>Resolved: it must know whether bookings have closed and whether this reader
    /// already has one.</remarks>
    EventBooking = 11,

    /// <summary>An event's own pictures.</summary>
    EventGallery = 12,

    /// <summary>The venue: where it is, its history, its rooms and what they sleep.</summary>
    /// <remarks>
    /// Resolved because the history and photos may be BORROWED from the venue under a grant that
    /// can be revoked (item 235 phase 8). A snapshot would keep showing a building's story after
    /// its owner took the loan back.
    /// </remarks>
    EventVenue = 13
}
