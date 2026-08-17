namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A web address an organization used to have, kept working after they changed it.
    /// </summary>
    /// <remarks>
    /// <para><b>A URL is a promise to everyone who wrote it down.</b> An organization's address is
    /// the one part of this product that ends up on a business card, in a client's bookmarks and in
    /// a social post that nobody can edit afterwards. Renaming used to break every one of those
    /// silently — the old address simply stopped resolving, and neither the group nor the person
    /// following the link would ever learn why.</para>
    ///
    /// <para><b>Aliases are kept, never reassigned.</b> Once a group has held an address, no other
    /// group may take it, even long after they stopped using it. Handing <c>/o/ghost-squad</c> to a
    /// different group would quietly point somebody's saved link at strangers, which is worse than
    /// the link being dead — a broken link says "gone", while a captured one says something false.
    /// </para>
    ///
    /// <para><b>This is only for organizations.</b> Cases, investigations and events generate their
    /// slug once and never change it, so they have nothing to alias. If any of those ever becomes
    /// editable, it needs this same treatment on the same day.</para>
    /// </remarks>
    public class OrganizationUrlNameAlias
    {
        public Guid Id { get; set; }

        /// <summary>The organization that holds this address.</summary>
        public Guid OrganizationId { get; set; }

        /// <summary>The old address, normalized exactly as a current one is.</summary>
        public string UrlName { get; set; } = null!;

        /// <summary>When it stopped being the current address.</summary>
        public DateTime DateCreated { get; set; }

        /// <summary>Who made the change that retired it.</summary>
        public Guid? CreatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
    }
}
