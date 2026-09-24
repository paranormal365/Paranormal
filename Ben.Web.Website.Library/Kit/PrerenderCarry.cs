using System.Text.Json;

namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// Whether a page's server-fetched data is small enough to carry into its live copy
/// ([PersistentState]) — see PagesDoNotBlinkTests.
/// </summary>
/// <remarks>
/// <para><b>Why there is a budget at all.</b> What a page carries is written into the HTML and sent
/// back to the server in the live connection's first message, and SignalR hangs up on a message
/// over its limit (32 KB by default). A page over it is not merely slow: it is drawn but dead —
/// no button answers. The storefront met exactly that at 35 KB.</para>
///
/// <para><b>So a page that does not fit carries nothing</b> and simply loads again when it comes
/// alive — the old blink, which is the worst case this allows. 12 KB of JSON is about 16 KB once
/// the page encodes it, leaving the rest of the message for the sign-in state and anything else
/// the layout carries. Lists with no natural limit (a group's events, its published cases) are
/// the reason: the test database's are small, a long-running group's may not be.</para>
/// </remarks>
public static class PrerenderCarry
{
    public const int MaxBytes = 12 * 1024;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static bool Fits<T>(T value) => value is not null && JsonSerializer.SerializeToUtf8Bytes(value, Web).Length <= MaxBytes;
}
