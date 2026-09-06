using System.Text.RegularExpressions;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Every slider's label template is told the same step the slider actually ticks at.
/// </summary>
/// <remarks>
/// <para>The template labels the first tick and the last one, and decides "last" by asking whether
/// this tick plus the step it was given runs past the maximum. Give it a step larger than the
/// slider's own and several ticks near the end all answer yes, so the labels pile up on top of
/// each other — "0 0.8 0.9 1" crammed into the right-hand end.</para>
///
/// <para>The editor fixed exactly this once before, by hand, and it came straight back the next
/// time somebody added sliders (2026-09-05 audit, found on screen while verifying phase 8). A
/// mismatch is invisible to the compiler and obvious in the browser, which is what a source scan
/// is for.</para>
/// </remarks>
public sealed class SliderLabelStepGuardTests
{
    /// <summary>Each slider that has both a LargeStep and an Endpoints template.</summary>
    public static TheoryData<string, string, string> LabelledSliders()
    {
        var data = new TheoryData<string, string, string>();

        // One <TelerikSlider …> element at a time, however many lines it spans.
        var slider = new Regex(@"<TelerikSlider\b(?<body>.*?)/>", RegexOptions.Singleline);
        var large  = new Regex(@"LargeStep=""(?<v>[^""]+)""");
        // Greedy to the last ")" before the closing quote, so an argument that is itself an
        // expression with brackets in it is read whole rather than cut in half.
        var labels = new Regex(@"SliderLabelTemplates\.Endpoints\((?<args>[^""]*)\)");

        foreach (var file in RazorFiles())
        {
            var text = File.ReadAllText(file);

            foreach (Match match in slider.Matches(text))
            {
                var body = match.Groups["body"].Value;
                var l = large.Match(body);
                var t = labels.Match(body);
                if (!l.Success || !t.Success) continue;

                var step = ThirdArgument(t.Groups["args"].Value);
                if (step is null) continue;

                data.Add(Path.GetFileName(file), l.Groups["v"].Value.Trim(), step);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LabelledSliders))]
    public void A_sliders_label_template_uses_the_sliders_own_step(
        string file, string largeStep, string templateStep)
    {
        var message =
            $"{file}: a slider ticks at {largeStep} and labels as if it ticked at {templateStep}, "
            + "so the labels near the end will pile up on each other.";

        // Both literal numbers: compare the numbers, so 0.25 and 0.25d agree.
        if (Number(largeStep) is { } a && Number(templateStep) is { } b)
        {
            Assert.True(a == b, message);
            return;
        }

        // One or both is an expression. The same expression on both sides is right by
        // construction; anything else cannot be checked here and is worth being told about.
        Assert.True(Normalise(largeStep) == Normalise(templateStep), message);
    }

    // ── Support ───────────────────────────────────────────────────────────────

    /// <summary>Reads 0.25, 0.25f and 0.25d as the same number; null for an expression.</summary>
    private static double? Number(string raw) =>
        double.TryParse(raw.TrimEnd('d', 'D', 'f', 'F', 'm', 'M'),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>An expression stripped of Razor's "@", its brackets and its spaces.</summary>
    private static string Normalise(string raw) =>
        new(raw.Where(c => !char.IsWhiteSpace(c) && c is not ('@' or '(' or ')')).ToArray());

    /// <summary>The third argument of a call, respecting brackets inside the arguments.</summary>
    private static string? ThirdArgument(string args)
    {
        var depth = 0;
        var parts = new List<string>();
        var start = 0;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is '(') depth++;
            else if (args[i] is ')') depth--;
            else if (args[i] is ',' && depth == 0)
            {
                parts.Add(args[start..i]);
                start = i + 1;
            }
        }
        parts.Add(args[start..]);

        return parts.Count >= 3 ? parts[2].Trim() : null;
    }

    private static IEnumerable<string> RazorFiles()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var components = Path.Combine(dir.FullName, "Ben.Video.Editor", "Components");
            if (Directory.Exists(components))
                return Directory.EnumerateFiles(components, "*.razor", SearchOption.AllDirectories);
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Ben.Video.Editor/Components.");
    }
}
