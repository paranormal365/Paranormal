using System.Reflection;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Pictures and files are fetched many to a page, so the routes that serve them stay out of the
/// sitewide rate limit.
/// </summary>
/// <remarks>
/// A helper method was added between the thumbnail route and its attribute, so
/// <c>[DisableRateLimiting]</c> landed on the private helper and thumbnails were counted like any
/// other request. The compiler said so only as a misplaced-comment warning (cleared 10/27/2026).
/// </remarks>
public sealed class FileServingSkipsTheRateLimitTests
{
    [Theory]
    [InlineData(nameof(UploadFileController.Download))]
    [InlineData(nameof(UploadFileController.Thumbnail))]
    public void The_route_is_not_rate_limited(string action)
    {
        var method = typeof(UploadFileController).GetMethod(action, BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);
        Assert.NotNull(method.GetCustomAttribute<DisableRateLimitingAttribute>());
    }
}
