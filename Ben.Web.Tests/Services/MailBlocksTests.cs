using Ben.Data.Common.Mail;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The pieces a letter is built from, and the rules email HTML has to obey (item 246).
/// </summary>
/// <remarks>
/// These assert the constraints rather than the markup. Asserting the exact HTML would fail every
/// time somebody adjusted a colour; asserting "no stylesheet, no flexbox, styles on the element"
/// fails only when a block stops being email-safe — which is the thing that cannot be seen in a
/// preview, because a browser renders all of it happily and Outlook does not.
/// </remarks>
public sealed class MailBlocksTests
{
    public static TheoryData<string> EveryBlock()
    {
        var data = new TheoryData<string>();
        foreach (var b in MailBlocks.All) data.Add(b.Key);
        return data;
    }

    private static string Html(string key) => MailBlocks.Find(key)!.Html;

    [Theory]
    [MemberData(nameof(EveryBlock))]
    public void No_block_relies_on_a_stylesheet(string key)
    {
        var html = Html(key);

        // Gmail strips <style> blocks in enough contexts that a block depending on one renders
        // unstyled for a large share of readers — and looks perfect in the preview.
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(EveryBlock))]
    public void No_block_uses_layout_Outlook_ignores(string key)
    {
        var html = Html(key);

        // Word's engine draws Outlook mail. None of these do anything there.
        Assert.DoesNotContain("display:flex", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("display:grid", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("float:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("position:absolute", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(EveryBlock))]
    public void Every_block_styles_the_element_itself(string key)
        => Assert.Contains("style=", Html(key), StringComparison.OrdinalIgnoreCase);

    [Theory]
    [MemberData(nameof(EveryBlock))]
    public void Every_block_is_balanced(string key)
    {
        var html = Html(key);
        Assert.Equal(Count(html, "<table"), Count(html, "</table>"));
        Assert.Equal(Count(html, "<tr"), Count(html, "</tr>"));
        Assert.Equal(Count(html, "<td"), Count(html, "</td>"));
        Assert.Equal(Count(html, "<div"), Count(html, "</div>"));

        static int Count(string s, string needle)
        {
            int n = 0, i = 0;
            while ((i = s.IndexOf(needle, i, StringComparison.OrdinalIgnoreCase)) >= 0) { n++; i += needle.Length; }
            return n;
        }
    }

    /// <summary>
    /// The layout Ben asked for by name, and the reason it is a block at all.
    /// </summary>
    [Fact]
    public void Two_columns_are_a_table_row_and_the_side_is_a_choice()
    {
        var right = MailBlocks.TwoColumns(logoOnTheRight: true);
        var left = MailBlocks.TwoColumns(logoOnTheRight: false);

        Assert.Contains("<table", right, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(right, left);

        // The narrow cell is first when the logo goes on the left, and last when it goes right.
        Assert.True(left.IndexOf("width=\"80\"", StringComparison.Ordinal)
                  < left.IndexOf("{AppUsers.DisplayName}", StringComparison.Ordinal));
        Assert.True(right.IndexOf("width=\"80\"", StringComparison.Ordinal)
                  > right.IndexOf("{AppUsers.DisplayName}", StringComparison.Ordinal));
    }

    /// <summary>A button in an email is a link; a real button would do nothing.</summary>
    [Fact]
    public void The_button_is_a_link()
    {
        var html = MailBlocks.Button();
        Assert.Contains("<a href=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<button", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Blocks that carry tokens carry ones a letter could actually fill in.</summary>
    [Fact]
    public void The_tokens_a_block_ships_with_are_real_ones()
    {
        // The footer and the two-column block are the ones with tokens baked in.
        Assert.Empty(MailTokens.Unresolvable(MailBlocks.Footer(), MailKinds.ResetYourPassword));
        Assert.Empty(MailTokens.Unresolvable(
            MailBlocks.TwoColumns(logoOnTheRight: true), MailKinds.ResetYourPassword));
    }

    [Fact]
    public void Every_block_has_a_unique_key_and_says_what_it_is()
    {
        var keys = MailBlocks.All.Select(b => b.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(MailBlocks.All, b =>
        {
            Assert.False(string.IsNullOrWhiteSpace(b.Title));
            Assert.EndsWith(".", b.What);
            Assert.False(string.IsNullOrWhiteSpace(b.Html));
        });
    }
}
