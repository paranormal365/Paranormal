using Microsoft.JSInterop;

namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// Which clock this reader wants a case, investigation, event or tour read on: the place's
/// ("Local time") or their own ("My time"). One per circuit, remembered in the browser (2026-09-28).
/// </summary>
/// <remarks>
/// <para>Ben: "You can see the event and tour time in the event or tour timezone or the user's
/// timezone." A switch rather than both at once, and remembered across the site, so somebody who
/// plans from Seattle does not flip it on every page.</para>
///
/// <para><b>Local time is the default.</b> A walk that starts at eight in Nashville starts at eight
/// for everybody reading about it, and the ticket, the guest mail and the calendar file all say
/// so — see <c>EventClock</c>. Reading it in your own time is the choice.</para>
///
/// <para>Kept in <c>localStorage</c> because it is a way of reading, not an account setting: the
/// same person may want the place's time on a laptop at home and their own on a phone abroad.</para>
/// </remarks>
public sealed class TimeView(IJSRuntime js)
{
    private const string StorageKey = "ben-time-view";
    private Task? _loading;

    /// <summary>True when times read on the reader's own clock; false (the default) for the place's.</summary>
    public bool InMyTime { get; private set; }

    /// <summary>Raised when the choice is loaded or changed, so every time on the page redraws.</summary>
    public event Action? Changed;

    /// <summary>Reads the remembered choice once per circuit; later callers await the same read.</summary>
    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            var stored = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (stored == "mine" && !InMyTime)
            {
                InMyTime = true;
                Changed?.Invoke();
            }
        }
        catch { /* prerender, or storage blocked: the place's time stands */ }
    }

    public async Task SetAsync(bool inMyTime)
    {
        if (InMyTime == inMyTime) return;
        InMyTime = inMyTime;
        Changed?.Invoke();
        try { await js.InvokeVoidAsync("localStorage.setItem", StorageKey, inMyTime ? "mine" : "local"); }
        catch { /* remembered for this page only */ }
    }
}
