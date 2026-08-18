namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// Replacement for TelerikNotification: a scoped queue the layout's <c>BenToastHost</c> renders.
/// </summary>
/// <remarks>
/// Scoped, so it lives and dies with the circuit — a toast raised for one session must never
/// surface in another, the same reasoning that makes <c>NotificationState</c> and
/// <c>AvatarCache</c> per-circuit.
/// <para>
/// Auto-dismissal is a timer here rather than Bootstrap's Toast plugin, so nothing outside
/// Blazor removes the element: the host renders the list, and the list is the only truth.
/// </para>
/// </remarks>
public sealed class BenToastService : IDisposable
{
    private readonly List<BenToast> _toasts = new();
    private readonly List<Timer> _timers = new();
    private readonly Lock _gate = new();

    /// <summary>Raised when the queue changes. Fires off the renderer's synchronization context
    /// when a timer expires, so subscribers must marshal with InvokeAsync.</summary>
    public event Action? Changed;

    public IReadOnlyList<BenToast> Current
    {
        get { lock (_gate) return _toasts.ToArray(); }
    }

    public void Success(string message, string? title = null) => Show(message, title, BenToastLevel.Success);
    public void Info(string message, string? title = null)    => Show(message, title, BenToastLevel.Info);
    public void Warning(string message, string? title = null) => Show(message, title, BenToastLevel.Warning);

    /// <summary>Errors persist until dismissed — an error that vanishes on its own is one the
    /// reader may never have seen.</summary>
    public void Error(string message, string? title = null)
        => Show(message, title, BenToastLevel.Error, autoDismiss: false);

    public void Show(string message, string? title, BenToastLevel level,
                     bool autoDismiss = true, TimeSpan? duration = null)
    {
        var toast = new BenToast(Guid.NewGuid(), message, title, level);

        lock (_gate) _toasts.Add(toast);
        Changed?.Invoke();

        if (!autoDismiss) return;

        var timer = new Timer(_ => Dismiss(toast.Id), null,
                              duration ?? TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        lock (_gate) _timers.Add(timer);
    }

    public void Dismiss(Guid id)
    {
        bool removed;
        lock (_gate) removed = _toasts.RemoveAll(t => t.Id == id) > 0;
        if (removed) Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_gate) _toasts.Clear();
        Changed?.Invoke();
    }

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

public enum BenToastLevel { Success, Info, Warning, Error }

public sealed record BenToast(Guid Id, string Message, string? Title, BenToastLevel Level)
{
    /// <summary>Bootstrap background utility for the toast header.</summary>
    public string HeaderClass => Level switch
    {
        BenToastLevel.Success => "text-bg-success",
        BenToastLevel.Warning => "text-bg-warning",
        BenToastLevel.Error   => "text-bg-danger",
        _                     => "text-bg-primary",
    };

    /// <summary>Sprite icon name matching the level.</summary>
    public string Icon => Level switch
    {
        BenToastLevel.Success => "check-circle",
        BenToastLevel.Warning => "alert-triangle",
        BenToastLevel.Error   => "alert-octagon",
        _                     => "info",
    };

    public string DefaultTitle => Level switch
    {
        BenToastLevel.Success => "Done",
        BenToastLevel.Warning => "Careful",
        BenToastLevel.Error   => "Something went wrong",
        _                     => "Note",
    };
}
