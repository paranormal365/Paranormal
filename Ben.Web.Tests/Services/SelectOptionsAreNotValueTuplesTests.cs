using System.Text.RegularExpressions;
using Ben.Web.Website.Library.Kit;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A dropdown's options must be a type whose member names exist at runtime.
/// </summary>
/// <remarks>
/// <para>A value tuple's element names are compile-time metadata: at runtime
/// <c>(string Value, string Label)</c> has fields <c>Item1</c>/<c>Item2</c> and no properties
/// named <c>Value</c> or <c>Label</c> at all. <see cref="SelectValue.GetMember"/> looks those up
/// by name, finds nothing, and deliberately falls back to the item itself rather than throwing
/// mid-render — so the picker silently renders each option as "(viridis, Viridis)" and, because
/// the value never matches the bound one either, shows blank until something is chosen.</para>
///
/// <para>Two pickers shipped like this: the audio editor's spectrogram colormap and the image
/// editor's filter preset. Both were found by looking at the page, not by any test
/// (2026-09-06 audio walk, finding T).</para>
/// </remarks>
public sealed class SelectOptionsAreNotValueTuplesTests
{
    /// <summary>The behaviour the guard below exists because of.</summary>
    [Fact]
    public void A_value_tuples_element_names_do_not_exist_at_runtime()
    {
        var tuple = (Value: "viridis", Label: "Viridis");

        Assert.Null(tuple.GetType().GetProperty("Label"));
        // GetMember falls back to the item, which is how the raw tuple reaches the screen as text.
        Assert.Equal(tuple.ToString(), SelectValue.GetMember(tuple, "Label")?.ToString());
        Assert.Equal("(viridis, Viridis)", SelectValue.GetMember(tuple, "Label")?.ToString());
    }

    [Fact]
    public void A_records_do()
    {
        var option = new Option("viridis", "Viridis");

        Assert.Equal("Viridis", SelectValue.GetMember(option, "Label"));
        Assert.Equal("viridis", SelectValue.GetMember(option, "Value"));
    }

    private sealed record Option(string Value, string Label);

    /// <summary>
    /// No component hands a list of value tuples to a picker.
    /// </summary>
    [Fact]
    public void No_dropdown_is_fed_a_list_of_value_tuples()
    {
        List<string> offenders = [];

        foreach (var file in Directory.EnumerateFiles(LibraryRoot(), "*.razor", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);

            // Fields declared as a list of value tuples, e.g. "List<(string Value, string Label)> _colormapOptions".
            var tupleLists = Regex.Matches(source, @"List<\((?<members>[^)]*)\)>\s+(?<name>_\w+)")
                .Select(m => m.Groups["name"].Value)
                .ToHashSet(StringComparer.Ordinal);

            if (tupleLists.Count == 0) continue;

            // …that are then bound as a picker's Data.
            foreach (Match bound in Regex.Matches(source, @"Data\s*=\s*""@(?<name>_\w+)"""))
            {
                var name = bound.Groups["name"].Value;
                if (tupleLists.Contains(name))
                    offenders.Add($"{Path.GetFileName(file)}: Data=\"@{name}\" is a List<(...)>");
            }
        }

        Assert.True(offenders.Count == 0,
            "These pickers are bound to value tuples, whose element names do not exist at runtime, "
            + "so every option renders as its ToString and the selection never matches. Use a record:\n  "
            + string.Join("\n  ", offenders));
    }

    private static string LibraryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Web.Website.Library")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Ben.Web.Website.Library");
    }
}
