using Ben.Data.Common.Helpers;
using System.Text;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// Tests for <see cref="SvgSanitizer"/> — verifies that JavaScript vectors are
/// stripped while valid SVG content is preserved.
/// </summary>
public class SvgSanitizerTests
{
    private static string Sanitize(string svg)
    {
        var bytes  = Encoding.UTF8.GetBytes(svg);
        var result = SvgSanitizer.Sanitize(bytes);
        return Encoding.UTF8.GetString(result);
    }

    // ── Script elements ───────────────────────────────────────────────────────

    [Fact]
    public void Sanitize_RemovesScriptElement()
    {
        var svg  = "<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script><circle r='5'/></svg>";
        var out_ = Sanitize(svg);

        Assert.DoesNotContain("<script", out_, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", out_);
    }

    [Fact]
    public void Sanitize_RemovesNestedScriptElement()
    {
        var svg  = "<svg xmlns='http://www.w3.org/2000/svg'><g><script>evil()</script></g></svg>";
        var out_ = Sanitize(svg);

        Assert.DoesNotContain("script", out_, StringComparison.OrdinalIgnoreCase);
    }

    // ── Event-handler attributes ──────────────────────────────────────────────

    [Fact]
    public void Sanitize_RemovesOnloadAttribute()
    {
        var svg  = "<svg xmlns='http://www.w3.org/2000/svg' onload=\"alert(1)\"><rect/></svg>";
        var out_ = Sanitize(svg);

        Assert.DoesNotContain("onload", out_, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", out_);
    }

    [Fact]
    public void Sanitize_RemovesOnclickOnChildElement()
    {
        var svg  = "<svg xmlns='http://www.w3.org/2000/svg'><circle onclick='evil()' r='10'/></svg>";
        var out_ = Sanitize(svg);

        Assert.DoesNotContain("onclick", out_, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_RemovesOnerrorAttribute()
    {
        var svg  = "<svg xmlns='http://www.w3.org/2000/svg'><image href='x' onerror='evil()'/></svg>";
        var out_ = Sanitize(svg);

        Assert.DoesNotContain("onerror", out_, StringComparison.OrdinalIgnoreCase);
    }

    // ── JavaScript href values ────────────────────────────────────────────────

    [Fact]
    public void Sanitize_RemovesJavascriptHref()
    {
        var svg  = "<svg xmlns='http://www.w3.org/2000/svg'><a href='javascript:alert(1)'><text>x</text></a></svg>";
        var out_ = Sanitize(svg);

        Assert.DoesNotContain("javascript:", out_, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_RemovesVbscriptHref()
    {
        var svg  = "<svg xmlns='http://www.w3.org/2000/svg'><a href='vbscript:msgbox(1)'><text>x</text></a></svg>";
        var out_ = Sanitize(svg);

        Assert.DoesNotContain("vbscript:", out_, StringComparison.OrdinalIgnoreCase);
    }

    // ── Blocked elements ──────────────────────────────────────────────────────

    [Fact]
    public void Sanitize_RemovesEmbedElement()
    {
        var svg  = "<svg xmlns='http://www.w3.org/2000/svg'><embed src='evil.swf'/></svg>";
        var out_ = Sanitize(svg);

        Assert.DoesNotContain("embed", out_, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_RemovesIframeElement()
    {
        var svg  = "<svg xmlns='http://www.w3.org/2000/svg'><iframe src='//evil.com'/></svg>";
        var out_ = Sanitize(svg);

        Assert.DoesNotContain("iframe", out_, StringComparison.OrdinalIgnoreCase);
    }

    // ── Valid content preservation ────────────────────────────────────────────

    [Fact]
    public void Sanitize_PreservesCircleElement()
    {
        var svg  = "<svg xmlns='http://www.w3.org/2000/svg'><circle cx='50' cy='50' r='40' fill='red'/></svg>";
        var out_ = Sanitize(svg);

        Assert.Contains("<circle", out_);
        Assert.Contains("fill=\"red\"", out_);
    }

    [Fact]
    public void Sanitize_PreservesTextContent()
    {
        var svg  = "<svg xmlns='http://www.w3.org/2000/svg'><text x='10' y='20'>Hello</text></svg>";
        var out_ = Sanitize(svg);

        Assert.Contains(">Hello<", out_);
    }

    [Fact]
    public void Sanitize_PreservesNonJavascriptHref()
    {
        var svg  = "<svg xmlns='http://www.w3.org/2000/svg'><a href='https://example.com'><text>x</text></a></svg>";
        var out_ = Sanitize(svg);

        Assert.Contains("https://example.com", out_);
    }

    [Fact]
    public void Sanitize_PreservesStyles()
    {
        var svg  = "<svg xmlns='http://www.w3.org/2000/svg'><style>.cls{fill:blue}</style><rect class='cls'/></svg>";
        var out_ = Sanitize(svg);

        // <style> is not blocked — CSS is safe (no JS execution)
        Assert.Contains("fill:blue", out_);
    }

    // ── Invalid SVG ───────────────────────────────────────────────────────────

    [Fact]
    public void Sanitize_ThrowsForMalformedXml()
    {
        var bytes = Encoding.UTF8.GetBytes("<svg><unclosed>");

        Assert.Throws<InvalidOperationException>(() => SvgSanitizer.Sanitize(bytes));
    }

    [Fact]
    public void Sanitize_ThrowsForNonXmlContent()
    {
        var bytes = Encoding.UTF8.GetBytes("This is not SVG");

        // Not well-formed XML → exception
        Assert.Throws<InvalidOperationException>(() => SvgSanitizer.Sanitize(bytes));
    }

    [Fact]
    public void Sanitize_ReturnsUtf8Bytes()
    {
        var svg   = "<svg xmlns='http://www.w3.org/2000/svg'><circle r='5'/></svg>";
        var bytes = Encoding.UTF8.GetBytes(svg);

        var result = SvgSanitizer.Sanitize(bytes);

        Assert.NotEmpty(result);
        // Result must be valid UTF-8
        var decoded = Encoding.UTF8.GetString(result);
        Assert.Contains("<circle", decoded);
    }
}
