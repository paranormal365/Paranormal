using Microsoft.AspNetCore.DataProtection;

namespace Ben.Web.Website.Services;

/// <summary>
/// Lets the browser send upload chunks for a session it started, without the access token ever
/// appearing in the page.
/// </summary>
/// <remarks>
/// <para>The same shape as <see cref="MediaTicketService"/>, for the opposite direction. Chunk
/// PUTs are made by page JavaScript straight to this site's relay endpoints — through the Blazor
/// circuit they would crawl (SignalR streams a file in 32 KB messages) and hold the circuit for
/// the life of a multi-gigabyte transfer. But that JavaScript holds no bearer token, and must
/// not: the token lives in the circuit. So the circuit — which started the session and knows who
/// the uploader is — mints a ticket bound to that one session id, and the relay endpoint
/// unprotects it and speaks to the API with the uploader's own token.</para>
///
/// <para>A separate protector purpose from media tickets, so neither kind of ticket can ever be
/// replayed as the other. The lifetime is generous by design: a 2 GB upload on a home connection
/// is hours, not minutes, and an expiring ticket mid-transfer would discard everything sent so
/// far. The ticket still dies with the session it names — after Complete or Abort the session id
/// resolves to nothing, so a leaked ticket outlives its usefulness, not its risk.</para>
/// </remarks>
public sealed class UploadTicketService
{
    private readonly IDataProtector _protector;

    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    public UploadTicketService(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("Ben.Web.Website.UploadTicket.v1");

    /// <summary>Mints a ticket for one upload session, for the caller holding this token.</summary>
    public string Protect(Guid sessionId, string accessToken)
    {
        var expires = DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds();
        var payload = $"{sessionId:N}|{expires}|{accessToken}";
        return _protector.Protect(payload);
    }

    /// <summary>
    /// Reads a ticket back, returning the access token when it is valid for this session.
    /// </summary>
    /// <returns>Null when the ticket is unreadable, expired, or minted for another session.</returns>
    public string? Unprotect(Guid sessionId, string ticket)
    {
        string payload;
        try { payload = _protector.Unprotect(ticket); }
        catch { return null; }   // tampered, or from a previous key ring

        var parts = payload.Split('|', 3);
        if (parts.Length != 3) return null;
        if (!string.Equals(parts[0], sessionId.ToString("N"), StringComparison.OrdinalIgnoreCase)) return null;
        if (!long.TryParse(parts[1], out var expires)) return null;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expires) return null;

        return parts[2];
    }
}
