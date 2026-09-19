using System.Security.Cryptography;

namespace Ben.Data.WebApi.Services.Billing;

/// <summary>
/// Makes the code strings for a generated batch.
/// </summary>
/// <remarks>
/// <para><b>The alphabet is missing six letters and four digits on purpose.</b> These codes get
/// read off a printed card, a name badge or a forwarded email and typed by somebody who cannot
/// check their work. <c>O</c> and <c>0</c>, <c>I</c> and <c>1</c> and <c>l</c>, <c>S</c> and
/// <c>5</c>, <c>B</c> and <c>8</c>, <c>Z</c> and <c>2</c> are the confusions that actually happen,
/// and every one of them turns into a support message rather than a redemption. Dropping them
/// costs about a bit and a half per character, which the length absorbs.</para>
///
/// <para><b>Cryptographically random, not <c>Random</c>.</b> Not because guessing a code is a
/// serious attack — the redemption limits bound the damage — but because a predictable sequence
/// means one leaked code implies the rest of the batch, and the batch is often worth real money.
/// <see cref="RandomNumberGenerator.GetInt32(int)"/> is also unbiased, which a modulo of a byte
/// is not.</para>
///
/// <para><b>Uniqueness is the database's job.</b> This returns distinct strings within one call,
/// but the unique index on <c>CouponCode.Code</c> is the authority: two SuperAdmins generating
/// batches at the same moment is unlikely and entirely possible, and a collision must fail the
/// insert rather than quietly overwrite somebody's campaign.</para>
/// </remarks>
public static class CouponCodeGenerator
{
    /// <summary>Upper-case letters and digits, minus every pair that gets misread. 26 symbols.</summary>
    private const string Alphabet = "ACDEFGHJKMNPQRTUVWXY34679";

    /// <summary>Longest sensible prefix, leaving room for the random part inside 64 characters.</summary>
    public const int MaxPrefixLength = 24;

    /// <summary>
    /// One code: an optional prefix, then a dash, then <paramref name="randomLength"/> symbols.
    /// </summary>
    /// <param name="prefix">
    /// A campaign marker such as <c>PARACON</c>, upper-cased. Purely for the humans handing the
    /// codes out and reading redemption reports — it is not checked at redemption.
    /// </param>
    /// <param name="randomLength">
    /// How many random symbols. Eight gives about 37 bits, which is far beyond guessing for a batch
    /// of a few hundred and still short enough to read aloud.
    /// </param>
    public static string One(string? prefix = null, int randomLength = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(randomLength, 4);

        var body = string.Create(randomLength, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });

        var clean = Normalise(prefix);

        return clean.Length == 0 ? body : $"{clean}-{body}";
    }

    /// <summary>
    /// A batch of distinct codes.
    /// </summary>
    /// <remarks>
    /// Distinctness is enforced by a set rather than assumed from the randomness. At eight symbols
    /// the birthday collision chance across five hundred codes is tiny but not zero, and "tiny but
    /// not zero" describes every bug that takes a year to find.
    /// </remarks>
    public static IReadOnlyList<string> Batch(int count, string? prefix = null, int randomLength = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 10_000);

        var made = new HashSet<string>(StringComparer.Ordinal);

        // Bounded rather than while(true): if the alphabet or length ever made saturation possible,
        // an unbounded loop here would hang a request instead of failing it.
        for (var attempts = 0; made.Count < count && attempts < count * 20; attempts++)
            made.Add(One(prefix, randomLength));

        if (made.Count < count)
            throw new InvalidOperationException(
                $"Could only generate {made.Count} distinct codes of {count}. "
              + "Use a longer random part.");

        return [.. made];
    }

    /// <summary>
    /// The stored form of a code: upper-cased and trimmed, so what is typed matches what is saved.
    /// </summary>
    /// <remarks>
    /// The one function both sides must agree on. Generation, the redemption lookup and the unique
    /// index all go through here, because a code stored in one form and looked up in another is a
    /// bug that only shows for the people who use lower case.
    /// </remarks>
    public static string Normalise(string? code) =>
        (code ?? string.Empty).Trim().ToUpperInvariant();
}
