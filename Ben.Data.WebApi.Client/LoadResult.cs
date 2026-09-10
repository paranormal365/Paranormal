namespace Ben.Web.Services.WebApi;

/// <summary>
/// The answer to "give me this list", including the possibility that it could not be fetched.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <remarks>
/// <para><b>The bug this exists to end.</b> <c>GetAsync</c> returns <c>default</c> for any non-2xx
/// and the adapters follow it with <c>?? []</c>, so a 403, a 500 and a genuinely empty list all
/// arrive at a component as the same value. Every list surface then renders the same sentence —
/// "No records available" — and the page tells somebody their group is empty when the truth is
/// that the server refused them. Three separate bugs on 2026-08-20 shared exactly this cause;
/// see item 120.</para>
///
/// <para><b>A struct with a flag, not an exception.</b> Most callers want to render <i>something</i>
/// either way, and a component that throws inside a lifecycle method on a Blazor Server circuit
/// takes more with it than the list it was drawing. The type is deliberately small enough that
/// adopting it at a call site is a two-line change.</para>
///
/// <para><b>Empty and failed are different, and the type refuses to conflate them.</b>
/// <see cref="Items"/> is always safe to enumerate — a failed load has no items — so a call site
/// that ignores <see cref="Failed"/> behaves exactly as it does today and cannot be made worse by
/// the change. Rendering the difference is opt-in, which is what makes gradual adoption honest
/// rather than a promise to come back later.</para>
/// </remarks>
public readonly struct LoadResult<T>
{
    private readonly IReadOnlyList<T>? _items;

    private LoadResult(IReadOnlyList<T>? items, bool failed, string? reason, bool sessionExpired = false)
    {
        _items         = items;
        Failed         = failed;
        Reason         = reason;
        SessionExpired = sessionExpired;
    }

    /// <summary>What came back. Empty when the load failed — never null.</summary>
    public IReadOnlyList<T> Items => _items ?? [];

    /// <summary>True when the list could not be fetched at all.</summary>
    /// <remarks>
    /// Distinct from an empty <see cref="Items"/>: a successful load of nothing is
    /// <c>Failed == false</c> with no items, which is a real and different thing to say.
    /// </remarks>
    public bool Failed { get; }

    /// <summary>
    /// The server's own sentence, when it gave one worth showing. Null otherwise.
    /// </summary>
    /// <remarks>
    /// Only prose survives the trip — a ProblemDetails blob or an HTML error page is dropped
    /// rather than shown to a person, on the same reasoning as
    /// <c>WebApiClient.SendExpectingReasonAsync</c>.
    /// </remarks>
    public string? Reason { get; }

    /// <summary>True when the load succeeded and returned nothing — "there is genuinely nothing here".</summary>
    public bool IsEmpty => !Failed && Items.Count == 0;

    /// <summary>
    /// The caller is no longer signed in — the server answered 401.
    /// </summary>
    /// <remarks>
    /// <para>A subset of <see cref="Failed"/>, so any call site that only checks <c>Failed</c>
    /// keeps working. It is separated out because 401 is the one status where "couldn't load this,
    /// try again" is the wrong sentence: nothing about the list is broken and retrying will fail
    /// identically. The person has been signed out, and the only useful thing to say is so.</para>
    ///
    /// <para>Deliberately <b>not</b> 403. Forbidden means the session is fine and this particular
    /// thing is not theirs to see — telling them to sign in again would send them round a loop
    /// that ends where it started.</para>
    /// </remarks>
    public bool SessionExpired { get; }

    /// <summary>A successful load.</summary>
    public static LoadResult<T> Ok(IReadOnlyList<T>? items) => new(items, failed: false, reason: null);

    /// <summary>A load that did not happen. Carries the server's sentence when there was one.</summary>
    public static LoadResult<T> Failure(string? reason = null) => new(null, failed: true, reason);

    /// <summary>
    /// Projects the items, carrying every part of the outcome across unchanged.
    /// </summary>
    /// <remarks>
    /// An adapter that reshapes a response — <c>Ok(result.Items.Select(…))</c> — silently drops
    /// <see cref="SessionExpired"/> and <see cref="Reason"/> unless it remembers to copy them, and
    /// the two places that already did this by hand had both dropped the first one. Mapping keeps
    /// the answer and changes only the shape.
    /// </remarks>
    public LoadResult<TOut> Map<TOut>(Func<T, TOut> project) =>
        Failed
            ? new LoadResult<TOut>(null, failed: true, Reason, SessionExpired)
            : LoadResult<TOut>.Ok([.. Items.Select(project)]);

    /// <summary>A load refused because the caller is not signed in any more.</summary>
    /// <remarks>
    /// No reason string: "The server answered 401 (Unauthorized)" is a fact about HTTP, not
    /// something to show a person, and the surface renders its own sentence for this state.
    /// </remarks>
    public static LoadResult<T> SessionEnded() => new(null, failed: true, reason: null, sessionExpired: true);
}
