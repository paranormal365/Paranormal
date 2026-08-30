using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A page that fetches a <c>LoadResult</c> must do something with the failure it can now see.
/// </summary>
/// <remarks>
/// <para><b>The half-conversion this exists to stop.</b> Changing an adapter method to return
/// <c>LoadResult&lt;T&gt;</c> makes a refusal visible; it does not make it <i>shown</i>. The
/// cheapest way to fix the compile error is <c>.Items</c> at the call site, which leaves the page
/// exactly as wrong as it was before — "No records available" over a 403 — while the ratchet in
/// <see cref="SwallowedFailureRatchetTests"/> happily records progress. Item 120's whole point is
/// the sentence on the screen, not the type in the adapter.</para>
///
/// <para><b>What it requires.</b> A <c>.razor</c> file that calls one of the converted case-area
/// methods must mention <c>BenListState</c> or read <c>.Failed</c> somewhere. That is a low bar on
/// purpose: it cannot check that the right thing is rendered, only that the failure was not
/// dropped on the floor without a decision.</para>
///
/// <para><b>It ran with a debt list for one day.</b> When item 120's conversion finished, 22 pages
/// could see a refusal and still rendered two states. They were listed in an
/// <c>AwaitingRenderPass</c> array with its own ratchet — visible and shrinking, rather than
/// quietly excluded — and the list reached zero on 2026-08-22 (item 141), so it is gone. If this
/// guard ever needs one again, that is the shape: a list that can only get shorter, never a
/// silent exemption.</para>
///
/// <para><b>Deliberate exceptions are listed, with the reason.</b> Some fetches really are
/// decorations, and a warning panel over a badge would be worse than the badge not appearing. Those
/// belong in <see cref="Decorations"/> where the choice is written down, rather than passing
/// silently because nobody looked.</para>
/// </remarks>
public sealed class LoadResultRenderedGuardTests
{
    /// <summary>The case-area methods converted to <c>LoadResult</c>.</summary>
    private static readonly string[] ConvertedMethods =
    [
        "FindPlaceCandidatesAsync", "GetAddressMemberAccessAsync", "GetAdminEquipmentBrandsAsync",
        "GetAdminEquipmentCategoriesAsync", "GetAdminEquipmentModelsAsync", "GetAllCasesAsync",
        "GetAllExperienceCategoriesAsync", "GetAllExperienceTypesAsync",
        "GetAllInvestigationsAsync", "GetAllUsersAsync", "GetAnonymousListAsync",
        "GetAttendedInvestigationsAsync", "GetAudioMarkersAsync", "GetAuditLogEntityTypesAsync",
        "GetCalendarEventAttendeesAsync", "GetCalendarEventTypesAsync", "GetCalendarEventsAsync",
        "GetCaseFilesAsync", "GetCaseInvitesAsync", "GetCaseMessagesAsync", "GetCaseNotesAsync",
        "GetCaseReportsAsync", "GetCaseResearchAsync", "GetCaseTimelineAsync",
        "GetCaseTransfersAsync", "GetCaseVoteSummariesAsync", "GetCheckoutPhotosAsync",
        "GetCheckoutRenewalsAsync", "GetChildClipsAsync", "GetClientRequestOrgsAsync",
        "GetCmsPagesAsync", "GetCmsTemplatesAsync", "GetCoClientsAsync", "GetEmbeddableCasesAsync",
        "GetEmbeddableInvestigationsAsync", "GetEquipmentBrandsAsync",
        "GetEquipmentCategoriesAsync", "GetEquipmentFaqsAsync", "GetEquipmentItemCheckoutsAsync",
        "GetEquipmentItemHistoryAsync", "GetEquipmentModelsForBrandAsync", "GetEvidenceVotesAsync",
        "GetExperienceTaxonomyAsync", "GetFeedReportsAsync", "GetFileCommentsAsync",
        "GetFileOrgSharesAsync", "GetFilePermissionRequestsAsync",
        "GetFileTypesWithExtensionsAsync", "GetGroupMembersAsync", "GetGroupsAsync",
        "GetInvestigationAttendeesAsync", "GetInvestigationFindingsAsync",
        "GetInvestigationRosterAsync", "GetInvestigationsAsync", "GetListAsync",
        "GetLookupTypesAsync", "GetMediaLibraryFilesAsync", "GetMembershipQuestionsAsync",
        "GetMembershipRequestsAsync", "GetMembershipVotesAsync", "GetMyAddressesAsync", "GetOrgBillingHistoryAsync",
        "GetMyAskedQuestionsAsync", "GetMyCaseMessagesAsync", "GetMyCaseReportsAsync",
        "GetMyCasesAsync", "GetMyClientRequestsAsync", "GetMyEmailsAsync", "GetMyEquipmentAsync",
        "GetMyEquipmentCheckoutsAsync", "GetMyEquipmentSharesAsync", "GetMyInvestigationsAsync",
        "GetMyLinksAsync", "GetMyMessagesAsync", "GetMyOrganizationsAsync", "GetMyPhonesAsync",
        "GetMyPhotosAsync", "GetMyPublicEventsAsync", "GetMyReceivedQuestionsAsync",
        "GetMyScheduleProposalsAsync", "GetMySubscriptionsAsync", "GetMyVideoProjectsAsync",
        "GetOrgAddressTypesAsync", "GetOrgAddressesAsync", "GetOrgCasesAsync",
        "GetOrgEquipmentServiceLogAsync", "GetOrgFileDeleteLogAsync", "GetOrgFilesAsync",
        "GetOrgInboxAsync", "GetOrgInvestigationsAsync", "GetOrgLogosAsync",
        "GetOrgPendingRequestsAsync", "GetOrgPublicationPostsAsync", "GetOrgPublicationsAsync",
        "GetOrgRoleMembersAsync", "GetOrgRolePermissionsAsync", "GetOrgRolesAsync",
        "GetOrgSentAsync", "GetOrgSharedEquipmentAsync", "GetOrgSharedFilesAsync",
        "GetOrgUserDirectoryAsync", "GetOrganizationMembersAsync", "GetOrganizationUsersAsync",
        "GetOrganizationsAsync", "GetPagePermissionsAsync",
        "GetPendingPermissionRequestsForMeAsync", "GetPendingPermissionRequestsForReviewerAsync",
        "GetPlaceInvestigationsAsync", "GetProductReviewsAsync", "GetPublicCasesAsync",
        "GetPublicEquipmentItemsAsync", "GetPublicEventsAsync", "GetPublicFileTypesAsync",
        "GetPublicPublicationsAsync", "GetPublishableCaseMediaAsync",
        "GetPublishedInvestigationsAsync", "GetRegionNotesAsync", "GetRelatedPeopleAsync",
        "GetRolesAsync", "GetScheduleProposalsAsync", "GetSharesV2Async",
        "GetSidecarTelemetryAsync", "GetSiteSettingsAsync", "GetSupportTicketRepliesAsync",
        "GetThreadAsync", "GetUploadFileTypesAsync", "GetUploadFilesAsync",
        "GetUserAddressTypesAsync", "GetUserEmailTypesAsync", "GetUserLinkTypesAsync",
        "GetUserNoteTypesAsync", "GetUserPhoneTypesAsync", "GetUsersAsync", "GetVideoAssetsAsync",
        "SearchEquipmentModelsAsync", "SearchOrganizationsAsync", "SearchUsersAsync",
    ];

    /// <summary>
    /// Files that read one of these lists purely to decorate something, where the reader loses
    /// nothing they can act on if the fetch fails.
    /// </summary>
    private static readonly Dictionary<string, string> Decorations = new()
    {

        ["StartGroupPage.razor"] =
            "The address-type lookup runs inside the wizard's best-effort follow-up block AFTER "
          + "the group exists: a failure skips creating the optional first address, which the "
          + "founder adds from the hub in a minute — an error panel over a freshly founded group "
          + "would be a worse answer than a missing optional row (item 166 W1).",

        ["MyCaseDetail.razor"] =
            "Reads the experience taxonomy purely to turn type ids into names. A failed lookup "
          + "shows ids instead of labels, which is worse than labels and far better than an error "
          + "panel over somebody's case. The lists on that page report their own failures.",

        ["CaseTimeline.razor"] =
            "Same taxonomy lookup, for the filter chips' labels. The timeline itself is wrapped "
          + "and reports its own refusal.",

        ["MyProfile.razor"] =
            "Attended investigations feed the pins on the profile's own map. A refusal drops pins "
          + "from a personal map that already has its own empty state; nothing on the page claims "
          + "the person attended nothing.",

        ["EquipmentCheckoutRequestDialog.razor"] =
            "The investigation picker is explicitly optional — the label says so and the default "
          + "option is \"Not for a specific visit\". A refused fetch leaves an optional picker "
          + "empty and the request still goes through.",

        ["AdminUserDetail.razor"] =
            "The five lookup-type dropdowns — address, email, phone, link and note types. They "
          + "populate <select> options in the admin's edit form; the user's actual addresses and "
          + "phones come from the detail record, which the page reports on separately. A refused "
          + "lookup leaves a dropdown short, which is visible in the act of using it.",

        ["OrgCmsPageEdit.razor"] =
            "The user directory, used only to turn ids into display names on the permission rows. "
          + "A refusal shows ids where names would be — visible, and not a claim about access.",

        ["OrgCmsEditor.razor"] =
            "Same directory, same purpose — names for the member-management picker. The membership "
          + "list beside it is a separate call that reports its own failure.",

        ["MyEquipmentItemEditor.razor"] =
            "Category, brand and model feed the three cascading pickers in the item form. A failed "
          + "fetch leaves a picker empty, which the person sees and can retry by reopening; a "
          + "warning panel inside a form field would be worse. The item lists that show what they "
          + "own report their own failures.",

        ["OrgEquipmentEditor.razor"] =
            "The same three cascading pickers, in the group's version of the same form.",

        ["AdminAuditLog.razor"] =
            "The entity-type filter's options. A refusal narrows the filter; it does not misreport "
          + "the log, which has its own state.",

        ["PublicCaseDiscovery.razor"] =
            "Vote summaries only mark which cards the viewer has already voted on. A failed lookup "
          + "leaves the marks off — the cases themselves still render, and a warning panel over a "
          + "badge would be a worse page than a missing badge.",
    };



    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    /// <summary>
    /// Strips Razor and C# comments before scanning.
    /// </summary>
    /// <remarks>
    /// <para>Two traps, both hit while writing this. The first is the familiar one: a scanner that
    /// reads comments measures the documentation, so <c>@* … *@</c> and <c>//</c> go first.</para>
    ///
    /// <para>The second is new and cost a false accusation. A naive <c>/* … */</c> strip eats a
    /// file-input's <c>accept="image/*,audio/*,video/*"</c> and everything after it up to the next
    /// real <c>*/</c> — which in <c>MyCaseDetail.razor</c> deleted 700 lines including every
    /// failure branch, and the guard then reported the one page most carefully converted as the
    /// only offender. The lookbehind requires <c>/*</c> to start a token, so a MIME wildcard is
    /// left alone.</para>
    /// </remarks>
    private static string StripComments(string source)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(
            source, @"@\*.*?\*@", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        s = System.Text.RegularExpressions.Regex.Replace(
            s, @"(?<![\w""'])/\*.*?\*/", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        return string.Join('\n', s.Split('\n').Select(line =>
        {
            var slashes = line.IndexOf("//", StringComparison.Ordinal);
            return slashes >= 0 ? line[..slashes] : line;
        }));
    }

    private static IEnumerable<string> RazorFiles() =>
        new[] { "Ben.Web.Website.Library", "Ben.Web.Website" }
            .Select(p => Path.Combine(RepoRoot().FullName, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    [Fact]
    public void A_page_that_can_see_a_refusal_does_not_drop_it()
    {
        var offenders = new List<string>();

        foreach (var file in RazorFiles())
        {
            var name = Path.GetFileName(file);
            if (Decorations.ContainsKey(name)) continue;

            var source = StripComments(File.ReadAllText(file));

            var called = ConvertedMethods.Where(m => source.Contains(m, StringComparison.Ordinal)).ToList();
            if (called.Count == 0) continue;

            var handles = source.Contains("BenListState", StringComparison.Ordinal)
                       || source.Contains(".Failed", StringComparison.Ordinal);

            if (!handles) offenders.Add($"{name} — calls {string.Join(", ", called)}");
        }

        Assert.True(
            offenders.Count == 0,
            $"""
             These pages fetch a list that can report a refusal, and then ignore it:

               {string.Join("\n  ", offenders)}

             Render the failure — wrap the list in BenListState, or branch on .Failed where the
             list is mutated in place. If the fetch is genuinely a decoration, add the file to
             LoadResultRenderedGuardTests.Decorations with the reason, so the choice is on record.
             """);
    }

    /// <summary>
    /// An allowlist that outlives its entries stops guarding. Every exception must still be a file
    /// that exists and still calls one of these methods.
    /// </summary>
    [Fact]
    public void Every_declared_decoration_is_still_real()
    {
        var files = RazorFiles().ToList();

        foreach (var (name, reason) in Decorations)
        {
            var match = files.FirstOrDefault(f => Path.GetFileName(f) == name);
            Assert.True(match is not null, $"Decoration '{name}' no longer exists — remove it.");
            Assert.False(string.IsNullOrWhiteSpace(reason), $"Decoration '{name}' has no reason.");

            var source = StripComments(File.ReadAllText(match!));
            Assert.True(
                ConvertedMethods.Any(m => source.Contains(m, StringComparison.Ordinal)),
                $"Decoration '{name}' no longer calls a converted method — remove it from the list.");
        }
    }
}
