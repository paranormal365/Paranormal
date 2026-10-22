using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// What <see cref="BenTextBox"/> and <see cref="BenTextArea"/> share: a box that reports every
/// keystroke and is only ever rewritten when code changes its text.
/// </summary>
/// <remarks>
/// <para><b>Why not a plain box.</b> A box drawn as <c>value="@_field"</c> with <c>@oninput</c>
/// setting the field has its value sent back from the server after every keystroke. When the reply
/// lagged behind the typing (holding Backspace, a slow connection), an older copy of the text was
/// written over the box and the browser moved the cursor to the end, so the next delete removed
/// the end of the text instead of the letter beside the cursor (Ben, 10/15/2026, in the feed
/// composer).</para>
///
/// <para><b>How this avoids it.</b> The box binds with <c>@bind:get</c>/<c>@bind:set</c> and
/// <c>@bind:event="oninput"</c>. For a bind, Blazor takes the text the browser sent with the event
/// as its own record of what the box shows before it redraws, so drawing the same text again sends
/// nothing. Only a change made by code — clearing after saving, inserting an emoji, a handler that
/// tidies what was typed — differs from that record and reaches the box.</para>
///
/// <para><b>Drop-in for the plain box.</b> <see cref="Value"/> takes whatever the old
/// <c>value="@..."</c> did (text, or a number for a number box), <see cref="OnInput"/> takes the
/// old <c>@oninput</c> handler unchanged, and every other attribute — id, class, type, maxlength,
/// disabled, <c>@onkeydown</c> — goes onto the element as written.</para>
/// </remarks>
public abstract class BenTypedBox : ComponentBase, IDisposable
{
    /// <summary>The text the box shows. A number is written the way a number box expects it.</summary>
    [Parameter] public object? Value { get; set; }

    /// <summary>Called on every keystroke with the box's whole text, as <c>@oninput</c> was.</summary>
    [Parameter] public EventCallback<ChangeEventArgs> OnInput { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>What the box holds: the last text typed, or the last text code gave it.</summary>
    protected string Shown { get; private set; } = string.Empty;

    /// <summary>The last <see cref="Value"/> code gave, so a redraw that repeats it changes nothing.</summary>
    private string? _given;

    // ── A change from code that crossed a keystroke on the way ───────────────
    // Code changed the text (the CMS fills a page's address in from its title) while the person was
    // typing, and the two crossed: the change was sent first and arrived after the keystroke. It then
    // sits on top of what was typed, and the bind believes the keystroke, so nothing puts it right:
    // on a slow connection the address box showed one address while the page saved another
    // (10/22/2026). Messages arrive in order, and the browser confirms each screen update it applies
    // (OnAfterRender runs on that confirmation), so a keystroke that reaches us before the
    // confirmation was typed before the change landed. For those only, the box is written again
    // with what was typed: once with the change, so the browser's copy and ours agree, then with
    // the typing. Ordinary typing, with no change from code in flight, is never written back.

    /// <summary>Renders started and confirmed, counted, to know when a change from code has landed.</summary>
    /// <remarks>Starts at one: the first render does not ask <see cref="ShouldRender"/>.</remarks>
    private int _renders = 1, _confirmed;

    /// <summary>The render that carries the latest change from code; it has landed once confirmed.</summary>
    private int _changeRender;

    protected override bool ShouldRender()
    {
        _renders++;
        return true;
    }

    protected override void OnAfterRender(bool firstRender) => _confirmed++;

    private bool AChangeIsOnItsWay => _confirmed < _changeRender;

    protected override void OnParametersSet()
    {
        // Only a Value that differs from the last one given is code changing the text. A handler
        // that rejects a keystroke and keeps the old value leaves the box as typed, as the plain
        // box did.
        var given = AsText(Value);
        if (given != _given)
        {
            var first = _given is null;
            _given = given;
            if (given != Shown && !first) _changeRender = _renders + 1;
            Shown = given;
        }
    }

    /// <summary>What the box is drawn with: <see cref="Shown"/>, except while a crossed change is put right.</summary>
    protected string Drawn => _crossed ?? Shown;

    /// <summary>The change from code that a keystroke crossed, drawn once so both sides agree on it.</summary>
    private string? _crossed;

    private bool _disposed;

    protected async Task OnTypedAsync(string? typed)
    {
        typed ??= string.Empty;
        var crossed = AChangeIsOnItsWay && typed != Shown ? Shown : null;

        // The page learns what was typed first, and at once: a Save already waiting behind this
        // keystroke must find it (10/22/2026, a page saved under the address it was given before the
        // typed one, when this waited first).
        Shown = typed;
        await OnInput.InvokeAsync(new ChangeEventArgs { Value = Shown });

        if (crossed is null || _disposed) return;

        // Then the box: the browser is about to show the change, so draw that, then the typing.
        _crossed = crossed;
        StateHasChanged();
        await Task.Yield();
        _crossed = null;
        if (_disposed) return;
        _changeRender = _renders + 1;
        StateHasChanged();
    }

    public void Dispose() => _disposed = true;

    private static string AsText(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
