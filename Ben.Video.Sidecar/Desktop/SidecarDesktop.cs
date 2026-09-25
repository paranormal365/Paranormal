using System.Windows.Forms;
using Ben.Video.Sidecar.Lifetime;
using Ben.Video.Sidecar.Security;

namespace Ben.Video.Sidecar.Desktop;

/// <summary>
/// Everything the Windows build does beyond serving: one copy at a time, and the pairing window.
/// </summary>
/// <remarks>
/// <para>The Windows build is a windowed program with no console, so nothing appears when it
/// starts unless this decides to show something. It shows the pairing window when:</para>
/// <list type="bullet">
/// <item>no browser has paired with this install yet - a new install, however it was started
/// (the Store's Open button, the Start menu, or the editor's Turn on) - or</item>
/// <item>somebody starts it again from the Start menu while it is already running, which is the
/// only thing a person can mean by that.</item>
/// </list>
/// <para>Once paired, a start from the editor's Turn on, or at sign-in, shows nothing.</para>
///
/// <para>One copy at a time matters more now that nothing is visible: a second start used to find
/// port 43117 taken and quietly serve a second sidecar on 43118. A second start now hands over to
/// the first and ends.</para>
/// </remarks>
internal sealed class SidecarDesktop : IDisposable
{
    private const string InstanceName = @"Local\IsHaunted.BenVideoSidecar";
    private const string ShowSignalName = @"Local\IsHaunted.BenVideoSidecar.ShowPairing";
    private const string Title = "IsHaunted.com SideCar";

    private readonly Mutex _instance;
    private readonly EventWaitHandle _showSignal;
    private readonly object _gate = new();
    private PairingTokenStore? _store;
    private RegisteredWaitHandle? _showWait;
    private PairingWindow? _window;
    private bool _windowStarting;
    private bool _stopping;

    private SidecarDesktop(Mutex instance, EventWaitHandle showSignal)
    {
        _instance = instance;
        _showSignal = showSignal;
    }

    /// <summary>
    /// Claims this session's one copy of the sidecar. Returns null when another copy already has
    /// it, after asking that copy to show its pairing window if this start came from a person
    /// rather than from the editor's Turn on link - the caller should then end without serving.
    /// </summary>
    public static SidecarDesktop? TryClaim(string[] args)
    {
        var instance = new Mutex(initiallyOwned: true, InstanceName, out var createdNew);
        var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        if (createdNew) return new SidecarDesktop(instance, showSignal);

        if (PairingWindowPolicy.SecondStartShowsWindow(args)) showSignal.Set();

        showSignal.Dispose();
        instance.Dispose();
        return null;
    }

    /// <summary>Called once the server is listening.</summary>
    public void Start(PairingTokenStore store, IHostApplicationLifetime lifetime)
    {
        _store = store;
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showSignal, (_, _) => ShowPairingWindow(), null, Timeout.Infinite, executeOnlyOnce: false);

        // "Off" in the editor stops the process; a window left open would otherwise keep a
        // stopped sidecar's code on screen until the process exits under it.
        lifetime.ApplicationStopping.Register(() =>
        {
            PairingWindow? open;
            lock (_gate) { _stopping = true; open = _window; }
            if (open is { IsHandleCreated: true, IsDisposed: false })
                open.BeginInvoke(open.Close);
        });

        if (PairingWindowPolicy.ShowOnStart(store.AwaitingFirstPairing)) ShowPairingWindow();
    }

    /// <summary>Shows the pairing window, or brings it forward if it is already open.</summary>
    public void ShowPairingWindow()
    {
        PairingTokenStore store;
        lock (_gate)
        {
            if (_stopping || _store is null) return;
            store = _store;
            if (_window is { } open) { open.BringForward(); return; }
            if (_windowStarting) return;
            _windowStarting = true;
        }

        // Windows Forms wants a single-threaded apartment with its own message loop. The server's
        // threads have neither, so the window gets a thread of its own for as long as it is open.
        var thread = new Thread(() =>
        {
            try
            {
                EnsureWindowsFormsReady();
                using var window = new PairingWindow(store, Title);
                lock (_gate) { _window = window; _windowStarting = false; }
                Application.Run(window);
            }
            finally
            {
                lock (_gate) { _window = null; _windowStarting = false; }
            }
        })
        {
            IsBackground = true, // the server's lifetime decides when the process ends, not this
            Name = "Pairing window",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static int _formsReady;

    // These may only be set before the process's first window, and only once.
    private static void EnsureWindowsFormsReady()
    {
        if (Interlocked.Exchange(ref _formsReady, 1) == 1) return;
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }

    public void Dispose()
    {
        _showWait?.Unregister(null);
        _showSignal.Dispose();
        try { _instance.ReleaseMutex(); }
        catch (ApplicationException) { /* not the owning thread; the handle closing releases it */ }
        _instance.Dispose();
    }
}
