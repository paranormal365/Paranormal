using Ben.Video.Sidecar.Storage;
using Ben.Video.Sidecar.Validation;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace Ben.Video.Sidecar.Api;

/// <summary>
/// Source-cache endpoints — lets the browser upload an OPFS clip once and reuse it across many
/// jobs (<c>HEAD</c> to check first) instead of re-uploading per job. Every id is validated by
/// <see cref="SpecValidator"/> before it touches <see cref="SourceCache"/> — see that class's doc
/// comment for why a raw request string never reaches a path.
/// </summary>
public static class SourceEndpoints
{
    public static void MapSourceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/v1/sources/{clipId}", ["HEAD"], (
            HttpContext context, string clipId, string? ext, SpecValidator validator, SourceCache cache) =>
        {
            // Written directly against HttpContext.Response, not via Results.Ok()/NotFound() —
            // ASP.NET's IResult writers don't reliably set Content-Length for HEAD requests (a
            // real bug found live: curl and any other HTTP/1.1 keep-alive client hangs waiting
            // for a body that will never come, because neither Content-Length nor a connection
            // close ever tells it the response is already complete). HEAD must always send the
            // same framing headers a GET would, with no body — set explicitly here.
            context.Response.ContentLength = 0;

            if (!validator.TryParseId(clipId, out var id) || validator.ValidateExtension(ext) is not { } validExt)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return Task.CompletedTask;
            }

            if (!cache.TryGetEntry(id, validExt, out var entry))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            context.Response.Headers["X-BenVideo-Size"] = entry.SizeBytes.ToString();
            context.Response.Headers["X-BenVideo-LastModified"] = entry.LastModifiedUtc.ToUnixTimeSeconds().ToString();
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        app.MapPut("/v1/sources/{clipId}", async (
            string clipId, string? ext, HttpRequest request,
            SpecValidator validator, SourceCache cache, IOptions<SidecarOptions> options, CancellationToken ct) =>
        {
            if (!validator.TryParseId(clipId, out var id)) return Results.BadRequest("Invalid clip id.");
            var validExt = validator.ValidateExtension(ext);
            if (validExt is null) return Results.BadRequest("Unsupported or missing file extension.");

            // Raise the body-size limit only for this endpoint, only for this one request —
            // Kestrel's global default (SidecarOptions.DefaultMaxRequestBodyBytes) stays tiny for
            // every other route (item #38 phase E threat T6).
            var bodySizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodySizeFeature is { IsReadOnly: false })
                bodySizeFeature.MaxRequestBodySize = options.Value.MaxUploadBodyBytes;

            var written = await cache.WriteAsync(id, validExt, request.Body, ct);
            return Results.Ok(new { sizeBytes = written });
        });

        app.MapDelete("/v1/sources/{clipId}", (
            string clipId, string? ext, SpecValidator validator, SourceCache cache) =>
        {
            if (!validator.TryParseId(clipId, out var id)) return Results.BadRequest();
            var validExt = validator.ValidateExtension(ext);
            if (validExt is null) return Results.BadRequest();

            cache.Delete(id, validExt);
            return Results.NoContent();
        });
    }
}
