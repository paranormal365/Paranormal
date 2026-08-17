using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Something a group has built once and wants to build again — a section, or a whole page's
    /// worth of them.
    /// </summary>
    /// <remarks>
    /// <para>The user half of the template library. We ship the <b>blocks</b>: a card, a collapsible
    /// list, a carousel. A group assembles those into something of its own — an investigation
    /// write-up layout, a standard "about us" — and saves it here to start from next time.</para>
    ///
    /// <para><b>Owned by the organization, not the person who saved it.</b> It is the group's site,
    /// and a member leaving should not take its building blocks with them — the same reasoning as
    /// group-owned equipment.</para>
    ///
    /// <para><b>Inserting a template copies it.</b> A page built from a template does not track it,
    /// so editing the template later leaves existing pages alone. A reference would be more powerful
    /// and much more surprising: nobody expects tidying a template to rewrite a page that has been
    /// live for a year. The FAQ promotion in the equipment work made the same call.</para>
    ///
    /// <para><see cref="ContentJson"/> holds a section's own content for
    /// <see cref="CmsTemplateScope.Section"/>, and an ordered array of sections for
    /// <see cref="CmsTemplateScope.Page"/>. It is sanitized on save like any other author markup —
    /// that a block came from our palette says nothing about what was typed into it afterwards.</para>
    /// </remarks>
    public class OrganizationCmsTemplate : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }

        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        public CmsTemplateScope Scope { get; set; }

        /// <summary>Meaningful only for a section template; ignored for a page one.</summary>
        public CmsSectionType SectionType { get; set; }

        public string ContentJson { get; set; } = "{}";

        public int SortOrder { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
