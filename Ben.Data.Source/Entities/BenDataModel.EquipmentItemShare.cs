using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One personal <see cref="EquipmentItem"/> shared with one <see cref="Organization"/>, so that
    /// group's members can see the owner has it. Per item and per group, not a list-level toggle:
    /// an owner can put most of their kit in front of a group while keeping one piece out of it.
    /// </summary>
    /// <remarks>
    /// <para>Only valid for personal items — org-owned gear belongs to a group already, and the
    /// controller rejects an attempt to share it rather than letting a meaningless row exist.</para>
    ///
    /// <para>Sharing is about <i>visibility</i> alone. Whether the piece can actually be borrowed
    /// is <see cref="EquipmentItem.LoanAudience"/>, deliberately separate: telling a group you own
    /// something is not offering to lend it.</para>
    ///
    /// <para>A share row never exposes the owner's serial number, not even to the group it is
    /// shared with — see <c>EquipmentAccess</c>, which resolves that field server-side.</para>
    /// </remarks>
    public class EquipmentItemShare : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid EquipmentItemId { get; set; }
        public Guid OrganizationId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual EquipmentItem EquipmentItem { get; set; } = null!;
        public virtual Organization Organization { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
