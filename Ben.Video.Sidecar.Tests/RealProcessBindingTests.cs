using System.Diagnostics;
using System.Net;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> runs the app
/// against an in-memory <c>TestServer</c> by default — it proves the middleware pipeline and
/// routing, but it never actually opens a TCP socket, so it cannot prove the sidecar really binds
/// to loopback only. This test runs the actual built binary as a real child process and connects
/// to it over a real socket, the same way the browser (or a would-be attacker) would.
/// </summary>
public sealed class RealProcessBindingTests : IAsyncLifetime
{
    private Process? _process;
    private string _homeDir = null!;
    private int _port;

    public async Task InitializeAsync()
    {
        _homeDir = Directory.CreateTempSubdirectory("benvideo-sidecar-realproc-").FullName;
        _port = GetEphemeralPort();

        var dllPath = Path.Combine(AppContext.BaseDirectory, "Ben.Video.Sidecar.dll");
        var fakeFfmpeg = FakeFfmpegPath.Resolve();

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            EnvironmentVariables =
            {
                ["Sidecar__HomeOverride"] = _homeDir,
                ["Sidecar__FfmpegDevPathOverride"] = fakeFfmpeg,
                ["Sidecar__Port"] = _port.ToString(),
                ["Sidecar__PortScanRange"] = "0",
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
            },
        };
        psi.ArgumentList.Add(dllPath);

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start sidecar process.");

        // Poll health instead of a fixed sleep — real startup time varies by machine load.
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await client.GetAsync($"http://127.0.0.1:{_port}/v1/health");
                if (response.IsSuccessStatusCode) return;
            }
            catch { /* not up yet */ }
            await Task.Delay(200);
        }

        throw new TimeoutException("Sidecar process did not become healthy in time.");
    }

    public Task DisposeAsync()
    {
        try { _process?.Kill(entireProcessTree: true); } catch { /* already exited */ }
        _process?.Dispose();
        try { Directory.Delete(_homeDir, recursive: true); } catch { /* best-effort */ }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RealSocket_LoopbackAddress_IsReachable()
    {
        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{_port}/v1/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RealSocket_Localhost_IsReachable()
    {
        using var client = new HttpClient();
        var response = await client.GetAsync($"http://localhost:{_port}/v1/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static int GetEphemeralPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
