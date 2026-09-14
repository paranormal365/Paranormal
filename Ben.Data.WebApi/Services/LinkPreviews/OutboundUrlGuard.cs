using System.Net;
using System.Net.Sockets;

namespace Ben.Data.WebApi.Services.LinkPreviews;

/// <summary>
/// Decides whether this server may connect to an address somebody else chose.
/// </summary>
/// <remarks>
/// <para>A link preview is a request our server makes to a URL a member pasted. Unguarded, that is how a server is used
/// against the network it sits in: a link to <c>http://169.254.169.254/</c> reads cloud credentials, one to
/// <c>http://localhost:5252/</c> reaches the API from the inside, one to a router's address probes it. So an address is
/// refused unless it is plain web (http or https, ports 80 and 443, no user name in it) and every IP its host resolves
/// to is a public one.</para>
/// <para>Checking the name is not enough on its own, because DNS can answer differently a moment later. The fetcher
/// therefore connects only to an address this class has just approved (see <see cref="OpenGraphFetcher"/>).</para>
/// </remarks>
public static class OutboundUrlGuard
{
    /// <summary>Why the address may not be fetched, or null when it may.</summary>
    public static string? WhyNot(Uri url)
    {
        if (!url.IsAbsoluteUri) return "not an absolute address";
        if (url.Scheme is not ("http" or "https")) return "not a web address";
        if (!string.IsNullOrEmpty(url.UserInfo)) return "the address carries a user name";
        if (!url.IsDefaultPort && url.Port is not (80 or 443)) return "the address names an unusual port";
        if (url.HostNameType is UriHostNameType.Dns && !url.Host.Contains('.')) return "the host is not a public name";
        if (url.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
            && System.Net.IPAddress.TryParse(url.Host.Trim('[', ']'), out var literal) && !IsPublic(literal))
            return "the address is not on the public internet";
        if (url.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || url.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || url.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return "the host is a local name";
        return null;
    }

    /// <summary>The public addresses <paramref name="host"/> resolves to; empty when any of them is not public.</summary>
    /// <remarks>All-or-nothing: a name that resolves to one public and one private address is refused whole.</remarks>
    public static async Task<IReadOnlyList<IPAddress>> PublicAddressesAsync(string host, CancellationToken ct)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
            addresses = [literal];
        else
        {
            try { addresses = await Dns.GetHostAddressesAsync(host, ct); }
            catch (SocketException) { return []; }
        }

        return addresses.Length > 0 && addresses.All(IsPublic) ? addresses : [];
    }

    /// <summary>True for an address on the public internet.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0                                   // "this network"
                  || b[0] == 10                                  // private
                  || b[0] == 127                                 // loopback
                  || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)  // carrier-grade NAT
                  || (b[0] == 169 && b[1] == 254)                // link-local, cloud metadata
                  || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)   // private
                  || (b[0] == 192 && b[1] == 0 && b[2] == 0)     // IETF protocol assignments
                  || (b[0] == 192 && b[1] == 0 && b[2] == 2)     // documentation
                  || (b[0] == 192 && b[1] == 168)                // private
                  || (b[0] == 198 && (b[1] == 18 || b[1] == 19)) // benchmarking
                  || (b[0] == 198 && b[1] == 51 && b[2] == 100)  // documentation
                  || (b[0] == 203 && b[1] == 0 && b[2] == 113)   // documentation
                  || b[0] >= 224);                               // multicast, reserved, broadcast
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IPv6Loopback.Equals(address) || IPAddress.IPv6None.Equals(address)) return false;
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return false;
            var b = address.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return false;                        // unique local fc00::/7
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return false; // documentation 2001:db8::/32
            if (address.IsIPv6Teredo || (b[0] == 0x20 && b[1] == 0x02)) return false;     // tunnels that can wrap private v4
            return true;
        }

        return false;
    }
}
