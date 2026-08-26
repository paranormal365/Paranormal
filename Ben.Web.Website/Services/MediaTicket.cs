using Microsoft.AspNetCore.DataProtection;

namespace Ben.Web.Website.Services;

/// <summary>
/// Lets the browser fetch a file it is entitled to, without the server holding the file.
/// </summary>
/// <remarks>
/// <para><b>The problem this solves.</b> Previews used to be fetched by the SERVER and handed to
/// the page as base64 data URLs — a copy of every file in the website's memory, per card, per
/// render. A media library of recordings took the process to sixteen gigabytes and the host was
/// killed. The obvious fix, pointing an <c>&lt;img&gt;</c> straight at the API, works only for
/// PUBLIC files: the browser has no bearer token, the API answers 401, and the page then reports
/// a perfectly healthy file as broken.</para>
///
/// <para><b>Why a ticket.</b> The access token lives in localStorage and is loaded into the
/// Blazor circuit — a plain image request carries none of it. So the circuit, which does know who
/// the viewer is, mints a ticket the browser can put in a URL. The website's media endpoint
/// unprotects it, calls the API with that viewer's own token, and streams the reply straight
/// through.</para>
///
/// <para><b>What keeps it safe.</b> The payload is encrypted with ASP.NET Data Protection, so the
/// token is never readable in a URL, a log or a referrer. It is bound to ONE file id, so a ticket
/// lifted from one image cannot fetch another. It expires. And the API is still the authority:
/// the ticket only carries the caller's identity, it never asserts what they may see.</para>
/// </remarks>
public sealed class MediaTicketService
{
    private readonly IDataProtector _protector;

    /// <summary>
    /// Long enough to view a page and scroll it; short enough that a leaked URL is worthless.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public MediaTicketService(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("Ben.Web.Website.MediaTicket.v1");

    /// <summary>Mints a ticket for one file, for the caller holding this token.</summary>
    public string Protect(Guid fileId, string accessToken)
    {
        // The expiry is rounded DOWN to the hour so the same viewer gets the same URL for the
        // same file across renders — otherwise every re-render would produce a new URL and the
        // browser could never cache the image, which is half the point of getting the bytes out
        // of the server.
        var slot = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600 * 3600;
        var payload = $"{fileId:N}|{slot}|{accessToken}";
        return _protector.Protect(payload);
    }

    /// <summary>
    /// Reads a ticket back, returning the access token when it is valid for this file.
    /// </summary>
    /// <returns>Null when the ticket is unreadable, expired, or was minted for another file.</returns>
    public string? Unprotect(Guid fileId, string ticket)
    {
        string payload;
        try { payload = _protector.Unprotect(ticket); }
        catch { return null; }   // tampered, or from a previous key ring

        var parts = payload.Split('|', 3);
        if (parts.Length != 3) return null;
        if (!Guid.TryParseExact(parts[0], "N", out var ticketFileId) || ticketFileId != fileId) return null;
        if (!long.TryParse(parts[1], out var slot)) return null;

        var issued = DateTimeOffset.FromUnixTimeSeconds(slot);
        // The slot is rounded down, so a ticket is good for its hour plus the lifetime.
        if (DateTimeOffset.UtcNow > issued + Lifetime + TimeSpan.FromHours(1)) return null;

        return string.IsNullOrWhiteSpace(parts[2]) ? null : parts[2];
    }
}
