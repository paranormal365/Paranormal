using Ben.Video.Core.SidecarContracts;
using Ben.Video.Editor.Models;
using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

public enum NativeSidecarState
{
    /// <summary><see cref="Models.VideoEditorOptions.NativeSidecar"/> is off — nothing probed, nothing shown.</summary>
    Disabled,
    /// <summary>Probed every port in range and found nothing listening.</summary>
    Disconnected,
    /// <summary>A sidecar answered health, but no valid stored pairing token exists yet.</summary>
    FoundUnpaired,
    /// <summary>A sidecar answered health and a valid token is on file — fully usable.</summary>
    Paired,
    /// <summary>A sidecar answered health, but the previously-stored token was rejected (e.g. the
    /// sidecar was restarted with <c>--reset-token</c>) — needs re-pairing.</summary>
    TokenRejected,
}

/// <summary>
/// Browser-side counterpart to the sidecar's <c>/v1/health</c> and <c>/v1/status</c> endpoints —
/// item #38 phase E. Owns the connection/pairing state machine; does NOT yet route any real
/// render/export work (that's <c>NativeSidecarBackend</c>, phase F) — this phase only proves the
/// sidecar can be found and paired with, safely.
/// </summary>
public sealed class NativeSidecarService(
    SidecarTransport transport,
    IJSRuntime js,
    ISidecarPairingReporter? pairingReporter = null) : IAsyncDisposable
{
    private const string ModulePath = "js/sidecarInterop.js";
    private IJSObjectReference? _module;

    /// <summary>Per-port budget for the discovery scan. Short on purpose: with nothing installed
    /// this runs to completion on every editor load, so the whole scan has to stay well inside the
    /// time it takes a user to notice.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(1.5);

    /// <summary>Budget for the two authenticated follow-ups once a port has answered. Longer than
    /// <see cref="ProbeTimeout"/> because at this point something is definitely listening.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    /// <summary>How long to keep looking for a sidecar after asking Windows to start one.</summary>
    /// <remarks>
    /// Patient on purpose. The browser may show the person a prompt first and wait on their answer,
    /// and the sidecar itself re-hashes two ~145 MB binaries at startup before it serves anything.
    /// </remarks>
    private static readonly TimeSpan StartupWait = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan StartupPollEvery = TimeSpan.FromSeconds(1);

    public NativeSidecarState State { get; private set; } = NativeSidecarState.Disabled;
    public HealthInfo? Info { get; private set; }
    public int? DiscoveredPort { get; private set; }
    public string? LastError { get; private set; }

    public bool IsConnected => State == NativeSidecarState.Paired;

    /// <summary>What the currently-connected sidecar can do — item #70 phase 158. Refreshed on
    /// every successful pair/probe, reset to <see cref="SidecarCapabilitySet.None"/> the moment
    /// the connection is lost or forgotten, so a stale capability can never keep a later phase
    /// routing work at a sidecar that isn't there.</summary>
    public SidecarCapabilitySet Capabilities { get; private set; } = SidecarCapabilitySet.None;

    /// <summary>Convenience gate for every phase-159+ feature check. False whenever not paired,
    /// regardless of what the last-seen capability list said.</summary>
    public bool HasCapability(string capability) => IsConnected && Capabilities.Has(capability);

    /// <summary>Identity of the connected sidecar process (phase 160 uses it to invalidate
    /// retained-segment ids across a sidecar restart). Null for a legacy sidecar.</summary>
    public Guid? InstanceId => Capabilities.InstanceId;

    public event Action? OnChanged;

    /// <summary>
    /// Scans <see cref="SidecarProtocol.DefaultPort"/>.. for a responding sidecar, then — if one
    /// answers and a token is already stored from a previous session — verifies that token still
    /// works. Safe to call repeatedly (e.g. a "retry connection" button); each call re-evaluates
    /// from scratch rather than trusting cached state, since the sidecar may have been
    /// restarted/reset/closed between calls.
    /// </summary>
    /// <summary>
    /// Tells a connected sidecar that the editor is still open, so it does not stop underneath one.
    /// </summary>
    /// <remarks>
    /// <para>Ben, 2026-09-19: the sidecar should run only while the video editor is being used. It
    /// decides that by how long it has been since anything asked it for anything — which without
    /// this would mean "since the last render", not "since the editor was closed". An hour of local
    /// editing with no export looks exactly like a closed editor from the sidecar's side, and it
    /// would stop; the next export would then have to wake it and re-render every retained segment,
    /// because a new process means a new InstanceId and the remote-segment index is dropped.</para>
    ///
    /// <para>A plain health GET, because any request at all is what the sidecar counts. It is
    /// deliberately quiet about failure: a sidecar that has gone away is not news here — the next
    /// real call reports it properly, and this must never put an error in front of somebody who is
    /// editing.</para>
    /// </remarks>
    public async Task HeartbeatAsync(CancellationToken ct = default)
    {
        if (DiscoveredPort is not { } port) return;

        try
        {
            await transport.SendAsync(
                "GET", $"http://127.0.0.1:{port}/v1/health", token: string.Empty,
                timeout: ProbeTimeout, ct: ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Gone, asleep, or busy. Not this method's business.
        }
    }

    public async Task ProbeAsync(CancellationToken ct = default)
    {
        for (var port = SidecarProtocol.DefaultPort; port <= SidecarProtocol.DefaultPort + SidecarProtocol.DefaultPortScanRange; port++)
        {
            HealthInfo? info;
            try
            {
                // LenientResponses (item #70 phase 158), NOT Default: a newer sidecar that adds a
                // field to HealthInfo must not make this parse throw, because the catch below
                // treats any throw as "nothing on this port" — which would silently lose an
                // otherwise-working connection instead of degrading gracefully.
                var response = await transport.SendAsync(
                    "GET", $"http://127.0.0.1:{port}/v1/health", token: string.Empty,
                    timeout: ProbeTimeout, ct: ct);
                info = response.IsSuccess ? response.ReadJson<HealthInfo>() : null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                continue; // nothing listening on this port — try the next one
            }

            if (info is null) continue;

            DiscoveredPort = port;
            Info = info;
            LastError = null;

            var storedToken = await GetStoredTokenAsync();
            if (string.IsNullOrEmpty(storedToken))
            {
                Capabilities = SidecarCapabilitySet.None;
                SetState(NativeSidecarState.FoundUnpaired);
                return;
            }

            var stillValid = await CheckTokenAsync(port, storedToken, ct);
            // Re-fetched on every probe rather than cached across calls: the process listening on
            // this port may have been restarted (new InstanceId, possibly different capabilities)
            // or swapped for an older build since last time.
            Capabilities = stillValid
                ? await FetchCapabilitiesAsync(port, storedToken, ct)
                : SidecarCapabilitySet.None;
            SetState(stillValid ? NativeSidecarState.Paired : NativeSidecarState.TokenRejected);
            return;
        }

        DiscoveredPort = null;
        Info = null;
        Capabilities = SidecarCapabilitySet.None;
        SetState(NativeSidecarState.Disconnected);
    }

    /// <summary>
    /// Verifies a freshly-pasted pairing code against the discovered sidecar and, on success,
    /// stores it for future sessions. Returns false (and sets <see cref="LastError"/>) on a wrong
    /// code or if no sidecar was ever discovered — callers should have called
    /// <see cref="ProbeAsync"/> first.
    /// </summary>
    public async Task<bool> PairAsync(string code, CancellationToken ct = default)
    {
        if (DiscoveredPort is not { } port)
        {
            LastError = "No sidecar detected — click Retry first.";
            return false;
        }

        code = code.Trim();
        if (string.IsNullOrEmpty(code))
        {
            LastError = "Enter the 6-digit code from the sidecar's pairing page.";
            return false;
        }

        // Pairing v2: a 6-digit code from the /pair page is exchanged for the long token at
        // POST /v1/pair. Pasting the long token itself still works (older installs, power users) —
        // the shapes can't collide, since the token is 43 url-safe-base64 characters.
        string token;
        if (code.Length == 6 && code.All(char.IsAsciiDigit))
        {
            var exchanged = _module is null ? null : await _module.InvokeAsync<string?>(
                "exchangePairCode", ct, $"http://127.0.0.1:{port}/v1/pair", code);
            if (exchanged is null)
            {
                LastError = "That code was rejected — it may have expired or already been used. " +
                            "Reload the sidecar's pairing page for a fresh one.";
                SetState(NativeSidecarState.FoundUnpaired);
                return false;
            }
            token = exchanged;
        }
        else
        {
            if (!await CheckTokenAsync(port, code, ct))
            {
                LastError = "That pairing token was rejected.";
                SetState(NativeSidecarState.FoundUnpaired);
                return false;
            }
            token = code;
        }

        await SetStoredTokenAsync(token);
        LastError = null;
        Capabilities = await FetchCapabilitiesAsync(port, token, ct);
        SetState(NativeSidecarState.Paired);

        // Tell the host, if it wants to know. Deliberately after the state is already Paired and
        // deliberately not awaited into the result: the pairing has succeeded either way, and a
        // reporting failure must not turn a working pairing into an error the user sees.
        if (pairingReporter is not null && Info?.InstallId is { } installId)
        {
            try
            {
                await pairingReporter.ReportPairedAsync(installId, Info.AppVersion, Info.Platform, ct);
            }
            catch
            {
                // Contractually the reporter shouldn't throw; if one does, it is still not the
                // user's problem.
            }
        }

        return true;
    }

    /// <summary>Returns the (port, token) pair a caller needs to make its own authenticated
    /// request directly against the sidecar — item #38 phase 123, used by
    /// <see cref="NativeSidecarBackend"/>. Returns <c>null</c> unless <see cref="State"/> is
    /// currently <see cref="NativeSidecarState.Paired"/>; re-reads the stored token each call
    /// rather than caching it, since <see cref="ForgetPairingAsync"/> can clear it at any time.</summary>
    public async Task<(int Port, string Token)?> GetConnectionAsync()
    {
        if (State != NativeSidecarState.Paired || DiscoveredPort is not { } port) return null;
        var token = await GetStoredTokenAsync();
        return string.IsNullOrEmpty(token) ? null : (port, token);
    }

    /// <summary>
    /// Called by <see cref="NativeSidecarBackend"/> when a request fails at the transport level
    /// (connection refused/reset — the process is gone, not just one bad response) — item #38
    /// phase 123. Flips <see cref="State"/> to <see cref="NativeSidecarState.Disconnected"/>
    /// immediately rather than waiting for the next explicit <see cref="ProbeAsync"/>, which
    /// nothing calls periodically. Without this, a killed sidecar would leave <see cref="State"/>
    /// stuck at <see cref="NativeSidecarState.Paired"/> forever, and <c>FallbackRenderBackend</c>'s
    /// <c>primaryAvailable</c> check (which reads <see cref="IsConnected"/>) would keep routing
    /// every subsequent job at a process that will never answer — the opposite of the "kill
    /// sidecar mid-queue → seamless wasm fallback" behavior the design requires. A later
    /// <see cref="ProbeAsync"/> (the user reopening the panel, or a future periodic heartbeat)
    /// can rediscover the sidecar and restore <see cref="NativeSidecarState.Paired"/>.
    /// </summary>
    public void ReportConnectionLost()
    {
        if (State != NativeSidecarState.Paired) return;
        // Clear capabilities alongside the state: phase 160+ keys remote-resource ids off
        // InstanceId, and a lost connection means none of those ids can be trusted any more.
        Capabilities = SidecarCapabilitySet.None;
        SetState(NativeSidecarState.Disconnected);
    }

    /// <summary>
    /// Whether to offer the on/off switch, asked of the browser once and remembered.
    /// </summary>
    /// <remarks>
    /// Null until something has asked. <see cref="RefreshSwitchAvailabilityAsync"/> settles it.
    /// </remarks>
    public bool? SwitchIsOffered { get; private set; }

    /// <summary>How many jobs the sidecar said were running the last time it refused to stop.</summary>
    public int LastRefusedJobCount { get; private set; }

    /// <summary>
    /// Asks the browser what it is running on, to decide whether the switch can do anything here.
    /// </summary>
    public async Task RefreshSwitchAvailabilityAsync()
    {
        if (SwitchIsOffered is not null) return;

        await EnsureModuleAsync();
        string? platform = null;
        if (_module is not null)
        {
            try { platform = await _module.InvokeAsync<string?>("getPlatform"); }
            catch { /* stays null, which offers no switch - see SidecarSwitch.IsOfferedOn */ }
        }

        SwitchIsOffered = SidecarSwitch.IsOfferedOn(platform);
        OnChanged?.Invoke();
    }

    /// <summary>
    /// Turns the sidecar on: asks Windows to start it, then looks for it.
    /// </summary>
    /// <remarks>
    /// <para>The launch itself tells us nothing - a page cannot learn whether a program started -
    /// so this probes until one answers or the wait runs out. Starting a self-contained .NET app
    /// that hashes two large binaries at startup is not instant, hence a patient deadline rather
    /// than a single look.</para>
    ///
    /// <para>The browser may put a prompt in front of the person first ("Open BenVideo Sidecar?"),
    /// and they may take a moment over it, or refuse. Refusing looks exactly like not starting,
    /// and is reported the same way: nothing answered.</para>
    /// </remarks>
    /// <returns>True when a sidecar answered before the deadline.</returns>
    public async Task<bool> StartAsync(CancellationToken ct = default)
    {
        await EnsureModuleAsync();
        if (_module is null) return false;

        try { await _module.InvokeVoidAsync("launchViaProtocol", ct, SidecarSwitch.LaunchUri); }
        catch { return false; }

        var deadline = DateTimeOffset.UtcNow + StartupWait;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(StartupPollEvery, ct);
            await ProbeAsync(ct);
            if (State is not NativeSidecarState.Disconnected) return true;
        }
        return false;
    }

    /// <summary>
    /// Turns the sidecar off, by asking it to stop.
    /// </summary>
    /// <param name="force">Stop even though work is in progress. The person gives this answer
    /// after being told what it will cost, by pressing the switch a second time.</param>
    /// <remarks>
    /// Only ever asks a sidecar we are paired with: the endpoint is token-gated, so an unpaired
    /// editor has nothing to ask with. A sidecar found but not paired is left alone rather than
    /// being reported as a failure - nothing was connected, and nothing is being turned off.
    /// </remarks>
    public async Task<SidecarStopOutcome> StopAsync(bool force = false, CancellationToken ct = default)
    {
        if (await GetConnectionAsync() is not { } connection)
            return SidecarStopOutcome.NotConnected;
        var (port, token) = connection;

        SidecarTransport.Response response;
        try
        {
            response = await transport.SendAsync(
                "POST", $"http://127.0.0.1:{port}/v1/shutdown?force={(force ? "true" : "false")}",
                token, timeout: RequestTimeout, ct: ct);
        }
        catch
        {
            // Connection refused mid-request is not a failure to stop; it is the process having
            // gone while we were asking. Treat it as stopped, because it is.
            Capabilities = SidecarCapabilitySet.None;
            DiscoveredPort = null;
            SetState(NativeSidecarState.Disconnected);
            return SidecarStopOutcome.Stopped;
        }

        var outcome = SidecarSwitch.ReadStopResponse(response.Status);

        if (outcome == SidecarStopOutcome.WorkInProgress)
        {
            LastRefusedJobCount = ReadActiveJobs(response);
            return outcome;
        }

        if (outcome == SidecarStopOutcome.Stopped)
        {
            // Said before it is proven, and correctly: the sidecar answered that it is stopping,
            // and anything still routed at it in the meantime would be routed at a dying process.
            Capabilities = SidecarCapabilitySet.None;
            DiscoveredPort = null;
            SetState(NativeSidecarState.Disconnected);
        }

        return outcome;
    }

    /// <summary>How many jobs the sidecar said it was running, or one when it would not say.</summary>
    /// <remarks>
    /// Only ever used for wording. A body that cannot be read still means work is in progress -
    /// that was the status code, not the body - so the safe answer is "something", not "none".
    /// </remarks>
    private static int ReadActiveJobs(SidecarTransport.Response response)
    {
        try { return response.ReadJson<ShutdownRefusedBody>()?.ActiveJobs ?? 1; }
        catch { return 1; }
    }

    private sealed record ShutdownRefusedBody(int ActiveJobs);

    /// <summary>Forgets the stored token and returns to the unpaired state, e.g. if the user
    /// wants to pair with a different sidecar instance.</summary>
    public async Task ForgetPairingAsync()
    {
        await EnsureModuleAsync();
        if (_module is not null) await _module.InvokeVoidAsync("clearStoredToken");
        Capabilities = SidecarCapabilitySet.None;
        SetState(DiscoveredPort is not null ? NativeSidecarState.FoundUnpaired : NativeSidecarState.Disconnected);
    }

    /// <summary>
    /// Asks a paired sidecar what it can do. A 404 means a pre-158 sidecar that simply has no such
    /// endpoint — that's <see cref="SidecarCapabilitySet.Legacy"/> (segment rendering, exactly as
    /// before), never an error. Any other failure also degrades to Legacy rather than None: the
    /// token check immediately before this already proved the sidecar is alive and authenticated,
    /// so segment rendering is known-good whatever went wrong here.
    /// </summary>
    private async Task<SidecarCapabilitySet> FetchCapabilitiesAsync(int port, string token, CancellationToken ct)
    {
        try
        {
            var response = await transport.SendAsync(
                "GET", $"http://127.0.0.1:{port}/v1/capabilities", token,
                timeout: RequestTimeout, ct: ct);
            if (!response.IsSuccess) return SidecarCapabilitySet.Legacy;

            return SidecarCapabilitySet.FromResponse(response.ReadJson<CapabilitiesInfo>());
        }
        catch
        {
            return SidecarCapabilitySet.Legacy;
        }
    }

    private async Task<bool> CheckTokenAsync(int port, string token, CancellationToken ct)
    {
        try
        {
            var response = await transport.SendAsync(
                "GET", $"http://127.0.0.1:{port}/v1/status", token,
                timeout: RequestTimeout, ct: ct);
            return response.IsSuccess;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> GetStoredTokenAsync()
    {
        await EnsureModuleAsync();
        if (_module is null) return null;
        try { return await _module.InvokeAsync<string?>("getStoredToken"); }
        catch { return null; }
    }

    private async Task SetStoredTokenAsync(string token)
    {
        await EnsureModuleAsync();
        if (_module is null) return;
        try { await _module.InvokeVoidAsync("setStoredToken", token); } catch { /* best-effort */ }
    }

    private async Task EnsureModuleAsync()
    {
        if (_module is not null) return;
        try { _module = await js.InvokeAsync<IJSObjectReference>("benImportEditorModule", ModulePath); }
        catch { /* stays null — every caller already treats that as "unavailable" */ }
    }

    private void SetState(NativeSidecarState state)
    {
        State = state;
        OnChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); } catch { /* JS runtime may already be torn down */ }
        }
    }
}
