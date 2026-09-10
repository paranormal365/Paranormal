namespace Ben.Web.Services.WebApi;

/// <summary>
/// The answer to "give me this one thing", including the possibility that it could not be fetched.
/// </summary>
/// <typeparam name="T">The response type.</typeparam>
/// <remarks>
/// <para><b>The half of item 120 that never got done.</b> Lists got <see cref="LoadResult{T}"/>;
/// single-object GETs did not. <c>WebApiClient.GetAsync&lt;T&gt;</c> still returns <c>default</c>
/// for a 401, a 403, a 404 and a 500 alike, and — unlike the list path — never catches
/// <c>HttpRequestException</c>, so an unreachable API throws out of <c>OnInitializedAsync</c> and
/// takes the circuit with it.</para>
///
/// <para><b>What that cost.</b> On 2026-08-27 a restarted API had invalidated every bearer token
/// (see <c>DataProtectionSetup</c>). Every request 401'd, and because a 401 and an absent record
/// arrive here as the same <c>null</c>, the profile page could only guess — it said the session
/// "may" have expired, which is the sentence you write when the client cannot tell. The page was
/// not broken and its twelve Playwright tests were green.</para>
///
/// <para><b>Deliberately the same shape as <see cref="LoadResult{T}"/>.</b> Same flags, same
/// meanings, same rule that only prose from the server survives the trip. A person reading one
/// call site should not have to learn a second vocabulary because the endpoint returns an object
/// instead of an array.</para>
///
/// <para><b>404 is a failure here, not an emptiness.</b> Tempting to map it to "there is no such
/// record", and wrong: a mistyped route 404s identically, and conflating the two is how a broken
/// deployment renders as a page politely reporting that your case does not exist. The list path
/// made the same call for the same reason.</para>
/// </remarks>
public readonly struct ItemResult<T>
{
    private ItemResult(T? item, bool failed, string? reason, bool sessionExpired = false)
    {
        Item           = item;
        Failed         = failed;
        Reason         = reason;
        SessionExpired = sessionExpired;
    }

    /// <summary>What came back. Null when the fetch failed, and null is also a valid success.</summary>
    public T? Item { get; }

    /// <summary>True when the thing could not be fetched at all.</summary>
    public bool Failed { get; }

    /// <summary>The server's own sentence, when it gave one worth showing. Null otherwise.</summary>
    public string? Reason { get; }

    /// <summary>The fetch succeeded and the server had nothing to send — a 204 or an empty body.</summary>
    public bool IsEmpty => !Failed && Item is null;

    /// <summary>
    /// The caller is no longer signed in — the server answered 401.
    /// </summary>
    /// <remarks>
    /// A subset of <see cref="Failed"/>, so a call site that only checks <c>Failed</c> keeps
    /// working. Separated for the same reason as on <see cref="LoadResult{T}"/>: 401 is the one
    /// status where "couldn't load this, try again" is the wrong sentence. Not 403 — forbidden
    /// means the session is fine and this is not theirs to see.
    /// </remarks>
    public bool SessionExpired { get; }

    /// <summary>A successful fetch.</summary>
    public static ItemResult<T> Ok(T? item) => new(item, failed: false, reason: null);

    /// <summary>A fetch that did not happen. Carries the server's sentence when there was one.</summary>
    public static ItemResult<T> Failure(string? reason = null) => new(default, failed: true, reason);

    /// <summary>A fetch refused because the caller is not signed in any more.</summary>
    public static ItemResult<T> SessionEnded() => new(default, failed: true, reason: null, sessionExpired: true);

    /// <summary>
    /// Reshapes the item, carrying every part of the outcome across unchanged.
    /// </summary>
    /// <remarks>
    /// Adapters reshape responses constantly, and <c>Ok(Map(result.Item))</c> written by hand drops
    /// <see cref="SessionExpired"/> and <see cref="Reason"/> — which is exactly what happened twice
    /// on the list side before <c>LoadResult.Map</c> existed.
    /// </remarks>
    public ItemResult<TOut> Map<TOut>(Func<T, TOut?> project) =>
        Failed
            ? new ItemResult<TOut>(default, failed: true, Reason, SessionExpired)
            : ItemResult<TOut>.Ok(Item is null ? default : project(Item));
}
