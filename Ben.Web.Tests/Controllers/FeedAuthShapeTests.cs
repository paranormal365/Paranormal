using Ben.Data.WebApi.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Reflection;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Item 186: the feed's auth shape, pinned structurally — anyone reads, only signed-in people write.
/// </summary>
/// <remarks>
/// <para>This is a ratchet, not a behaviour test. The behaviour tests next door prove a visitor
/// gets posts back; this proves the NEXT endpoint somebody adds has to make a deliberate choice
/// about who may call it. The two ways this goes wrong are equally bad and equally quiet: a read
/// that starts demanding sign-in closes the front door, and a write that stops demanding it lets
/// anonymous strangers post — neither would fail a single existing test.</para>
///
/// <para><b>Probe-regressed</b> by removing <c>[AllowAnonymous]</c> from <c>GetFeed</c> (the reads
/// assertion fails) and by removing <c>[Authorize]</c> from <c>CreatePost</c> (the writes
/// assertion fails, naming the method).</para>
/// </remarks>
public sealed class FeedAuthShapeTests
{
    /// <summary>
    /// The reads a visitor is allowed. Named, not inferred from the HTTP verb: a future GET that
    /// exposes something per-reader should have to be added here on purpose.
    /// </summary>
    /// <remarks>
    /// <see cref="FeedController.GetPostMedia"/> was added in F4 and this list is why it was a
    /// decision rather than an accident: media on a public post is as public as the post, and the
    /// endpoint itself refuses anything unscreened or hidden. Anyone adding the fifth entry should
    /// have to write down why it belongs.
    /// </remarks>
    private static readonly string[] AnonymousReads =
    [
        nameof(FeedController.GetFeed),
        nameof(FeedController.GetThread),
        nameof(FeedController.GetProfile),
        nameof(FeedController.GetPostMedia),
    ];

    private static IEnumerable<MethodInfo> Endpoints()
        => typeof(FeedController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

    [Fact]
    public void The_class_itself_carries_no_blanket_authorize()
    {
        // A class-level [Authorize] would silently re-close every read: [AllowAnonymous] on the
        // action would still win today, but the intent belongs at the action either way, and this
        // is how the arrangement stays legible.
        Assert.Empty(typeof(FeedController).GetCustomAttributes<AuthorizeAttribute>());
    }

    [Fact]
    public void Exactly_the_declared_reads_are_anonymous()
    {
        var anonymous = Endpoints()
            .Where(m => m.GetCustomAttributes<AllowAnonymousAttribute>().Any())
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(AnonymousReads.OrderBy(n => n).ToList(), anonymous);
    }

    [Fact]
    public void Every_endpoint_that_is_not_an_anonymous_read_requires_sign_in()
    {
        var unguarded = Endpoints()
            .Where(m => !AnonymousReads.Contains(m.Name))
            .Where(m => !m.GetCustomAttributes<AuthorizeAttribute>().Any())
            .Select(m => m.Name)
            .ToList();

        Assert.True(unguarded.Count == 0,
            "These feed endpoints are neither an allowed anonymous read nor [Authorize]d, so "
            + "anybody at all may call them: " + string.Join(", ", unguarded));
    }
}
