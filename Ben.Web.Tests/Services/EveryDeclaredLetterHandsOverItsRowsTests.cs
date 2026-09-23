using System.Text.RegularExpressions;
using Ben.Web.Tests.Support;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A letter that says what kind it is also hands a template the rows that kind promises.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> A kind declares the tables its template may read, and the editor offers
/// their columns and accepts a template that uses them. But nineteen letters named a kind and handed
/// over nothing, and a table token with no row renders as an empty string. Two such templates were
/// PUBLISHED on production — an organizer greeted by nobody about nothing, a guest told their event
/// was removed with the reason left out. Found 2026-09-23, before either had been sent.</para>
///
/// <para><b>What it checks.</b> Every <c>new EmailMessage(…)</c> in <c>Ben.Data.WebApi</c> that
/// passes <c>Kind:</c> also passes <c>Payload:</c>. A letter with no declared kind is left alone:
/// it cannot have a template, so there is nothing to render blank.</para>
///
/// <para>It cannot check that the RIGHT rows are handed over — that is what the per-letter tests
/// are for. It stops the shape that let all nineteen through: naming a kind and passing nothing.</para>
/// </remarks>
public sealed class EveryDeclaredLetterHandsOverItsRowsTests
{
    private static readonly Regex NewLetter = new(@"new\s+(?:[\w.]+\.)?EmailMessage\s*\(");

    [Fact]
    public void Every_letter_that_names_its_kind_hands_over_its_rows()
    {
        var root = RepoFiles.Root().FullName;
        var api = Path.Combine(root, "Ben.Data.WebApi") + Path.DirectorySeparatorChar;
        var offenders = new List<string>();

        foreach (var path in RepoFiles.Paths("*.cs").Where(p => p.StartsWith(api, StringComparison.Ordinal)))
        {
            var source = NoCredentialsInLogsTests.WithoutComments(File.ReadAllText(path));

            foreach (Match m in NewLetter.Matches(source))
            {
                var args = Arguments(source, m.Index + m.Length);
                if (args is null) continue;
                if (!Regex.IsMatch(args, @"\bKind\s*:")) continue;
                if (Regex.IsMatch(args, @"\bPayload\s*:")) continue;

                var line = 1 + source.AsSpan(0, m.Index).Count('\n');
                offenders.Add($"{Path.GetRelativePath(root, path)}:{line}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These letters name a kind but hand a template nothing, so every table token in a "
          + "template for them renders blank. Pass Payload: MailRows.For(kind, …the entities the "
          + "letter is about…):\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>The argument text up to the matching ')', string literals respected.</summary>
    private static string? Arguments(string s, int from)
    {
        var depth = 1;
        for (var i = from; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '"')
            {
                // Skip a string literal, escapes and all; good enough for arguments that are
                // mostly identifiers and short literals.
                for (i++; i < s.Length && s[i] != '"'; i++) if (s[i] == '\\') i++;
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return s[from..i];
        }
        return null;
    }
}
