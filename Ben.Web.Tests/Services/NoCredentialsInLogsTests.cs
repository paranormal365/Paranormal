using System.Text;
using System.Text.RegularExpressions;
using Ben.Web.Tests.Support;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// No log line in the API carries a token or a link built from one.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The links these endpoints mail are credentials. An <c>/attending/{token}</c>
/// link, used by anybody, confirms attendance in the addressee's name — asking for a place on a tour
/// date or a hosted weekend — makes an account for that address if there is none, and burns the
/// single-use link so the real person's own letter stops working. The account-handover link is
/// worse: it carries a password-reset token, and whoever holds it can set the password. Several of
/// these were written to the log whenever mail was not set up, so the only thing between a working
/// credential and anybody who could read the log was which log it was.</para>
///
/// <para><b>What it scans.</b> Every <c>Log*</c> call in <c>Ben.Data.WebApi</c> whose arguments
/// name a <c>{Token}</c> or <c>{Link}</c> placeholder — as a structured-logging template or inside an
/// interpolated string. Comments are removed first, by a scanner that leaves string literals
/// alone: the usual regex strips everything after <c>//</c>, which in a template reading
/// "https://..." removes the very placeholder this is looking for.</para>
///
/// <para><b>Allowances, if one is ever needed, are by message, not by file</b>, so a new token log
/// elsewhere in the same file still fails; and an allowance that no longer matches anything fails too
/// — otherwise a removed line would leave a permanent hole behind it. There are none.</para>
/// </remarks>
public sealed class NoCredentialsInLogsTests
{
    /// <summary>File, a distinctive part of the message, and why it may carry a link.</summary>
    /// <remarks>
    /// <para><b>Empty, and meant to stay so.</b> Two lines were allowed when this guard was written,
    /// because the browser suite followed the confirmation and seat-pick links out of the API's log.
    /// Those letters now queue whether or not mail is set up, and the tests read them from the
    /// outbox instead (BenTestBase.LinkFromTheOutboxAsync), so nothing here needs a link in a log.
    /// An entry added later needs its reason written out, and still fails once it stops matching.</para>
    /// </remarks>
    private static readonly (string File, string Message, string Why)[] Allowed = [];

    private static readonly Regex Credential = new(@"\{(Token|Link)\}", RegexOptions.IgnoreCase);

    private static readonly Regex LogCall = new(
        @"\.Log(?:Trace|Debug|Information|Warning|Error|Critical)?\s*\(");

    [Fact]
    public void No_log_line_in_the_api_carries_a_token_or_a_link()
    {
        var offenders = new List<string>();
        var used = new HashSet<int>();
        var root = RepoFiles.Root().FullName;
        var api = Path.Combine(root, "Ben.Data.WebApi") + Path.DirectorySeparatorChar;

        foreach (var path in RepoFiles.Paths("*.cs").Where(p => p.StartsWith(api, StringComparison.Ordinal)))
        {
            var name = Path.GetFileName(path);
            var source = WithoutComments(File.ReadAllText(path));

            foreach (var (args, line) in LogCalls(source))
            {
                if (!Credential.IsMatch(args)) continue;

                var allowance = Array.FindIndex(Allowed, a => a.File == name && args.Contains(a.Message, StringComparison.Ordinal));
                if (allowance >= 0) { used.Add(allowance); continue; }

                offenders.Add($"{Path.GetRelativePath(root, path)}:{line}  {Squash(args)}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These log a token, or a link built from one. Those links are credentials — used by "
          + "anybody, they act as the person they were mailed to — so the log must say what failed "
          + "and for which event or account, never the link itself:\n  "
          + string.Join("\n  ", offenders));

        var stale = Allowed.Where((_, i) => !used.Contains(i)).Select(a => $"{a.File}: \"{a.Message}\"").ToList();
        Assert.True(stale.Count == 0,
            "These allowances no longer match any log line. Remove them, so the gap they left "
          + "closes:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>
    /// The scanner's own claim: a URL inside a template survives, and a comment does not.
    /// </summary>
    /// <remarks>
    /// The comment-stripping regex the other guards use cuts from <c>//</c> to the end of the line
    /// wherever it appears, so it would turn the first line below into <c>_log.LogWarning("Go to
    /// https:</c> — and the <c>{Link}</c> this guard exists to find would be gone. A guard that
    /// passes because it cannot see is worse than none.
    /// </remarks>
    [Fact]
    public void A_url_in_a_template_survives_and_a_comment_does_not()
    {
        const string source = """
            _log.LogWarning("Go to https://example.test/{Link} now", link); // {Token} in a comment
            _log.LogInformation($"Sent {(ok ? "the \"link\"" : "nothing")} to {Link}");
            /* _log.LogWarning("{Token}", token); */
            """;

        var calls = LogCalls(WithoutComments(source)).Select(c => c.Args).ToList();

        Assert.Equal(2, calls.Count);
        Assert.Contains("https://example.test/{Link}", calls[0]);
        Assert.DoesNotContain("{Token}", string.Join("", calls));
        Assert.Contains("{Link}", calls[1]);
    }

    // ── the scanner ──────────────────────────────────────────────────────────

    /// <summary>Each Log call's argument text and the line it starts on.</summary>
    internal static IEnumerable<(string Args, int Line)> LogCalls(string source)
    {
        foreach (Match m in LogCall.Matches(source))
        {
            var close = MatchingParen(source, m.Index + m.Length);
            if (close < 0) continue;
            var line = 1 + source.AsSpan(0, m.Index).Count('\n');
            yield return (source[(m.Index + m.Length)..close], line);
        }
    }

    /// <summary>The index of the ')' closing the '(' just before <paramref name="from"/>.</summary>
    private static int MatchingParen(string s, int from)
    {
        var depth = 1;
        for (var i = from; i < s.Length; i++)
        {
            var end = EndOfLiteral(s, i);
            if (end > i) { i = end - 1; continue; }
            if (s[i] == '(') depth++;
            else if (s[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>
    /// Removes // and /* */ comments and nothing else: string and character literals pass through
    /// untouched, however many slashes they hold.
    /// </summary>
    internal static string WithoutComments(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var end = EndOfLiteral(s, i);
            if (end > i) { sb.Append(s, i, end - i); i = end - 1; continue; }

            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                if (i < s.Length) sb.Append('\n');
                continue;
            }
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop = close < 0 ? s.Length : close + 2;
                // Keep the newlines, so line numbers still point at the right place.
                sb.Append('\n', s.AsSpan(i, stop - i).Count('\n'));
                i = stop - 1;
                continue;
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// If a string or character literal starts at <paramref name="i"/>, the index just past it;
    /// otherwise <paramref name="i"/>. Handles "", @"", $"", $@"" / @$"", raw """ and '' literals,
    /// and the code inside an interpolation hole, which may hold literals of its own.
    /// </summary>
    internal static int EndOfLiteral(string s, int i)
    {
        var p = i;
        var interpolated = false;
        var verbatim = false;
        while (p < s.Length && (s[p] == '$' || s[p] == '@') && p - i < 3)
        {
            if (s[p] == '$') interpolated = true; else verbatim = true;
            p++;
        }
        if (p >= s.Length) return i;

        if (s[p] == '\'' && p == i)
        {
            var j = p + 1;
            while (j < s.Length && s[j] != '\'') j += s[j] == '\\' ? 2 : 1;
            return Math.Min(j + 1, s.Length);
        }
        if (s[p] != '"') return i;
        if (p > i && !interpolated && !verbatim) return i;

        // Raw string literal: three or more quotes, closed by the same run.
        if (p + 2 < s.Length && s[p + 1] == '"' && s[p + 2] == '"')
        {
            var run = 0;
            while (p + run < s.Length && s[p + run] == '"') run++;
            var fence = new string('"', run);
            var close = s.IndexOf(fence, p + run, StringComparison.Ordinal);
            return close < 0 ? s.Length : close + run;
        }

        for (var j = p + 1; j < s.Length; j++)
        {
            var c = s[j];
            if (verbatim && c == '"' && j + 1 < s.Length && s[j + 1] == '"') { j++; continue; }
            if (!verbatim && c == '\\') { j++; continue; }
            if (c == '"') return j + 1;
            if (interpolated && c == '{')
            {
                if (j + 1 < s.Length && s[j + 1] == '{') { j++; continue; }
                // Code until the matching '}', skipping any literals inside it.
                var depth = 1;
                for (j++; j < s.Length && depth > 0; j++)
                {
                    var inner = EndOfLiteral(s, j);
                    if (inner > j) { j = inner - 1; continue; }
                    if (s[j] == '{') depth++;
                    else if (s[j] == '}') depth--;
                }
                j--;
            }
        }
        return s.Length;
    }

    private static string Squash(string args)
    {
        var flat = Regex.Replace(args, @"\s+", " ").Trim();
        return flat.Length > 160 ? flat[..160] + "…" : flat;
    }
}
