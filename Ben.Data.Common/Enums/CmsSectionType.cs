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
    EmbeddedCases = 8
}
