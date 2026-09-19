using System.Net;
using System.Net.Sockets;

namespace Ben.Data.WebApi.Services.LinkUnfurl;

/// <summary>
/// Which addresses the link-unfurl fetcher may be pointed at, and which network addresses it may
/// connect to. Pure: no DNS, no network, no clock.
/// </summary>
/// <remarks>
/// <para><b>Why this exists at all.</b> <c>PublicLinkPreviewController</c> refuses to fetch anything,
/// because a server that fetches an address a stranger chose is the shape of every server-side
/// request forgery there has been: the cloud metadata endpoint, the SQL Server listening on this
/// box's own LAN address (192.168.1.71), a router's admin page. Ben reversed that rule for the canvas
/// on 2026-09-14 so a pasted link becomes a preview card. This class is most of what makes the
/// reversal safe; <see cref="SafeUrlFetcher"/> is the rest.</para>
///
/// <para><b>Two questions, asked at two moments.</b> <see cref="Refuse"/> is asked of the URL before
/// anything happens: https only, port 443, no credentials, a real DNS name. <see cref="IsForbidden"/>
/// is asked of every address the name resolves to, and again by the connect callback of the one
/// address actually dialled — so a name that resolves to a private address is refused however
/// ordinary the name looks.</para>
///
/// <para><b>IPv6 spellings of IPv4 addresses</b> (canvas plan review R21). A filter that knows
/// 127.0.0.1 is loopback and does not know <c>::ffff:127.0.0.1</c>, <c>::7f00:1</c>,
/// <c>64:ff9b::7f00:1</c>, <c>2002:7f00:1::</c> or a Teredo address whose client half decodes to it
/// is a filter with a door in it. Each form that embeds an IPv4 has that IPv4 extracted and tested by
/// the IPv4 rules; the forms with no reliable extraction (local-use NAT64 <c>64:ff9b:1::/48</c>,
/// discard-only <c>100::/64</c>) are refused outright.</para>
///
/// <para><b>A deny list, deliberately complete</b> rather than an allow list of "global unicast":
/// every special-purpose block in the IANA registries that can route somewhere private, local,
/// shared or reserved is named below, so a reader can check it against the registry line by line.</para>
/// </remarks>
public static class SafeUrlPolicy
{
    /// <summary>The longest URL the fetcher will take, in characters.</summary>
    public const int MaxUrlLength = 2048;

    private static readonly string[] ForbiddenSuffixes = [".local", ".internal", ".localhost", ".home.arpa", ".lan", ".intranet", ".corp"];

    /// <summary>
    /// Why this URL may not be fetched, as a sentence, or null when it may.
    /// </summary>
    public static string? Refuse(Uri url)
    {
        if (url is null || !url.IsAbsoluteUri) return "Only a full https address can be previewed.";
        if (!string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            return "Only https addresses can be previewed.";
        if (!string.IsNullOrEmpty(url.UserInfo))
            return "An address with a user name or password in it cannot be previewed.";
        if (url.Port != 443)
            return "Only addresses on the standard https port can be previewed.";
        if (url.OriginalString.Length > MaxUrlLength)
            return $"An address longer than {MaxUrlLength.ToString(System.Globalization.CultureInfo.InvariantCulture)} characters cannot be previewed.";

        if (url.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
            || IPAddress.TryParse(url.Host.Trim('[', ']'), out _))
            return "An address that is a bare IP number cannot be previewed; use the site's name.";

        if (url.HostNameType != UriHostNameType.Dns)
            return "That site name cannot be previewed.";

        var host = url.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0 || host.Any(c => c == '%' || char.IsWhiteSpace(c)))
            return "That site name cannot be previewed.";
        if (host == "localhost" || ForbiddenSuffixes.Any(s => host.EndsWith(s, StringComparison.Ordinal)))
            return "That site name belongs to a private network and cannot be previewed.";
        if (!host.Contains('.'))
            return "A site name with no dot in it is a private network name and cannot be previewed.";

        return null;
    }

    /// <summary>
    /// Whether the fetcher must never connect to this address: private, loopback, link-local,
    /// shared, documentation, benchmarking, multicast, reserved, or an IPv6 form carrying one.
    /// </summary>
    public static bool IsForbidden(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.AddressFamily == AddressFamily.InterNetwork)
            return IsForbiddenV4(address.GetAddressBytes());

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return true;   // anything that is neither is not something we dial

        var b = address.GetAddressBytes();

        // ::ffff:0:0/96 — IPv4-mapped. The OS dials the IPv4.
        if (address.IsIPv4MappedToIPv6) return IsForbiddenV4(b[12..16]);

        // ::/96 — IPv4-compatible (deprecated), including :: and ::1 themselves.
        if (AllZero(b, 0, 12))
            return AllZero(b, 12, 4) || (AllZero(b, 12, 3) && b[15] == 1) || IsForbiddenV4(b[12..16]);

        // 64:ff9b::/96 — well-known NAT64; the gateway dials the embedded IPv4.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && AllZero(b, 4, 8))
            return IsForbiddenV4(b[12..16]);

        // 64:ff9b:1::/48 — local-use NAT64 (RFC 8215): the embedding is operator-defined, so there is
        // no reliable IPv4 to test. Refused.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b[4] == 0x00 && b[5] == 0x01)
            return true;

        // 2002::/16 — 6to4; bits 16..48 are the IPv4 the tunnel ends at.
        if (b[0] == 0x20 && b[1] == 0x02) return IsForbiddenV4(b[2..6]);

        // 2001::/32 — Teredo: server IPv4 in bits 32..64, client IPv4 obfuscated (XOR) in the last 32.
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00)
        {
            byte[] client = [(byte)~b[12], (byte)~b[13], (byte)~b[14], (byte)~b[15]];
            return IsForbiddenV4(b[4..8]) || IsForbiddenV4(client);
        }

        // 100::/64 — discard-only.
        if (b[0] == 0x01 && b[1] == 0x00 && AllZero(b, 2, 6)) return true;

        // fc00::/7 unique local (includes fd00:ec2::254, the AWS metadata address over IPv6).
        if ((b[0] & 0xfe) == 0xfc) return true;
        // fe80::/10 link-local, fec0::/10 site-local (deprecated but still routed by some stacks).
        if (b[0] == 0xfe && (b[1] & 0xc0) is 0x80 or 0xc0) return true;
        // ff00::/8 multicast.
        if (b[0] == 0xff) return true;
        // 2001:db8::/32 documentation.
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8) return true;
        // 2001:10::/28 ORCHID and 2001:20::/28 ORCHIDv2: not routable.
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && (b[3] & 0xf0) is 0x10 or 0x20) return true;
        // 5f00::/16 SRv6 SIDs, 3fff::/20 documentation (RFC 9637).
        if (b[0] == 0x5f && b[1] == 0x00) return true;
        if (b[0] == 0x3f && b[1] == 0xff && (b[2] & 0xf0) == 0x00) return true;

        return false;
    }

    private static bool IsForbiddenV4(byte[] a)
    {
        return a[0] switch
        {
            0 => true,                                           // 0.0.0.0/8 "this network"
            10 => true,                                          // 10.0.0.0/8 private
            100 when (a[1] & 0xc0) == 64 => true,                // 100.64.0.0/10 shared (CGNAT)
            127 => true,                                         // 127.0.0.0/8 loopback
            169 when a[1] == 254 => true,                        // 169.254.0.0/16 link-local, cloud metadata
            172 when (a[1] & 0xf0) == 16 => true,                // 172.16.0.0/12 private
            192 when a[1] == 0 && a[2] == 0 => true,             // 192.0.0.0/24 IETF protocol assignments
            192 when a[1] == 0 && a[2] == 2 => true,             // 192.0.2.0/24 documentation
            192 when a[1] == 88 && a[2] == 99 => true,           // 192.88.99.0/24 6to4 relay anycast
            192 when a[1] == 168 => true,                        // 192.168.0.0/16 private
            198 when (a[1] & 0xfe) == 18 => true,                // 198.18.0.0/15 benchmarking
            198 when a[1] == 51 && a[2] == 100 => true,          // 198.51.100.0/24 documentation
            203 when a[1] == 0 && a[2] == 113 => true,           // 203.0.113.0/24 documentation
            >= 224 => true,                                      // 224/4 multicast, 240/4 reserved, 255.255.255.255
            _ => false,
        };
    }

    private static bool AllZero(byte[] bytes, int start, int count)
    {
        for (var i = start; i < start + count; i++)
            if (bytes[i] != 0) return false;
        return true;
    }
}
