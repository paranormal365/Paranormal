using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Ben.Data.WebApi.Filters;

/// <summary>
/// Enables <see cref="HttpRequest.Body"/> buffering so an action can re-read the raw request JSON
/// after ASP.NET Core's model binder has already consumed the stream once. Scoped to a specific
/// controller (via <c>[TypeFilter(typeof(EnableRequestBufferingFilter))]</c>) rather than applied
/// globally, since buffering forces the whole body into memory/disk — fine for the small JSON
/// payloads this exists for (<see cref="Ben.Data.WebApi.Controllers.AdminEntityControllerBase{TEntity,TRecord}"/>'s
/// <c>Create</c> needing to distinguish a caller-supplied <c>false</c> from an omitted property),
/// wasteful applied to every request including large file uploads elsewhere in the app.
/// </summary>
public sealed class EnableRequestBufferingFilter : IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
        => context.HttpContext.Request.EnableBuffering();

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
