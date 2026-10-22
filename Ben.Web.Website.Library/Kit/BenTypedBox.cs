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
public abstract class BenTypedBox : ComponentBase
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

    protected override void OnParametersSet()
    {
        // Only a Value that differs from the last one given is code changing the text. A handler
        // that rejects a keystroke and keeps the old value leaves the box as typed, as the plain
        // box did.
        var given = AsText(Value);
        if (given != _given)
        {
            _given = given;
            Shown = given;
        }
    }

    protected async Task OnTypedAsync(string? typed)
    {
        Shown = typed ?? string.Empty;
        await OnInput.InvokeAsync(new ChangeEventArgs { Value = Shown });
    }

    private static string AsText(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
