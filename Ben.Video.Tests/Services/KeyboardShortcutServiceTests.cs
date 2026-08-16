using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

public sealed class KeyboardShortcutServiceTests
{
    // ── Lifecycle / registration contract ────────────────────────────────────

    [Fact]
    public void CanCreate_WithIJSRuntime()
    {
        // KeyboardShortcutService requires IJSRuntime injected via ctor.
        // We verify the type is instantiable from the constructor (no DI needed).
        var svc = new KeyboardShortcutService(new NoOpJSRuntime());
        Assert.NotNull(svc);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotThrow_WhenNeverRegistered()
    {
        var svc = new KeyboardShortcutService(new NoOpJSRuntime());
        // Should not throw — no JS module was ever imported
        await svc.DisposeAsync();
    }

    [Fact]
    public async Task UnregisterAsync_DoesNotThrow_WhenNeverRegistered()
    {
        var svc = new KeyboardShortcutService(new NoOpJSRuntime());
        await svc.UnregisterAsync();
    }

    [Fact]
    public async Task OnKeyDown_WhenNoTargetRegistered_DoesNotThrow()
    {
        var svc = new KeyboardShortcutService(new NoOpJSRuntime());
        // Simulate JS calling back before RegisterAsync (edge case)
        await svc.OnKeyDown(" ", false, false, false);
    }

    [Fact]
    public async Task OnKeyDown_ForwardsToRegisteredTarget()
    {
        var svc    = new KeyboardShortcutService(new NoOpJSRuntime());
        var target = new RecordingCommandTarget();

        // Manually inject a target (bypass JS module via internal state)
        await svc.RegisterAsync(target, skipJs: true);

        await svc.OnKeyDown("s", false, false, false);

        Assert.Single(target.Received);
        Assert.Equal("s", target.Received[0].Key);
    }

    [Fact]
    public async Task OnKeyDown_MultipleKeys_AllForwarded()
    {
        var svc    = new KeyboardShortcutService(new NoOpJSRuntime());
        var target = new RecordingCommandTarget();
        await svc.RegisterAsync(target, skipJs: true);

        await svc.OnKeyDown("Delete", false, false, false);
        await svc.OnKeyDown(" ",      false, false, false);
        await svc.OnKeyDown("z",      true,  false, false);

        Assert.Equal(3, target.Received.Count);
        Assert.Equal("Delete", target.Received[0].Key);
        Assert.Equal(" ",      target.Received[1].Key);
        Assert.Equal("z",      target.Received[2].Key);
        Assert.True(target.Received[2].Ctrl);
    }

    [Fact]
    public async Task UnregisterAsync_ClearsTarget()
    {
        var svc    = new KeyboardShortcutService(new NoOpJSRuntime());
        var target = new RecordingCommandTarget();
        await svc.RegisterAsync(target, skipJs: true);

        await svc.UnregisterAsync();

        // After unregister, further key events should not reach the old target
        await svc.OnKeyDown("?", false, false, false);
        Assert.Empty(target.Received);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class NoOpJSRuntime : Microsoft.JSInterop.IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,
            System.Threading.CancellationToken cancellationToken, object?[]? args)
            => ValueTask.FromResult(default(TValue)!);
    }

    private sealed record KeyEvent(string Key, bool Ctrl, bool Shift, bool Alt);

    private sealed class RecordingCommandTarget : IKeyboardCommandTarget
    {
        public List<KeyEvent> Received { get; } = [];

        public Task OnEditorKeyDown(string key, bool ctrl, bool shift, bool alt)
        {
            Received.Add(new KeyEvent(key, ctrl, shift, alt));
            return Task.CompletedTask;
        }
    }
}
