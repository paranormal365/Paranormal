namespace Ben.Canvas.Editor.Services;

// Copied from Ben.Web.Website.Library/Kit/BenToastService.cs, because Kit cannot be referenced from
// WebAssembly (Razor SDK plus the Ben.Web.Services FrameworkReference). Keep behaviour identical so
// the editor's messages read like the site's.

/// <summary>
/// A scoped queue of toasts that <c>BcToastHost</c> renders.
/// </summary>
/// <remarks>
/// Auto-dismissal is a timer here rather than Bootstrap's Toast plugin, so nothing outside Blazor
/// removes the element: the host renders the list, and the list is the only truth.
/// </remarks>
public sealed class BcToastService : IDisposable
{
    private readonly List<BcToast> _toasts = new();
    private readonly List<Timer> _timers = new();
    private readonly Lock _gate = new();

    /// <summary>Raised when the queue changes. Fires off the renderer's synchronization context when
    /// a timer expires, so subscribers must marshal with InvokeAsync.</summary>
    public event Action? Changed;

    /// <summary>The toasts currently showing.</summary>
    public IReadOnlyList<BcToast> Current
    {
        get { lock (_gate) return _toasts.ToArray(); }
    }

    /// <summary>Something worked.</summary>
    public void Success(string message, string? title = null) => Show(message, title, BcToastLevel.Success);

    /// <summary>Something worth knowing.</summary>
    public void Info(string message, string? title = null) => Show(message, title, BcToastLevel.Info);

    /// <summary>Something was refused or needs care.</summary>
    public void Warning(string message, string? title = null) => Show(message, title, BcToastLevel.Warning);

    /// <summary>Errors persist until dismissed - an error that vanishes on its own is one the reader
    /// may never have seen.</summary>
    public void Error(string message, string? title = null)
        => Show(message, title, BcToastLevel.Error, autoDismiss: false);

    /// <summary>Shows a toast.</summary>
    public void Show(string message, string? title, BcToastLevel level,
                     bool autoDismiss = true, TimeSpan? duration = null)
    {
        var toast = new BcToast(Guid.NewGuid(), message, title, level);

        lock (_gate) _toasts.Add(toast);
        Changed?.Invoke();

        if (!autoDismiss) return;

        var timer = new Timer(_ => Dismiss(toast.Id), null,
                              duration ?? TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        lock (_gate) _timers.Add(timer);
    }

    /// <summary>Removes one toast.</summary>
    public void Dismiss(Guid id)
    {
        bool removed;
        lock (_gate) removed = _toasts.RemoveAll(t => t.Id == id) > 0;
        if (removed) Changed?.Invoke();
    }

    /// <summary>Removes every toast.</summary>
    public void Clear()
    {
        lock (_gate) _toasts.Clear();
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var timer in _timers) timer.Dispose();
            _timers.Clear();
            _toasts.Clear();
        }
    }
}

/// <summary>How serious a toast is.</summary>
public enum BcToastLevel { Success, Info, Warning, Error }

/// <summary>One toast.</summary>
public sealed record BcToast(Guid Id, string Message, string? Title, BcToastLevel Level)
{
    /// <summary>Bootstrap background utility for the toast header.</summary>
    public string HeaderClass => Level switch
    {
        BcToastLevel.Success => "text-bg-success",
        BcToastLevel.Warning => "text-bg-warning",
        BcToastLevel.Error   => "text-bg-danger",
        _                    => "text-bg-primary",
    };

    /// <summary>Sprite icon name matching the level.</summary>
    public string Icon => Level switch
    {
        BcToastLevel.Success => "check-circle",
        BcToastLevel.Warning => "alert-triangle",
        BcToastLevel.Error   => "alert-octagon",
        _                    => "info",
    };

    /// <summary>The heading used when the caller gave none.</summary>
    public string DefaultTitle => Level switch
    {
        BcToastLevel.Success => "Done",
        BcToastLevel.Warning => "Careful",
        BcToastLevel.Error   => "Something went wrong",
        _                    => "Note",
    };
}
