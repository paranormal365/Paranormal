using System.Runtime.CompilerServices;
using Microsoft.JSInterop;

namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// Starts the page's one countdown ticker (wwwroot/js/eventCountdown.js), however many countdowns
/// ask in the same render.
/// </summary>
/// <remarks>
/// <para>A list of fifty events draws fifty <see cref="EventCountdown"/>s, and each one finishing
/// its first render asks for the ticker. Fifty imports and fifty "start" calls over the circuit
/// would be the per-row cost the design exists to avoid, so the asks are folded together: one
/// circuit (one <see cref="IJSRuntime"/>) imports the module once, and while a start is on its way
/// every other ask waits for that one rather than sending its own.</para>
///
/// <para>Kept beside the runtime rather than registered as a service, so every host and every
/// component test that renders a countdown works without remembering to add it.</para>
/// </remarks>
internal sealed class EventCountdownTicker
{
    /// <summary>Root-relative, as every asset path is (the Safari base-href bug, 08/11/2026).</summary>
    public const string ModulePath = "/_content/Ben.Web.Website.Library/js/eventCountdown.js";

    private static readonly ConditionalWeakTable<IJSRuntime, EventCountdownTicker> PerCircuit = new();

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private Task? _starting;

    private EventCountdownTicker(IJSRuntime js) => _js = js;

    public static Task EnsureRunningAsync(IJSRuntime js)
        => PerCircuit.GetValue(js, r => new EventCountdownTicker(r)).StartAsync();

    private Task StartAsync()
        => _starting is { IsCompleted: false } inFlight ? inFlight : _starting = RunAsync();

    private async Task RunAsync()
    {
        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            await _module.InvokeVoidAsync("start");
        }
        // The circuit went, or the page did: a countdown nobody is looking at needs no ticker.
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException) { }   // prerendering: there is no browser yet
    }
}
