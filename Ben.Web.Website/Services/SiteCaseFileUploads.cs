using Ben.Web.Services;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Website.Services;

/// <inheritdoc cref="ICaseFileUploads"/>
public sealed class SiteCaseFileUploads(
    UploadTicketService tickets,
    IWebApiTokenStore tokenStore) : ICaseFileUploads
{
    public string? SaveUrlFor(Guid organizationId, Guid caseId)
    {
        var accessToken = tokenStore.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken)) return null;

        var ticket = tickets.Protect(caseId, accessToken);
        return $"/uploads/case-file/{organizationId}/{caseId}?t={Uri.EscapeDataString(ticket)}";
    }
}
