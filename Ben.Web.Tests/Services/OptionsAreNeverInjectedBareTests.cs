using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A type registered with <c>Configure&lt;T&gt;</c> is never asked for bare (2026-09-20).
/// </summary>
/// <remarks>
/// <para><b>What this cost.</b> <c>Configure&lt;T&gt;</c> registers <c>IOptions&lt;T&gt;</c> and
/// nothing else, so a constructor asking for a plain <c>T</c> cannot be satisfied. On a SCOPED
/// service that failure does not happen at startup — it happens when a request is served. The API
/// comes up healthy, passes its own health check, and then returns 500 to everything, BEFORE
/// <c>[Authorize]</c> ever runs. A production deploy failed its smoke checks that way: a public
/// endpoint 500ing where 200 was expected, and a protected one 500ing where 401 was.</para>
///
/// <para><b>Why a compile-time list rather than building the container.</b> Building the API's real
/// service provider in a test would catch more, but it needs configuration, a database and every
/// external client the app touches — so it would fail for reasons that have nothing to do with
/// this, and get switched off. Reading the constructors is narrow, fast and never wrong for a
/// reason nobody cares about.</para>
///
/// <para><b>The better fix is one line in the host</b>, and it is not this test's job:
/// <c>UseDefaultServiceProvider(o =&gt; o.ValidateOnBuild = true)</c> turns every missing
/// registration into a refusal to start. Worth doing; this guard stands whether or not it is.</para>
/// </remarks>
public sealed class OptionsAreNeverInjectedBareTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>The types the host registers as options, read from the host itself.</summary>
    /// <remarks>
    /// Read rather than listed, so a new <c>Configure&lt;T&gt;</c> is covered the day it is added
    /// — which is the day somebody is most likely to inject it the wrong way.
    /// </remarks>
    private static IReadOnlyList<string> OptionsOnlyTypes()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "Ben.Data.WebApi", "Program.cs"));

        return Regex.Matches(program, @"Configure<([A-Za-z0-9_.]+)>")
            .Select(m => m.Groups[1].Value.Split('.').Last())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<string> SourceFiles()
    {
        foreach (var project in new[] { "Ben.Data.WebApi", "Ben.Web.Services", "Ben.Web.Website.Library" })
        {
            var root = Path.Combine(RepoRoot(), project);
            if (!Directory.Exists(root)) continue;

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                if (!file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                 && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    yield return file;
        }
    }

    [Fact]
    public void The_host_still_registers_some_options()
    {
        // If Program.cs is restructured and nothing matches, every check below passes by finding
        // nothing — silently, and for ever.
        Assert.NotEmpty(OptionsOnlyTypes());
        Assert.Contains("SiteIdentity", OptionsOnlyTypes());
    }

    [Fact]
    public void No_constructor_asks_for_an_options_type_directly()
    {
        var options = OptionsOnlyTypes();
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);

            foreach (Match ctor in Regex.Matches(
                text, @"public\s+(\w+)\s*\(([^)]*)\)", RegexOptions.Singleline))
            {
                var name = ctor.Groups[1].Value;
                var parameters = ctor.Groups[2].Value;

                // Only constructors — "public Foo(" where Foo is the type name — and only ones
                // the container would build. A static helper taking a SiteIdentity is fine: the
                // caller passes its own already-unwrapped copy, which is how BenEmailLayout works.
                if (!text.Contains($"class {name}") && !text.Contains($"sealed class {name}")) continue;

                foreach (var type in options)
                {
                    // Bare: "SiteIdentity site", never "IOptions<SiteIdentity> site".
                    if (!Regex.IsMatch(parameters, $@"(^|[,\s(]){Regex.Escape(type)}\s+\w+")) continue;
                    if (Regex.IsMatch(parameters, $@"IOptions\s*<\s*{Regex.Escape(type)}\s*>")) continue;

                    offenders.Add($"{Path.GetFileName(file)}  {name}(… {type} …)");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These constructors ask for a type the host registers with Configure<T>, which "
          + "provides IOptions<T> and nothing else. A scoped service built this way does not fail "
          + "at startup — it returns 500 to every request, before [Authorize] runs:"
          + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders)
          + Environment.NewLine + Environment.NewLine
          + "Take IOptions<T> and unwrap it once, as EventOrganizerMailer does: "
          + "\"public EventOrganizerMailer(IEmailService email, IOptions<SiteIdentity> site) "
          + "{ _site = site.Value; }\".");
    }
}
