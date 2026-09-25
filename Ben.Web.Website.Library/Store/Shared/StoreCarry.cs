using System.Text.Json;

namespace Ben.Web.Website.Library.Store.Shared;

/// <summary>
/// Whether a store page's server-fetched data is small enough to carry into its live copy
/// ([PersistentState]) — the storefront's own budget, the same rule develop's PrerenderCarry states.
/// </summary>
/// <remarks>
/// <para>What a page carries rides the live connection's first message, and the connection hangs up
/// on a message over its limit (128 KB here, Program.cs): the page is then drawn but DEAD. The
/// store's lists have no fixed size — every shelf is on the listing's side, every top-level one on
/// the front page — so a big enough catalogue would kill its own pages. Found 09/24 when the test
/// database's shelves (one per test product) carried 75 KB.</para>
///
/// <para>So a page that does not fit carries nothing and loads again when it comes alive: it blinks,
/// which is the worst this allows. 48 KB of JSON leaves the rest of the message for the layout.</para>
/// </remarks>
public static class StoreCarry
{
    public const int MaxBytes = 48 * 1024;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static bool Fits<T>(T value) => value is not null && JsonSerializer.SerializeToUtf8Bytes(value, Web).Length <= MaxBytes;

    /// <summary>The value when it fits, otherwise nothing — so an oversized page is never carried.</summary>
    public static T? IfItFits<T>(T? value) where T : class => value is not null && Fits(value) ? value : null;
}
