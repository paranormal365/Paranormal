using System.Text.Json;
using System.Text.Json.Nodes;
using Ben.Data.WebApi.Services.FieldSessions;
using Ben.Service.Models.FieldSessions;
using Jint;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The web player's replay against the shared rules — on the same fixtures the phone is held to.
/// </summary>
/// <remarks>
/// <para>Each fixture goes the whole way a real session goes on the website: the phone's document
/// through the server's <see cref="FieldSessionTimelineBuilder"/>, serialized as the API sends it,
/// then into the browser's own frame function (<c>wwwroot/js/fieldKitTimeline.js</c>), run here in
/// Jint. The phone runs the same files through its import and replay
/// (<c>ReplayConformanceTests.swift</c>). A difference between the two players is a failure on
/// one side or the other, with the moment and the field named.</para>
///
/// <para>Numbers compare within a tolerance, never exactly: .NET, Jint, Safari and the phone
/// disagree in the last decimal place, and that is not a difference in the rules.</para>
/// </remarks>
public sealed class ReplayConformanceTests
{
    public static TheoryData<string> Names => new() { "walk", "media", "interrupted" };

    [Theory]
    [MemberData(nameof(Names))]
    public void The_web_player_shows_what_the_rules_say(string name)
    {
        var fixture = Fixture(name);
        var timelineJson = TimelineJson(fixture);
        var js = Harness(timelineJson);
        var failures = new List<string>();

        var bounds = fixture["bounds"]!.AsObject();
        var timeline = JsonNode.Parse(timelineJson)!;
        Check(failures, $"{name}: end", bounds["end_ms"]!.GetValue<double>(), timeline["durationMs"]!.GetValue<double>(), 0.5);

        foreach (var moment in fixture["moments"]!.AsArray())
        {
            var atMs = moment!["at_ms"]!.GetValue<long>();
            var frame = JsonNode.Parse(js.Evaluate($"frameJson({atMs})").AsString())!.AsObject();
            var here = $"{name} @ {atMs} ms";

            foreach (var (key, expected) in moment["expect"]!.AsObject())
            {
                switch (key)
                {
                    case "emf_ut": CheckNumber(failures, here, key, expected, frame["emf"]); break;
                    case "emf_base_ut": CheckNumber(failures, here, key, expected, frame["emfBase"]); break;
                    case "field_mg": CheckNumber(failures, here, key, expected, frame["fieldMg"]); break;
                    case "sound_dbfs": CheckNumber(failures, here, key, expected, frame["sound"]); break;
                    case "room": CheckText(failures, here, key, expected, frame["room"]); break;
                    case "lat": CheckNumber(failures, here, key, expected, frame["position"]?["lat"], 1e-6); break;
                    case "lon": CheckNumber(failures, here, key, expected, frame["position"]?["lon"], 1e-6); break;
                    case "accuracy_m": CheckNumber(failures, here, key, expected, frame["position"]?["accuracy"]); break;
                    case "facing":
                        if (expected is null)
                        {
                            if (frame["facing"] is not null) failures.Add($"{here}: facing should be none, is {frame["facing"]!.ToJsonString()}");
                        }
                        else
                        {
                            CheckText(failures, here, "facing kind", expected["kind"], frame["facing"]?["kind"]);
                            CheckNumber(failures, here, "facing deg", expected["deg"], frame["facing"]?["deg"], 0.5);
                        }
                        break;
                    case "audio":
                    case "video":
                        if (expected is null)
                        {
                            if (frame[key] is not null) failures.Add($"{here}: {key} should be none, is {frame[key]!["clip"]!["path"]}");
                        }
                        else
                        {
                            CheckText(failures, here, $"{key} clip", expected["path"], frame[key]?["clip"]?["path"]);
                            CheckNumber(failures, here, $"{key} offset", expected["offset_ms"], frame[key]?["offsetMs"], 0.5);
                        }
                        break;
                    case "motion":
                        var showing = frame["motion"] is not null;
                        if (showing != expected!.GetValue<bool>()) failures.Add($"{here}: motion should be {expected}, is {showing}");
                        break;
                    case "glowing_photo": CheckText(failures, here, key, expected, frame["glowingPhoto"]?["path"]); break;
                    case "nearest_marker_ms": CheckNumber(failures, here, key, expected, frame["nearestMarker"]?["t"], 0.5); break;
                    default: failures.Add($"{here}: the fixture names {key}, which this test does not know"); break;
                }
            }
        }

        foreach (var jump in fixture["jumps"]?.AsArray() ?? [])
        {
            var from = jump!["from_ms"]!.GetValue<long>();
            var forward = jump["forward"]!.GetValue<bool>();
            var lands = js.Evaluate($"stepJson({from}, {(forward ? "true" : "false")})");
            Check(failures, $"{name}: stepping {(forward ? "forward" : "back")} from {from} ms",
                  jump["lands_ms"]!.GetValue<double>(), lands.IsNull() ? double.NaN : lands.AsNumber(), 0.5);
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // ── The chain: document → server → browser ─────────────────────────────────

    private static JsonObject Fixture(string name)
        => JsonNode.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "replay-conformance", $"{name}.json")))!.AsObject();

    /// <summary>
    /// The fixture's document through the server's builder, as the API would send it. The files are
    /// the ones the document names; their lengths are the fixture's, as the server reads them from
    /// the files themselves.
    /// </summary>
    private static string TimelineJson(JsonObject fixture)
    {
        var durations = fixture["durations"]?.AsObject().ToDictionary(p => p.Key, p => p.Value!.GetValue<double>())
                        ?? [];
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var reading in fixture["document"]!["readings"]!.AsArray())
        {
            if (reading?["audio_ref"]?["filename"]?.GetValue<string>() is { } file) paths.Add(file);
            if (reading?["note"]?.GetValue<string>() is { } note && note.IndexOf(": media/", StringComparison.Ordinal) is var at and > 0)
                paths.Add(note[(at + 2)..]);
        }
        var files = paths.Select(p => new TimelineFile(Guid.NewGuid(), p, durations.TryGetValue(p, out var s) ? s : null)).ToList();

        var timeline = FieldSessionTimelineBuilder.Build(fixture["document"]!.ToJsonString(), files, TimelinePositionPolicy.Exact);
        return JsonSerializer.Serialize(timeline, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    /// <summary>The browser's module, loaded as the page loads it, with the timeline parsed once.</summary>
    private static Engine Harness(string timelineJson)
    {
        var module = File.ReadAllText(Path.Combine(RepoRoot(), "Ben.Web.Website.Library", "wwwroot", "js", "fieldKitTimeline.js"));
        var engine = new Engine();
        engine.Modules.Add("timeline", module);
        engine.Modules.Add("harness", """
            import { frameAt, stepMarker } from 'timeline';
            const tl = JSON.parse(globalThis.timelineJson);
            globalThis.frameJson = ms => JSON.stringify(frameAt(tl, ms));
            globalThis.stepJson = (ms, forward) => stepMarker(tl, ms, forward);
            """);
        engine.SetValue("timelineJson", timelineJson);
        engine.Modules.Import("harness");
        return engine;
    }

    // ── Comparing ───────────────────────────────────────────────────────────────

    private static void CheckNumber(List<string> failures, string here, string key, JsonNode? expected, JsonNode? actual,
                                    double tolerance = 1e-6)
    {
        if (expected is null)
        {
            if (actual is not null) failures.Add($"{here}: {key} should be none, is {actual}");
            return;
        }
        if (actual is null) { failures.Add($"{here}: {key} should be {expected}, is none"); return; }
        Check(failures, $"{here}: {key}", expected.GetValue<double>(), actual.GetValue<double>(), tolerance);
    }

    private static void CheckText(List<string> failures, string here, string key, JsonNode? expected, JsonNode? actual)
    {
        var want = expected?.GetValue<string>();
        var got = actual?.GetValue<string>();
        if (want != got) failures.Add($"{here}: {key} should be {want ?? "none"}, is {got ?? "none"}");
    }

    private static void Check(List<string> failures, string what, double expected, double actual, double tolerance)
    {
        if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
            failures.Add($"{what} should be {expected}, is {actual}");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
