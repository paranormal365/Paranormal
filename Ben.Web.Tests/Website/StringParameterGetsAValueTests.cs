using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// A component's text parameter is handed a value, not the name of one.
/// </summary>
/// <remarks>
/// Razor reads <c>Flag="_isOn"</c> as code when the parameter is a bool or an object, but as a
/// literal string when the parameter is a <see cref="string"/>. So <c>Refusal="_othersRefusal"</c>
/// passed the eleven characters "_othersRefusal", never the field: What I'm going to showed
/// "_othersRefusal Try again" to every guest, always, whether the list had loaded or not. Unit
/// tests that set the parameter directly and the e2e suite both passed; only reading the help
/// screenshots found it (10/16/2026). The codebase names its fields with a leading underscore, so a
/// string parameter given a bare <c>_name</c> (or <c>_name.Member</c>) is this mistake.
/// </remarks>
public class StringParameterGetsAValueTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    /// <summary>Every component's string-typed [Parameter] names, by component name.</summary>
    private static Dictionary<string, HashSet<string>> StringParameters()
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var name in new[] { "Ben.Web.Website.Library", "Ben.Web.Website" })
        {
            Assembly assembly;
            try { assembly = Assembly.Load(name); }
            catch (FileNotFoundException) { continue; }

            foreach (var type in assembly.GetTypes().Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract))
            {
                var strings = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.PropertyType == typeof(string) && p.GetCustomAttribute<ParameterAttribute>() is not null)
                    .Select(p => p.Name);
                var simple = type.Name.Split('`')[0];
                if (!map.TryGetValue(simple, out var set)) map[simple] = set = new HashSet<string>(StringComparer.Ordinal);
                set.UnionWith(strings);
            }
        }
        return map;
    }

    private static readonly Regex Tag = new(@"<([A-Z][A-Za-z0-9]*)\b([^<>]*?)/?>", RegexOptions.Singleline);
    // A bare attribute only: `@bind-Value="_x"` and `Value:after=` are Razor directives whose value
    // is always code, so the name must follow whitespace, not "bind-" or "@".
    private static readonly Regex Attribute = new(@"(?<=\s)([A-Z][A-Za-z0-9]*)=""(_[A-Za-z]\w*(?:\??\.\w+)*)""");

    [Fact]
    public void No_string_parameter_is_given_a_bare_field_name()
    {
        var parameters = StringParameters();
        Assert.True(parameters.Count > 50, $"Only {parameters.Count} components were found; the assemblies didn't load.");

        var root = RepoRoot().FullName;
        var offences = new List<string>();
        var scanned = 0;

        foreach (var dir in new[] { "Ben.Web.Website", "Ben.Web.Website.Library" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, dir), "*.razor", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            var text = Regex.Replace(File.ReadAllText(file), @"@\*.*?\*@", m => new string('\n', m.Value.Count(c => c == '\n')), RegexOptions.Singleline);
            scanned++;

            foreach (Match tag in Tag.Matches(text))
            {
                if (!parameters.TryGetValue(tag.Groups[1].Value, out var strings) || strings.Count == 0) continue;
                foreach (Match attr in Attribute.Matches(tag.Groups[2].Value))
                {
                    if (!strings.Contains(attr.Groups[1].Value)) continue;
                    var line = text[..tag.Index].Count(c => c == '\n') + 1;
                    offences.Add($"{Path.GetRelativePath(root, file)}:{line} — <{tag.Groups[1].Value} {attr.Groups[1].Value}=\"{attr.Groups[2].Value}\"> passes the text \"{attr.Groups[2].Value}\"; write \"@{attr.Groups[2].Value}\"");
                }
            }
        }

        Assert.True(scanned > 100, $"Only {scanned} .razor files were scanned.");
        Assert.True(offences.Count == 0, "A string parameter is given a field's name as text:\n" + string.Join("\n", offences));
    }
}
