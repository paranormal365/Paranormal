using System.Net;

namespace Ben.Data.WebApi.Client.Auth;

/// <summary>
/// Which deployment of Ben.Data.WebApi a client talks to.
/// </summary>
/// <remarks>
/// <para>The dev address is <c>127.0.0.1</c> and not <c>localhost</c>, deliberately. .NET on macOS
/// has a bug in its IPv6 accept path that kills the process from a threadpool thread, so every dev
/// host in this repo binds IPv4 only (item 187). A client asking for <c>localhost</c> would try
/// <c>::1</c> first and be refused.</para>
///
/// <para>Production is a sub-path, <c>/webapi</c>, because the API is an IIS sub-application.
/// <c>HttpClient.BaseAddress</c> silently DROPS the path for any request path beginning with a
/// slash, which turns every call into a 404 against the website. Either write relative paths or
/// keep <c>ApiBasePathHandler</c> in the pipeline; the handler is the safer of the two because it
/// cannot be forgotten one call site at a time.</para>
/// </remarks>
public sealed record ApiEnvironment(string Name, Uri BaseUrl)
{
    public static readonly ApiEnvironment Dev =
        new("Development", new Uri("http://127.0.0.1:5252"));

    public static readonly ApiEnvironment Production =
        new("Production", new Uri("https://ishaunted.com/webapi"));

    /// <summary>
    /// Where a build points when nobody has chosen: the dev API for a Debug build, production
    /// otherwise.
    /// </summary>
    public static ApiEnvironment Fallback =>
#if DEBUG
        Dev;
#else
        Production;
#endif

    /// <summary>
    /// Whether this address could possibly work on somebody else's machine.
    /// </summary>
    /// <remarks>
    /// A saved development address is the one setting guaranteed to be wrong for everyone except
    /// the person who set it, and the symptom it produces is an app that simply cannot reach
    /// anything — with no message that names the cause. A release build must refuse to honour one.
    /// </remarks>
    public bool IsReachableOffDevelopmentMachine
    {
        get
        {
            var host = BaseUrl.Host;

            if (BaseUrl.IsLoopback) return false;
            if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return false;

            if (!IPAddress.TryParse(host, out var ip)) return true;

            var b = ip.GetAddressBytes();
            if (b.Length != 4) return true;

            // RFC 1918, plus link-local. 172.16-172.31 only — 172.32 is public, and treating the
            // whole of 172 as private is the usual way this check is written wrong.
            if (b[0] == 10) return false;
            if (b[0] == 192 && b[1] == 168) return false;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;
            if (b[0] == 169 && b[1] == 254) return false;

            return true;
        }
    }
}
