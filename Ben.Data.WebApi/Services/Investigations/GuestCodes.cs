using System.Security.Cryptography;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Investigations;

/// <summary>
/// The code a guide holds up, and the credential a scan of it produces (item 248).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-20: <i>"generate a qr code for the employees to allow someone to scan with
/// their phone which would let the person scanning it to have credentials to use their phone for
/// investigation."</i></para>
///
/// <para><b>What a holder may do, stated once here so it cannot drift.</b> Add their own
/// recordings, readings and photographs to THIS investigation, and read back their own. That is
/// the whole list. Not the case, not the client, not the address, not anybody else's evidence,
/// and nothing after the code expires.</para>
///
/// <para><b>Why it is not an attendee row.</b> The obvious implementation — write an
/// <see cref="InvestigationAttendee"/> and let the existing door do the rest — would hand a
/// stranger from the pavement everything a member of the team can see, because that row is what
/// <c>MayContributeAsync</c> reads, and that same method is the READ door for every session on
/// the visit. A walk-up is somebody the group has not vetted, standing at a private address they
/// were brought to. So the credential is its own row, and it is asked about only where something
/// is being WRITTEN.</para>
/// </remarks>
public static class GuestCodes
{
    /// <summary>
    /// The alphabet the typed code is drawn from.
    /// </summary>
    /// <remarks>
    /// Upper-case, digits, and then everything a person could mistake for something else taken
    /// out: no <c>I</c> or <c>1</c>, no <c>O</c> or <c>0</c>, no <c>L</c> against <c>1</c>, no
    /// <c>U</c> against <c>V</c>. The reader is holding a printed sheet in the dark and reading it
    /// out to somebody who is typing it, which is the worst case for an ambiguous glyph — and a
    /// code that has to be read twice is one the guide stops using.
    /// </remarks>
    internal const string TypedAlphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    /// <summary>How many characters the typed code carries, before the dash.</summary>
    /// <remarks>
    /// Eight, shown as two groups of four. Thirty to the eighth is about six hundred billion, so
    /// guessing is not a way in even before the expiry and the rate limit; six would have been
    /// seven hundred million, which is a number a script can walk.
    /// </remarks>
    internal const int TypedLength = 8;

    /// <summary>The longest a night's code may run, whatever the caller asks for.</summary>
    /// <remarks>
    /// A code scanned at seven in the evening must not still open anything in March. Twenty-four
    /// hours covers a visit that runs past midnight and the morning after, which is when somebody
    /// gets home and finally has the signal to send up what they recorded.
    /// </remarks>
    public static readonly TimeSpan LongestLife = TimeSpan.FromHours(24);

    // ── making one ───────────────────────────────────────────────────────────

    /// <summary>256 bits, hex — what the QR carries.</summary>
    /// <remarks>
    /// Random rather than derived, for the same reason a share link's token is: this has to stop
    /// working the moment a guide says so, and a handle computed from its inputs would come back
    /// the next time those inputs recurred. Hex because a QR stores upper-case alphanumerics in a
    /// denser mode than mixed case — the same secret in fewer squares, which scans in worse light.
    /// </remarks>
    internal static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>A typed code, formatted the way it is printed.</summary>
    internal static string NewTypedCode()
    {
        var chars = new char[TypedLength + 1];
        var at = 0;
        for (var i = 0; i < TypedLength; i++)
        {
            if (i == TypedLength / 2) chars[at++] = '-';
            chars[at++] = TypedAlphabet[RandomNumberGenerator.GetInt32(TypedAlphabet.Length)];
        }
        return new string(chars);
    }

    /// <summary>
    /// What somebody typed, as the stored code would look.
    /// </summary>
    /// <remarks>
    /// Case is folded, spaces and dashes dropped, then the dash put back where it is printed. A
    /// guest types what they see and a guest types what they hear, and those are different
    /// strings for the same code — refusing the second one would be refusing the code.
    /// </remarks>
    public static string? NormaliseTyped(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;

        Span<char> kept = stackalloc char[TypedLength];
        var n = 0;
        foreach (var c in typed)
        {
            if (c is ' ' or '-' or '_' or '.') continue;
            if (n == TypedLength) return null;              // longer than a code: not one

            var upper = char.ToUpperInvariant(c);
            if (!TypedAlphabet.Contains(upper)) return null; // a glyph no code contains
            kept[n++] = upper;
        }

        if (n != TypedLength) return null;
        return string.Concat(kept[..(TypedLength / 2)], "-", kept[(TypedLength / 2)..]);
    }

    /// <summary>
    /// Makes this investigation a fresh code, retiring whatever it had.
    /// </summary>
    /// <remarks>
    /// <para><b>Rotating is how a code is taken back.</b> A sheet left on a pub table is the
    /// failure this has to survive, and the answer is a new sheet — so making one always retires
    /// the last, and there is never a moment when two codes for one night are both live.</para>
    ///
    /// <para>Passes already minted are NOT revoked. The people holding them scanned the old code
    /// in good faith and are standing in the building; taking their credential away because the
    /// sheet was reprinted would delete the evening's work of everybody who arrived early. One
    /// holder is revoked one at a time, deliberately, which is the whole reason the pass is its
    /// own row.</para>
    ///
    /// <para>Nothing is saved here: the caller owns the transaction.</para>
    /// </remarks>
    public static async Task<InvestigationJoinCode> IssueAsync(
        BenDataContext db, Investigation investigation, Guid actorId, DateTime expiresUtc,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var ceiling = now + LongestLife;
        if (expiresUtc > ceiling) expiresUtc = ceiling;
        if (expiresUtc <= now) expiresUtc = now + TimeSpan.FromHours(12);

        foreach (var old in await LiveForAsync(db, investigation.Id, ct))
            Revoke(old, actorId);

        var code = new InvestigationJoinCode
        {
            Id = Guid.NewGuid(),
            InvestigationId = investigation.Id,
            OrganizationId = investigation.OrganizationId,
            Token = NewToken(),
            TypedCode = await FreeTypedCodeAsync(db, ct),
            ExpiresUtc = expiresUtc,
            DateCreated = now,
            CreatedByAppUserId = actorId,
        };
        db.InvestigationJoinCodes.Add(code);
        return code;
    }

    /// <summary>Takes a code out of use. The first revocation is the one kept.</summary>
    public static void Revoke(InvestigationJoinCode code, Guid actorId)
    {
        if (code.RevokedUtc is not null) return;

        code.RevokedUtc = DateTime.UtcNow;
        code.RevokedByAppUserId = actorId;
        code.DateUpdated = DateTime.UtcNow;
        code.UpdatedByAppUserId = actorId;
    }

    // ── reading one ──────────────────────────────────────────────────────────

    /// <summary>Whether this code still admits anybody.</summary>
    public static bool IsLive(InvestigationJoinCode code, DateTime? asOf = null)
        => code.RevokedUtc is null && code.ExpiresUtc > (asOf ?? DateTime.UtcNow);

    /// <summary>This investigation's live codes, newest first. Normally none or one.</summary>
    /// <remarks>
    /// A list rather than a single row because "issue" runs in a transaction somebody else may
    /// have raced: two guides pressing the button at once must leave a state this can tidy, not
    /// one where the second write throws.
    /// </remarks>
    public static async Task<List<InvestigationJoinCode>> LiveForAsync(
        BenDataContext db, Guid investigationId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await db.InvestigationJoinCodes
            .Where(c => c.InvestigationId == investigationId
                     && c.RevokedUtc == null && c.ExpiresUtc > now)
            .OrderByDescending(c => c.DateCreated)
            .ToListAsync(ct);
    }

    /// <summary>The code behind a scanned token or a typed string, live or not.</summary>
    /// <remarks>
    /// Expired and revoked codes are still FOUND, because "that code ran out at midnight" is a
    /// different thing to say than "we don't recognise that", and only one of them tells the
    /// person in front of you what to do.
    /// </remarks>
    public static async Task<InvestigationJoinCode?> FindAsync(
        BenDataContext db, string? tokenOrTyped, CancellationToken ct)
    {
        var raw = tokenOrTyped?.Trim();
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 128) return null;

        if (NormaliseTyped(raw) is { } typed)
        {
            return await db.InvestigationJoinCodes
                .Include(c => c.Investigation)
                .FirstOrDefaultAsync(c => c.TypedCode == typed, ct);
        }

        return await db.InvestigationJoinCodes
            .Include(c => c.Investigation)
            .FirstOrDefaultAsync(c => c.Token == raw, ct);
    }

    /// <summary>
    /// Why this code does not let somebody in, or null when it does.
    /// </summary>
    /// <remarks>
    /// Sentences, like every other door on the site: the reader is a guest holding a phone in a
    /// field, and "404" is not something a guide can help them with.
    /// </remarks>
    public static string? WhyThisCodeIsRefused(InvestigationJoinCode? code)
    {
        if (code is null)
            return "We don't recognise that code. Check it against the sheet, or ask the guide for a new one.";

        if (code.RevokedUtc is not null)
            return "That code was replaced. Ask the guide for the one they're showing now.";

        if (code.ExpiresUtc <= DateTime.UtcNow)
            return "That code has run out. Ask the guide for tonight's.";

        return null;
    }

    // ── the credential ───────────────────────────────────────────────────────

    /// <summary>
    /// Gives this person a pass against this code, keeping the one they hold.
    /// </summary>
    /// <remarks>
    /// <para>Idempotent per person. Somebody scans the sheet, loses the app, and scans it again;
    /// that is one credential, not two, or a guide revoking "them" would revoke one of two rows
    /// and wonder why the phone kept working.</para>
    ///
    /// <para>A REVOKED pass is not quietly reissued by a second scan. The guide took it away on
    /// purpose and the sheet is still on the table; handing it back to whoever scans again would
    /// make revocation mean nothing. They get the refusal and the guide gets asked.</para>
    ///
    /// <para>Nothing is saved here: the caller owns the transaction.</para>
    /// </remarks>
    public static async Task<(InvestigationGuestPass? Pass, string? Refused)> RedeemAsync(
        BenDataContext db, InvestigationJoinCode code, Guid userId, string? displayName,
        CancellationToken ct)
    {
        if (WhyThisCodeIsRefused(code) is { } no) return (null, no);

        var held = await db.InvestigationGuestPasses
            .FirstOrDefaultAsync(p => p.InvestigationJoinCodeId == code.Id && p.AppUserId == userId, ct);

        if (held is { RevokedUtc: not null })
            return (null, "The guide took that pass back. Ask them before you scan again.");

        if (held is not null) return (held, null);

        var now = DateTime.UtcNow;
        var pass = new InvestigationGuestPass
        {
            Id = Guid.NewGuid(),
            InvestigationJoinCodeId = code.Id,
            InvestigationId = code.InvestigationId,
            AppUserId = userId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            IssuedUtc = now,
            DateCreated = now,
            CreatedByAppUserId = userId,
        };
        db.InvestigationGuestPasses.Add(pass);
        return (pass, null);
    }

    /// <summary>
    /// Whether this person holds a live guest pass for this investigation.
    /// </summary>
    /// <remarks>
    /// <para><b>The one question the write doors ask.</b> A pass is live while it has not been
    /// revoked and the code that minted it has not been revoked or run out — so a guide revoking
    /// the sheet at the end of the night closes everybody's, and revoking one row closes one
    /// person's, without either needing to know about the other.</para>
    ///
    /// <para>This is deliberately NOT wired into <c>MayContributeAsync</c>, which is also the
    /// read door for every session on the visit. See the remarks on this class.</para>
    /// </remarks>
    public static Task<bool> HoldsALivePassAsync(
        BenDataContext db, Guid investigationId, Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty) return Task.FromResult(false);

        var now = DateTime.UtcNow;
        return db.InvestigationGuestPasses.AsNoTracking()
            .AnyAsync(p => p.InvestigationId == investigationId
                        && p.AppUserId == userId
                        && p.RevokedUtc == null
                        && p.InvestigationJoinCode!.RevokedUtc == null
                        && p.InvestigationJoinCode.ExpiresUtc > now, ct);
    }

    /// <summary>Takes one person's credential away, leaving everybody else's alone.</summary>
    public static void RevokePass(InvestigationGuestPass pass, Guid actorId)
    {
        if (pass.RevokedUtc is not null) return;

        pass.RevokedUtc = DateTime.UtcNow;
        pass.RevokedByAppUserId = actorId;
        pass.DateUpdated = DateTime.UtcNow;
        pass.UpdatedByAppUserId = actorId;
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A typed code no row holds.
    /// </summary>
    /// <remarks>
    /// The space is six hundred billion and the index is unique, so this loop is not the reason
    /// the code is unique — the index is. This only saves the guide a failed save on the day the
    /// coin lands on its edge, and gives up rather than spinning if something is badly wrong.
    /// </remarks>
    private static async Task<string> FreeTypedCodeAsync(BenDataContext db, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = NewTypedCode();
            if (!await db.InvestigationJoinCodes.AnyAsync(c => c.TypedCode == candidate, ct))
                return candidate;
        }
        return NewTypedCode();
    }
}
