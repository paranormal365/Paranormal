using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.Source.Context
{
    public partial class BenDataContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
    {
        public BenDataContext(DbContextOptions<BenDataContext> options) : base(options) { }

        public virtual DbSet<AppUser> AppUsers { get; set; }
        public virtual DbSet<UserAddressType> UserAddressTypes { get; set; }
        public virtual DbSet<UserEmailType> UserEmailTypes { get; set; }
        public virtual DbSet<UserPhoneType> UserPhoneTypes { get; set; }
        public virtual DbSet<UserLinkType> UserLinkTypes { get; set; }
        public virtual DbSet<UserMessageType> UserMessageTypes { get; set; }
        public virtual DbSet<UserNoteType> UserNoteTypes { get; set; }
        public virtual DbSet<UserAddress> UserAddresses { get; set; }
        public virtual DbSet<UserEmail> UserEmails { get; set; }
        public virtual DbSet<UserPhone> UserPhones { get; set; }
        public virtual DbSet<UserLink> UserLinks { get; set; }
        public virtual DbSet<UserMessage> UserMessages { get; set; }
        public virtual DbSet<UserMessageTo> UserMessageTos { get; set; }
        public virtual DbSet<UserNote> UserNotes { get; set; }
        public virtual DbSet<Organization> Organizations { get; set; }
        public virtual DbSet<OrganizationAddress> OrganizationAddresses { get; set; }
        public virtual DbSet<OrganizationEmail> OrganizationEmails { get; set; }
        public virtual DbSet<OrganizationPhone> OrganizationPhones { get; set; }
        public virtual DbSet<OrganizationLink> OrganizationLinks { get; set; }
        public virtual DbSet<OrganizationNote> OrganizationNotes { get; set; }
        public virtual DbSet<OrganizationAddressType> OrganizationAddressTypes { get; set; }
        public virtual DbSet<OrganizationEmailType> OrganizationEmailTypes { get; set; }
        public virtual DbSet<OrganizationLinkType> OrganizationLinkTypes { get; set; }
        public virtual DbSet<OrganizationPhoneType> OrganizationPhoneTypes { get; set; }
        public virtual DbSet<OrganizationNoteType> OrganizationNoteTypes { get; set; }
        public virtual DbSet<OrganizationPage> OrganizationPages { get; set; }
        public virtual DbSet<OrganizationLogo> OrganizationLogos { get; set; }
        public virtual DbSet<OrgMemberGroup> OrgMemberGroups { get; set; }
        public virtual DbSet<OrgMemberGroupMembership> OrgMemberGroupMemberships { get; set; }
        public virtual DbSet<CmsSection> CmsSections { get; set; }
        public virtual DbSet<CmsPagePermission> CmsPagePermissions { get; set; }
        public virtual DbSet<OrganizationUserMembership> OrganizationUserMemberships { get; set; }
        public virtual DbSet<OrganizationAccessGrant> OrganizationAccessGrants { get; set; }
        public virtual DbSet<OrganizationMembershipRequest> OrganizationMembershipRequests { get; set; }
        public virtual DbSet<OrganizationFile> OrganizationFiles { get; set; }
        public virtual DbSet<OrganizationFileDeleteLog> OrganizationFileDeleteLogs { get; set; }
        public virtual DbSet<OrganizationAddressMapConfig> OrganizationAddressMapConfigs { get; set; }
        public virtual DbSet<UploadFileType> UploadFileTypes { get; set; }
        public virtual DbSet<UploadFileTypeExtension> UploadFileTypeExtensions { get; set; }
        public virtual DbSet<UploadFile> UploadFiles { get; set; }
        public virtual DbSet<UploadFileOrganizationShare> UploadFileOrganizationShares { get; set; }
        public virtual DbSet<UploadFilePermissionRequest> UploadFilePermissionRequests { get; set; }
        public virtual DbSet<UploadFileAudioConfig> UploadFileAudioConfigs { get; set; }
        public virtual DbSet<UploadFileRegionNote> UploadFileRegionNotes { get; set; }
        public virtual DbSet<UploadFileVote> UploadFileVotes { get; set; }
        public virtual DbSet<AuditLog> AuditLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder); // configures Identity tables

            // Keep table name consistent with original schema
            modelBuilder.Entity<AppUser>().ToTable("AppUsers");

            // ── UserAddressType ──────────────────────────────────────────────
            modelBuilder.Entity<UserAddressType>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserAddressType>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── UserEmailType ────────────────────────────────────────────────
            modelBuilder.Entity<UserEmailType>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserEmailType>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── UserPhoneType ────────────────────────────────────────────────
            modelBuilder.Entity<UserPhoneType>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserPhoneType>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── UserLinkType ─────────────────────────────────────────────────
            modelBuilder.Entity<UserLinkType>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserLinkType>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── UserMessageType ──────────────────────────────────────────────
            modelBuilder.Entity<UserMessageType>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserMessageType>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── UserNoteType ─────────────────────────────────────────────────
            modelBuilder.Entity<UserNoteType>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserNoteType>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── UserAddress ──────────────────────────────────────────────────
            modelBuilder.Entity<UserAddress>()
                .HasOne(e => e.UserAddressType).WithMany(e => e.UserAddresses)
                .HasForeignKey(e => e.UserAddressTypeId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserAddress>()
                .HasOne(e => e.AppUser).WithMany(e => e.UserAddresses)
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UserAddress>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserAddress>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── UserEmail ────────────────────────────────────────────────────
            modelBuilder.Entity<UserEmail>()
                .HasOne(e => e.UserEmailType).WithMany(e => e.UserEmails)
                .HasForeignKey(e => e.UserEmailTypeId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserEmail>()
                .HasOne(e => e.AppUser).WithMany(e => e.UserEmails)
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserEmail>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserEmail>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── UserPhone ────────────────────────────────────────────────────
            modelBuilder.Entity<UserPhone>()
                .HasOne(e => e.UserPhoneType).WithMany(e => e.UserPhones)
                .HasForeignKey(e => e.UserPhoneTypeId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserPhone>()
                .HasOne(e => e.AppUser).WithMany(e => e.UserPhones)
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserPhone>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserPhone>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── UserLink ─────────────────────────────────────────────────────
            modelBuilder.Entity<UserLink>()
                .HasOne(e => e.UserLinkType).WithMany(e => e.UserLinks)
                .HasForeignKey(e => e.UserLinkTypeId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserLink>()
                .HasOne(e => e.AppUser).WithMany(e => e.UserLinks)
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserLink>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserLink>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserLink>()
                .HasOne<AppUser>().WithMany()
                .HasForeignKey(e => e.VerifiedApprovedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── UserMessage ──────────────────────────────────────────────────
            modelBuilder.Entity<UserMessage>()
                .HasOne(e => e.UserMessageType).WithMany(e => e.UserMessages)
                .HasForeignKey(e => e.UserMessageTypeId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserMessage>()
                .HasOne(e => e.CreatedByAppUser).WithMany(e => e.CreatedMessages)
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserMessage>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserMessage>()
                .HasOne<UserMessage>().WithMany()
                .HasForeignKey(e => e.ParentMessageId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── UserMessageTo ────────────────────────────────────────────────
            modelBuilder.Entity<UserMessageTo>()
                .HasOne(e => e.UserMessage).WithMany(e => e.UserMessageTos)
                .HasForeignKey(e => e.MessageId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UserMessageTo>()
                .HasOne(e => e.ToAppUser).WithMany(e => e.ReceivedUserMessageTos)
                .HasForeignKey(e => e.ToAppUserId).OnDelete(DeleteBehavior.NoAction);

            // ── UserNote ─────────────────────────────────────────────────────
            modelBuilder.Entity<UserNote>()
                .HasOne(e => e.UserNoteType).WithMany(e => e.UserNotes)
                .HasForeignKey(e => e.UserNoteTypeId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserNote>()
                .HasOne(e => e.CreatedByAppUser).WithMany(e => e.CreatedUserNotes)
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserNote>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserNote>()
                .HasOne<UserNote>().WithMany()
                .HasForeignKey(e => e.ParentNoteId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── Organization ─────────────────────────────────────────────────
            modelBuilder.Entity<Organization>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<Organization>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── OrganizationAddress ──────────────────────────────────────────
            modelBuilder.Entity<OrganizationAddress>()
                .HasOne(e => e.Organization).WithMany(e => e.OrganizationAddresses)
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationAddress>()
                .HasOne(e => e.OrganizationAddressType).WithMany(e => e.OrganizationAddresses)
                .HasForeignKey(e => e.OrganizationAddressTypeId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationAddress>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationAddress>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── OrganizationEmail ────────────────────────────────────────────
            modelBuilder.Entity<OrganizationEmail>()
                .HasOne(e => e.Organization).WithMany(e => e.OrganizationEmails)
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationEmail>()
                .HasOne(e => e.OrganizationEmailType).WithMany(e => e.OrganizationEmails)
                .HasForeignKey(e => e.OrganizationEmailTypeId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationEmail>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationEmail>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── OrganizationPhone ────────────────────────────────────────────
            modelBuilder.Entity<OrganizationPhone>()
                .HasOne(e => e.Organization).WithMany(e => e.OrganizationPhones)
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationPhone>()
                .HasOne(e => e.OrganizationPhoneType).WithMany(e => e.OrganizationPhones)
                .HasForeignKey(e => e.OrganizationPhoneTypeId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationPhone>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationPhone>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── OrganizationLink ─────────────────────────────────────────────
            modelBuilder.Entity<OrganizationLink>()
                .HasOne(e => e.Organization).WithMany(e => e.OrganizationLinks)
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationLink>()
                .HasOne(e => e.OrganizationLinkType).WithMany(e => e.OrganizationLinks)
                .HasForeignKey(e => e.OrganizationLinkTypeId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationLink>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationLink>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationLink>()
                .HasOne<AppUser>().WithMany()
                .HasForeignKey(e => e.VerifiedApprovedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── OrganizationNote ─────────────────────────────────────────────
            modelBuilder.Entity<OrganizationNote>()
                .HasOne(e => e.Organization).WithMany(e => e.OrganizationNotes)
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationNote>()
                .HasOne(e => e.OrganizationNoteType).WithMany(e => e.OrganizationNotes)
                .HasForeignKey(e => e.OrganizationNoteTypeId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationNote>()
                .HasOne(e => e.ParentNote).WithMany(e => e.ChildNotes)
                .HasForeignKey(e => e.ParentNoteId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationNote>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationNote>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── OrganizationAddressType ──────────────────────────────────────
            modelBuilder.Entity<OrganizationAddressType>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationAddressType>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── OrganizationEmailType ────────────────────────────────────────
            modelBuilder.Entity<OrganizationEmailType>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationEmailType>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── OrganizationLinkType ─────────────────────────────────────────
            modelBuilder.Entity<OrganizationLinkType>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationLinkType>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── OrganizationPhoneType ────────────────────────────────────────
            modelBuilder.Entity<OrganizationPhoneType>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationPhoneType>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── OrganizationNoteType ─────────────────────────────────────────
            modelBuilder.Entity<OrganizationNoteType>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationNoteType>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── OrganizationPage ─────────────────────────────────────────────
            modelBuilder.Entity<OrganizationPage>()
                .HasOne(e => e.Organization).WithMany(e => e.OrganizationPages)
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationPage>()
                .HasOne(e => e.ParentPage).WithMany(e => e.ChildPages)
                .HasForeignKey(e => e.ParentPageId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationPage>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationPage>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── OrganizationLogo ─────────────────────────────────────────────
            modelBuilder.Entity<OrganizationLogo>()
                .HasOne(e => e.Organization).WithMany(e => e.OrganizationLogos)
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationLogo>()
                .HasOne(e => e.UploadFile).WithMany()
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationLogo>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationLogo>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── OrgMemberGroup ───────────────────────────────────────────────
            modelBuilder.Entity<OrgMemberGroup>()
                .HasOne(e => e.Organization).WithMany(e => e.MemberGroups)
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrgMemberGroup>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgMemberGroup>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── OrgMemberGroupMembership ─────────────────────────────────────
            modelBuilder.Entity<OrgMemberGroupMembership>()
                .HasIndex(e => new { e.OrgMemberGroupId, e.OrganizationUserMembershipId })
                .IsUnique();
            modelBuilder.Entity<OrgMemberGroupMembership>()
                .HasOne(e => e.OrgMemberGroup).WithMany(e => e.Members)
                .HasForeignKey(e => e.OrgMemberGroupId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrgMemberGroupMembership>()
                .HasOne(e => e.OrganizationUserMembership).WithMany()
                .HasForeignKey(e => e.OrganizationUserMembershipId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgMemberGroupMembership>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);

            // ── CmsSection ───────────────────────────────────────────────────
            modelBuilder.Entity<CmsSection>()
                .HasOne(e => e.OrganizationPage).WithMany(e => e.CmsSections)
                .HasForeignKey(e => e.OrganizationPageId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<CmsSection>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CmsSection>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CmsSection>()
                .Property(e => e.ContentJson).HasColumnType("nvarchar(max)");

            // ── CmsPagePermission ────────────────────────────────────────────
            modelBuilder.Entity<CmsPagePermission>()
                .HasOne(e => e.OrganizationPage).WithMany(e => e.PagePermissions)
                .HasForeignKey(e => e.OrganizationPageId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<CmsPagePermission>()
                .HasOne(e => e.AppUser).WithMany()
                .HasForeignKey(e => e.AppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CmsPagePermission>()
                .HasOne(e => e.OrgMemberGroup).WithMany()
                .HasForeignKey(e => e.OrgMemberGroupId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CmsPagePermission>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CmsPagePermission>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── OrganizationUserMembership ──────────────────────────────────
            modelBuilder.Entity<OrganizationUserMembership>().ToTable("OrganizationUserMemberships");
            modelBuilder.Entity<OrganizationUserMembership>()
                .HasIndex(e => new { e.OrganizationId, e.AppUserId })
                .IsUnique();
            modelBuilder.Entity<OrganizationUserMembership>()
                .HasOne<Organization>()
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationUserMembership>()
                .HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(e => e.AppUserId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationUserMembership>()
                .HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId)
                .OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationUserMembership>()
                .HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.NoAction);

            // ── OrganizationAccessGrant ─────────────────────────────────────
            modelBuilder.Entity<OrganizationAccessGrant>().ToTable("OrganizationAccessGrants");
            modelBuilder.Entity<OrganizationAccessGrant>()
                .HasIndex(e => new { e.OrganizationId, e.AppUserId, e.TableName })
                .IsUnique();
            modelBuilder.Entity<OrganizationAccessGrant>()
                .HasOne<Organization>()
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationAccessGrant>()
                .HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(e => e.AppUserId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationAccessGrant>()
                .HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId)
                .OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationAccessGrant>()
                .HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.NoAction);

            // ── UploadFileType ───────────────────────────────────────────────
            modelBuilder.Entity<UploadFileType>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFileType>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── UploadFileTypeExtension ──────────────────────────────────────
            modelBuilder.Entity<UploadFileTypeExtension>()
                .HasOne(e => e.UploadFileType).WithMany(e => e.AllowedExtensions)
                .HasForeignKey(e => e.UploadFileTypeId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UploadFileTypeExtension>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFileTypeExtension>()
                .HasIndex(e => new { e.UploadFileTypeId, e.Pattern })
                .IsUnique();

            // ── UploadFile ───────────────────────────────────────────────────
            modelBuilder.Entity<UploadFile>()
                .HasOne(e => e.UploadFileType).WithMany(e => e.UploadFiles)
                .HasForeignKey(e => e.UploadFileTypeId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFile>()
                .HasOne(e => e.AppUser).WithMany()
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UploadFile>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFile>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFile>()
                .Property(e => e.FileData).HasColumnType("varbinary(max)").IsRequired(false);

            modelBuilder.Entity<UploadFile>()
                .Property(e => e.StoragePath).HasMaxLength(500).IsRequired(false);

            // ── UploadFileOrganizationShare ───────────────────────────────────
            modelBuilder.Entity<UploadFileOrganizationShare>()
                .HasIndex(e => new { e.UploadFileId, e.OrganizationId })
                .IsUnique();
            modelBuilder.Entity<UploadFileOrganizationShare>()
                .HasOne(e => e.UploadFile).WithMany(e => e.OrganizationShares)
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UploadFileOrganizationShare>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UploadFileOrganizationShare>()
                .HasOne(e => e.SharedByAppUser).WithMany()
                .HasForeignKey(e => e.SharedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFileOrganizationShare>()
                .HasOne(e => e.RemovedByAppUser).WithMany()
                .HasForeignKey(e => e.RemovedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFileOrganizationShare>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFileOrganizationShare>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── UploadFilePermissionRequest ───────────────────────────────────
            modelBuilder.Entity<UploadFilePermissionRequest>()
                .HasOne(e => e.UploadFile).WithMany(e => e.PermissionRequests)
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UploadFilePermissionRequest>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFilePermissionRequest>()
                .HasOne(e => e.RequestedByAppUser).WithMany()
                .HasForeignKey(e => e.RequestedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFilePermissionRequest>()
                .HasOne(e => e.ReviewedByAppUser).WithMany()
                .HasForeignKey(e => e.ReviewedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFilePermissionRequest>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFilePermissionRequest>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── AuditLog ─────────────────────────────────────────────────────
            // No FK to AppUser — audit records are intentionally independent of user lifecycle.
            modelBuilder.Entity<AuditLog>().ToTable("AuditLogs");
            modelBuilder.Entity<AuditLog>()
                .Property(e => e.EntityType).HasMaxLength(128).IsRequired();
            modelBuilder.Entity<AuditLog>()
                .Property(e => e.Source).HasMaxLength(64).IsRequired();
            modelBuilder.Entity<AuditLog>()
                .Property(e => e.ChangesJson).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<AuditLog>()
                .HasIndex(e => new { e.EntityType, e.EntityId });
            modelBuilder.Entity<AuditLog>()
                .HasIndex(e => e.UserId);
            modelBuilder.Entity<AuditLog>()
                .HasIndex(e => e.OccurredAt);

            // ── UploadFile self-reference (clip parent/child) ─────────────────
            modelBuilder.Entity<UploadFile>()
                .HasOne(e => e.ParentFile).WithMany(e => e.ChildClips)
                .HasForeignKey(e => e.ParentFileId)
                .IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── UploadFileAudioConfig ────────────────────────────────────────
            // One-to-one with UploadFile; cascade so config is deleted with the file.
            modelBuilder.Entity<UploadFileAudioConfig>()
                .HasOne(e => e.UploadFile).WithOne(e => e.AudioConfig)
                .HasForeignKey<UploadFileAudioConfig>(e => e.UploadFileId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UploadFileAudioConfig>()
                .HasIndex(e => e.UploadFileId).IsUnique();
            modelBuilder.Entity<UploadFileAudioConfig>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFileAudioConfig>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            // JSON option columns — nvarchar(max) to accommodate any plugin config
            modelBuilder.Entity<UploadFileAudioConfig>()
                .Property(e => e.HoverOptionsJson).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<UploadFileAudioConfig>()
                .Property(e => e.TimelineOptionsJson).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<UploadFileAudioConfig>()
                .Property(e => e.ZoomOptionsJson).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<UploadFileAudioConfig>()
                .Property(e => e.MinimapOptionsJson).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<UploadFileAudioConfig>()
                .Property(e => e.SpectrogramOptionsJson).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<UploadFileAudioConfig>()
                .Property(e => e.SpectrogramWindowedOptionsJson).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<UploadFileAudioConfig>()
                .Property(e => e.EnvelopeOptionsJson).HasColumnType("nvarchar(max)");

            // ── UploadFileRegionNote ─────────────────────────────────────────
            modelBuilder.Entity<UploadFileRegionNote>()
                .HasOne(e => e.UploadFile).WithMany(e => e.RegionNotes)
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UploadFileRegionNote>()
                .HasIndex(e => e.UploadFileId);
            modelBuilder.Entity<UploadFileRegionNote>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFileRegionNote>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFileRegionNote>()
                .Property(e => e.NoteHtml).HasColumnType("nvarchar(max)");

            // ── UploadFileVote ───────────────────────────────────────────────
            // One vote per (user, file): unique index enforces the business rule.
            modelBuilder.Entity<UploadFileVote>()
                .HasOne(e => e.UploadFile).WithMany(e => e.Votes)
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UploadFileVote>()
                .HasOne(e => e.AppUser).WithMany()
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFileVote>()
                .HasIndex(e => new { e.UploadFileId, e.AppUserId }).IsUnique();

            // ── OrganizationMembershipRequest ────────────────────────────────
            modelBuilder.Entity<OrganizationMembershipRequest>().ToTable("OrganizationMembershipRequests");
            modelBuilder.Entity<OrganizationMembershipRequest>()
                .HasOne(e => e.Organization).WithMany(e => e.MembershipRequests)
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationMembershipRequest>()
                .HasOne(e => e.Applicant).WithMany()
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationMembershipRequest>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationMembershipRequest>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationMembershipRequest>()
                .HasIndex(e => new { e.OrganizationId, e.AppUserId });
            modelBuilder.Entity<OrganizationMembershipRequest>()
                .Property(e => e.RequestMessage).HasMaxLength(2000).IsRequired(false);

            // ── OrganizationFile ─────────────────────────────────────────────
            modelBuilder.Entity<OrganizationFile>().ToTable("OrganizationFiles");
            modelBuilder.Entity<OrganizationFile>()
                .HasOne(e => e.Organization).WithMany(e => e.OrganizationFiles)
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationFile>()
                .HasOne(e => e.UploadFileType).WithMany()
                .HasForeignKey(e => e.UploadFileTypeId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationFile>()
                .HasOne(e => e.SourceUploadFile).WithMany()
                .HasForeignKey(e => e.SourceUploadFileId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationFile>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationFile>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationFile>()
                .Property(e => e.FileData).HasColumnType("varbinary(max)").IsRequired(false);
            modelBuilder.Entity<OrganizationFile>()
                .Property(e => e.StoragePath).HasMaxLength(500).IsRequired(false);
            modelBuilder.Entity<OrganizationFile>()
                .HasOne(e => e.PublishedByAppUser).WithMany()
                .HasForeignKey(e => e.PublishedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── OrganizationFileDeleteLog ────────────────────────────────────
            // Intentionally no FKs — immutable audit snapshot, same pattern as AuditLog.
            modelBuilder.Entity<OrganizationFileDeleteLog>().ToTable("OrganizationFileDeleteLogs");
            modelBuilder.Entity<OrganizationFileDeleteLog>()
                .Property(e => e.OrganizationName).HasMaxLength(256).IsRequired();
            modelBuilder.Entity<OrganizationFileDeleteLog>()
                .Property(e => e.FileName).HasMaxLength(512).IsRequired();
            modelBuilder.Entity<OrganizationFileDeleteLog>()
                .Property(e => e.ContentType).HasMaxLength(128).IsRequired();
            modelBuilder.Entity<OrganizationFileDeleteLog>()
                .Property(e => e.StoragePath).HasMaxLength(500).IsRequired(false);
            modelBuilder.Entity<OrganizationFileDeleteLog>()
                .Property(e => e.WasPublishedByDisplayName).HasMaxLength(256).IsRequired(false);
            modelBuilder.Entity<OrganizationFileDeleteLog>()
                .Property(e => e.DeletedByDisplayName).HasMaxLength(256).IsRequired();
            modelBuilder.Entity<OrganizationFileDeleteLog>()
                .HasIndex(e => e.OrganizationId);
            modelBuilder.Entity<OrganizationFileDeleteLog>()
                .HasIndex(e => e.DeletedByAppUserId);

            // ── OrganizationAddressMapConfig ──────────────────────────────────
            // One-to-one with OrganizationAddress; cascade so config is deleted with the address.
            modelBuilder.Entity<OrganizationAddressMapConfig>().ToTable("OrganizationAddressMapConfigs");
            modelBuilder.Entity<OrganizationAddressMapConfig>()
                .HasOne(e => e.OrganizationAddress).WithOne(e => e.MapConfig)
                .HasForeignKey<OrganizationAddressMapConfig>(e => e.OrganizationAddressId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationAddressMapConfig>()
                .HasIndex(e => e.OrganizationAddressId).IsUnique();
            modelBuilder.Entity<OrganizationAddressMapConfig>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationAddressMapConfig>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationAddressMapConfig>()
                .Property(e => e.MarkerColor).HasMaxLength(50);
            modelBuilder.Entity<OrganizationAddressMapConfig>()
                .Property(e => e.RegionFillColor).HasMaxLength(50);
            modelBuilder.Entity<OrganizationAddressMapConfig>()
                .Property(e => e.RegionStrokeColor).HasMaxLength(50);
            modelBuilder.Entity<OrganizationAddressMapConfig>()
                .Property(e => e.MarkerIconKey).HasMaxLength(64).IsRequired(false);
        }
    }
}
