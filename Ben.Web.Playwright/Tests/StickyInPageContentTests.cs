using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// <c>position: sticky</c> works inside a page, and a page still cannot scroll sideways.
/// </summary>
/// <remarks>
/// <para>The layout scrolls inside <c>main.app-body</c>. The template gave <c>.content-wrapper</c>,
/// which sits between it and every page, <c>overflow-x: hidden</c> — and <c>hidden</c> on one axis
/// turns the other into <c>auto</c>, so the wrapper was a scroll container that never scrolled.
/// Every sticky element in a page stuck to it, which is to say never: the event page's index, the
/// seat picker's summary bar, the phone checkout bar and the case editor's Save all ran into it,
/// and three were built around it. Fixed 10/13/2026 with <c>overflow-x: clip</c> (signal.css),
/// which cuts off the same overflow without making a scroll container.</para>
///
/// <para>The two tests pull against each other on purpose. Dropping the clip altogether also makes
/// sticky work, and brings back the sideways scroll the template was preventing — and that scroll
/// lands on <c>.app-body</c>, not the document, so a check of <c>document.scrollWidth</c> alone
/// would miss it. Both use a probe rather than a real page, so neither depends on seeded data.</para>
/// </remarks>
[TestFixture]
[Category("Guard")]
public sealed class StickyInPageContentTests : BenTestBase
{
    [Test]
    public async Task A_sticky_element_in_a_page_stays_in_view_while_the_page_scrolls()
    {
        await Page.SetViewportSizeAsync(1440, 900);
        await Page.GotoAsync($"{BaseUrl}/");
        await WaitUntilLoadedAsync();

        var r = await Page.EvaluateAsync<double[]>(@"() => {
            const host = document.querySelector('.content-wrapper');
            const body = document.querySelector('main.app-body');
            const wrap = document.createElement('div');
            wrap.innerHTML = '<div style=""position:sticky;top:0;height:40px""></div><div style=""height:4000px""></div>';
            host.prepend(wrap);
            body.scrollTop = 1500;
            const result = [body.scrollTop, wrap.firstChild.getBoundingClientRect().top, body.getBoundingClientRect().top];
            wrap.remove();
            body.scrollTop = 0;
            return result;
        }");

        var (scrolled, pinTop, bodyTop) = (r[0], r[1], r[2]);
        Assert.That(scrolled, Is.GreaterThan(1000), "the probe could not scroll main.app-body, so it proves nothing");
        Assert.That(pinTop, Is.EqualTo(bodyTop).Within(2),
            $"a sticky element in .content-wrapper should pin to the top of main.app-body ({bodyTop}px) and is at "
          + $"{pinTop}px — something between them has become a scroll container again (overflow hidden/auto)");
    }

    [Test]
    public async Task Content_wider_than_a_phone_does_not_scroll_the_page_sideways()
    {
        await Page.SetViewportSizeAsync(375, 812);
        await Page.GotoAsync($"{BaseUrl}/");
        await WaitUntilLoadedAsync();

        var r = await Page.EvaluateAsync<int[]>(@"() => {
            const host = document.querySelector('.content-wrapper');
            const body = document.querySelector('main.app-body');
            const wide = document.createElement('div');
            wide.style.cssText = 'width:2000px;height:10px';
            host.appendChild(wide);
            const result = [document.documentElement.scrollWidth - window.innerWidth, body.scrollWidth - body.clientWidth];
            wide.remove();
            return result;
        }");

        Assert.That(r[0], Is.LessThanOrEqualTo(2), $"the document scrolls sideways by {r[0]}px");
        Assert.That(r[1], Is.LessThanOrEqualTo(2), $"main.app-body scrolls sideways by {r[1]}px");
    }
}
