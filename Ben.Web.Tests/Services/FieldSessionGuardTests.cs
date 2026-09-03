using System.Text;
using Ben.Data.WebApi.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A hostile upload cannot run anything, but it can lie. These are the lies the door now refuses.
/// </summary>
public sealed class FieldSessionGuardTests
{
    private const string Start = "2026-08-25T02:05:07.000Z";
    private const string End   = "2026-08-25T02:09:07.000Z";

    private static string Document(string readings) => $$"""
        {"format_version":"1.0.0","device":{"model":"iPhone17,1"},
         "session":{"started_at":"{{Start}}","ended_at":"{{End}}"},
         "readings":[{{readings}}]}
        """;

    private static string Reading(string at, string measurements = "\"emf\":{\"value\":48.0,\"unit\":\"uT\"}", string extra = "")
        => $$"""{"at":"{{at}}","measurements":{{{measurements}}}{{extra}}}""";

    [Fact]
    public void A_session_the_app_would_write_passes()
    {
        var doc = Document(Reading("2026-08-25T02:05:07.000Z") + "," + Reading("2026-08-25T02:06:07.000Z",
            "\"emf\":{\"value\":53.0},\"sound_level\":{\"value\":-52.1},\"room\":{\"value\":\"Cellar\"}",
            ",\"position\":{\"latitude\":36.1627,\"longitude\":-86.7816,\"accuracy_meters\":28},\"motion\":{\"heading_degrees\":271.5}"));
        Assert.Null(FieldSessionDocumentGuard.Refusal(doc));
    }

    [Theory]
    [InlineData("2026-08-24T02:05:07.000Z", "outside the session's own window")]   // a day early
    [InlineData("2026-08-25T03:00:00.000Z", "outside the session's own window")]   // an hour after it ended
    public void A_reading_outside_the_sessions_window_is_named(string at, string expected)
    {
        var refusal = FieldSessionDocumentGuard.Refusal(Document(Reading(at)));
        Assert.NotNull(refusal);
        Assert.Contains(expected, refusal);
        Assert.Contains("reading 1", refusal);
    }

    [Fact]
    public void Readings_must_not_run_backwards()
    {
        var doc = Document(Reading("2026-08-25T02:07:07.000Z") + "," + Reading("2026-08-25T02:06:07.000Z"));
        Assert.Contains("reading 2 is stamped before", FieldSessionDocumentGuard.Refusal(doc));
    }

    [Theory]
    [InlineData("\"emf\":{\"value\":50000}", "no phone measures that")]
    [InlineData("\"emf\":{\"value\":-1}", "no phone measures that")]
    [InlineData("\"sound_level\":{\"value\":12}", "tops out at 0")]
    public void Fields_no_phone_can_measure_are_named(string measurement, string expected)
        => Assert.Contains(expected, FieldSessionDocumentGuard.Refusal(Document(Reading(Start, measurement))));

    [Theory]
    [InlineData(",\"position\":{\"latitude\":91,\"longitude\":0}", "stops at 90")]
    [InlineData(",\"position\":{\"latitude\":0,\"longitude\":-181}", "stops at 180")]
    [InlineData(",\"position\":{\"latitude\":0,\"longitude\":0,\"accuracy_meters\":-5}", "negative position accuracy")]
    [InlineData(",\"motion\":{\"heading_degrees\":400}", "compass goes to 360")]
    public void Positions_off_the_earth_are_named(string extra, string expected)
        => Assert.Contains(expected, FieldSessionDocumentGuard.Refusal(Document(Reading(Start, extra: extra))));

    [Fact]
    public void Too_many_readings_is_refused_with_the_limit()
    {
        var sb = new StringBuilder();
        for (var i = 0; i <= FieldSessionDocumentGuard.MaxReadings; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Reading(Start));
        }
        Assert.Contains("at most", FieldSessionDocumentGuard.Refusal(Document(sb.ToString())));
    }

    // ── files ────────────────────────────────────────────────────────────────

    private static byte[] Bytes(params int[] b) => b.Select(x => (byte)x).ToArray();
    private static readonly byte[] Ftyp = Bytes(0, 0, 0, 0x20, 'f', 't', 'y', 'p', 'M', '4', 'A', ' ', 0, 0, 0, 0);
    private static readonly byte[] Riff = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt ");
    private static readonly byte[] Zeros = new byte[16];

    [Theory]
    [InlineData("media/audio-001.m4a", "audio/mp4")]
    [InlineData("media/clip-001.mov", "video/quicktime")]
    [InlineData("media/clip-001.mp4", "video/mp4")]
    public void The_apps_iso_containers_pass(string path, string type)
        => Assert.Null(FieldSessionFileGuard.Refusal(path, type, 1000, Ftyp, 0));

    [Fact]
    public void A_wav_passes_and_a_jpeg_passes()
    {
        Assert.Null(FieldSessionFileGuard.Refusal("media/audio-001.wav", "audio/wav", 1000, Riff, 0));
        Assert.Null(FieldSessionFileGuard.Refusal("media/photo-001.jpg", "image/jpeg", 1000, Bytes(0xFF, 0xD8, 0xFF, 0xE0), 0));
    }

    /// <summary>The simulator's placeholder: the right name, the right type, no recording inside.</summary>
    [Fact]
    public void Zero_filled_bytes_named_m4a_are_refused()
    {
        var refusal = FieldSessionFileGuard.Refusal("media/audio-001.m4a", "audio/mp4", 2048, Zeros, 0);
        Assert.NotNull(refusal);
        Assert.Contains("are not a M4A file", refusal);
    }

    [Fact]
    public void A_kind_the_field_kit_does_not_make_is_refused()
    {
        Assert.Contains("only the recordings and photos", FieldSessionFileGuard.Refusal("media/notes.html", "text/html", 100, Encoding.ASCII.GetBytes("<html><script>"), 0));
        Assert.Contains("only the recordings and photos", FieldSessionFileGuard.Refusal("media/tool.exe", "application/octet-stream", 100, Bytes('M', 'Z'), 0));
    }

    [Fact]
    public void A_name_and_a_declared_type_that_disagree_are_refused()
        => Assert.Contains("sent as text/html", FieldSessionFileGuard.Refusal("media/audio-001.m4a", "text/html", 100, Ftyp, 0));

    [Fact]
    public void Limits_are_stated_as_sentences()
    {
        Assert.Contains("at most", FieldSessionFileGuard.Refusal("media/audio-001.m4a", "audio/mp4", 1000, Ftyp, FieldSessionFileGuard.MaxFilesPerSession));
        Assert.Contains("GB", FieldSessionFileGuard.Refusal("media/clip.mov", "video/quicktime", FieldSessionFileGuard.MaxFileBytes + 1, Ftyp, 0));
    }
}
