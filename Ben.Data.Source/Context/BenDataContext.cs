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
        public virtual DbSet<AppUserPhoto> AppUserPhotos { get; set; }
        public virtual DbSet<SupportTicket> SupportTickets { get; set; }
        public virtual DbSet<SupportTicketReply> SupportTicketReplies { get; set; }
        public virtual DbSet<SiteSetting> SiteSettings { get; set; }
        public virtual DbSet<SignInEvent> SignInEvents { get; set; }
        public virtual DbSet<EventReminderSent> EventReminderSents { get; set; }
        public virtual DbSet<VideoAsset> VideoAssets { get; set; }
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
        public virtual DbSet<OrganizationRole> OrganizationRoles { get; set; }
        public virtual DbSet<OrganizationRolePermission> OrganizationRolePermissions { get; set; }
        public virtual DbSet<OrganizationRoleMembership> OrganizationRoleMemberships { get; set; }
        public virtual DbSet<CmsSection> CmsSections { get; set; }
        public virtual DbSet<CmsPagePermission> CmsPagePermissions { get; set; }
        public virtual DbSet<ExperienceCategory> ExperienceCategories { get; set; }
        public virtual DbSet<ExperienceType> ExperienceTypes { get; set; }
        public virtual DbSet<OrganizationAreaOfOperation> OrganizationAreaOfOperations { get; set; }
        public virtual DbSet<OrganizationUserMembership> OrganizationUserMemberships { get; set; }
        public virtual DbSet<OrganizationMemberLevel> OrganizationMemberLevels { get; set; }
        public virtual DbSet<InvestigationDuty> InvestigationDuties { get; set; }
        public virtual DbSet<InvestigationDutyAssignment> InvestigationDutyAssignments { get; set; }
        public virtual DbSet<CaseContact> CaseContacts { get; set; }
        public virtual DbSet<OrganizationAccessGrant> OrganizationAccessGrants { get; set; }
        public virtual DbSet<OrganizationUrlNameAlias> OrganizationUrlNameAliases { get; set; }
        public virtual DbSet<OrganizationMembershipRequest> OrganizationMembershipRequests { get; set; }
        public virtual DbSet<OrganizationMembershipQuestion> OrganizationMembershipQuestions { get; set; }
        public virtual DbSet<OrganizationMembershipAnswer> OrganizationMembershipAnswers { get; set; }
        public virtual DbSet<MembershipReviewVote> MembershipReviewVotes { get; set; }
        public virtual DbSet<OrganizationFile> OrganizationFiles { get; set; }
        public virtual DbSet<OrganizationFileDeleteLog> OrganizationFileDeleteLogs { get; set; }
        public virtual DbSet<OrganizationAddressMapConfig> OrganizationAddressMapConfigs { get; set; }
        public virtual DbSet<OrganizationAddressMemberAccess> OrganizationAddressMemberAccesses { get; set; }
        public virtual DbSet<UploadFileType> UploadFileTypes { get; set; }
        public virtual DbSet<UploadFileTypeExtension> UploadFileTypeExtensions { get; set; }
        public virtual DbSet<UploadFile> UploadFiles { get; set; }
        public virtual DbSet<UploadFileOrganizationShare> UploadFileOrganizationShares { get; set; }
        public virtual DbSet<UploadFileShare> UploadFileShares { get; set; }
        public virtual DbSet<UploadFileComment> UploadFileComments { get; set; }
        public virtual DbSet<UploadFilePermissionRequest> UploadFilePermissionRequests { get; set; }
        public virtual DbSet<ClientRequest> ClientRequests { get; set; }
        public virtual DbSet<ClientRequestOrganization> ClientRequestOrganizations { get; set; }
        public virtual DbSet<ClientRequestFile> ClientRequestFiles { get; set; }
        public virtual DbSet<Place> Places { get; set; }
        public virtual DbSet<Case> Cases { get; set; }
        public virtual DbSet<CaseTimelineEntry> CaseTimelineEntries { get; set; }
        public virtual DbSet<CaseTimelineEntryExperienceType> CaseTimelineEntryExperienceTypes { get; set; }
        public virtual DbSet<CaseTimelineEntryFile> CaseTimelineEntryFiles { get; set; }
        public virtual DbSet<OrgMessage> OrgMessages { get; set; }
        public virtual DbSet<OrgMessageRecipient> OrgMessageRecipients { get; set; }
        public virtual DbSet<OrgMessageView> OrgMessageViews { get; set; }
        public virtual DbSet<OrgMessageMention> OrgMessageMentions { get; set; }
        public virtual DbSet<OrgMessageHashtag> OrgMessageHashtags { get; set; }
        public virtual DbSet<OrgMessageReport> OrgMessageReports { get; set; }
        public virtual DbSet<UserFollow> UserFollows { get; set; }
        public virtual DbSet<Publication> Publications { get; set; }
        public virtual DbSet<PublicationPost> PublicationPosts { get; set; }
        public virtual DbSet<PublicationSubscription> PublicationSubscriptions { get; set; }
        public virtual DbSet<OrgCalendarEventType> OrgCalendarEventTypes { get; set; }
        public virtual DbSet<OrgCalendarEvent> OrgCalendarEvents { get; set; }
        public virtual DbSet<OrgCalendarEventAttendee> OrgCalendarEventAttendees { get; set; }
        public virtual DbSet<Investigation> Investigations { get; set; }
        public virtual DbSet<InvestigationAttendee> InvestigationAttendees { get; set; }
        public virtual DbSet<InvestigationFinding> InvestigationFindings { get; set; }
        public virtual DbSet<EvidenceVote> EvidenceVotes { get; set; }
        public virtual DbSet<CaseVote> CaseVotes { get; set; }
        public virtual DbSet<CaseTransferLog> CaseTransferLogs { get; set; }
        public virtual DbSet<CaseMessage> CaseMessages { get; set; }
        public virtual DbSet<CaseClientAccess> CaseClientAccesses { get; set; }
        public virtual DbSet<CaseClientInvite> CaseClientInvites { get; set; }
        public virtual DbSet<UploadFileMetadata> UploadFileMetadata { get; set; }
        public virtual DbSet<CaseReport> CaseReports { get; set; }
        public virtual DbSet<CaseReportSection> CaseReportSections { get; set; }
        public virtual DbSet<CaseReportSectionFile> CaseReportSectionFiles { get; set; }
        public virtual DbSet<CaseResearchEntry> CaseResearchEntries { get; set; }
        public virtual DbSet<CaseFile> CaseFiles { get; set; }
        public virtual DbSet<CaseRelatedPerson> CaseRelatedPeople { get; set; }
        public virtual DbSet<CaseNote> CaseNotes { get; set; }
        public virtual DbSet<InvestigationScheduleProposal> InvestigationScheduleProposals { get; set; }
        public virtual DbSet<ScheduleProposalSlot> ScheduleProposalSlots { get; set; }
        public virtual DbSet<UploadFileAudioConfig> UploadFileAudioConfigs { get; set; }
        public virtual DbSet<UploadFileRegionNote> UploadFileRegionNotes { get; set; }
        public virtual DbSet<UploadFileVote> UploadFileVotes { get; set; }
        public virtual DbSet<AudioMarker> AudioMarkers { get; set; }
        public virtual DbSet<AuditLog> AuditLogs { get; set; }
        public virtual DbSet<SidecarInstallLog> SidecarInstallLogs { get; set; }
        public virtual DbSet<EquipmentCategory> EquipmentCategories { get; set; }
        public virtual DbSet<EquipmentBrand> EquipmentBrands { get; set; }
        public virtual DbSet<EquipmentModel> EquipmentModels { get; set; }
        public virtual DbSet<EquipmentItem> EquipmentItems { get; set; }
        public virtual DbSet<EquipmentItemPhoto> EquipmentItemPhotos { get; set; }
        public virtual DbSet<EquipmentItemShare> EquipmentItemShares { get; set; }
        public virtual DbSet<EquipmentServiceLog> EquipmentServiceLogs { get; set; }

        public virtual DbSet<SubscriptionTier> SubscriptionTiers { get; set; }
        public virtual DbSet<SubscriptionTierPrice> SubscriptionTierPrices { get; set; }
        public virtual DbSet<SubscriptionTierLimit> SubscriptionTierLimits { get; set; }
        public virtual DbSet<SubscriptionTierPermissionArea> SubscriptionTierPermissionAreas { get; set; }
        public virtual DbSet<SubscriptionTierExcludedCapability> SubscriptionTierExcludedCapabilities { get; set; }
        public virtual DbSet<UserTourState> UserTourStates { get; set; }
        public virtual DbSet<OrganizationAd> OrganizationAds { get; set; }
        public virtual DbSet<SubscriptionContractTerms> SubscriptionContractTerms { get; set; }
        public virtual DbSet<TierChangeNotice> TierChangeNotices { get; set; }
        public virtual DbSet<EventEvidenceSubmission> EventEvidenceSubmissions { get; set; }
        public virtual DbSet<OrganizationSubscription> OrganizationSubscriptions { get; set; }
        public virtual DbSet<OrganizationBillingContact> OrganizationBillingContacts { get; set; }
        public virtual DbSet<Coupon> Coupons { get; set; }
        public virtual DbSet<CouponCode> CouponCodes { get; set; }
        public virtual DbSet<CouponRedemption> CouponRedemptions { get; set; }
        public virtual DbSet<EquipmentCheckout> EquipmentCheckouts { get; set; }
        public virtual DbSet<EquipmentCheckoutPhoto> EquipmentCheckoutPhotos { get; set; }
        public virtual DbSet<EquipmentCheckoutRenewal> EquipmentCheckoutRenewals { get; set; }
        public virtual DbSet<EquipmentItemFaq> EquipmentItemFaqs { get; set; }
        public virtual DbSet<EquipmentQuestion> EquipmentQuestions { get; set; }
        public virtual DbSet<EquipmentLoanFeedback> EquipmentLoanFeedbacks { get; set; }
        public virtual DbSet<OrganizationCmsTemplate> OrganizationCmsTemplates { get; set; }
        public virtual DbSet<EventAttendanceInvite> EventAttendanceInvites { get; set; }
        public virtual DbSet<VideoProject> VideoProjects { get; set; }

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
            modelBuilder.Entity<UserMessageType>()
                .HasIndex(e => e.Name).IsUnique();

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
            // Lat/lon require 10 decimal places for sub-metre precision
            modelBuilder.Entity<UserAddress>().Property(e => e.Latitude).HasPrecision(18, 10);
            modelBuilder.Entity<UserAddress>().Property(e => e.Longitude).HasPrecision(18, 10);

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
            // Lat/lon require 10 decimal places for sub-metre precision
            modelBuilder.Entity<OrganizationAddress>().Property(e => e.Latitude).HasPrecision(18, 10);
            modelBuilder.Entity<OrganizationAddress>().Property(e => e.Longitude).HasPrecision(18, 10);

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

            modelBuilder.Entity<Investigation>().Property(e => e.UrlName).HasMaxLength(140);
            modelBuilder.Entity<Investigation>()
                .HasIndex(e => new { e.OrganizationId, e.UrlName })
                .IsUnique()
                .HasFilter("[UrlName] IS NOT NULL");

            modelBuilder.Entity<Case>().Property(e => e.UrlName).HasMaxLength(120);
            // One slug per organization. Filtered: a private case has none, and a pile of nulls
            // would collide.
            modelBuilder.Entity<Case>()
                .HasIndex(e => new { e.OrganizationId, e.UrlName })
                .IsUnique()
                .HasFilter("[UrlName] IS NOT NULL");

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
            modelBuilder.Entity<EventAttendanceInvite>()
                .HasOne(e => e.OrgCalendarEvent).WithMany()
                .HasForeignKey(e => e.OrgCalendarEventId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<EventAttendanceInvite>()
                .HasOne(e => e.ConfirmedByAppUser).WithMany()
                .HasForeignKey(e => e.ConfirmedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EventAttendanceInvite>().Property(e => e.Email).HasMaxLength(320);
            modelBuilder.Entity<EventAttendanceInvite>().Property(e => e.DisplayName).HasMaxLength(200);
            modelBuilder.Entity<EventAttendanceInvite>().Property(e => e.Token).HasMaxLength(128);
            // The token is how the link is resolved, and it must be unique while it exists. Filtered
            // because it is cleared on confirmation and a pile of nulls would collide.
            modelBuilder.Entity<EventAttendanceInvite>()
                .HasIndex(e => e.Token).IsUnique().HasFilter("[Token] IS NOT NULL");
            // One pending invitation per address per event: asking twice is the same statement, and
            // this is what lets a resend reuse the row rather than litter.
            modelBuilder.Entity<EventAttendanceInvite>()
                .HasIndex(e => new { e.OrgCalendarEventId, e.Email });

            // NoAction: deleting a place must not delete the record that somebody met there.
            modelBuilder.Entity<OrgCalendarEvent>()
                .HasOne(e => e.Place).WithMany()
                .HasForeignKey(e => e.PlaceId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            // The public list is "this org's public events, soonest first".
            modelBuilder.Entity<OrgCalendarEvent>()
                .HasIndex(e => new { e.OrganizationId, e.IsPublic, e.StartDateTime });
            modelBuilder.Entity<OrgCalendarEvent>().Property(e => e.UrlName).HasMaxLength(120);
            // One slug per organization. Filtered, because private events have none and a pile of
            // nulls would collide.
            modelBuilder.Entity<OrgCalendarEvent>()
                .HasIndex(e => new { e.OrganizationId, e.UrlName })
                .IsUnique()
                .HasFilter("[UrlName] IS NOT NULL");

            modelBuilder.Entity<OrganizationCmsTemplate>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationCmsTemplate>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationCmsTemplate>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationCmsTemplate>().Property(e => e.Name).HasMaxLength(200);
            modelBuilder.Entity<OrganizationCmsTemplate>().Property(e => e.Description).HasMaxLength(500);
            // One name per group per kind: "Investigation Results" meaning two different things in
            // one group's picker is a worse problem than being asked to rename it.
            modelBuilder.Entity<OrganizationCmsTemplate>()
                .HasIndex(e => new { e.OrganizationId, e.Scope, e.Name }).IsUnique();

            // A draft points at the page it will replace. NoAction rather than Cascade: SQL Server
            // refuses a self-referencing cascade, and deleting a live page with an outstanding draft
            // should be a decision somebody makes explicitly, not a side effect.
            modelBuilder.Entity<OrganizationPage>()
                .HasOne(e => e.DraftOfOrganizationPage).WithMany(e => e.Drafts)
                .HasForeignKey(e => e.DraftOfOrganizationPageId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            // One draft per page. Filtered so the millions of live pages, all null, do not collide.
            modelBuilder.Entity<OrganizationPage>()
                .HasIndex(e => e.DraftOfOrganizationPageId)
                .IsUnique()
                .HasFilter("[DraftOfOrganizationPageId] IS NOT NULL");

            // ── VideoAsset ───────────────────────────────────────────────────
            // NoAction on both file FKs: deleting a file must not silently delete the catalog
            // entry that projects reference by id. Retire it with IsActive instead.
            modelBuilder.Entity<VideoAsset>()
                .HasOne(e => e.UploadFile).WithMany()
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<VideoAsset>()
                .HasOne(e => e.ThumbnailUploadFile).WithMany()
                .HasForeignKey(e => e.ThumbnailUploadFileId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<VideoAsset>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<VideoAsset>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<VideoAsset>().Property(e => e.Name).HasMaxLength(200).IsRequired();
            modelBuilder.Entity<VideoAsset>().Property(e => e.Description).HasMaxLength(1000);
            modelBuilder.Entity<VideoAsset>().Property(e => e.Category).HasMaxLength(100);
            modelBuilder.Entity<VideoAsset>().Property(e => e.Tags).HasMaxLength(500);
            modelBuilder.Entity<VideoAsset>().Property(e => e.PresetColors).HasMaxLength(500);
            modelBuilder.Entity<VideoAsset>().Property(e => e.ContentHash).HasMaxLength(64).IsRequired();
            // The catalog is read in full on every editor sync — index the filter it uses.
            modelBuilder.Entity<VideoAsset>().HasIndex(e => new { e.IsActive, e.SortOrder });

            // ── SiteSetting ──────────────────────────────────────────────────
            modelBuilder.Entity<SiteSetting>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<SiteSetting>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<SiteSetting>()
                .Property(e => e.Key).HasMaxLength(128).IsRequired();
            // Unique so a setting can't end up with two rows disagreeing about its value.
            modelBuilder.Entity<SiteSetting>()
                .HasIndex(e => e.Key).IsUnique();
            modelBuilder.Entity<SiteSetting>()
                .Property(e => e.Description).HasMaxLength(512);

            // ── SignInEvent ──────────────────────────────────────────────────
            // NoAction on the user FK, and the column is nullable: deleting an account must not
            // silently delete the record that it once signed in, and a failed attempt against an
            // address matching no account has no user to point at in the first place.
            modelBuilder.Entity<SignInEvent>()
                .HasOne(e => e.AppUser).WithMany()
                .HasForeignKey(e => e.AppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<SignInEvent>()
                .Property(e => e.Method).HasMaxLength(32).IsRequired();
            // Every dashboard query is "attempts between these dates", so the date leads. The
            // covering columns let the common counts be answered from the index alone.
            modelBuilder.Entity<SignInEvent>()
                .HasIndex(e => new { e.Utc, e.Succeeded });
            // "Who has signed in lately" — distinct users within a window.
            modelBuilder.Entity<SignInEvent>()
                .HasIndex(e => new { e.AppUserId, e.Utc });

            // ── EventReminderSent ────────────────────────────────────────────
            // The unique index IS the idempotency: the reminder job runs every few minutes and
            // would otherwise find the same event, and the same attendee, on every pass. Enforcing
            // it in the database rather than in the query means it still holds if two instances
            // ever run at once.
            modelBuilder.Entity<EventReminderSent>()
                .HasIndex(e => new { e.OrgCalendarEventId, e.AppUserId }).IsUnique();
            // Cascade from the event: a deleted event's reminder markers are meaningless, and the
            // job will never look for them again.
            modelBuilder.Entity<EventReminderSent>()
                .HasOne(e => e.OrgCalendarEvent).WithMany()
                .HasForeignKey(e => e.OrgCalendarEventId).OnDelete(DeleteBehavior.Cascade);
            // NoAction on the user, matching every other user FK here: deleting an account must
            // not cascade into unrelated tables.
            modelBuilder.Entity<EventReminderSent>()
                .HasOne(e => e.AppUser).WithMany()
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.NoAction);

            // ── AppUserPhoto ─────────────────────────────────────────────────
            // The subject FK cascades: deleting a user takes their photo rows. The
            // CreatedBy/UpdatedBy FKs to the same table must be NoAction or SQL Server sees
            // multiple cascade paths into AppUserPhotos and refuses the migration (error 1785).
            modelBuilder.Entity<AppUserPhoto>()
                .HasOne(e => e.AppUser).WithMany(e => e.Photos)
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<AppUserPhoto>()
                .HasOne(e => e.UploadFile).WithMany()
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<AppUserPhoto>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<AppUserPhoto>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            // One active photo per slot is the invariant the whole feature rests on — enforced
            // here rather than only in the controller, so a concurrent activate can't produce two.
            modelBuilder.Entity<AppUserPhoto>()
                .HasIndex(e => new { e.AppUserId, e.IsPublic })
                .HasFilter("[IsActive] = 1")
                .IsUnique();

            // ── SupportTicket ────────────────────────────────────────────────
            // Every FK is NoAction. A ticket outlives the account that raised it: deleting a user
            // must not erase the record of what they reported, or a staff member's replies.
            modelBuilder.Entity<SupportTicket>()
                .HasOne(e => e.AppUser).WithMany()
                .HasForeignKey(e => e.AppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<SupportTicket>()
                .HasOne(e => e.AssignedToAppUser).WithMany()
                .HasForeignKey(e => e.AssignedToAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            // The tracking link is the only credential on an anonymous thread, so a duplicate
            // token would hand one sender another's conversation.
            modelBuilder.Entity<SupportTicket>()
                .HasIndex(e => e.AccessToken).IsUnique();
            modelBuilder.Entity<SupportTicket>()
                .HasIndex(e => e.Reference).IsUnique();
            // The queue is read by status, newest first, on every admin page load.
            modelBuilder.Entity<SupportTicket>()
                .HasIndex(e => new { e.Status, e.DateCreated });
            // Rate limiting looks up recent submissions by sender — both halves of that check.
            modelBuilder.Entity<SupportTicket>()
                .HasIndex(e => new { e.FromEmail, e.DateCreated });
            modelBuilder.Entity<SupportTicket>()
                .HasIndex(e => new { e.SourceIpHash, e.DateCreated });

            // ── SupportTicketReply ───────────────────────────────────────────
            modelBuilder.Entity<SupportTicketReply>()
                .HasOne(e => e.SupportTicket).WithMany(e => e.Replies)
                .HasForeignKey(e => e.SupportTicketId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<SupportTicketReply>()
                .HasOne(e => e.AuthorAppUser).WithMany()
                .HasForeignKey(e => e.AuthorAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

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

            // ── OrganizationRole ─────────────────────────────────────────────────────
            modelBuilder.Entity<OrganizationRole>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationRole>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationRole>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── OrganizationRolePermission ──────────────────────────────────────────
            modelBuilder.Entity<OrganizationRolePermission>()
                .HasOne(e => e.OrganizationRole).WithMany(r => r.Permissions)
                .HasForeignKey(e => e.OrganizationRoleId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationRolePermission>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationRolePermission>()
                .HasIndex(e => new { e.OrganizationRoleId, e.TableName }).IsUnique();

            // ── OrganizationRoleMembership ──────────────────────────────────────────
            modelBuilder.Entity<OrganizationRoleMembership>()
                .HasOne(e => e.OrganizationRole).WithMany(r => r.Members)
                .HasForeignKey(e => e.OrganizationRoleId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationRoleMembership>()
                .HasOne(e => e.OrganizationUserMembership).WithMany()
                .HasForeignKey(e => e.OrganizationUserMembershipId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationRoleMembership>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationRoleMembership>()
                .HasIndex(e => new { e.OrganizationRoleId, e.OrganizationUserMembershipId }).IsUnique();

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

            // ── UploadFileShare ────────────────────────────────────────────────
            modelBuilder.Entity<UploadFileShare>()
                .HasIndex(e => new { e.UploadFileId, e.TargetType, e.IsActive });
            modelBuilder.Entity<UploadFileShare>()
                .HasIndex(e => e.TargetAppUserId);
            modelBuilder.Entity<UploadFileShare>()
                .HasIndex(e => e.TargetInvestigationId);
            modelBuilder.Entity<UploadFileShare>()
                .HasIndex(e => e.TargetOrganizationId);
            modelBuilder.Entity<UploadFileShare>()
                .HasOne(e => e.UploadFile).WithMany(e => e.Shares)
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UploadFileShare>()
                .HasOne(e => e.TargetAppUser).WithMany()
                .HasForeignKey(e => e.TargetAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFileShare>()
                .HasOne(e => e.TargetInvestigation).WithMany()
                .HasForeignKey(e => e.TargetInvestigationId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFileShare>()
                .HasOne(e => e.TargetOrganization).WithMany()
                .HasForeignKey(e => e.TargetOrganizationId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFileShare>()
                .HasOne(e => e.SharedByAppUser).WithMany()
                .HasForeignKey(e => e.SharedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFileShare>()
                .HasOne(e => e.RemovedByAppUser).WithMany()
                .HasForeignKey(e => e.RemovedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFileShare>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFileShare>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── UploadFileComment ─────────────────────────────────────────────
            modelBuilder.Entity<UploadFileComment>()
                .HasIndex(e => new { e.UploadFileId, e.DateCreated });
            modelBuilder.Entity<UploadFileComment>()
                .Property(e => e.Text).HasColumnType("nvarchar(max)").IsRequired();
            modelBuilder.Entity<UploadFileComment>()
                .HasOne(e => e.UploadFile).WithMany(e => e.Comments)
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UploadFileComment>()
                .HasOne(e => e.AuthorAppUser).WithMany()
                .HasForeignKey(e => e.AuthorAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFileComment>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFileComment>()
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

            // ── VideoProject ──────────────────────────────────────────────────
            modelBuilder.Entity<VideoProject>()
                .HasOne(e => e.Case).WithMany()
                .HasForeignKey(e => e.CaseId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<VideoProject>()
                .HasOne(e => e.PublishedUploadFile).WithMany()
                .HasForeignKey(e => e.PublishedUploadFileId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<VideoProject>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<VideoProject>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<VideoProject>()
                .Property(e => e.ProjectJson).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<VideoProject>()
                .HasIndex(e => e.CaseId);
            modelBuilder.Entity<VideoProject>()
                .HasIndex(e => e.CreatedByAppUserId);

            // ── UploadFile self-reference (clip parent/child) ─────────────────
            modelBuilder.Entity<UploadFile>()
                .HasOne(e => e.ParentFile).WithMany(e => e.ChildClips)
                .HasForeignKey(e => e.ParentFileId)
                .IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFile>()
                .Property(e => e.EditStateJson).HasColumnType("nvarchar(max)");

            // ── UploadFile self-reference (case-copy lineage, item #6 phase 2) ─
            // Deliberately a separate FK from ParentFile above — see the field's doc comment.
            modelBuilder.Entity<UploadFile>()
                .HasOne(e => e.CaseCopySourceFile).WithMany(e => e.CaseCopies)
                .HasForeignKey(e => e.CaseCopyOfUploadFileId)
                .IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UploadFile>()
                .Property(e => e.AllowInvestigationTeamComments).HasDefaultValue(false);
            modelBuilder.Entity<UploadFile>()
                .Property(e => e.AllowClientComments).HasDefaultValue(false);
            modelBuilder.Entity<UploadFile>()
                .Property(e => e.AllowOrganizationComments).HasDefaultValue(false);
            modelBuilder.Entity<UploadFile>()
                .Property(e => e.AllowPublicComments).HasDefaultValue(false);

            // ── UploadFile self-reference (archived prior version, item #6 phase 3) ─
            // Deliberately a separate FK from ParentFile/CaseCopySourceFile above — see the field's doc comment.
            modelBuilder.Entity<UploadFile>()
                .HasOne(e => e.ArchivedFromUploadFile).WithMany(e => e.ArchivedVersions)
                .HasForeignKey(e => e.ArchivedFromUploadFileId)
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

            // ── AudioMarker ────────────────────────────────────────────────
            modelBuilder.Entity<AudioMarker>()
                .HasOne(e => e.UploadFile).WithMany(e => e.AudioMarkers)
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<AudioMarker>()
                .HasIndex(e => e.UploadFileId);
            modelBuilder.Entity<AudioMarker>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<AudioMarker>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<AudioMarker>()
                .Property(e => e.Label).HasMaxLength(200);
            modelBuilder.Entity<AudioMarker>()
                .Property(e => e.Note).HasColumnType("nvarchar(max)");
            // NoAction, not Cascade: deleting a clip must not take the marker it was cut from with
            // it — the marker is the finding, the clip is one artefact of it.
            modelBuilder.Entity<AudioMarker>()
                .HasOne(e => e.LinkedClipUploadFile).WithMany()
                .HasForeignKey(e => e.LinkedClipUploadFileId).IsRequired(false)
                .OnDelete(DeleteBehavior.NoAction);
            // The review workflow always reads "this file's markers at this status" — render the
            // pending candidates, dedupe a re-scan against the dismissed ones.
            modelBuilder.Entity<AudioMarker>()
                .HasIndex(e => new { e.UploadFileId, e.ReviewStatus });

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
            // Only one Pending request per (org, user) at a time — filtered so a user can still
            // re-apply after a prior request was Accepted/Denied/Withdrawn.
            modelBuilder.Entity<OrganizationMembershipRequest>()
                .HasIndex(e => new { e.OrganizationId, e.AppUserId })
                .HasFilter("[Status] = 0")
                .IsUnique();
            modelBuilder.Entity<OrganizationMembershipRequest>()
                .Property(e => e.RequestMessage).HasMaxLength(2000).IsRequired(false);
            modelBuilder.Entity<OrganizationMembershipRequest>()
                .Property(e => e.DenialReason).HasMaxLength(2000).IsRequired(false);

            // ── OrganizationMembershipQuestion ────────────────────────────────
            modelBuilder.Entity<OrganizationMembershipQuestion>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationMembershipQuestion>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationMembershipQuestion>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationMembershipQuestion>()
                .Property(e => e.QuestionText).HasMaxLength(1000);

            // ── OrganizationMembershipAnswer ──────────────────────────────────
            modelBuilder.Entity<OrganizationMembershipAnswer>()
                .HasOne(e => e.MembershipRequest).WithMany(e => e.Answers)
                .HasForeignKey(e => e.OrganizationMembershipRequestId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationMembershipAnswer>()
                .HasOne(e => e.Question).WithMany()
                .HasForeignKey(e => e.OrganizationMembershipQuestionId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationMembershipAnswer>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationMembershipAnswer>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationMembershipAnswer>()
                .Property(e => e.AnswerText).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<OrganizationMembershipAnswer>()
                .HasIndex(e => new { e.OrganizationMembershipRequestId, e.OrganizationMembershipQuestionId }).IsUnique();

            // ── MembershipReviewVote ──────────────────────────────────────────
            modelBuilder.Entity<MembershipReviewVote>()
                .HasOne(e => e.MembershipRequest).WithMany(e => e.ReviewVotes)
                .HasForeignKey(e => e.OrganizationMembershipRequestId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<MembershipReviewVote>()
                .HasOne(e => e.VoterAppUser).WithMany()
                .HasForeignKey(e => e.VoterAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<MembershipReviewVote>()
                .Property(e => e.Comment).HasMaxLength(1000);
            modelBuilder.Entity<MembershipReviewVote>()
                .HasIndex(e => new { e.OrganizationMembershipRequestId, e.VoterAppUserId }).IsUnique();

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

            // ── OrganizationAddressMemberAccess ──────────────────────────────────────
            modelBuilder.Entity<OrganizationAddressMemberAccess>()
                .HasOne(e => e.OrganizationAddress).WithMany()
                .HasForeignKey(e => e.OrganizationAddressId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationAddressMemberAccess>()
                .HasOne(e => e.OrganizationUserMembership).WithMany()
                .HasForeignKey(e => e.OrganizationUserMembershipId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationAddressMemberAccess>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationAddressMemberAccess>()
                .HasIndex(e => new { e.OrganizationAddressId, e.OrganizationUserMembershipId }).IsUnique();

            // ── ExperienceCategory ───────────────────────────────────────────
            modelBuilder.Entity<ExperienceCategory>()
                .HasOne(e => e.ProposedByOrganization).WithMany()
                .HasForeignKey(e => e.ProposedByOrganizationId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<ExperienceCategory>()
                .HasOne(e => e.ApprovedByAppUser).WithMany()
                .HasForeignKey(e => e.ApprovedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<ExperienceCategory>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<ExperienceCategory>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── ExperienceType ───────────────────────────────────────────────
            // One name per category, enforced by the database. Both create paths check first, but a
            // check-then-insert is a race, and the equipment catalog has had this backstop since it
            // was built — the experience taxonomy grew the same way without one.
            // 100 to match the length the proposal endpoint has always advertised. The column was
            // nvarchar(max), so that limit was politeness rather than a rule, and an index cannot
            // cover an unbounded column anyway.
            modelBuilder.Entity<ExperienceType>()
                .Property(e => e.Name).HasMaxLength(100);
            modelBuilder.Entity<ExperienceType>()
                .HasIndex(e => new { e.ExperienceCategoryId, e.Name }).IsUnique();
            modelBuilder.Entity<ExperienceType>()
                .HasOne(e => e.ExperienceCategory).WithMany(e => e.ExperienceTypes)
                .HasForeignKey(e => e.ExperienceCategoryId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<ExperienceType>()
                .HasOne(e => e.ProposedByOrganization).WithMany()
                .HasForeignKey(e => e.ProposedByOrganizationId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<ExperienceType>()
                .HasOne(e => e.ApprovedByAppUser).WithMany()
                .HasForeignKey(e => e.ApprovedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<ExperienceType>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<ExperienceType>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── OrganizationAreaOfOperation ──────────────────────────────────
            modelBuilder.Entity<OrganizationAreaOfOperation>()
                .HasOne(e => e.Organization).WithOne(e => e.AreaOfOperation)
                .HasForeignKey<OrganizationAreaOfOperation>(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationAreaOfOperation>()
                .HasIndex(e => e.OrganizationId).IsUnique();
            modelBuilder.Entity<OrganizationAreaOfOperation>()
                .Property(e => e.CenterLatitude).HasPrecision(18, 10);
            modelBuilder.Entity<OrganizationAreaOfOperation>()
                .Property(e => e.CenterLongitude).HasPrecision(18, 10);
            modelBuilder.Entity<OrganizationAreaOfOperation>()
                .Property(e => e.RadiusMiles).HasPrecision(10, 2);
            modelBuilder.Entity<OrganizationAreaOfOperation>()
                .Property(e => e.DisplayLabel).HasMaxLength(256);
            modelBuilder.Entity<OrganizationAreaOfOperation>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationAreaOfOperation>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── ClientRequest ─────────────────────────────────────────────────
            modelBuilder.Entity<ClientRequest>()
                .HasOne(e => e.AppUser).WithMany()
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<ClientRequest>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<ClientRequest>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<ClientRequest>()
                .Property(e => e.Latitude).HasPrecision(18, 10);
            modelBuilder.Entity<ClientRequest>()
                .Property(e => e.Longitude).HasPrecision(18, 10);
            modelBuilder.Entity<ClientRequest>()
                .Property(e => e.Description).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<ClientRequest>()
                .Property(e => e.StreetAddress1).HasMaxLength(256);
            modelBuilder.Entity<ClientRequest>()
                .Property(e => e.City).HasMaxLength(128);
            modelBuilder.Entity<ClientRequest>()
                .Property(e => e.State).HasMaxLength(64);
            modelBuilder.Entity<ClientRequest>()
                .Property(e => e.ZipCode).HasMaxLength(20);
            modelBuilder.Entity<ClientRequest>()
                .Property(e => e.Country).HasMaxLength(64);

            // ── ClientRequestOrganization ─────────────────────────────────────
            modelBuilder.Entity<ClientRequestOrganization>()
                .HasOne(e => e.ClientRequest).WithMany(e => e.OrganizationApplications)
                .HasForeignKey(e => e.ClientRequestId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<ClientRequestOrganization>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<ClientRequestOrganization>()
                .HasOne(e => e.RespondedByAppUser).WithMany()
                .HasForeignKey(e => e.RespondedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<ClientRequestOrganization>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<ClientRequestOrganization>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<ClientRequestOrganization>()
                .HasIndex(e => new { e.ClientRequestId, e.OrganizationId }).IsUnique();

            // ── ClientRequestFile ─────────────────────────────────────────────
            modelBuilder.Entity<ClientRequestFile>()
                .HasOne(e => e.ClientRequest).WithMany(e => e.Files)
                .HasForeignKey(e => e.ClientRequestId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<ClientRequestFile>()
                .HasOne(e => e.UploadFile).WithMany()
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<ClientRequestFile>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<ClientRequestFile>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<ClientRequestFile>()
                .HasIndex(e => new { e.ClientRequestId, e.UploadFileId }).IsUnique();

            // ── Place ─────────────────────────────────────────────────────────
            modelBuilder.Entity<Place>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<Place>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<Place>()
                .Property(e => e.Latitude).HasPrecision(18, 10);
            modelBuilder.Entity<Place>()
                .Property(e => e.Longitude).HasPrecision(18, 10);
            modelBuilder.Entity<Place>()
                .Property(e => e.Name).HasMaxLength(256);
            modelBuilder.Entity<Place>()
                .Property(e => e.StreetAddress1).HasMaxLength(256);
            modelBuilder.Entity<Place>()
                .Property(e => e.StreetAddress2).HasMaxLength(256);
            modelBuilder.Entity<Place>()
                .Property(e => e.City).HasMaxLength(128);
            modelBuilder.Entity<Place>()
                .Property(e => e.State).HasMaxLength(64);
            modelBuilder.Entity<Place>()
                .Property(e => e.ZipCode).HasMaxLength(20);
            modelBuilder.Entity<Place>()
                .Property(e => e.Country).HasMaxLength(64);
            modelBuilder.Entity<Place>()
                .Property(e => e.GeocodeNote).HasMaxLength(512);
            // Rounded coordinates are what P8's deduplication will search on, and a name-only
            // landmark has none — so this indexes the lookup rather than enforcing uniqueness.
            // Uniqueness would be wrong here: two flats at one postcode are two places.
            modelBuilder.Entity<Place>()
                .HasIndex(e => new { e.Latitude, e.Longitude });

            // ── Case ──────────────────────────────────────────────────────────
            modelBuilder.Entity<Case>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.NoAction);
            // NoAction, not Cascade: deleting a place must never delete the cases that happened
            // there. Every FK added on this branch is NoAction anyway — both tables already reach
            // AppUser by another path, and SQL Server rejects the second cascade route (error 1785).
            modelBuilder.Entity<Case>()
                .HasOne(e => e.Place).WithMany(e => e.Cases)
                .HasForeignKey(e => e.PlaceId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<Case>()
                .HasOne(e => e.ClientRequest).WithMany()
                .HasForeignKey(e => e.ClientRequestId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<Case>()
                .HasOne(e => e.CaseManagerAppUser).WithMany()
                .HasForeignKey(e => e.CaseManagerAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<Case>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<Case>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<Case>()
                .Property(e => e.Description).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<Case>()
                .Property(e => e.Latitude).HasPrecision(18, 10);
            modelBuilder.Entity<Case>()
                .Property(e => e.Longitude).HasPrecision(18, 10);
            modelBuilder.Entity<Case>()
                .Property(e => e.Title).HasMaxLength(256);
            modelBuilder.Entity<Case>()
                .Property(e => e.PublicPseudonym).HasMaxLength(128);
            modelBuilder.Entity<Case>()
                .Property(e => e.ClientDisplayAlias).HasMaxLength(128);
            modelBuilder.Entity<Case>()
                .Property(e => e.StreetAddress1).HasMaxLength(256);
            modelBuilder.Entity<Case>()
                .Property(e => e.City).HasMaxLength(128);
            modelBuilder.Entity<Case>()
                .Property(e => e.State).HasMaxLength(64);
            modelBuilder.Entity<Case>()
                .Property(e => e.ZipCode).HasMaxLength(20);
            modelBuilder.Entity<Case>()
                .HasIndex(e => new { e.OrganizationId, e.CaseYear, e.OrgCaseNumber }).IsUnique();

            // ── OrganizationPage.CaseId ───────────────────────────────────────
            modelBuilder.Entity<OrganizationPage>()
                .HasOne(e => e.Case).WithMany()
                .HasForeignKey(e => e.CaseId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);

            // ── CaseTimelineEntry ─────────────────────────────────────────────
            modelBuilder.Entity<CaseTimelineEntry>()
                .HasOne(e => e.Case).WithMany(e => e.TimelineEntries)
                .HasForeignKey(e => e.CaseId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<CaseTimelineEntry>()
                .HasOne(e => e.AuthorAppUser).WithMany()
                .HasForeignKey(e => e.AuthorAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseTimelineEntry>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseTimelineEntry>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseTimelineEntry>()
                .Property(e => e.Body).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<CaseTimelineEntry>()
                .Property(e => e.Title).HasMaxLength(256);
            // NoAction rather than SetNull or Cascade. Cascade is wrong outright — deleting an
            // investigation must not delete the notes and readings taken during it, since the
            // findings outlive the visit. SetNull would express that, but SQL Server rejects it
            // here: Case already cascades to both Investigations and CaseTimelineEntries, so a
            // SetNull on this FK is a second path to the same rows ("may cause cycles or multiple
            // cascade paths", error 1785). InvestigationController.Delete therefore detaches the
            // entries explicitly before removing the investigation.
            modelBuilder.Entity<CaseTimelineEntry>()
                .HasOne(e => e.Investigation).WithMany()
                .HasForeignKey(e => e.InvestigationId).IsRequired(false)
                .OnDelete(DeleteBehavior.NoAction);
            // The binder query is always "this investigation's entries".
            modelBuilder.Entity<CaseTimelineEntry>()
                .HasIndex(e => e.InvestigationId);

            // ── CaseTimelineEntryExperienceType ───────────────────────────────
            modelBuilder.Entity<CaseTimelineEntryExperienceType>()
                .HasKey(e => new { e.CaseTimelineEntryId, e.ExperienceTypeId });
            modelBuilder.Entity<CaseTimelineEntryExperienceType>()
                .HasOne(e => e.CaseTimelineEntry).WithMany(e => e.ExperienceTypes)
                .HasForeignKey(e => e.CaseTimelineEntryId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<CaseTimelineEntryExperienceType>()
                .HasOne(e => e.ExperienceType).WithMany()
                .HasForeignKey(e => e.ExperienceTypeId).OnDelete(DeleteBehavior.NoAction);

            // ── CaseTimelineEntryFile ─────────────────────────────────────────
            modelBuilder.Entity<CaseTimelineEntryFile>()
                .HasOne(e => e.CaseTimelineEntry).WithMany(e => e.Files)
                .HasForeignKey(e => e.CaseTimelineEntryId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<CaseTimelineEntryFile>()
                .HasOne(e => e.UploadFile).WithMany()
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseTimelineEntryFile>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseTimelineEntryFile>()
                .HasIndex(e => new { e.CaseTimelineEntryId, e.UploadFileId }).IsUnique();

            // ── OrgMessage ────────────────────────────────────────────────────
            modelBuilder.Entity<OrgMessage>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgMessage>()
                .HasOne(e => e.AuthorAppUser).WithMany()
                .HasForeignKey(e => e.AuthorAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgMessage>()
                .HasOne(e => e.ParentMessage).WithMany(e => e.Replies)
                .HasForeignKey(e => e.ParentMessageId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgMessage>()
                .HasOne(e => e.Case).WithMany()
                .HasForeignKey(e => e.CaseId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgMessage>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgMessage>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgMessage>()
                .Property(e => e.Body).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<OrgMessage>()
                .Property(e => e.Subject).HasMaxLength(256);
            modelBuilder.Entity<OrgMessage>()
                .HasOne(e => e.HiddenByAppUser).WithMany()
                .HasForeignKey(e => e.HiddenByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            // The feed's own query: newest first, within one channel, excluding hidden posts.
            // HiddenUtc is in the key rather than in a filtered index because "hidden" is a normal
            // state the moderation queue reads too, not an exceptional one.
            modelBuilder.Entity<OrgMessage>()
                .HasIndex(e => new { e.ChannelType, e.HiddenUtc, e.DateCreated });

            // ── OrgMessageMention ─────────────────────────────────────────────
            modelBuilder.Entity<OrgMessageMention>()
                .HasOne(e => e.OrgMessage).WithMany(e => e.Mentions)
                .HasForeignKey(e => e.OrgMessageId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrgMessageMention>()
                .HasOne(e => e.MentionedAppUser).WithMany()
                .HasForeignKey(e => e.MentionedAppUserId).OnDelete(DeleteBehavior.NoAction);
            // Naming somebody twice in one post is one mention, not two notifications.
            modelBuilder.Entity<OrgMessageMention>()
                .HasIndex(e => new { e.OrgMessageId, e.MentionedAppUserId }).IsUnique();
            // "Which posts mentioned me, newest first" — the notification bucket's query.
            modelBuilder.Entity<OrgMessageMention>()
                .HasIndex(e => new { e.MentionedAppUserId, e.DateCreated });

            // ── OrgMessageHashtag ─────────────────────────────────────────────
            modelBuilder.Entity<OrgMessageHashtag>()
                .HasOne(e => e.OrgMessage).WithMany(e => e.Hashtags)
                .HasForeignKey(e => e.OrgMessageId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrgMessageHashtag>()
                .Property(e => e.Tag).HasMaxLength(64).IsRequired();
            // Using the same tag twice in one post is one tag.
            modelBuilder.Entity<OrgMessageHashtag>()
                .HasIndex(e => new { e.OrgMessageId, e.Tag }).IsUnique();
            // The tag page. Tag leads, because that is what is being looked up; the date orders
            // what comes back without a sort.
            modelBuilder.Entity<OrgMessageHashtag>()
                .HasIndex(e => new { e.Tag, e.DateCreated });

            // ── OrgMessageReport ──────────────────────────────────────────────
            modelBuilder.Entity<OrgMessageReport>()
                .HasOne(e => e.OrgMessage).WithMany(e => e.Reports)
                .HasForeignKey(e => e.OrgMessageId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrgMessageReport>()
                .HasOne(e => e.ReportedByAppUser).WithMany()
                .HasForeignKey(e => e.ReportedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgMessageReport>()
                .HasOne(e => e.ResolvedByAppUser).WithMany()
                .HasForeignKey(e => e.ResolvedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgMessageReport>()
                .Property(e => e.Reason).HasMaxLength(1000);
            // One report per person per post. Reporting a thing twice is not twice the signal, and
            // without this a single objector could make a post look like a pile-on.
            modelBuilder.Entity<OrgMessageReport>()
                .HasIndex(e => new { e.OrgMessageId, e.ReportedByAppUserId }).IsUnique();
            // The moderation queue: everything still pending, oldest first.
            modelBuilder.Entity<OrgMessageReport>()
                .HasIndex(e => new { e.Outcome, e.DateCreated });

            // ── UserFollow ────────────────────────────────────────────────────
            modelBuilder.Entity<UserFollow>()
                .HasOne(e => e.FollowerAppUser).WithMany()
                .HasForeignKey(e => e.FollowerAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserFollow>()
                .HasOne(e => e.FollowedAppUser).WithMany()
                .HasForeignKey(e => e.FollowedAppUserId).OnDelete(DeleteBehavior.NoAction);
            // Following somebody twice is following them once. The unique index is what makes
            // Follow idempotent without a read-then-write race.
            modelBuilder.Entity<UserFollow>()
                .HasIndex(e => new { e.FollowerAppUserId, e.FollowedAppUserId }).IsUnique();
            // "Who follows this person" — the follower count, and the other direction of the feed.
            modelBuilder.Entity<UserFollow>()
                .HasIndex(e => e.FollowedAppUserId);

            // ── Publication ──────────────────────────────────────────────────
            modelBuilder.Entity<Publication>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<Publication>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<Publication>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<Publication>().Property(e => e.Title).HasMaxLength(200).IsRequired();
            modelBuilder.Entity<Publication>().Property(e => e.UrlName).HasMaxLength(120).IsRequired();
            modelBuilder.Entity<Publication>().Property(e => e.Description).HasMaxLength(1000);
            // Unique across the whole site, not per organisation: the public address carries no
            // organisation in it, so two publications sharing a UrlName would mean /publications/x
            // serving whichever row came back first — the exact fault item 89 found on org URLs.
            modelBuilder.Entity<Publication>()
                .HasIndex(e => e.UrlName).IsUnique();

            // ── PublicationPost ──────────────────────────────────────────────
            modelBuilder.Entity<PublicationPost>()
                .HasOne(e => e.Publication).WithMany(e => e.Posts)
                .HasForeignKey(e => e.PublicationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<PublicationPost>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<PublicationPost>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<PublicationPost>().Property(e => e.Title).HasMaxLength(300).IsRequired();
            modelBuilder.Entity<PublicationPost>().Property(e => e.UrlName).HasMaxLength(160).IsRequired();
            modelBuilder.Entity<PublicationPost>().Property(e => e.Excerpt).HasMaxLength(1000);
            modelBuilder.Entity<PublicationPost>().Property(e => e.BodyHtml).HasColumnType("nvarchar(max)");
            // Unique within its publication only — two publications may each have a post called
            // "welcome", and their addresses differ by the publication that carries them.
            modelBuilder.Entity<PublicationPost>()
                .HasIndex(e => new { e.PublicationId, e.UrlName }).IsUnique();
            // The reader's query: this publication's published posts, newest first.
            modelBuilder.Entity<PublicationPost>()
                .HasIndex(e => new { e.PublicationId, e.PublishedUtc });

            // ── PublicationSubscription ──────────────────────────────────────
            modelBuilder.Entity<PublicationSubscription>()
                .HasOne(e => e.Publication).WithMany(e => e.Subscriptions)
                .HasForeignKey(e => e.PublicationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<PublicationSubscription>()
                .HasOne(e => e.SubscriberAppUser).WithMany()
                .HasForeignKey(e => e.SubscriberAppUserId).OnDelete(DeleteBehavior.NoAction);
            // One subscription per person per publication. Re-subscribing clears the cancellation
            // rather than adding a row, so this index is what keeps "am I subscribed" a single
            // question with a single answer.
            modelBuilder.Entity<PublicationSubscription>()
                .HasIndex(e => new { e.PublicationId, e.SubscriberAppUserId }).IsUnique();
            // "What am I subscribed to" — the reader's own list.
            modelBuilder.Entity<PublicationSubscription>()
                .HasIndex(e => new { e.SubscriberAppUserId, e.CancelledUtc });

            // ── OrgMessageRecipient ───────────────────────────────────────────
            modelBuilder.Entity<OrgMessageRecipient>()
                .HasOne(e => e.OrgMessage).WithMany(e => e.Recipients)
                .HasForeignKey(e => e.OrgMessageId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrgMessageRecipient>()
                .HasOne(e => e.RecipientAppUser).WithMany()
                .HasForeignKey(e => e.RecipientAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgMessageRecipient>()
                .HasIndex(e => new { e.OrgMessageId, e.RecipientAppUserId }).IsUnique();

            // ── OrgMessageView ────────────────────────────────────────────────
            modelBuilder.Entity<OrgMessageView>()
                .HasKey(e => new { e.OrgMessageId, e.ViewerAppUserId });
            modelBuilder.Entity<OrgMessageView>()
                .HasOne(e => e.OrgMessage).WithMany(e => e.Views)
                .HasForeignKey(e => e.OrgMessageId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrgMessageView>()
                .HasOne(e => e.ViewerAppUser).WithMany()
                .HasForeignKey(e => e.ViewerAppUserId).OnDelete(DeleteBehavior.NoAction);

            // ── OrgCalendarEventType ──────────────────────────────────────────
            modelBuilder.Entity<OrgCalendarEventType>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgCalendarEventType>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgCalendarEventType>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgCalendarEventType>()
                .Property(e => e.Name).HasMaxLength(128);

            // ── OrganizationMemberLevel (item 157) ────────────────────────────
            modelBuilder.Entity<OrganizationMemberLevel>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationMemberLevel>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationMemberLevel>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationMemberLevel>()
                .Property(e => e.Name).HasMaxLength(128);
            modelBuilder.Entity<OrganizationMemberLevel>()
                .HasIndex(e => new { e.OrganizationId, e.SortOrder });

            // Deleting a rung clears the title from members who held it rather than blocking —
            // a ladder edit must never be refused because somebody is standing on the rung.
            modelBuilder.Entity<OrganizationUserMembership>()
                .HasOne(e => e.MemberLevel).WithMany()
                .HasForeignKey(e => e.MemberLevelId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);

            // ── InvestigationDuty + assignments (item 158) ────────────────────
            modelBuilder.Entity<InvestigationDuty>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.NoAction);
            // Deleting a ladder rung nulls the eligibility requirement rather than blocking.
            modelBuilder.Entity<InvestigationDuty>()
                .HasOne(e => e.MinimumMemberLevel).WithMany()
                .HasForeignKey(e => e.MinimumMemberLevelId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<InvestigationDuty>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<InvestigationDuty>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<InvestigationDuty>()
                .Property(e => e.Name).HasMaxLength(128);
            modelBuilder.Entity<InvestigationDuty>()
                .HasIndex(e => new { e.OrganizationId, e.SortOrder });

            modelBuilder.Entity<InvestigationDutyAssignment>()
                .HasOne(e => e.InvestigationAttendee).WithMany()
                .HasForeignKey(e => e.InvestigationAttendeeId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<InvestigationDutyAssignment>()
                .HasOne(e => e.InvestigationDuty).WithMany()
                .HasForeignKey(e => e.InvestigationDutyId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<InvestigationDutyAssignment>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<InvestigationDutyAssignment>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            // One attendee holds a given duty once; "holds it twice" is a UI bug, not a state.
            modelBuilder.Entity<InvestigationDutyAssignment>()
                .HasIndex(e => new { e.InvestigationAttendeeId, e.InvestigationDutyId }).IsUnique();

            // ── CaseContact (item 158) ────────────────────────────────────────
            modelBuilder.Entity<CaseContact>()
                .HasOne(e => e.Case).WithMany()
                .HasForeignKey(e => e.CaseId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseContact>()
                .HasOne(e => e.AppUser).WithMany()
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseContact>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseContact>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseContact>()
                .HasIndex(e => new { e.CaseId, e.AppUserId }).IsUnique();

            // ── OrgCalendarEvent ──────────────────────────────────────────────
            modelBuilder.Entity<OrgCalendarEvent>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgCalendarEvent>()
                .HasOne(e => e.EventType).WithMany()
                .HasForeignKey(e => e.EventTypeId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<OrgCalendarEvent>()
                .HasOne(e => e.Case).WithMany()
                .HasForeignKey(e => e.CaseId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            // NoAction, not SetNull: SQL Server already refuses multiple cascade paths into this
            // table (error 1785), and an address removed from the org should surface as a stale
            // reference to fix rather than silently detaching itself from every event held there.
            modelBuilder.Entity<OrgCalendarEvent>()
                .HasOne(e => e.OrganizationAddress).WithMany()
                .HasForeignKey(e => e.OrganizationAddressId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgCalendarEvent>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgCalendarEvent>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgCalendarEvent>()
                .Property(e => e.Title).HasMaxLength(256);
            modelBuilder.Entity<OrgCalendarEvent>()
                .Property(e => e.Description).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<OrgCalendarEvent>()
                .Property(e => e.Location).HasMaxLength(512);
            modelBuilder.Entity<OrgCalendarEvent>()
                .Property(e => e.RecurrenceRule).HasMaxLength(512);

            // ── OrgCalendarEventAttendee ──────────────────────────────────────
            modelBuilder.Entity<OrgCalendarEventAttendee>()
                .HasOne(e => e.OrgCalendarEvent).WithMany(e => e.Attendees)
                .HasForeignKey(e => e.OrgCalendarEventId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrgCalendarEventAttendee>()
                .HasOne(e => e.AppUser).WithMany()
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgCalendarEventAttendee>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrgCalendarEventAttendee>()
                .Property(e => e.AssignedTask).HasMaxLength(512);
            modelBuilder.Entity<OrgCalendarEventAttendee>()
                .HasIndex(e => new { e.OrgCalendarEventId, e.AppUserId }).IsUnique();

            // ── Investigation ─────────────────────────────────────────────────
            modelBuilder.Entity<Investigation>()
                .HasOne(e => e.Case).WithMany()
                .HasForeignKey(e => e.CaseId).IsRequired(false).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Investigation>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.NoAction);
            // Every org-scoped list of investigations filters on this now, so it is worth an index
            // of its own rather than relying on the case's.
            modelBuilder.Entity<Investigation>()
                .HasIndex(e => e.OrganizationId);
            modelBuilder.Entity<Investigation>()
                .HasOne(e => e.OrgCalendarEvent).WithMany()
                .HasForeignKey(e => e.OrgCalendarEventId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<Investigation>()
                .HasOne(e => e.Place).WithMany(e => e.Investigations)
                .HasForeignKey(e => e.PlaceId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<Investigation>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<Investigation>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            // Without these the columns default to decimal(18,2) — about 1.1km of precision, which
            // is a pin in the wrong street. Every other coordinate column in the schema is (18,10);
            // these were missed when AddInvestigationCoordinates created them, and it went unnoticed
            // because nothing wrote them until Area 9's P2.
            modelBuilder.Entity<Investigation>()
                .Property(e => e.Latitude).HasPrecision(18, 10);
            modelBuilder.Entity<Investigation>()
                .Property(e => e.Longitude).HasPrecision(18, 10);
            modelBuilder.Entity<Investigation>()
                .Property(e => e.Title).HasMaxLength(256);
            modelBuilder.Entity<Investigation>()
                .Property(e => e.Description).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<Investigation>()
                .Property(e => e.Notes).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<Investigation>()
                .Property(e => e.Location).HasMaxLength(512);

            // ── InvestigationAttendee ─────────────────────────────────────────
            modelBuilder.Entity<InvestigationAttendee>()
                .HasOne(e => e.Investigation).WithMany(e => e.Attendees)
                .HasForeignKey(e => e.InvestigationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<InvestigationAttendee>()
                .HasOne(e => e.AppUser).WithMany()
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<InvestigationAttendee>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            // NoAction like every other AppUser path on this branch — SQL Server rejects a second
            // cascade route into a table it can already reach (error 1785).
            modelBuilder.Entity<InvestigationAttendee>()
                .HasOne(e => e.AttendanceRecordedByAppUser).WithMany()
                .HasForeignKey(e => e.AttendanceRecordedByAppUserId)
                .IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<InvestigationAttendee>()
                .Property(e => e.AssignedRole).HasMaxLength(128);
            modelBuilder.Entity<InvestigationAttendee>()
                .HasIndex(e => new { e.InvestigationId, e.AppUserId }).IsUnique();

            // ── InvestigationFinding ──────────────────────────────────────────
            modelBuilder.Entity<InvestigationFinding>()
                .HasOne(e => e.Investigation).WithMany(e => e.Findings)
                .HasForeignKey(e => e.InvestigationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<InvestigationFinding>()
                .HasOne(e => e.AppUser).WithMany()
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<InvestigationFinding>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            // One account per person per visit. The endpoint upserts rather than inserting, and
            // this is what stops a retry or a double-click turning into two accounts.
            modelBuilder.Entity<InvestigationFinding>()
                .HasIndex(e => new { e.InvestigationId, e.AppUserId }).IsUnique();

            // ── EvidenceVote ──────────────────────────────────────────────────
            modelBuilder.Entity<EvidenceVote>()
                .HasOne(e => e.UploadFile).WithMany()
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EvidenceVote>()
                .HasOne(e => e.VoterAppUser).WithMany()
                .HasForeignKey(e => e.VoterAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EvidenceVote>()
                .HasOne(e => e.VoterOrganization).WithMany()
                .HasForeignKey(e => e.VoterOrganizationId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EvidenceVote>()
                .HasOne(e => e.Case).WithMany()
                .HasForeignKey(e => e.CaseId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<EvidenceVote>()
                .Property(e => e.Comment).HasMaxLength(1000);
            modelBuilder.Entity<EvidenceVote>()
                .Property(e => e.VoterOrganizationName).HasMaxLength(200);
            modelBuilder.Entity<EvidenceVote>()
                .HasIndex(e => new { e.UploadFileId, e.VoterAppUserId }).IsUnique();

            // ── CaseVote ──────────────────────────────────────────────────────
            modelBuilder.Entity<CaseVote>()
                .HasOne(e => e.Case).WithMany()
                .HasForeignKey(e => e.CaseId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseVote>()
                .HasOne(e => e.VoterAppUser).WithMany()
                .HasForeignKey(e => e.VoterAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseVote>()
                .Property(e => e.Comment).HasMaxLength(1000);
            modelBuilder.Entity<CaseVote>()
                .HasIndex(e => new { e.CaseId, e.VoterAppUserId }).IsUnique();

            // ── CaseTransferLog ───────────────────────────────────────────────
            modelBuilder.Entity<CaseTransferLog>()
                .HasOne(e => e.Case).WithMany()
                .HasForeignKey(e => e.CaseId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseTransferLog>()
                .HasOne(e => e.FromOrganization).WithMany()
                .HasForeignKey(e => e.FromOrganizationId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseTransferLog>()
                .HasOne(e => e.ToOrganization).WithMany()
                .HasForeignKey(e => e.ToOrganizationId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseTransferLog>()
                .HasOne(e => e.ProposedByAppUser).WithMany()
                .HasForeignKey(e => e.ProposedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseTransferLog>()
                .HasOne(e => e.RespondedByAppUser).WithMany()
                .HasForeignKey(e => e.RespondedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseTransferLog>()
                .Property(e => e.TransferReason).HasMaxLength(1000);
            modelBuilder.Entity<CaseTransferLog>()
                .Property(e => e.RejectionReason).HasMaxLength(1000);

            // ── CaseMessage ─────────────────────────────────────────
            modelBuilder.Entity<CaseMessage>()
                .HasOne(e => e.Case).WithMany()
                .HasForeignKey(e => e.CaseId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseMessage>()
                .HasOne(e => e.AuthorAppUser).WithMany()
                .HasForeignKey(e => e.AuthorAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseMessage>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseMessage>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseMessage>()
                .Property(e => e.Body).HasMaxLength(4000);
            modelBuilder.Entity<CaseMessage>()
                .HasIndex(e => new { e.CaseId, e.DateCreated });

            // ── CaseClientAccess ────────────────────────────────────────
            modelBuilder.Entity<CaseClientAccess>()
                .HasOne(e => e.Case).WithMany()
                .HasForeignKey(e => e.CaseId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseClientAccess>()
                .HasOne(e => e.AppUser).WithMany()
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseClientAccess>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseClientAccess>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseClientAccess>()
                .HasIndex(e => new { e.CaseId, e.AppUserId }).IsUnique();

            // ── CaseClientInvite (item #4 remaining piece) ───────────────────
            modelBuilder.Entity<CaseClientInvite>()
                .HasOne(e => e.Case).WithMany()
                .HasForeignKey(e => e.CaseId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseClientInvite>()
                .HasOne(e => e.AcceptedByAppUser).WithMany()
                .HasForeignKey(e => e.AcceptedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseClientInvite>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseClientInvite>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseClientInvite>()
                .Property(e => e.Email).HasMaxLength(320); // RFC 5321 max
            modelBuilder.Entity<CaseClientInvite>()
                .Property(e => e.Token).HasMaxLength(64);
            modelBuilder.Entity<CaseClientInvite>()
                .HasIndex(e => e.Token).IsUnique();
            modelBuilder.Entity<CaseClientInvite>()
                .HasIndex(e => new { e.CaseId, e.Email });

            // ── CaseTimelineEntry.IpAddress ─────────────────────────────────
            modelBuilder.Entity<CaseTimelineEntry>()
                .Property(e => e.IpAddress).HasMaxLength(45); // supports IPv6

            // ── UploadFileMetadata ──────────────────────────────────────────
            modelBuilder.Entity<UploadFileMetadata>()
                .HasOne(e => e.UploadFile).WithOne()
                .HasForeignKey<UploadFileMetadata>(e => e.UploadFileId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UploadFileMetadata>()
                .Property(e => e.MediaKind).HasMaxLength(20);
            modelBuilder.Entity<UploadFileMetadata>()
                .Property(e => e.AudioCodec).HasMaxLength(50);
            modelBuilder.Entity<UploadFileMetadata>()
                .Property(e => e.CameraManufacturer).HasMaxLength(100);
            modelBuilder.Entity<UploadFileMetadata>()
                .Property(e => e.CameraModel).HasMaxLength(100);

            // ── CaseReport ──────────────────────────────────────────────────
            modelBuilder.Entity<CaseReport>()
                .HasOne(e => e.Case).WithMany()
                .HasForeignKey(e => e.CaseId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseReport>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseReport>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseReport>()
                .HasOne(e => e.PublishedByAppUser).WithMany()
                .HasForeignKey(e => e.PublishedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseReport>()
                .Property(e => e.Title).HasMaxLength(300);
            modelBuilder.Entity<CaseReport>()
                .HasIndex(e => e.CaseId);

            // ── CaseReportSection ────────────────────────────────────────────
            modelBuilder.Entity<CaseReportSection>()
                .HasOne(e => e.CaseReport).WithMany(e => e.Sections)
                .HasForeignKey(e => e.CaseReportId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<CaseReportSection>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseReportSection>()
                .Property(e => e.Title).HasMaxLength(300);

            // ── CaseReportSectionFile ────────────────────────────────────────
            modelBuilder.Entity<CaseReportSectionFile>()
                .HasOne(e => e.Section).WithMany(e => e.Files)
                .HasForeignKey(e => e.CaseReportSectionId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<CaseReportSectionFile>()
                .HasOne(e => e.UploadFile).WithMany()
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseReportSectionFile>()
                .Property(e => e.Caption).HasMaxLength(500);

            // ── CaseNote ──────────────────────────────────────────────────
            modelBuilder.Entity<CaseNote>()
                .HasOne(e => e.Case).WithMany()
                .HasForeignKey(e => e.CaseId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseNote>()
                .HasOne(e => e.AuthorAppUser).WithMany()
                .HasForeignKey(e => e.AuthorAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseNote>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseNote>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseNote>()
                .Property(e => e.Title).HasMaxLength(300);
            modelBuilder.Entity<CaseNote>()
                .Property(e => e.Body).HasMaxLength(10000);

            // ── CaseResearchEntry ─────────────────────────────────────
            modelBuilder.Entity<CaseResearchEntry>()
                .HasOne(e => e.Case).WithMany()
                .HasForeignKey(e => e.CaseId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseResearchEntry>()
                .HasOne(e => e.UploadFile).WithMany()
                .HasForeignKey(e => e.UploadFileId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<CaseResearchEntry>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseResearchEntry>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseResearchEntry>()
                .Property(e => e.Title).HasMaxLength(300);
            modelBuilder.Entity<CaseResearchEntry>()
                .Property(e => e.Url).HasMaxLength(2000);
            modelBuilder.Entity<CaseResearchEntry>()
                .HasIndex(e => new { e.CaseId, e.SortOrder });

            // ── CaseFile ──────────────────────────────────────────────────────
            modelBuilder.Entity<CaseFile>()
                .HasOne(e => e.Case).WithMany(e => e.CaseFiles)
                .HasForeignKey(e => e.CaseId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<CaseFile>()
                .HasOne(e => e.UploadFile).WithMany(e => e.CaseFiles)
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<CaseFile>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseFile>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseFile>()
                .Property(e => e.Description).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<CaseFile>()
                .HasIndex(e => e.CaseId);
            modelBuilder.Entity<CaseFile>()
                .HasIndex(e => e.UploadFileId);

            // ── CaseRelatedPerson ─────────────────────────────────────────────
            modelBuilder.Entity<CaseRelatedPerson>()
                .HasOne(e => e.Case).WithMany(e => e.RelatedPeople)
                .HasForeignKey(e => e.CaseId).OnDelete(DeleteBehavior.Cascade);
            // NoAction, not Cascade: deleting the photo should never silently delete the record
            // that this person exists. Clearing the reference is the client's decision, not a
            // side effect of tidying up files.
            modelBuilder.Entity<CaseRelatedPerson>()
                .HasOne(e => e.UploadFile).WithMany()
                .HasForeignKey(e => e.UploadFileId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseRelatedPerson>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseRelatedPerson>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CaseRelatedPerson>()
                .Property(e => e.Name).HasMaxLength(200);
            modelBuilder.Entity<CaseRelatedPerson>()
                .Property(e => e.Relationship).HasMaxLength(100);
            modelBuilder.Entity<CaseRelatedPerson>()
                .Property(e => e.Notes).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<CaseRelatedPerson>()
                .HasIndex(e => e.CaseId);

            // ── InvestigationScheduleProposal ──────────────────────────────
            modelBuilder.Entity<InvestigationScheduleProposal>()
                .HasOne(e => e.Case).WithMany()
                .HasForeignKey(e => e.CaseId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<InvestigationScheduleProposal>()
                .HasOne(e => e.Investigation).WithMany()
                .HasForeignKey(e => e.InvestigationId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<InvestigationScheduleProposal>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<InvestigationScheduleProposal>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<InvestigationScheduleProposal>()
                .Property(e => e.Notes).HasMaxLength(2000);
            modelBuilder.Entity<InvestigationScheduleProposal>()
                .Property(e => e.ClientResponseNotes).HasMaxLength(1000);
            modelBuilder.Entity<InvestigationScheduleProposal>()
                .HasIndex(e => new { e.CaseId, e.Status });

            // ── ScheduleProposalSlot ──────────────────────────────────────────
            modelBuilder.Entity<ScheduleProposalSlot>()
                .HasOne(e => e.Proposal).WithMany(e => e.Slots)
                .HasForeignKey(e => e.ProposalId).OnDelete(DeleteBehavior.Cascade);

            // ── String lengths (audit C5, first slice) ─────────────────────────
            //
            // Without an explicit length EF maps string to nvarchar(max): it cannot be indexed,
            // it inflates the query optimiser's memory grants, and it accepts 2 GB into a field
            // meant to hold a city name. 257 string properties existed against 82 length
            // configurations; this bounds the five most-queried entities, which is where the
            // indexing and memory cost actually lands.
            //
            // Only lengths are set here — never IsRequired — so nullability stays exactly as the
            // entity and any earlier configuration already declared it. Genuinely long free text
            // (Case.Description) and serialized documents (UploadFile.EditStateJson) keep max on
            // purpose; a limit there would be a functional change, not a tightening.
            //
            // Only columns that were actually nvarchar(max) appear below. A first attempt set a
            // length on every string of these entities and scaffolded 14 alterations to columns
            // that were ALREADY bounded — deliberately, at 256/128/64 — including narrowing
            // Places.GeocodeNote from 512 to 500, which could have truncated live data. Guessing a
            // number for a column somebody already chose a number for is not a tightening; it is
            // an unrequested schema change.

            // Legal name. Bounded like DisplayName rather than left as nvarchar(max) — an
            // unbounded string column is one nobody can index later without a migration.
            modelBuilder.Entity<AppUser>().Property(e => e.FirstName).HasMaxLength(100);
            modelBuilder.Entity<AppUser>().Property(e => e.LastName).HasMaxLength(100);
            modelBuilder.Entity<AppUser>().Property(e => e.DisplayName).HasMaxLength(200);
            modelBuilder.Entity<AppUser>().Property(e => e.Handle).HasMaxLength(30);
            // Unique, and filtered so that NULL does not collide with NULL. The filter is not a
            // way of tolerating accounts without a handle — every account has one, and the
            // backfill service fills any that predate the column — it is what let the column be
            // added to a populated table before that service had run.
            modelBuilder.Entity<AppUser>()
                .HasIndex(e => e.Handle).IsUnique().HasFilter("[Handle] IS NOT NULL");

            modelBuilder.Entity<Organization>().Property(e => e.Name).HasMaxLength(200);
            modelBuilder.Entity<Organization>().Property(e => e.UrlName).HasMaxLength(100);
            // One organization per address. Cases, investigations and events have had this since
            // they were built; organizations, whose address is the one people actually type, never
            // did — so two groups could hold "ghost-squad" and /o/ghost-squad resolved to whichever
            // row the database returned first.
            modelBuilder.Entity<Organization>()
                .HasIndex(e => e.UrlName).IsUnique().HasFilter("[UrlName] IS NOT NULL");

            // ── OrganizationUrlNameAlias ─────────────────────────────────────
            modelBuilder.Entity<OrganizationUrlNameAlias>().Property(e => e.UrlName)
                .HasMaxLength(100).IsRequired();
            // Unique across all aliases, and checked against current names on write: an address a
            // group has ever held is never handed to another one, because a captured link is worse
            // than a dead one.
            modelBuilder.Entity<OrganizationUrlNameAlias>()
                .HasIndex(e => e.UrlName).IsUnique();
            modelBuilder.Entity<OrganizationUrlNameAlias>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Organization>().Property(e => e.PublicPhone).HasMaxLength(50);
            modelBuilder.Entity<Organization>().Property(e => e.PublicEmail).HasMaxLength(256);
            modelBuilder.Entity<Organization>().Property(e => e.PublicWebsite).HasMaxLength(500);

            modelBuilder.Entity<Case>().Property(e => e.StreetAddress2).HasMaxLength(300);
            modelBuilder.Entity<Case>().Property(e => e.Country).HasMaxLength(100);

            modelBuilder.Entity<UploadFile>().Property(e => e.FileName).HasMaxLength(500);
            modelBuilder.Entity<UploadFile>().Property(e => e.StoredFileName).HasMaxLength(300);
            modelBuilder.Entity<UploadFile>().Property(e => e.ContentType).HasMaxLength(200);
            modelBuilder.Entity<UploadFile>().Property(e => e.Description).HasMaxLength(2000);

            // ── SidecarInstallLog ─────────────────────────────────────────────
            modelBuilder.Entity<SidecarInstallLog>().Property(e => e.EventType).HasMaxLength(20).IsRequired();
            modelBuilder.Entity<SidecarInstallLog>().Property(e => e.Version).HasMaxLength(50);
            modelBuilder.Entity<SidecarInstallLog>().Property(e => e.Platform).HasMaxLength(50);
            // 45 covers an IPv6 address in full text form.
            modelBuilder.Entity<SidecarInstallLog>().Property(e => e.IpAddress).HasMaxLength(45);
            modelBuilder.Entity<SidecarInstallLog>()
                .HasOne(e => e.AppUser).WithMany()
                .HasForeignKey(e => e.AppUserId)
                // The log outlives the account: deleting a user must not erase the record that a
                // sidecar was installed, so the row stays and simply stops naming anyone.
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<SidecarInstallLog>().HasIndex(e => e.InstallId);
            modelBuilder.Entity<SidecarInstallLog>().HasIndex(e => e.DateCreated);

            // ── Equipment ─────────────────────────────────────────────────────
            modelBuilder.Entity<EquipmentCategory>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentCategory>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentCategory>().Property(e => e.Name).HasMaxLength(100);
            modelBuilder.Entity<EquipmentCategory>().Property(e => e.Description).HasMaxLength(500);
            modelBuilder.Entity<EquipmentCategory>().Property(e => e.IconClass).HasMaxLength(100);
            modelBuilder.Entity<EquipmentCategory>().HasIndex(e => e.Name).IsUnique();

            modelBuilder.Entity<EquipmentBrand>()
                .HasOne(e => e.ProposedByOrganization).WithMany()
                .HasForeignKey(e => e.ProposedByOrganizationId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentBrand>()
                .HasOne(e => e.ProposedByAppUser).WithMany()
                .HasForeignKey(e => e.ProposedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentBrand>()
                .HasOne(e => e.ApprovedByAppUser).WithMany()
                .HasForeignKey(e => e.ApprovedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentBrand>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentBrand>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentBrand>().Property(e => e.Name).HasMaxLength(200);
            modelBuilder.Entity<EquipmentBrand>().HasIndex(e => e.Name).IsUnique();
            modelBuilder.Entity<EquipmentBrand>().Property(e => e.UrlName).HasMaxLength(100);
            modelBuilder.Entity<EquipmentBrand>()
                .HasIndex(e => e.UrlName).IsUnique().HasFilter("[UrlName] IS NOT NULL");

            modelBuilder.Entity<EquipmentModel>()
                .HasOne(e => e.EquipmentBrand).WithMany(e => e.EquipmentModels)
                .HasForeignKey(e => e.EquipmentBrandId).OnDelete(DeleteBehavior.Cascade);
            // NoAction, not Cascade: a category with models in use can't be casually deleted —
            // SuperAdmin re-categorizes the models first. Mirrors the CaseRelatedPerson/UploadFile
            // reasoning: deleting the taxonomy node should never silently delete real gear records.
            modelBuilder.Entity<EquipmentModel>()
                .HasOne(e => e.EquipmentCategory).WithMany(e => e.EquipmentModels)
                .HasForeignKey(e => e.EquipmentCategoryId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentModel>()
                .HasOne(e => e.ProposedByOrganization).WithMany()
                .HasForeignKey(e => e.ProposedByOrganizationId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentModel>()
                .HasOne(e => e.ProposedByAppUser).WithMany()
                .HasForeignKey(e => e.ProposedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentModel>()
                .HasOne(e => e.ApprovedByAppUser).WithMany()
                .HasForeignKey(e => e.ApprovedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentModel>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentModel>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentModel>().Property(e => e.Name).HasMaxLength(200);
            modelBuilder.Entity<EquipmentModel>().Property(e => e.ModelNumber).HasMaxLength(100);
            modelBuilder.Entity<EquipmentModel>().Property(e => e.Description).HasMaxLength(1000);
            modelBuilder.Entity<EquipmentModel>().HasIndex(e => new { e.EquipmentBrandId, e.Name }).IsUnique();
            modelBuilder.Entity<EquipmentModel>().Property(e => e.UrlName).HasMaxLength(100);
            // Scoped to the make, exactly as the name is: two manufacturers may both make an "X1".
            modelBuilder.Entity<EquipmentModel>()
                .HasIndex(e => new { e.EquipmentBrandId, e.UrlName })
                .IsUnique().HasFilter("[UrlName] IS NOT NULL");
            modelBuilder.Entity<EquipmentModel>().HasIndex(e => e.EquipmentCategoryId);

            // NoAction on both ownership FKs: an item's identity/history must outlive its owner
            // account or organization being deleted elsewhere — those flows retire/reassign
            // instead. Exactly one of OwnerAppUserId/OwningOrganizationId is set; enforced in the
            // controller (see EquipmentItem's remarks), not the database.
            modelBuilder.Entity<EquipmentItem>()
                .HasOne(e => e.OwnerAppUser).WithMany()
                .HasForeignKey(e => e.OwnerAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentItem>()
                .HasOne(e => e.OwningOrganization).WithMany()
                .HasForeignKey(e => e.OwningOrganizationId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentItem>()
                .HasOne(e => e.EquipmentModel).WithMany(e => e.EquipmentItems)
                .HasForeignKey(e => e.EquipmentModelId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentItem>()
                .HasOne(e => e.CurrentHolderAppUser).WithMany()
                .HasForeignKey(e => e.CurrentHolderAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentItem>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentItem>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentItem>().Property(e => e.DisplayName).HasMaxLength(200);
            modelBuilder.Entity<EquipmentItem>().Property(e => e.SerialNumber).HasMaxLength(100);
            modelBuilder.Entity<EquipmentItem>().Property(e => e.Notes).HasMaxLength(2000);
            modelBuilder.Entity<EquipmentItem>().Property(e => e.DefectNotes).HasMaxLength(2000);
            modelBuilder.Entity<EquipmentItem>().Property(e => e.WebsiteUrl).HasMaxLength(500);
            modelBuilder.Entity<EquipmentItem>().HasIndex(e => e.OwnerAppUserId);
            modelBuilder.Entity<EquipmentItem>().HasIndex(e => e.OwningOrganizationId);
            modelBuilder.Entity<EquipmentItem>().HasIndex(e => e.EquipmentModelId);

            // NoAction, not Cascade: deleting a photo should never silently delete the item it
            // documents — same reasoning as CaseRelatedPerson.UploadFile above.
            modelBuilder.Entity<EquipmentItemPhoto>()
                .HasOne(e => e.EquipmentItem).WithMany(e => e.Photos)
                .HasForeignKey(e => e.EquipmentItemId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<EquipmentItemPhoto>()
                .HasOne(e => e.UploadFile).WithMany()
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentItemPhoto>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentItemPhoto>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentItemPhoto>().Property(e => e.Caption).HasMaxLength(200);
            modelBuilder.Entity<EquipmentItemPhoto>().HasIndex(e => new { e.EquipmentItemId, e.UploadFileId }).IsUnique();

            modelBuilder.Entity<EquipmentItemShare>()
                .HasOne(e => e.EquipmentItem).WithMany(e => e.Shares)
                .HasForeignKey(e => e.EquipmentItemId).OnDelete(DeleteBehavior.Cascade);
            // NoAction on the organization: cascading here would give SQL Server two paths to this
            // table (Organization -> EquipmentItem -> Share as well as Organization -> Share). A
            // group being deleted leaves its share rows inert rather than deleting them, and the
            // membership check every read performs already stops an orphaned row granting anything.
            modelBuilder.Entity<EquipmentItemShare>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentItemShare>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentItemShare>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            // One row per (item, group): sharing twice is the same fact, and the replace-set
            // endpoint relies on this to stay honest under a double submit.
            modelBuilder.Entity<EquipmentItemShare>()
                .HasIndex(e => new { e.EquipmentItemId, e.OrganizationId }).IsUnique();
            modelBuilder.Entity<EquipmentItemShare>().HasIndex(e => e.OrganizationId);

            modelBuilder.Entity<EquipmentServiceLog>()
                .HasOne(e => e.EquipmentItem).WithMany(e => e.ServiceLog)
                .HasForeignKey(e => e.EquipmentItemId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<EquipmentServiceLog>()
                .HasOne(e => e.PerformedByAppUser).WithMany()
                .HasForeignKey(e => e.PerformedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentServiceLog>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentServiceLog>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── Subscriptions (items 84 and 85) ──────────────────────────────
            modelBuilder.Entity<SubscriptionTier>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<SubscriptionTier>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<SubscriptionTier>().Property(e => e.Name).HasMaxLength(100);
            // Bands are read by member count on every billing evaluation.
            modelBuilder.Entity<SubscriptionTier>().HasIndex(e => new { e.IsActive, e.MinMembers });

            // One price per band per cadence. The unique index is the whole point: two active
            // yearly prices for the same band is a question with two answers, and whichever the
            // query happened to order first would become the answer.
            modelBuilder.Entity<SubscriptionTierPrice>()
                .HasIndex(e => new { e.SubscriptionTierId, e.Interval }).IsUnique();
            modelBuilder.Entity<SubscriptionTierPrice>()
                .HasOne(e => e.SubscriptionTier).WithMany(t => t.Prices)
                .HasForeignKey(e => e.SubscriptionTierId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<SubscriptionTierPrice>().Property(e => e.Price).HasPrecision(18, 2);
            modelBuilder.Entity<SubscriptionTierPrice>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<SubscriptionTierPrice>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // The review queue reads "this org's pending, oldest first"; the event index serves
            // both the queue join and the public page's accepted list.
            modelBuilder.Entity<EventEvidenceSubmission>()
                .HasIndex(e => new { e.OrgCalendarEventId, e.Status });
            modelBuilder.Entity<EventEvidenceSubmission>().Property(e => e.Note).HasMaxLength(2000);
            modelBuilder.Entity<EventEvidenceSubmission>().Property(e => e.RejectionReason).HasMaxLength(1000);
            modelBuilder.Entity<EventEvidenceSubmission>()
                .HasOne(e => e.OrgCalendarEvent).WithMany()
                .HasForeignKey(e => e.OrgCalendarEventId).OnDelete(DeleteBehavior.Cascade);
            // Restrict: the file is the evidence — the review row must not orphan silently.
            modelBuilder.Entity<EventEvidenceSubmission>()
                .HasOne(e => e.UploadFile).WithMany()
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<EventEvidenceSubmission>()
                .HasOne(e => e.SubmittedByAppUser).WithMany()
                .HasForeignKey(e => e.SubmittedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EventEvidenceSubmission>()
                .HasOne(e => e.ReviewedByAppUser).WithMany()
                .HasForeignKey(e => e.ReviewedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EventEvidenceSubmission>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EventEvidenceSubmission>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // The delivery job's work queue is "due and undelivered", so that is the index.
            modelBuilder.Entity<TierChangeNotice>()
                .HasIndex(e => new { e.DeliveredAtUtc, e.DeliverAtUtc });
            modelBuilder.Entity<TierChangeNotice>().Property(e => e.Sentences).HasMaxLength(4000);
            modelBuilder.Entity<TierChangeNotice>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<TierChangeNotice>()
                .HasOne(e => e.SubscriptionTier).WithMany()
                .HasForeignKey(e => e.SubscriptionTierId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<TierChangeNotice>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<TierChangeNotice>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // A contract snapshot per period. The subscription cascades — the contract history of a
            // deleted group goes with the group — but the tier restricts, because a snapshot must
            // stay resolvable to the row it was copied from.
            modelBuilder.Entity<SubscriptionContractTerms>()
                .HasIndex(e => new { e.OrganizationSubscriptionId, e.PeriodStartUtc });
            modelBuilder.Entity<SubscriptionContractTerms>().Property(e => e.TierName).HasMaxLength(100);
            modelBuilder.Entity<SubscriptionContractTerms>().Property(e => e.Price).HasPrecision(18, 2);
            modelBuilder.Entity<SubscriptionContractTerms>()
                .HasOne(e => e.OrganizationSubscription).WithMany()
                .HasForeignKey(e => e.OrganizationSubscriptionId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<SubscriptionContractTerms>()
                .HasOne(e => e.SubscriptionTier).WithMany()
                .HasForeignKey(e => e.SubscriptionTierId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<SubscriptionContractTerms>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<SubscriptionContractTerms>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // One cap per band per thing, for the same reason as the prices: two rows for the same
            // limit is a question with two answers and no rule for picking.
            modelBuilder.Entity<SubscriptionTierLimit>()
                .HasIndex(e => new { e.SubscriptionTierId, e.Limit }).IsUnique();
            modelBuilder.Entity<SubscriptionTierLimit>()
                .HasOne(e => e.SubscriptionTier).WithMany(t => t.Limits)
                .HasForeignKey(e => e.SubscriptionTierId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<SubscriptionTierLimit>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<SubscriptionTierLimit>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // ── SubscriptionTierPermissionArea (item 156 Phase A) ─────────────
            modelBuilder.Entity<SubscriptionTierPermissionArea>()
                .HasOne(e => e.SubscriptionTier).WithMany(t => t.PermissionAreas)
                .HasForeignKey(e => e.SubscriptionTierId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<SubscriptionTierPermissionArea>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<SubscriptionTierPermissionArea>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            // One row per (tier, area): "included twice" is a save bug, not a state.
            modelBuilder.Entity<SubscriptionTierPermissionArea>()
                .HasIndex(e => new { e.SubscriptionTierId, e.Area }).IsUnique();

            modelBuilder.Entity<SubscriptionTierExcludedCapability>()
                .HasOne(e => e.SubscriptionTier).WithMany(t => t.ExcludedCapabilities)
                .HasForeignKey(e => e.SubscriptionTierId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<SubscriptionTierExcludedCapability>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<SubscriptionTierExcludedCapability>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<SubscriptionTierExcludedCapability>()
                .HasIndex(e => new { e.SubscriptionTierId, e.Capability }).IsUnique();

            modelBuilder.Entity<UserTourState>()
                .HasOne(e => e.AppUser).WithMany()
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UserTourState>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserTourState>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<UserTourState>()
                .Property(e => e.TourName).HasMaxLength(64);
            // One row per (person, tour): dismissed twice is an upsert, not a second row.
            modelBuilder.Entity<UserTourState>()
                .HasIndex(e => new { e.AppUserId, e.TourName }).IsUnique();

            modelBuilder.Entity<OrganizationAd>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationAd>()
                .HasOne(e => e.ImageUploadFile).WithMany()
                .HasForeignKey(e => e.ImageUploadFileId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<OrganizationAd>()
                .HasOne(e => e.ReviewedByAppUser).WithMany()
                .HasForeignKey(e => e.ReviewedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationAd>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationAd>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationAd>()
                .Property(e => e.Headline).HasMaxLength(80);
            modelBuilder.Entity<OrganizationAd>()
                .Property(e => e.Body).HasMaxLength(300);
            modelBuilder.Entity<OrganizationAd>()
                .Property(e => e.TargetKind).HasMaxLength(16);

            // One row per organization, enforced rather than assumed: a second row would make
            // "what does this group pay?" a question with two answers.
            modelBuilder.Entity<OrganizationSubscription>()
                .HasIndex(e => e.OrganizationId).IsUnique();
            modelBuilder.Entity<OrganizationSubscription>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            // Restrict, not Cascade: a tier that has priced a period must not be deletable out
            // from under it. Retire it instead — SubscriptionTier.IsActive.
            modelBuilder.Entity<OrganizationSubscription>()
                .HasOne(e => e.SubscriptionTier).WithMany()
                .HasForeignKey(e => e.SubscriptionTierId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<OrganizationSubscription>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationSubscription>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationSubscription>()
                .Property(e => e.PriceAtPeriodStart).HasPrecision(18, 2);
            // The notice job asks "whose period ends soon?" — an index on the date it scans.
            modelBuilder.Entity<OrganizationSubscription>()
                .HasIndex(e => new { e.Status, e.CurrentPeriodEnd });

            // One nomination per person per organization.
            modelBuilder.Entity<OrganizationBillingContact>()
                .HasIndex(e => new { e.OrganizationId, e.AppUserId }).IsUnique();
            modelBuilder.Entity<OrganizationBillingContact>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OrganizationBillingContact>()
                .HasOne(e => e.AppUser).WithMany()
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationBillingContact>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<OrganizationBillingContact>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Coupon>().Property(e => e.Name).HasMaxLength(150);
            modelBuilder.Entity<Coupon>().Property(e => e.Description).HasMaxLength(1000);
            modelBuilder.Entity<Coupon>().Property(e => e.AmountOff).HasPrecision(18, 2);
            modelBuilder.Entity<Coupon>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<Coupon>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // Codes are typed by hand, so they are matched case-insensitively and stored upper-
            // cased; the unique index is on the stored form, and it spans every campaign — two
            // batches that both generate ABC123 would make redemption ambiguous.
            modelBuilder.Entity<CouponCode>().Property(e => e.Code).HasMaxLength(64);
            modelBuilder.Entity<CouponCode>().Property(e => e.IssuedTo).HasMaxLength(256);
            modelBuilder.Entity<CouponCode>().HasIndex(e => e.Code).IsUnique();
            modelBuilder.Entity<CouponCode>()
                .HasOne(e => e.Coupon).WithMany(c => c.Codes)
                .HasForeignKey(e => e.CouponId).OnDelete(DeleteBehavior.Cascade);
            // A code addressed to one person. NoAction rather than Cascade: deleting the account
            // must not silently delete a code somebody may already have been told about.
            modelBuilder.Entity<CouponCode>()
                .HasOne(e => e.RestrictedToAppUser).WithMany()
                .HasForeignKey(e => e.RestrictedToAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CouponCode>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CouponCode>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // One redemption per organization per coupon. This index is not a convenience — it is
            // what makes the redemption limit safe when two groups redeem the last use at once.
            modelBuilder.Entity<CouponRedemption>()
                .HasIndex(e => new { e.CouponId, e.OrganizationId }).IsUnique();
            modelBuilder.Entity<CouponRedemption>().Property(e => e.ListPrice).HasPrecision(18, 2);
            modelBuilder.Entity<CouponRedemption>().Property(e => e.Discount).HasPrecision(18, 2);
            modelBuilder.Entity<CouponRedemption>().Property(e => e.Payable).HasPrecision(18, 2);
            modelBuilder.Entity<CouponRedemption>()
                .HasOne(e => e.Coupon).WithMany(c => c.Redemptions)
                .HasForeignKey(e => e.CouponId).OnDelete(DeleteBehavior.Restrict);
            // Restrict, not Cascade: the redemption is the financial record of why a group was
            // charged less, and withdrawing a code must not erase the answer.
            modelBuilder.Entity<CouponRedemption>()
                .HasOne(e => e.CouponCode).WithMany(c => c.Redemptions)
                .HasForeignKey(e => e.CouponCodeId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<CouponRedemption>()
                .HasOne(e => e.Organization).WithMany()
                .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<CouponRedemption>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<CouponRedemption>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentServiceLog>().Property(e => e.Notes).HasMaxLength(2000);
            modelBuilder.Entity<EquipmentServiceLog>().HasIndex(e => new { e.EquipmentItemId, e.EntryDate });

            // FAQ entries belong to the piece and go with it. Unlike loans, they are the owner's own
            // words about their own thing — there is no second party whose record would be destroyed.
            modelBuilder.Entity<EquipmentItemFaq>()
                .HasOne(e => e.EquipmentItem).WithMany(e => e.Faqs)
                .HasForeignKey(e => e.EquipmentItemId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<EquipmentItemFaq>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentItemFaq>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentItemFaq>().Property(e => e.Question).HasMaxLength(500);
            modelBuilder.Entity<EquipmentItemFaq>().Property(e => e.Answer).HasMaxLength(4000);
            modelBuilder.Entity<EquipmentItemFaq>().HasIndex(e => new { e.EquipmentItemId, e.SortOrder });

            modelBuilder.Entity<EquipmentQuestion>()
                .HasOne(e => e.EquipmentItem).WithMany(e => e.Questions)
                .HasForeignKey(e => e.EquipmentItemId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<EquipmentQuestion>()
                .HasOne(e => e.AskedByAppUser).WithMany()
                .HasForeignKey(e => e.AskedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentQuestion>()
                .HasOne(e => e.AnsweredByAppUser).WithMany()
                .HasForeignKey(e => e.AnsweredByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentQuestion>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentQuestion>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentQuestion>().Property(e => e.QuestionText).HasMaxLength(2000);
            modelBuilder.Entity<EquipmentQuestion>().Property(e => e.AnswerText).HasMaxLength(4000);
            // No FK on PromotedToFaqId on purpose: it stamps that publishing happened, and deleting
            // the published FAQ must not reopen a question that was genuinely answered.
            modelBuilder.Entity<EquipmentQuestion>().HasIndex(e => new { e.EquipmentItemId, e.Status });
            modelBuilder.Entity<EquipmentQuestion>().HasIndex(e => e.AskedByAppUserId);

            modelBuilder.Entity<EquipmentLoanFeedback>()
                .HasOne(e => e.EquipmentCheckout).WithMany(e => e.Feedback)
                .HasForeignKey(e => e.EquipmentCheckoutId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<EquipmentLoanFeedback>()
                .HasOne(e => e.AuthorAppUser).WithMany()
                .HasForeignKey(e => e.AuthorAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentLoanFeedback>()
                .HasOne(e => e.SubjectAppUser).WithMany()
                .HasForeignKey(e => e.SubjectAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentLoanFeedback>()
                .HasOne(e => e.SubjectOrganization).WithMany()
                .HasForeignKey(e => e.SubjectOrganizationId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentLoanFeedback>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentLoanFeedback>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentLoanFeedback>().Property(e => e.CounterpartyComment).HasMaxLength(2000);
            modelBuilder.Entity<EquipmentLoanFeedback>().Property(e => e.ProductComment).HasMaxLength(2000);
            // One row per side per loan. The endpoint checks first, but a double submit races it —
            // and two contradictory verdicts from the same person on the same loan is nonsense.
            modelBuilder.Entity<EquipmentLoanFeedback>()
                .HasIndex(e => new { e.EquipmentCheckoutId, e.Role }).IsUnique();
            // The reads are all "everything about this subject", never "this loan".
            modelBuilder.Entity<EquipmentLoanFeedback>().HasIndex(e => new { e.SubjectAppUserId, e.Role });
            modelBuilder.Entity<EquipmentLoanFeedback>().HasIndex(e => new { e.SubjectOrganizationId, e.Role });

            // NoAction from the item: a loan is the record of what happened to a piece of gear, and
            // an item with any loan history refuses deletion in favour of being retired, so there is
            // never a cascade to perform. Organization is NoAction for the multiple-cascade-path
            // reason the share table documents.
            modelBuilder.Entity<EquipmentCheckout>()
                .HasOne(e => e.EquipmentItem).WithMany()
                .HasForeignKey(e => e.EquipmentItemId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentCheckout>()
                .HasOne(e => e.BorrowerAppUser).WithMany()
                .HasForeignKey(e => e.BorrowerAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentCheckout>()
                .HasOne(e => e.BorrowedForOrganization).WithMany()
                .HasForeignKey(e => e.BorrowedForOrganizationId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentCheckout>()
                .HasOne(e => e.Investigation).WithMany()
                .HasForeignKey(e => e.InvestigationId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentCheckout>()
                .HasOne(e => e.ReviewedByAppUser).WithMany()
                .HasForeignKey(e => e.ReviewedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentCheckout>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentCheckout>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentCheckout>().Property(e => e.RequestNotes).HasMaxLength(1000);
            modelBuilder.Entity<EquipmentCheckout>().Property(e => e.ReviewNotes).HasMaxLength(1000);
            modelBuilder.Entity<EquipmentCheckout>().Property(e => e.ReturnConditionNotes).HasMaxLength(2000);
            modelBuilder.Entity<EquipmentCheckout>().HasIndex(e => new { e.EquipmentItemId, e.Status });
            modelBuilder.Entity<EquipmentCheckout>().HasIndex(e => e.BorrowerAppUserId);
            modelBuilder.Entity<EquipmentCheckout>().HasIndex(e => new { e.BorrowedForOrganizationId, e.Status });
            modelBuilder.Entity<EquipmentCheckout>().HasIndex(e => e.InvestigationId);

            modelBuilder.Entity<EquipmentCheckoutPhoto>()
                .HasOne(e => e.EquipmentCheckout).WithMany(e => e.Photos)
                .HasForeignKey(e => e.EquipmentCheckoutId).OnDelete(DeleteBehavior.Cascade);
            // NoAction on the file: deleting a photo must never take the loan record with it.
            modelBuilder.Entity<EquipmentCheckoutPhoto>()
                .HasOne(e => e.UploadFile).WithMany()
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentCheckoutPhoto>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentCheckoutPhoto>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentCheckoutPhoto>().Property(e => e.Caption).HasMaxLength(200);
            modelBuilder.Entity<EquipmentCheckoutPhoto>().HasIndex(e => new { e.EquipmentCheckoutId, e.Stage });

            modelBuilder.Entity<EquipmentCheckoutRenewal>()
                .HasOne(e => e.EquipmentCheckout).WithMany(e => e.Renewals)
                .HasForeignKey(e => e.EquipmentCheckoutId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<EquipmentCheckoutRenewal>()
                .HasOne(e => e.ReviewedByAppUser).WithMany()
                .HasForeignKey(e => e.ReviewedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentCheckoutRenewal>()
                .HasOne(e => e.CreatedByAppUser).WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentCheckoutRenewal>()
                .HasOne(e => e.UpdatedByAppUser).WithMany()
                .HasForeignKey(e => e.UpdatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<EquipmentCheckoutRenewal>().Property(e => e.RequestNotes).HasMaxLength(1000);
            modelBuilder.Entity<EquipmentCheckoutRenewal>().Property(e => e.ReviewNotes).HasMaxLength(1000);
            modelBuilder.Entity<EquipmentCheckoutRenewal>().HasIndex(e => new { e.EquipmentCheckoutId, e.Status });

        }
    }
}
