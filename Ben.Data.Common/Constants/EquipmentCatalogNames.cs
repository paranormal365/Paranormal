namespace Ben.Data.Common.Constants;

/// <summary>
/// Names in the equipment catalogue that mean something to the code, not just to a reader.
/// </summary>
/// <remarks>
/// One place, because the seeder writes these rows and the browse page has to recognise them, and
/// the two live in projects that cannot see each other. A string typed twice is a string that
/// drifts once.
/// </remarks>
public static class EquipmentCatalogNames
{
    /// <summary>
    /// The brand every "I don't know what this is" item is filed under.
    /// </summary>
    /// <remarks>
    /// <para>Seeded with one model per category — "Audio Recorder", "EMF Meter" — so somebody
    /// adding a borrowed meter with no badge on it has something to pick. That is its whole job:
    /// it exists to be CHOSEN, in a dropdown, by somebody who already knows what they own.</para>
    ///
    /// <para>W-V3 of the 2026-09-06 evaluation: the public catalogue listed all sixteen of them as
    /// makes and models with no model number. A visitor browsing to see what investigators use
    /// met a page whose first screen was placeholders. Nothing was broken — the rows were being
    /// shown to the one audience they were never for.</para>
    /// </remarks>
    public const string GenericBrand = "Generic / Unbranded";
}
