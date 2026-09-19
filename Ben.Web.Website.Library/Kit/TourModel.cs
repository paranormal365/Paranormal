namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// The logic of a walkthrough tour, separate from its overlay rendering (item 166 W0).
/// </summary>
/// <remarks>
/// Same reasoning as <see cref="WizardModel"/>: what a tour decides — step order, when it ends,
/// what dismissing means — is plain-class logic xUnit can hold; <c>BenTour</c> renders it and
/// does the element highlighting. Dismissal persistence is the CALLER's job (a
/// <c>UserTourState</c> row, never localStorage — an impersonating admin must see the real
/// person's tour state, and a cleared browser must not replay every tour).
/// </remarks>
public sealed class TourModel
{
    /// <summary>One step: a CSS selector to highlight, and what to say about it.</summary>
    public sealed record TourStep(string Selector, string Title, string Body);

    private readonly List<TourStep> _steps = [];

    public IReadOnlyList<TourStep> Steps => _steps;
    public int CurrentIndex { get; private set; }
    public TourStep Current => _steps[CurrentIndex];
    public bool IsFirst => CurrentIndex == 0;
    public bool IsLast  => CurrentIndex == _steps.Count - 1;

    /// <summary>Running means the overlay shows. Ended (completed OR skipped) means it is done
    /// for this visit; whether it ever auto-launches again is the persisted state's business.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>True when the person saw every step; false when they skipped out early.
    /// Both end states dismiss the tour — a skip is an answer, not an accident.</summary>
    public bool Completed { get; private set; }

    public TourModel(IEnumerable<TourStep> steps)
    {
        _steps.AddRange(steps);
        if (_steps.Count == 0)
            throw new ArgumentException("A tour needs at least one step.", nameof(steps));
    }

    public void Start()
    {
        if (IsRunning) return;
        CurrentIndex = 0;
        Completed    = false;
        IsRunning    = true;
    }

    /// <summary>Advances; on the last step, ends the tour as completed.</summary>
    public void Next()
    {
        if (!IsRunning) return;
        if (IsLast) { Completed = true; IsRunning = false; return; }
        CurrentIndex++;
    }

    public void Back()
    {
        if (!IsRunning || IsFirst) return;
        CurrentIndex--;
    }

    /// <summary>Ends the tour early. A skip still counts as dismissal for persistence.</summary>
    public void Skip()
    {
        if (!IsRunning) return;
        Completed = false;
        IsRunning = false;
    }
}
