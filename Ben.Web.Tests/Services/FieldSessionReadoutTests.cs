using Ben.Data.WebApi.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>The one-paragraph readout is a pure function of the document, and every clause a fact from it.</summary>
public sealed class FieldSessionReadoutTests
{
    private const string Session = """
        {"format_version":"1.0.0","device":{"model":"iPhone17,1"},
         "session":{"started_at":"2026-08-25T02:05:07.000Z","ended_at":"2026-08-25T02:07:31.000Z","location_label":"Demo: cellar"},
         "readings":[
           {"at":"2026-08-25T02:05:07.000Z","measurements":{"emf":{"value":48.0,"baseline":48.0},"room":{"value":"Hall"}}},
           {"at":"2026-08-25T02:05:11.000Z","measurements":{"emf":{"value":48.2},"room":{"value":"Hall"}},
            "audio_ref":{"filename":"media/audio-001.m4a","start_offset_seconds":0,"duration_seconds":120}},
           {"at":"2026-08-25T02:06:07.000Z","measurements":{"emf":{"value":49.0},"room":{"value":"Basement"}},"note":"moved to Basement"},
           {"at":"2026-08-25T02:06:19.000Z","measurements":{"emf":{"value":54.0},"sound_level":{"value":-52.0},"room":{"value":"Basement"},"marker":{"value":"manual_marker"}},"note":"Cold spot by the north wall"},
           {"at":"2026-08-25T02:06:40.000Z","measurements":{"emf":{"value":50.0}},"note":"photo: media/photo-001.jpg"},
           {"at":"2026-08-25T02:07:31.000Z","measurements":{"emf":{"value":48.1},"sound_level":{"value":-60.0}}}]}
        """;

    [Fact]
    public void The_paragraph_names_the_peak_when_where_what_was_recording_and_the_mark()
    {
        var r = FieldSessionReadout.Compose(Session)!;

        Assert.Equal(540, r.PeakMilligauss);
        Assert.Equal(480, r.BaselineMilligauss);
        Assert.Equal(TimeSpan.FromSeconds(72), r.PeakOffset);
        Assert.Equal("Basement", r.PeakRoom);
        Assert.Equal(["audio-001.m4a"], r.RecordingAtPeak);
        Assert.Equal("Cold spot by the north wall", r.MarkNearPeak);
        Assert.Equal(1, r.Photos);
        Assert.Equal(1, r.Recordings);

        Assert.Equal(
            "Over 2 min 24 s through Hall and Basement, the magnetic field peaked at 540 mG against a base of 480 mG "
            + "1 min 12 s in, in the Basement, while audio-001.m4a was recording. "
            + "A mark was placed there: “Cold spot by the north wall”. The session carries one recording and one photo.",
            r.Sentence);
    }

    [Fact]
    public void A_sound_only_session_says_so_instead_of_inventing_a_field()
    {
        const string soundOnly = """
            {"readings":[
              {"at":"2026-08-25T02:05:07.000Z","measurements":{"sound_level":{"value":-58.0}}},
              {"at":"2026-08-25T02:05:37.000Z","measurements":{"sound_level":{"value":-31.0}}}]}
            """;
        var r = FieldSessionReadout.Compose(soundOnly)!;
        Assert.Null(r.PeakMilligauss);
        Assert.Equal("Over 30 s, no magnetic field was recorded; sound peaked at -31 dBFS 30 s in.", r.Sentence);
    }

    [Fact]
    public void An_audio_reference_with_an_offset_places_the_recording_correctly_at_the_peak()
    {
        // Audio began 20 s before the reading that carries the reference; the peak is 5 s after
        // the session start, inside the recording's span only if the offset is honoured.
        const string doc = """
            {"readings":[
              {"at":"2026-08-25T02:05:00.000Z","measurements":{"emf":{"value":40}}},
              {"at":"2026-08-25T02:05:05.000Z","measurements":{"emf":{"value":90}}},
              {"at":"2026-08-25T02:05:30.000Z","measurements":{"emf":{"value":41}},
               "audio_ref":{"filename":"media/audio-001.m4a","start_offset_seconds":40,"duration_seconds":100}}]}
            """;
        var r = FieldSessionReadout.Compose(doc)!;
        Assert.Equal(["audio-001.m4a"], r.RecordingAtPeak);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"readings":[]}""")]
    [InlineData("""{"session":{}}""")]
    public void Nothing_readable_gives_no_readout_rather_than_an_invented_one(string? doc)
        => Assert.Null(FieldSessionReadout.Compose(doc));
}
