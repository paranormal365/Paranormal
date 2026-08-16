using Ben.Data.Common.Enums;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Web.Library.Services;
using Microsoft.Extensions.Options;

namespace Ben.Web.WebApp.Services.WebApi;

/// <summary>
/// Implements IBenAdminClient for Ben.Web.Library components by composing
/// IWebApiClient (HTTP) and IWebApiAuthService (token management).
/// </summary>
public sealed partial class BenAdminClientAdapter : IBenAdminClient
{
    private readonly IWebApiClient _api;
    private readonly IWebApiAuthService _auth;
    private readonly string _webApiBaseUrl;

    public BenAdminClientAdapter(IWebApiClient api, IWebApiAuthService auth, IOptions<WebApiOptions> options)
    {
        _api           = api;
        _auth          = auth;
        _webApiBaseUrl = options.Value.BaseUrl.TrimEnd('/');
    }

    // Every method now lives in a BenAdminClientAdapter.<Domain>.cs partial alongside this file.
}
