namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// A phone's model as people say it: "iPhone 16 Pro", not the "iPhone17,1" the device reports.
/// </summary>
/// <remarks>
/// The session lists showed the raw code, and the simulator's "arm64" (site audit, 10/09/2026). A model
/// missing from the table still reads as its family, so a new phone shows "iPhone" rather than a code.
/// </remarks>
public static class DeviceNames
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["iPhone13,1"] = "iPhone 12 mini", ["iPhone13,2"] = "iPhone 12", ["iPhone13,3"] = "iPhone 12 Pro", ["iPhone13,4"] = "iPhone 12 Pro Max",
        ["iPhone14,4"] = "iPhone 13 mini", ["iPhone14,5"] = "iPhone 13", ["iPhone14,2"] = "iPhone 13 Pro", ["iPhone14,3"] = "iPhone 13 Pro Max",
        ["iPhone14,6"] = "iPhone SE", ["iPhone14,7"] = "iPhone 14", ["iPhone14,8"] = "iPhone 14 Plus",
        ["iPhone15,2"] = "iPhone 14 Pro", ["iPhone15,3"] = "iPhone 14 Pro Max", ["iPhone15,4"] = "iPhone 15", ["iPhone15,5"] = "iPhone 15 Plus",
        ["iPhone16,1"] = "iPhone 15 Pro", ["iPhone16,2"] = "iPhone 15 Pro Max",
        ["iPhone17,1"] = "iPhone 16 Pro", ["iPhone17,2"] = "iPhone 16 Pro Max", ["iPhone17,3"] = "iPhone 16", ["iPhone17,4"] = "iPhone 16 Plus",
        ["iPhone17,5"] = "iPhone 16e",
    };

    public static string Friendly(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return "";
        var m = model.Trim();
        if (Known.TryGetValue(m, out var name)) return name;
        if (m is "arm64" or "x86_64" or "i386") return "Simulator";
        foreach (var family in new[] { "iPhone", "iPad", "iPod" })
            if (m.StartsWith(family, StringComparison.OrdinalIgnoreCase) && m.Length > family.Length && char.IsDigit(m[family.Length]))
                return family;
        return m;
    }
}
