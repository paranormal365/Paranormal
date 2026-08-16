using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ben.Video.Core.SidecarContracts;

/// <summary>
/// The one JSON serializer configuration shared by both sides of the sidecar protocol — item #38
/// phase E threat T4. <see cref="JsonUnmappedMemberHandling.Disallow"/> means an unexpected field
/// in a request body is a hard parse failure, not silently ignored; strict enum parsing (no
/// integer fallback) means a job spec's enums can only ever be the exact names this code knows
/// about. Both browser (serializing) and sidecar (deserializing/validating) use these options —
/// there is exactly one place that defines "what a valid job spec looks like at the wire level".
/// </summary>
public static class SidecarJsonOptions
{
    public static readonly JsonSerializerOptions Default = Create(JsonUnmappedMemberHandling.Disallow);

    /// <summary>
    /// Item #70 phase 158 — for the <b>browser parsing a sidecar's response</b> only. Requests
    /// keep using <see cref="Default"/>'s strict <see cref="JsonUnmappedMemberHandling.Disallow"/>
    /// (a malformed/unknown field in an inbound job spec must still be a hard failure), but a
    /// response arriving from a <i>newer</i> sidecar than this browser build knows about must not
    /// be fatal: with Disallow, a single additive response field would throw, and in
    /// <c>NativeSidecarService.ProbeAsync</c> that throw is caught per-port and reads as "no
    /// sidecar here" — silently losing a perfectly working connection. Skip lets this build ignore
    /// fields from the future and keep working with the ones it understands.
    /// </summary>
    public static readonly JsonSerializerOptions LenientResponses = Create(JsonUnmappedMemberHandling.Skip);

    private static JsonSerializerOptions Create(JsonUnmappedMemberHandling unmapped)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = unmapped,
            MaxDepth = 16,
            PropertyNameCaseInsensitive = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
