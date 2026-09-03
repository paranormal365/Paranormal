using Microsoft.AspNetCore.Http;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Optional paging for admin lists.
/// </summary>
/// <remarks>
/// These lists accepted <c>page</c> and <c>pageSize</c> and returned everything regardless, which
/// is the worst combination: a caller that asked for twenty rows got two thousand and had no way
/// to tell. Now the parameters are honoured when both are sent, the total goes in
/// <c>X-Total-Count</c> so a caller can tell "20 of 20" from "20 of 2,000", and a request with
/// neither still returns everything — the admin grids page on the client and depend on that.
/// </remarks>
public static class ListPaging
{
    public const int MaxPageSize = 500;

    /// <summary>
    /// The slice to return, or the whole list when no paging was asked for. Always stamps the
    /// total so the caller can see what it did not get.
    /// </summary>
    public static IReadOnlyList<T> Apply<T>(IReadOnlyList<T> all, int? page, int? pageSize, HttpResponse? response)
    {
        // Null outside a request — a controller built by hand in a unit test has no HttpContext,
        // and the slice is still the right answer there.
        if (response is not null) response.Headers["X-Total-Count"] = all.Count.ToString();
        if (page is null || pageSize is null) return all;

        var size = Math.Clamp(pageSize.Value, 1, MaxPageSize);
        var index = Math.Max(page.Value, 1);
        return all.Skip((index - 1) * size).Take(size).ToList();
    }
}
