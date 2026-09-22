using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Billing;

/// <summary>
/// How much a person may store when nothing is paying for them.
/// </summary>
/// <remarks>
/// <para><b>Why this exists beside <see cref="SubscriptionLimitGuard"/> rather than inside it.</b>
/// Every method on that guard takes an organization id, because every paid limit is a property of
/// a group's plan. A free individual — the person the field archive is FOR — belongs to no group,
/// so there is no id to pass and no subscription row to read. The result until now was that the
/// one kind of account with nothing paying for it was also the only one with no limit at all,
/// which became true the moment the sitewide upload caps were removed.</para>
///
/// <para><b>It counts what the person owns, not what they can see.</b> A member of a group whose
/// plan pays for storage is not charged here for the group's files: those live under the
/// organization's path and are governed by the group's own limit. This counts only files stored
/// against the account itself — field sessions belonging to no investigation, which is exactly
/// the free lane's output.</para>
///
/// <para><b>Refusal is a sentence, never a boolean.</b> The same shape
/// <see cref="SubscriptionLimitGuard"/> uses: null means allowed, and anything else is text a
/// person can act on. A cap that says only "no" teaches somebody to think the site is broken.</para>
/// </remarks>
public static class AccountStorageGuard
{
    /// <summary>What a free account gets when the setting is unset, in megabytes.</summary>
    /// <remarks>
    /// Ben, 2026-08-31: generous enough that a genuine contributor does not meet it in their first
    /// months, because the free lane exists to fill the archive and a cap that bites early
    /// collects nothing. Small enough that nobody uses the site as a file host.
    /// </remarks>
    public const int DefaultFreeMegabytes = 2048;

    private const long BytesPerMegabyte = 1024L * 1024L;

    /// <summary>
    /// Bytes this account is keeping to ITSELF — the only storage a free plan is charged for.
    /// </summary>
    /// <remarks>
    /// <para><b>Published sessions do not count</b> (Ben, 2026-08-31: "I just want to make sure I
    /// am not giving out space for things that do not benefit me"). A recording contributed to a
    /// place's public archive earns its disk: it is what makes the archive worth reading, what the
    /// comparison engine measures against, and what a stranger arriving at that place actually
    /// sees. A recording nobody but its owner will ever see earns nothing, and that is a private
    /// vault kept at the platform's expense.</para>
    ///
    /// <para>So the cap is a nudge rather than a tax. Contribute and your room comes back;
    /// hoard and you meet the ceiling and choose between publishing and paying. A paid plan is
    /// exempt from the whole question, which is the point of paying.</para>
    ///
    /// <para><b>It cannot be gamed</b>, and the interlock is worth naming: publishing frees the
    /// space, and RETRACTING a publication is itself a paid feature. A free account cannot publish
    /// to clear its usage and then quietly take the contribution back.</para>
    ///
    /// <para><b>Counted by where the bytes live, not by who the row names (C1, 2026-09-22).</b>
    /// This used to add one term per kind of upload, and its own remarks admitted the cost: place
    /// evidence arrived uncounted for a day because a term had to be remembered. The obvious fix —
    /// count every <c>UploadFile</c> whose <c>AppUserId</c> is this person — is WRONG, and the
    /// entity's own comment is what misleads: it says exactly one of <c>AppUserId</c> and
    /// <c>OwnerOrganizationId</c> is set, but the latter is only set when a file is HANDED OVER to
    /// a group (item 180 Phase B). Ordinary group work still sits under whoever uploaded it, so
    /// counting by ownership charges a member for their group's case evidence.</para>
    ///
    /// <para>Storage paths do separate them, totally and with no exceptions: there are exactly
    /// three builders — <c>users/{id}/</c>, <c>orgs/{id}/</c>, <c>cases/{id}/</c> — every write
    /// goes through one, and the field-session code already chooses between the first two on
    /// precisely this question ("a personal session lives under the person, not under a group they
    /// may not belong to"). So one rule covers every personal door, including doors added later,
    /// which is the property the per-kind list never had.</para>
    ///
    /// <para><b>What is exempt, and the principle behind it: a person is charged for what they can
    /// SEE and REMOVE.</b></para>
    ///
    /// <para>Superseded versions are not counted. Replacing a file archives the old bytes — a row
    /// carrying <c>ArchivedFromUploadFileId</c>, which every listing filters out and nothing
    /// prunes. Counting those would mean somebody who cut a 1 GB video down to 100 MB watched
    /// their usage RISE by their own good behaviour, with nothing on any screen showing the
    /// difference and no button to remove it. A refusal nobody can act on is worse than storing a
    /// few bytes we do not charge for; the archives want a retention job, not a surcharge.</para>
    ///
    /// <para><b>Clips and edited versions ARE counted, and the difference matters</b> (Ben,
    /// 2026-09-22: <i>"sometimes videos are clipped or audio creates clips and they don't want to
    /// delete the original version"</i>). A clip carries <c>ParentFileId</c> and no
    /// <c>ArchivedFromUploadFileId</c>, so it appears in the listing beside its original and both
    /// can be deleted. Keeping both is a deliberate choice somebody can undo, so both count —
    /// and nothing may ever prune them. Any future cleanup keys on
    /// <c>ArchivedFromUploadFileId</c>; keyed on <c>ParentFileId</c> it would delete exactly the
    /// originals and clips people meant to keep.</para>
    ///
    /// <para>Published sessions stay exempt, for the reason given above.</para>
    /// </remarks>
    public static async Task<long> UsedBytesAsync(
        BenDataContext db, Guid appUserId, CancellationToken ct)
    {
        // The one discriminator. LocalFileStorageService.UserFilePath builds exactly this, and
        // including the account id means the prefix is an ownership test as well as a personal-vs-
        // group one — another person's files cannot match it however the row is otherwise filled in.
        var mine = $"users/{appUserId}/";

        var kept = await db.UploadFiles.AsNoTracking()
            .Where(f => f.StoragePath != null
                     && f.StoragePath.StartsWith(mine)
                     && f.ArchivedFromUploadFileId == null)
            .SumAsync(f => (long?)f.FileSize, ct) ?? 0L;

        // Subtracted rather than filtered out of the sum above, because the exemption is a
        // property of the SESSION (published, or attached to an investigation) and not of the
        // file — it cannot be expressed as a condition on UploadFile at all.
        var earnedItsDisk = await db.FieldSessionUploadFiles.AsNoTracking()
            .Where(f => f.UploadFile.StoragePath != null
                     && f.UploadFile.StoragePath.StartsWith(mine)
                     && f.UploadFile.ArchivedFromUploadFileId == null
                     && (f.FieldSessionUpload.InvestigationId != null
                      || f.FieldSessionUpload.PublishedAtUtc != null))
            .SumAsync(f => (long?)f.UploadFile.FileSize, ct) ?? 0L;

        return kept - earnedItsDisk;
    }

    /// <summary>The cap in bytes, from settings, falling back to the built-in default.</summary>
    public static async Task<long> CapBytesAsync(BenDataContext db, CancellationToken ct)
    {
        var raw = await SiteSettingsService.GetAsync(db, SiteSettingKeys.FreeAccountStorageMegabytes, ct);

        // Unparseable or non-positive reads as unset — the degrade-to-default rule the other
        // numeric settings follow. A typo in an admin box must not remove everybody's limit, and
        // must not set it to zero either.
        var megabytes = int.TryParse(raw, out var value) && value > 0 ? value : DefaultFreeMegabytes;
        return megabytes * BytesPerMegabyte;
    }

    /// <summary>
    /// Why this account may not store <paramref name="incomingBytes"/> more, or null when it may.
    /// </summary>
    /// <remarks>
    /// <para><b>Members of a paying group are not capped here at all.</b> Their personal scouting
    /// sessions are a rounding error beside the work their group's plan already covers, and
    /// charging them twice for belonging somewhere would be a strange way to reward it. Somebody
    /// who leaves every group falls back under the cap for anything they upload afterwards —
    /// nothing already stored is touched, because retroactively refusing storage somebody was
    /// allowed to use is a broken promise rather than a limit.</para>
    ///
    /// <para>The incoming file is counted BEFORE it is written, so the cap is a limit rather than
    /// a thing noticed afterwards.</para>
    /// </remarks>
    public static async Task<string?> WhyCannotStoreAsync(
        BenDataContext db, Guid appUserId, long incomingBytes, CancellationToken ct)
    {
        // Asked through PaidPlan rather than re-queried here (2026-09-17 audit). This held its own
        // copy of that query — identical, so it was right, which is exactly how the drift starts:
        // PaidPlan is where "covered by a plan somebody is paying for" is defined, and it says so
        // in its own remarks ("there are now several callers and they must not drift"). A second
        // copy means the next change to what counts as paid — seats, trials, a grace window —
        // reaches storage only if somebody remembers this file.
        if (await PaidPlan.CoversAsync(db, appUserId, ct)) return null;

        var cap = await CapBytesAsync(db, ct);
        var used = await UsedBytesAsync(db, appUserId, ct);

        if (used + incomingBytes <= cap) return null;

        return $"That would take you past the {Describe(cap)} a free account can store "
             + $"(you're using {Describe(used)}). Joining a group on a paid plan, or removing "
             + "sessions you no longer need, makes room.";
    }

    /// <summary>Bytes in the units a person would say them in.</summary>
    private static string Describe(long bytes) => bytes >= 1024L * 1024L * 1024L
        ? $"{bytes / (double)(1024L * 1024L * 1024L):0.#} GB"
        : $"{bytes / (double)BytesPerMegabyte:0} MB";
}
