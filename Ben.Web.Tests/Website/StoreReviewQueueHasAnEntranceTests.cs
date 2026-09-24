using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The store's review queue can be found and worked from the site (storefront S1.11).
/// </summary>
/// <remarks>
/// Buyers' reviews wait for a person before anybody sees them. A queue with no menu entry is a
/// queue nobody opens, and every review in it waits for ever — the moderation-queue failure
/// <see cref="ModerationQueuesHaveAnEntranceTests"/> was written after. S6.2 extends this with the
/// buyer's side (the route that puts a review in the queue).
/// </remarks>
public sealed class StoreReviewQueueHasAnEntranceTests
{
    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
    }

    private static string WithoutComments(string source)
    {
        source = Regex.Replace(source, @"@\*.*?\*@", " ", RegexOptions.Singleline);
        source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(source, @"//[^\n]*", " ");
    }

    [Fact]
    public void The_administration_menu_opens_the_review_queue()
    {
        var nav = WithoutComments(Read("Ben.Web.Website", "Components", "Layout", "BenNav.razor"));
        Assert.Matches(@"new NavItem\(""Reviews"",\s*""message-square"",\s*""/admin/store/reviews""\)", nav);
    }

    [Fact]
    public void The_queue_page_exists_and_can_approve_refuse_and_reply()
    {
        var page = Read("Ben.Web.Website.Library", "SuperAdmin", "Store", "AdminStoreReviews.razor");
        Assert.Contains("@page \"/admin/store/reviews\"", page);
        foreach (var call in new[] { "GetStoreReviewsAsync", "ApproveStoreReviewAsync", "RejectStoreReviewAsync", "ReplyToStoreReviewAsync" })
            Assert.Contains(call, WithoutComments(page));
    }
}
