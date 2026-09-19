using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Both apps honour proxy headers, and do it before anything reads the scheme.
/// </summary>
/// <remarks>
/// <para><b>The failure is a redirect loop with nothing visibly wrong.</b> Behind a reverse proxy —
/// a Cloudflare Tunnel today, Azure App Service later — TLS terminates at the proxy and the request
/// reaches the app over plain HTTP. Without <c>UseForwardedHeaders</c>, ASP.NET Core sees
/// <c>IsHttps == false</c>, <c>UseHttpsRedirection</c> answers <c>307 → https://</c>, the proxy
/// fetches that, and round it goes. IIS looks healthy, the app looks healthy, and the site is
/// unreachable.</para>
///
/// <para><b>Order is the whole thing.</b> <c>UseForwardedHeaders</c> rewrites the scheme, so it has
/// to run before any middleware that inspects it. Registered after <c>UseHttpsRedirection</c> it
/// compiles, starts, serves every local request correctly, and still loops behind a proxy — which
/// is why this asserts position and not merely presence.</para>
///
/// <para><b>What is deliberately not asserted:</b> KnownProxies/KnownNetworks. The default trusts
/// forwarded headers only from loopback, and that is correct here because cloudflared runs on the
/// same host. Widening it would let any caller claim to have arrived over HTTPS from any address,
/// so the absence of that configuration is the secure state, not an omission.</para>
/// </remarks>
public sealed class ForwardedHeadersTests
{
    /// <summary>
    /// Source with comments removed.
    /// </summary>
    /// <remarks>
    /// Both Program.cs files explain in a comment above the call that it must precede
    /// <c>UseHttpsRedirection</c> — so a naive IndexOf finds the redirect in the prose first and
    /// reports the correct order as wrong. The guard flagged its own documentation on its first
    /// run, exactly as the stylesheet guard had days earlier. Comments are not code.
    /// </remarks>
    private static string CodeOnly(string path)
    {
        var text = File.ReadAllText(path);
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\n]*", "", RegexOptions.Multiline);
        return text;
    }

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    public static TheoryData<string, string> Apps => new()
    {
        { "the website", Path.Combine("Ben.Web.Website", "Program.cs") },
        { "the API",     Path.Combine("Ben.Data.WebApi", "Program.cs") },
    };

    [Theory]
    [MemberData(nameof(Apps))]
    public void Forwarded_headers_are_honoured_before_https_redirection(string label, string relativePath)
    {
        var source = CodeOnly(Path.Combine(RepoRoot().FullName, relativePath));

        var forwarded = source.IndexOf("UseForwardedHeaders", StringComparison.Ordinal);
        Assert.True(forwarded >= 0,
            $"{label} does not call UseForwardedHeaders. Behind a reverse proxy it will see every "
            + "request as plain HTTP and redirect to https:// forever.");

        var redirect = source.IndexOf("UseHttpsRedirection", StringComparison.Ordinal);
        if (redirect < 0) return;   // no redirect to loop against

        Assert.True(forwarded < redirect,
            $"{label} calls UseForwardedHeaders AFTER UseHttpsRedirection. The scheme is rewritten "
            + "too late, so the redirect still fires and the request loops behind a proxy — while "
            + "working perfectly on localhost.");
    }

    /// <summary>The proto header specifically — the one the redirect loop turns on.</summary>
    [Theory]
    [MemberData(nameof(Apps))]
    public void The_forwarded_proto_header_is_among_those_honoured(string label, string relativePath)
    {
        var source = CodeOnly(Path.Combine(RepoRoot().FullName, relativePath));

        // Options block only: a mention in a comment is not configuration.
        var block = Regex.Match(source, @"UseForwardedHeaders\s*\(.*?\}\s*\)", RegexOptions.Singleline);
        Assert.True(block.Success, $"{label} calls UseForwardedHeaders without an options block.");

        Assert.Contains("XForwardedProto", block.Value, StringComparison.Ordinal);
    }
}
