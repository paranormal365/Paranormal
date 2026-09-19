namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// The logic of a multi-step wizard, separate from its rendering (item 166 W0).
/// </summary>
/// <remarks>
/// A plain class on purpose: bUnit is absent from this codebase, so everything a wizard DECIDES
/// — step order, whether Next is allowed, what a refusal says, when Finish is reachable — lives
/// here where xUnit can hold it, and <c>BenWizard</c> only renders the answers.
///
/// <para>A step's <c>CanLeaveAsync</c> returns null to allow leaving forward, or a refusal
/// sentence which blocks Next and is the wizard's error line (the item-141 rule: a refusal the
/// UI discards is worse than no rule). Going BACK never validates — a person may always retreat
/// from a half-filled step.</para>
/// </remarks>
public sealed class WizardModel
{
    /// <summary>One step: a name for the progress header, and its forward gate.</summary>
    /// <param name="Title">Shown in the progress header.</param>
    /// <param name="CanLeaveAsync">Null to allow Next/Finish; a sentence to refuse, shown as the error.</param>
    public sealed record WizardStep(string Title, Func<Task<string?>>? CanLeaveAsync = null);

    private readonly List<WizardStep> _steps = [];

    public IReadOnlyList<WizardStep> Steps => _steps;
    public int CurrentIndex { get; private set; }
    public WizardStep Current => _steps[CurrentIndex];
    public bool IsFirst => CurrentIndex == 0;
    public bool IsLast  => CurrentIndex == _steps.Count - 1;
    public bool IsFinished { get; private set; }

    /// <summary>The current refusal sentence, cleared by any successful move (or going back).</summary>
    public string? Refusal { get; private set; }

    public WizardModel(IEnumerable<WizardStep> steps)
    {
        _steps.AddRange(steps);
        if (_steps.Count == 0)
            throw new ArgumentException("A wizard needs at least one step.", nameof(steps));
    }

    /// <summary>Moves forward when the current step allows it. False means refused (see <see cref="Refusal"/>).</summary>
    public async Task<bool> NextAsync()
    {
        if (IsLast || IsFinished) return false;
        if (!await TryLeaveAsync()) return false;

        CurrentIndex++;
        return true;
    }

    /// <summary>Backward is always allowed, and clears any standing refusal — the person is
    /// retreating from the field the refusal was about.</summary>
    public bool Back()
    {
        if (IsFirst || IsFinished) return false;
        CurrentIndex--;
        Refusal = null;
        return true;
    }

    /// <summary>Finishes from the last step, if it allows leaving. False means refused.</summary>
    public async Task<bool> FinishAsync()
    {
        if (!IsLast || IsFinished) return false;
        if (!await TryLeaveAsync()) return false;

        IsFinished = true;
        return true;
    }

    /// <summary>Restores a saved position (draft resume). Out-of-range indexes clamp rather than
    /// throw: a draft written by a wizard that has since lost a step must not kill the page.</summary>
    public void Restore(int stepIndex)
    {
        if (IsFinished) return;
        CurrentIndex = Math.Clamp(stepIndex, 0, _steps.Count - 1);
        Refusal = null;
    }

    private async Task<bool> TryLeaveAsync()
    {
        var refusal = Current.CanLeaveAsync is { } gate ? await gate() : null;
        Refusal = refusal;
        return refusal is null;
    }
}
