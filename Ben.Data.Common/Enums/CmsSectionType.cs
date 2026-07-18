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
    CustomHtml = 6
}
