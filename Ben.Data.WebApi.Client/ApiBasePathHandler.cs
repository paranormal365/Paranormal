namespace Ben.Web.Services.WebApi;

/// <summary>
/// Restores the API's base path on requests written with a leading slash.
/// </summary>
/// <remarks>
/// <para><see cref="HttpClient.BaseAddress"/> does not do what almost everyone assumes. It is
/// resolved by the ordinary URI rules, so a request path beginning with <c>/</c> is
/// <em>root-relative</em> and replaces the base address's path outright. With a base address of
/// <c>https://ishaunted.com/webapi</c>, a call to <c>"/api/me"</c> goes to
/// <c>https://ishaunted.com/api/me</c> — the <c>/webapi</c> silently disappears.</para>
///
/// <para>This never showed up in development because the API is served from the root there, where
/// discarding an empty base path changes nothing. It appeared the moment the API was mounted as an
/// IIS application under <c>/webapi</c>, and it appeared as a 404 on every single call: sign-in
/// bridging, media, cases, everything. The Entra symptom was the one that surfaced first — the
/// round trip to Microsoft succeeded, <c>/api/me</c> 404ed, and
/// <c>MainLayout.TryBridgeEntraAuthAsync</c> read the null result as "no account", cleared the
/// half-built session and left the user looking signed out with nothing logged anywhere.</para>
///
/// <para>Fixing the ~495 call sites to use relative paths would work and would be a large,
/// error-prone change that the next leading slash silently reverts. Fixing it here is one place,
/// and it is inert wherever the API has no base path — so development, where
/// <c>WebApi:BaseUrl</c> is an origin with no path, behaves exactly as before.</para>
/// </remarks>
public sealed class ApiBasePathHandler : DelegatingHandler
{
    private readonly string _basePath;

    /// <param name="baseUrl">
    /// The configured <c>WebApi:BaseUrl</c>. Only its path matters; an origin with no path
    /// disables this handler entirely.
    /// </param>
    public ApiBasePathHandler(string? baseUrl)
    {
        _basePath = ExtractBasePath(baseUrl);
    }

    /// <summary>The path portion of <paramref name="baseUrl"/>, trimmed, or "" when there is none.</summary>
    /// <remarks>Public so the behaviour can be asserted directly rather than through an HttpClient.</remarks>
    public static string ExtractBasePath(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return string.Empty;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return string.Empty;

        var path = uri.AbsolutePath.TrimEnd('/');
        // "/" alone is the root and means there is nothing to restore.
        return path == "/" ? string.Empty : path;
    }

    /// <summary>Prefixes <paramref name="basePath"/> onto <paramref name="absolutePath"/> unless it is already there.</summary>
    public static string ApplyBasePath(string basePath, string absolutePath)
    {
        if (basePath.Length == 0) return absolutePath;

        // Already correct - a caller that wrote the full path, or a retried request.
        if (absolutePath.Equals(basePath, StringComparison.OrdinalIgnoreCase)
            || absolutePath.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            return absolutePath;
        }

        return basePath + absolutePath;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri;
        if (_basePath.Length > 0 && uri is not null && uri.IsAbsoluteUri)
        {
            var rewritten = ApplyBasePath(_basePath, uri.AbsolutePath);
            if (!ReferenceEquals(rewritten, uri.AbsolutePath))
            {
                // Query and fragment are carried across untouched; only the path changes.
                request.RequestUri = new UriBuilder(uri) { Path = rewritten }.Uri;
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
