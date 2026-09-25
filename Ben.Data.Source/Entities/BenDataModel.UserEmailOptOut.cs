using System;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One letter one person has asked not to receive.
    /// </summary>
    /// <remarks>
    /// <para><b>Opt-OUTS only.</b> A row means "not this one"; no row means the letter is wanted.
    /// Storing the refusals rather than the permissions is what makes a letter added next year
    /// arrive by default instead of silently reaching nobody because no one has a row saying yes —
    /// which is the failure mode of a permissions table nobody backfills.</para>
    ///
    /// <para>It also keeps the table small: almost nobody declines almost anything, so this holds
    /// the exceptions rather than a row per person per letter.</para>
    ///
    /// <para><b>Only kinds that may be declined reach here.</b> Proving an address, resetting a
    /// password and a receipt are not on the screen at all — see
    /// <see cref="Ben.Data.Common.Mail.MailKindInfo.CanDecline"/> — so a row for one of them
    /// should never exist, and the send path checks the flag before it checks the table.</para>
    /// </remarks>
    public partial class UserEmailOptOut : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid AppUserId { get; set; }
        public AppUser? AppUser { get; set; }

        /// <summary>The letter's key, as <see cref="Ben.Data.Common.Mail.MailKinds"/> declares it.</summary>
        public string Kind { get; set; } = null!;

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }
    }
}
