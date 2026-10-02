namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// Whether the current page draws a hero of its own — a PageHero, the store's carousel — so the
/// layout's section photo band stands aside.
/// </summary>
/// <remarks>
/// signal.css already hides the band with <c>:has(.page-hero, [data-own-hero])</c>, which is what a
/// prerendered page relies on. Hidden is not gone, though: the band's words stayed in the page,
/// and anything reading the page's text — a screen reader, a test asking for "Find" — met them
/// first. Once the circuit runs, this takes the band out of the page instead of out of sight.
/// </remarks>
public sealed class OwnHeroState
{
    private int _count;

    public event Action? Changed;

    public bool Active => _count > 0;

    public void Enter() { _count++; if (_count == 1) Changed?.Invoke(); }

    public void Leave() { if (_count == 0) return; _count--; if (_count == 0) Changed?.Invoke(); }
}
