using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Capture;

/// <summary>
/// Sets the feed up for the App Store pictures: the test accounts' leftovers hidden, four posts a
/// visitor would recognise as the product written in their place.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-10-02: new App Store pictures for the new look. Taken against the test database
/// as it stood, the first frame was a feed of "o60d30c714c7 my own post" — what every automated run
/// leaves behind. A freshly seeded database has no posts at all, which is no better.</para>
///
/// <para><b>Hidden, not deleted</b>, through the same SuperAdmin page that exists for exactly this
/// (<c>/admin/feed/test-posts</c>), so every post can be put back from there. Only posts by the
/// seeded accounts are touched — that page refuses anybody else's.</para>
///
/// <para>Explicit: it changes the database it runs against. Run it on a test database only:
/// <c>scripts/run-e2e.sh --keep --filter "TestCategory=StoreShotsArrange"</c>.</para>
/// </remarks>
[TestFixture]
[Category("StoreShotsArrange")]
[Explicit("Changes the feed of the database it runs against; run deliberately, on a test database.")]
[NonParallelizable]
public sealed class StoreShotsArrange : BenTestBase
{
    private static readonly (Func<string> Email, Func<string> Password, string Body)[] Posts =
    [
        (() => ClientEmail, () => ClientPassword,
            "Our first night with the team at the house. We still don't know what we heard on the landing, "
            + "but it's on the recording now and they're going through it. Glad we finally asked someone."),
        (() => MemberEmail, () => MemberPassword,
            "Clear #EVP in the upstairs hall around 2am — three separate responses to direct questions. "
            + "Full audio going up tomorrow. @sarahmitchell was on the recorder."),
        (() => UserEmail, () => UserPassword,
            "Two of us felt the cold spot on the lower stair, about twenty minutes apart. The magnetometer "
            + "moved both times and nothing else in the house did. #coldspot"),
        (() => UserEmail, () => UserPassword,
            "Printers Alley Ghost Walk runs Friday at 8. Meet by the alley sign — bring your phone, the "
            + "walk records with you and you keep everything you capture. #ghostwalk"),
    ];

    [Test]
    public async Task Hide_the_test_posts_and_write_the_store_feed()
    {
        var admin = await ApiAsAsync(SuperAdminEmail, SuperAdminPassword);

        var list = await admin.GetAsync("/api/admin/feed/test-posts");
        Assert.That(list.Ok, Is.True, $"test-posts list: {list.Status}");
        var ids = (await list.JsonAsync())!.Value.EnumerateArray()
            .Where(p => !p.GetProperty("hidden").GetBoolean() && !p.GetProperty("isReply").GetBoolean())
            .Select(p => p.GetProperty("id").GetGuid())
            .ToList();
        if (ids.Count > 0)
        {
            var hidden = await admin.PostAsync("/api/admin/feed/test-posts/hide",
                new() { DataObject = new { ids } });
            Assert.That(hidden.Ok, Is.True, $"hide: {hidden.Status} {await hidden.TextAsync()}");
        }

        // The SuperAdmin's own leftovers ("posted from elsewhere #ta…") are not a seeded account's,
        // so the page above will not touch them. Reported by the member and hidden from the report,
        // as HelpMediaCapture clears the feed — except the session cards, which are the product.
        var member = await ApiAsAsync(MemberEmail, MemberPassword);
        var page = await admin.GetAsync("/api/feed?mode=all");
        foreach (var post in (await page.JsonAsync())!.Value.GetProperty("posts").EnumerateArray())
        {
            var body = post.GetProperty("body").GetString() ?? "";
            if (post.GetProperty("authorDisplayName").GetString() != "AverageBen" || body.Contains("is starting now")) continue;
            var id = post.GetProperty("id").GetString();
            await member.PostAsync($"/api/feed/posts/{id}/report", new() { DataObject = new { reason = "clearing the feed for the App Store pictures" } });
            var queue = await admin.GetAsync("/api/admin/feed/reports");
            foreach (var report in (await queue.JsonAsync())!.Value.EnumerateArray())
            {
                if (report.GetProperty("orgMessageId").GetString() != id) continue;
                await admin.PostAsync($"/api/admin/feed/reports/{report.GetProperty("id").GetString()}/resolve",
                    new() { DataObject = new { outcome = 2 } });   // Hidden
                break;
            }
        }

        // Oldest first, so the feed reads newest-on-top in the order above, reversed.
        foreach (var (email, password, body) in Posts.Reverse())
        {
            var api = await ApiAsAsync(email(), password());
            var form = Context.APIRequest.CreateFormData();
            form.Set("body", body);
            var made = await api.PostAsync("/api/feed/posts", new() { Multipart = form });
            Assert.That(made.Ok, Is.True, $"post as {email()}: {made.Status} {await made.TextAsync()}");
            await Task.Delay(1100);   // distinct times, so "newest first" is the order intended
        }

        var feed = await (await ApiAsAsync(UserEmail, UserPassword)).GetAsync("/api/feed?mode=all");
        var bodies = (await feed.JsonAsync())!.Value.GetProperty("posts").EnumerateArray()
            .Take(Posts.Length).Select(p => p.GetProperty("body").GetString()).ToList();
        Assert.That(bodies, Is.EquivalentTo(Posts.Select(p => p.Body)), "the feed does not open on the store posts");
    }

    private async Task<IAPIRequestContext> ApiAsAsync(string email, string password)
    {
        await using var anon = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await anon.PostAsync("/login", new()
        {
            DataObject = new Dictionary<string, object> { ["email"] = email, ["password"] = password },
        });
        Assert.That(login.Ok, Is.True, $"API sign-in failed for {email}: {login.Status}");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString()!;
        return await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
    }
}
