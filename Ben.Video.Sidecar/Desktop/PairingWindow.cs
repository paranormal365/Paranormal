using System.Drawing;
using System.Windows.Forms;
using Ben.Video.Sidecar.Security;

namespace Ben.Video.Sidecar.Desktop;

/// <summary>
/// The sidecar's one window on Windows: a pairing code, a button that copies it, and nothing else.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-25: "Instead of popping up a console, on the first launch, can we pop up a
/// simple window with the code and a button to copy it so they can paste it in on the web app?
/// Then, from that point on, they manage start and stop from app without it popping up any
/// console."</para>
///
/// <para>Closing it leaves the sidecar running - it is a way to read a code, not the program. The
/// code is minted here exactly as the /pair page mints one (<see cref="PairingTokenStore.BeginPairing"/>),
/// so it lasts ten minutes, works once, and a newer one from either place replaces it. The window
/// never mints a replacement on its own: that would silently invalidate a code the person may be
/// reading off the /pair page at the same moment. It says the code has stopped working and offers
/// a new one.</para>
/// </remarks>
internal sealed class PairingWindow : Form
{
    private readonly PairingTokenStore _store;
    private readonly Label _heading = new();
    private readonly Label _code = new();
    private readonly Label _status = new();
    private readonly Label _instructions = new();
    private readonly Button _copy = new();
    private readonly Button _newCode = new();
    private readonly Button _close = new();
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    private string _current = "";
    private bool _paired;

    public PairingWindow(PairingTokenStore store, string title)
    {
        _store = store;

        Text = title;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(24, 20, 24, 20);
        TopMost = true; // until it has been shown, so it does not open behind the browser

        _heading.Text = "Pair the video editor";
        _heading.Font = new Font("Segoe UI Semibold", 14f);
        _heading.AutoSize = true;

        _code.Font = new Font("Segoe UI Semibold", 34f);
        _code.AutoSize = true;
        _code.Margin = new Padding(0, 8, 0, 0);
        _code.AccessibleName = "Pairing code";

        _status.AutoSize = true;
        _status.ForeColor = SystemColors.GrayText;

        _instructions.AutoSize = true;
        _instructions.MaximumSize = new Size(380, 0);
        _instructions.Margin = new Padding(0, 12, 0, 0);

        _copy.Text = "Copy code";
        _copy.AutoSize = true;
        _copy.Padding = new Padding(10, 2, 10, 2);
        _copy.Click += (_, _) => CopyCode();

        _newCode.Text = "Show a new code";
        _newCode.AutoSize = true;
        _newCode.Padding = new Padding(10, 2, 10, 2);
        _newCode.Visible = false;
        _newCode.Click += (_, _) => ShowNewCode();

        _close.Text = "Close";
        _close.AutoSize = true;
        _close.Padding = new Padding(10, 2, 10, 2);
        _close.DialogResult = DialogResult.Cancel;
        _close.Click += (_, _) => Close();

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 16, 0, 0),
        };
        buttons.Controls.AddRange([_copy, _newCode, _close]);

        var footer = new Label
        {
            Text = "Closing this window leaves the SideCar running.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 12, 0, 0),
        };

        var layout = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };
        layout.Controls.AddRange([_heading, _code, _status, _instructions, buttons, footer]);
        Controls.Add(layout);

        AcceptButton = _copy;
        CancelButton = _close;

        _store.Paired += OnPaired;
        _tick.Tick += (_, _) => UpdateView(fromTimer: true);

        ShowNewCode();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        TopMost = false;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _store.Paired -= OnPaired;
        _tick.Dispose();
        base.OnFormClosed(e);
    }

    /// <summary>Brings an already-open window to the front, from any thread.</summary>
    public void BringForward()
    {
        if (!IsHandleCreated) return;
        BeginInvoke(() =>
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            TopMost = true;
            Activate();
            TopMost = false;
        });
    }

    private void ShowNewCode()
    {
        _current = _store.BeginPairing();
        UpdateView(fromTimer: false);
        _tick.Start();
        _copy.Focus();
    }

    private void CopyCode()
    {
        try
        {
            Clipboard.SetText(_current);
            _copy.Text = "Copied";
            var reset = new System.Windows.Forms.Timer { Interval = 2000 };
            reset.Tick += (_, _) => { reset.Dispose(); if (!IsDisposed) _copy.Text = "Copy code"; };
            reset.Start();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another program is holding the clipboard open. Rare, and the code is on screen.
            _copy.Text = "Try again";
        }
    }

    // Raised on the request thread that exchanged the code, not this one.
    private void OnPaired()
    {
        if (!IsHandleCreated || IsDisposed) return;
        BeginInvoke(() =>
        {
            _paired = true;
            UpdateView(fromTimer: false);
        });
    }

    private void UpdateView(bool fromTimer)
    {
        if (_paired)
        {
            _tick.Stop();
            _heading.Text = "Paired";
            _code.Text = "✓";
            _status.Text = "";
            _status.Visible = false;
            _instructions.Text = "The video editor can use the SideCar now. From here on it runs in the "
                               + "background, with no window: turn it on and off from the video editor.";
            _copy.Visible = false;
            _newCode.Visible = false;
            AcceptButton = _close;
            _close.Focus();
            return;
        }

        var stillWorks = _store.IsCurrentCode(_current);
        if (fromTimer && stillWorks) return; // nothing to redraw

        _heading.Text = "Pair the video editor";
        _code.Text = _current;
        _code.ForeColor = stillWorks ? SystemColors.ControlText : SystemColors.GrayText;
        _status.Visible = true;
        _status.Text = stillWorks
            ? $"Works once, until {_store.CodeExpiresUtc.ToLocalTime():h:mm tt}."
            : "This code no longer works.";
        _instructions.Text = "Copy the code, then paste it into the video editor at ishaunted.com, "
                           + "under Native acceleration. You only do this once for each browser.";
        _copy.Visible = stillWorks;
        _newCode.Visible = !stillWorks;
        AcceptButton = stillWorks ? _copy : _newCode;
        if (!stillWorks) _tick.Stop();
    }
}
