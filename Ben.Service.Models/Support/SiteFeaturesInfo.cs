namespace Ben.Service.Models.Support;

/// <summary>
/// Which sections of the site are switched on, as the public endpoint reports them.
/// </summary>
/// <remarks>
/// <para>A dictionary rather than a property per feature, deliberately. The website's job here is
/// to ask "is this key on?" for a key it already names in its own gate; a typed property per
/// feature would mean editing this record, the controller, the provider and the gate every time a
/// switch is added, and the compiler cannot check a feature that does not exist yet anyway.</para>
///
/// <para>It is still narrow in the way that matters: the controller fills it from the declared
/// feature list only, so a new NON-feature setting can never leak onto the anonymous endpoint by
/// being added to the settings table.</para>
/// </remarks>
/// <param name="Features">Feature key to on/off, already resolved against each flag's default.</param>
/// <param name="Announcement">The site-wide announcement, or null when none is set. Named
/// explicitly rather than smuggled through the dictionary, so the narrow-by-declaration property
/// above still holds — this is the one non-feature value the endpoint publishes on purpose.</param>
public sealed record SiteFeaturesInfo(IReadOnlyDictionary<string, bool> Features, string? Announcement = null)
{
    /// <summary>Whether a feature is on. Unknown keys read as off.</summary>
    public bool IsOn(string key) => Features.TryGetValue(key, out var on) && on;
}
