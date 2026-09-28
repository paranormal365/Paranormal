using Ben.Web.Tests.Support;
using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// A <c>BenListState</c> must know when its result has not arrived yet, or it says "nothing here" while it is still
/// loading.
/// </summary>
/// <remarks>
/// <para><b>What went wrong.</b> A <c>LoadResult&lt;T&gt;</c> nobody has fetched is <c>default</c>, and <c>default</c>
/// is not failed and has no items — so its <c>IsEmpty</c> is true. <c>BenListState</c> only shows its spinner when the
/// caller passes <c>Loading=</c>; without it, a page that renders the component before its fetch returns shows its
/// EmptyText for as long as sign-in and the fetch take. <c>/admin/users</c> told a SuperAdmin "No user accounts yet"
/// that way (found 2026-09-13), and a sweep of the other 76 call sites found the same on the feed attributions page
/// and the upload files page. That is item 120's rule broken from the other side: an empty list and a list not loaded
/// yet are different facts.</para>
///
/// <para><b>The rule.</b> Every <c>&lt;BenListState&gt;</c> either passes <c>Loading=</c>, or sits somewhere its
/// not-yet-fetched result cannot reach:</para>
/// <list type="bullet">
/// <item>inside a branch whose own condition is the result's <c>.Failed</c> — a default result is not failed, so it
/// never renders there (most call sites are this shape: the component draws only the failure card);</item>
/// <item>after an earlier branch of the same <c>if</c>/<c>else</c> chain that tests loading or a still-null list
/// (<c>@if (_loading) { spinner } else { … }</c>), at any level of nesting;</item>
/// <item>after an earlier <c>@if (_loading) { …; return; }</c>.</item>
/// </list>
/// <para>This reads the markup rather than rendering it, so it can only see guards written as control flow. A page
/// guarded some other way should pass <c>Loading=</c> anyway — it is one attribute, and it says so where the next
/// reader will look.</para>
/// </remarks>
public sealed class BenListStateLoadingGuardTests
{
    private static readonly Regex Comment          = new(@"@\*.*?\*@", RegexOptions.Singleline);
    private static readonly Regex Tag              = new(@"<BenListState\b(?:[^>""']|""[^""]*""|'[^']*')*>");
    private static readonly Regex LoadingAttribute = new(@"\sLoading\s*=");
    private static readonly Regex MentionsLoading  = new(@"load", RegexOptions.IgnoreCase);
    private static readonly Regex NullCheck        = new(@"\bis\s+null\b|==\s*null\b");
    private static readonly Regex Failed           = new(@"\.Failed\b");
    private static readonly Regex NegatedFailed    = new(@"!\s*\(?\s*[\w.?]+\.Failed\b");
    private static readonly Regex Keyword          = new(@"(?<![\w.])(?<kw>else\s+if|if|foreach|for|while|switch|using|lock)\s*$");
    private static readonly Regex Else             = new(@"(?<![\w.])else\s*$");
    private static readonly Regex EndsInReturn     = new(@"\breturn\s*;\s*$");
    private const int Window = 40;

    [Fact]
    public void Every_BenListState_is_told_when_its_result_has_not_arrived()
    {
        var files = RepoFiles.Paths("*.razor");
        Assert.True(files.Length > 100, $"only {files.Length} .razor files were found — a guard that reads nothing proves nothing");

        var tags      = 0;
        var offenders = new List<string>();
        foreach (var path in files)
        {
            var source = StripComments(File.ReadAllText(path));
            foreach (Match tag in Tag.Matches(source))
            {
                tags++;
                if (IsSafe(source, tag))
                    continue;

                var line = source.AsSpan(0, tag.Index).Count('\n') + 1;
                offenders.Add($"{Path.GetFileName(path)}:{line}");
            }
        }

        // Every call site of the component lives in these files; a tag pattern that silently matched none of them
        // would pass this for ever.
        Assert.True(tags > 50, $"only {tags} <BenListState> tags were found — the pattern is no longer reading them");

        Assert.True(offenders.Count == 0,
            "these BenListStates say their EmptyText before the fetch returns — a LoadResult nobody has fetched is empty. "
          + "Pass Loading=\"@_loading\" (true until the fetch completes), or render the component only inside an "
          + "explicit loading guard:\n  " + string.Join("\n  ", offenders));
    }

    [Theory]
    [InlineData("<BenListState Result=\"@_r\" />")]
    [InlineData("<div>@if (_r.Failed) { <p>x</p> }</div> <BenListState Result=\"@_r\" />")]
    [InlineData("@if (_r.Failed) { <p>x</p> } else { <BenListState Result=\"@_r\" /> }")]
    [InlineData("@if (!_r.Failed) { <BenListState Result=\"@_r\" /> }")]
    [InlineData("@if (_r.Failed || _items.Count == 0) { <BenListState Result=\"@_r\" /> }")]
    [InlineData("@if (_items is null) { <BenListState Result=\"@_r\" /> }")]
    [InlineData("@if (_loading) { <p>spinner</p> } <BenListState Result=\"@_r\" />")]
    [InlineData("@if (_showForm) { <p>form</p> } else { <BenListState Result=\"@_r\" /> }")]
    [InlineData("@foreach (var x in _xs) { <BenListState Result=\"@_r\" /> }")]
    public void Unguarded_shapes_are_caught(string markup)
    {
        var source = StripComments(markup);
        var tag    = Tag.Match(source);

        Assert.True(tag.Success);
        Assert.False(IsSafe(source, tag), markup);
    }

    [Theory]
    [InlineData("<BenListState Result=\"@_r\" Loading=\"@_loading\" />")]
    [InlineData("<BenListState TItem=\"Row\"\n    Result=\"@_r\"\n    Loading=\"@_loading\" OnRetry=\"@LoadAsync\">")]
    [InlineData("@if (_r.Failed) { <BenListState Result=\"@_r\" /> } else if (_rows.Count == 0) { <p>none</p> }")]
    [InlineData("@if (_loading) { <p>spinner</p> } else if (_r.Failed) { <BenListState Result=\"@_r\" /> }")]
    [InlineData("@if (_loading) { <p>spinner</p> } else { <div>@if (_editing) { <p>e</p> } else { <BenListState Result=\"@_r\" /> }</div> }")]
    [InlineData("@if (_rows is null) { <p>spinner</p> } else { <BenListState Result=\"@_r\" /> }")]
    [InlineData("@if (_loading) { <p>spinner</p> return; } <div><BenListState Result=\"@_r\" /></div>")]
    [InlineData("@if (_a.Failed || _b.Failed) { <BenListState Result=\"@(_a.Failed ? _a : _b)\" /> }")]
    [InlineData("@if (_r.Failed && !_loading) { <BenListState Result=\"@_r\" /> }")]
    [InlineData("@* <BenListState Result=\"@_r\" /> *@")]
    public void Guarded_shapes_pass(string markup)
    {
        var source = StripComments(markup);
        var tag    = Tag.Match(source);

        Assert.True(!tag.Success || IsSafe(source, tag), markup);
    }

    private static string StripComments(string source)
        => Comment.Replace(source, m => Regex.Replace(m.Value, @"[^\n]", " "));

    /// <summary>True when the tag passes Loading=, or its unfetched result cannot reach it.</summary>
    private static bool IsSafe(string source, Match tag)
    {
        if (LoadingAttribute.IsMatch(tag.Value))
            return true;

        var nearestBranch = true;
        var i = tag.Index - 1;
        while (i >= 0)
        {
            switch (source[i])
            {
                case '}':
                {
                    // A block that closed before the tag: only an early-returning loading branch guards what follows.
                    var open = MatchBackward(source, i, '{', '}');
                    if (open < 0) return false;

                    var (kind, condition, start) = HeaderOf(source, open);
                    if (kind == "if" && condition is not null && MentionsLoading.IsMatch(condition)
                        && EndsInReturn.IsMatch(source[(open + 1)..i]))
                        return true;

                    i = start - 1;
                    break;
                }
                case '{':
                {
                    // A block the tag sits inside.
                    var (kind, condition, start) = HeaderOf(source, i);
                    if (kind is "if" or "else if" or "else")
                    {
                        if (nearestBranch && IsFailedOnly(condition))
                            return true;
                        nearestBranch = false;

                        if (condition is not null && kind != "else" && MentionsLoading.IsMatch(condition))
                            return true;

                        if (EarlierConditions(source, kind, start).Any(c => MentionsLoading.IsMatch(c) || NullCheck.IsMatch(c)))
                            return true;
                    }

                    i = start - 1;
                    break;
                }
                default:
                    i--;
                    break;
            }
        }

        return false;
    }

    /// <summary>True when every <c>||</c> operand of the condition is some result's <c>.Failed</c>.</summary>
    private static bool IsFailedOnly(string? condition)
        => condition is not null
        && condition.Split("||").All(operand => Failed.IsMatch(operand) && !NegatedFailed.IsMatch(operand));

    /// <summary>The conditions of the branches before this one in its if/else chain.</summary>
    private static IEnumerable<string> EarlierConditions(string source, string kind, int start)
    {
        while (kind is "else" or "else if")
        {
            var j = SkipWhitespaceBackward(source, start - 1);
            if (j < 0 || source[j] != '}') yield break;

            var open = MatchBackward(source, j, '{', '}');
            if (open < 0) yield break;

            (kind, var condition, start) = HeaderOf(source, open);
            if (condition is not null)
                yield return condition;
        }
    }

    /// <summary>What introduces the block opening at <paramref name="open"/>: its keyword, condition and start.</summary>
    private static (string Kind, string? Condition, int Start) HeaderOf(string source, int open)
    {
        var j = SkipWhitespaceBackward(source, open - 1);
        if (j < 0)
            return ("block", null, open);

        if (source[j] == ')')
        {
            var paren = MatchBackward(source, j, '(', ')');
            if (paren < 0)
                return ("block", null, open);

            // A short window before the parenthesis, not the whole prefix: this runs for every block the walk passes,
            // and an end-anchored match over a 100 KB page each time makes the scan quadratic.
            var condition = source[(paren + 1)..j];
            var from      = Math.Max(0, paren - Window);
            var keyword   = Keyword.Match(source[from..paren]);
            if (!keyword.Success)
                return ("expr", condition, paren);

            var kind = keyword.Groups["kw"].Value.StartsWith("else") ? "else if" : keyword.Groups["kw"].Value;
            return (kind, condition, from + keyword.Groups["kw"].Index);
        }

        var elseFrom = Math.Max(0, j + 1 - Window);
        var @else    = Else.Match(source[elseFrom..(j + 1)]);
        return @else.Success ? ("else", null, elseFrom + @else.Index) : ("block", null, open);
    }

    private static int MatchBackward(string source, int close, char openChar, char closeChar)
    {
        var depth = 0;
        for (var i = close; i >= 0; i--)
        {
            if (source[i] == closeChar) depth++;
            else if (source[i] == openChar && --depth == 0) return i;
        }
        return -1;
    }

    private static int SkipWhitespaceBackward(string source, int i)
    {
        while (i >= 0 && char.IsWhiteSpace(source[i])) i--;
        return i;
    }
}
