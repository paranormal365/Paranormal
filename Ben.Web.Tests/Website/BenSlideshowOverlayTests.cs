using Ben.Web.Website.Library.Kit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>The slideshow draws a caller's overlay over each slide in place of its caption strip (storefront S2.4).</summary>
public sealed class BenSlideshowOverlayTests
{
    private static async Task<string> RenderAsync(RenderFragment<BenSlide>? overlay)
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<BenSlideshow>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(BenSlideshow.Slides)] = new[] { new BenSlide("/x.jpg", Caption: "The caption", LinkUrl: "/store/c/emf-meters", Title: "EMF Meters") },
                [nameof(BenSlideshow.Overlay)] = overlay,
            }));
            return output.ToHtmlString();
        });
    }

    [Fact]
    public async Task An_overlay_is_drawn_over_the_slide_instead_of_the_caption_strip()
    {
        var html = await RenderAsync(slide => b => b.AddMarkupContent(0, $"<h2 class=\"hero-title\">{slide.Title}</h2>"));

        Assert.Contains("ben-slideshow__overlay", html);
        Assert.Contains("<h2 class=\"hero-title\">EMF Meters</h2>", html);
        Assert.DoesNotContain("ben-slideshow__caption", html);
    }

    [Fact]
    public async Task Without_an_overlay_the_caption_strip_is_drawn_as_before()
    {
        var html = await RenderAsync(null);
        Assert.Contains("ben-slideshow__caption", html);
        Assert.DoesNotContain("ben-slideshow__overlay", html);
    }
}
