using System.Text.RegularExpressions;
using Ben.Video.Core.SidecarContracts;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #70 phase 173 — guards the two things that make the sidecar reachable at all, neither of
/// which any other test can see.
///
/// <para><b>1. Requests must originate in the browser.</b> The sidecar listens on the <i>user's</i>
/// loopback interface. A C# <c>HttpClient</c> call resolves 127.0.0.1 wherever the Blazor code is
/// running — the browser under WebAssembly, but the <i>server</i> under Blazor Server — and sends
/// no <c>Origin</c> header, which <c>SecurityMiddleware</c> 403s on every endpoint except bare
/// <c>/v1/health</c>. The failure is nastier than a plain outage: health succeeds, so the toolbar
/// chip reports a sidecar was found, and only pairing fails, reading as a bad pairing code. A test
/// is the only way to keep a future call site from quietly reintroducing it, because in the
/// Playground (WASM) an HttpClient call still works.</para>
///
/// <para><b>2. The header name is duplicated across the interop boundary.</b> It has to be — JS
/// can't read a C# const — so this pins the literal instead, the same way
/// <c>ConcatCopyArgBuilderTests</c> pins the JS argv it can't share.</para>
/// </summary>
public sealed class SidecarTransportContractTests
{
    /// <summary>Every service that talks to the sidecar. A new one belongs in this list.</summary>
    private static readonly string[] SidecarServiceFiles =
    [
        "SidecarTransport.cs",
        "NativeSidecarService.cs",
        "NativeSidecarBackend.cs",
        "NativeClipEncoder.cs",
        "SidecarSegmentClient.cs",
        "SidecarMediaClient.cs",
        "SidecarMediaProbe.cs",
        "SidecarSourceUploader.cs",
        "SidecarPreviewAssembler.cs",
        "SidecarExportAssembler.cs",
    ];

    [Fact]
    public void NoSidecarServiceUsesHttpClient()
    {
        var offenders = new List<string>();

        foreach (var file in SidecarServiceFiles)
        {
            var source = File.ReadAllText(Path.Combine(EditorDir(), "Services", file));

            // The doc comments in SidecarTransport and ServiceCollectionExtensions explain *why*
            // HttpClient is wrong here, so match code use rather than any mention of the word.
            if (Regex.IsMatch(source, @"\bIHttpClientFactory\b|\bnew HttpRequestMessage\b|\bCreateClient\("))
                offenders.Add(file);
        }

        Assert.True(
            offenders.Count == 0,
            "These sidecar services issue requests from C# instead of through SidecarTransport, " +
            "which breaks the sidecar under Blazor Server (wrong machine, no Origin header): " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void JsTokenHeaderMatchesTheProtocolConstant()
    {
        var js = File.ReadAllText(JsPath());

        Assert.Contains($"const TOKEN_HEADER = '{SidecarProtocol.TokenHeaderName}';", js);

        // Nothing may hardcode the header anywhere else — that is how the two copies would drift
        // without this test noticing.
        var literalCount = Regex.Matches(js, Regex.Escape(SidecarProtocol.TokenHeaderName)).Count;
        Assert.Equal(1, literalCount);
    }

    [Fact]
    public void JsModuleExportsEveryFunctionTheTransportInvokes()
    {
        var js = File.ReadAllText(JsPath());

        foreach (var name in new[] { "sendRequest", "sendRequestForBytes", "abortRequest" })
            Assert.Matches($@"export (async )?function {name}\(", js);
    }

    [Fact]
    public void SendRequestOnlySetsContentTypeWhenThereIsABody()
    {
        var js = File.ReadAllText(JsPath());

        // A GET/DELETE carrying Content-Type would add nothing but a header the sidecar's
        // preflight allowlist has to keep permitting; more importantly, sending a body-less POST
        // as though it had JSON would misrepresent the request to the model binder.
        Assert.Contains("if (bodyJson !== null && bodyJson !== undefined) headers['Content-Type'] = 'application/json';", js);
    }

    [Fact]
    public void AbortableRequestsAreTrackedAndAlwaysUntracked()
    {
        var js = File.ReadAllText(JsPath());

        // The finally is the load-bearing half: a Map entry left behind for every completed
        // request is an unbounded leak in a session that polls a job every 250ms.
        Assert.Contains("inFlight.set(requestId, controller)", js);
        Assert.Contains("if (requestId) inFlight.delete(requestId);", js);
        Assert.Matches(@"finally \{[^}]*inFlight\.delete\(requestId\)", js.Replace("\n", " "));
    }

    [Fact]
    public void ByteFetchReturnsTheArrayBareSoBlazorUsesItsBinaryTransfer()
    {
        var js = File.ReadAllText(JsPath());

        // Returning the Uint8Array nested in an object would send it through JSON interop and
        // arrive in C# as {"0":31,"1":139,...} — many times the size of the payload itself.
        Assert.Contains("if (result instanceof Uint8Array) return result;", js);
    }

    private static string EditorDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Video.Editor")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Ben.Video.Editor");
    }

    private static string JsPath()
    {
        var path = Path.Combine(EditorDir(), "wwwroot", "js", "sidecarInterop.js");
        Assert.True(File.Exists(path), $"sidecarInterop.js not found at {path}");
        return path;
    }
}
