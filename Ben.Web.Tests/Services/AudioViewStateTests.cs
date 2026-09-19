using Ben.Service.Models.Entities;
using Ben.Web.Website.Library.Manage.Audio;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Remembering how somebody looks at a recording.
/// </summary>
/// <remarks>
/// <c>UploadFileAudioConfig</c> — the table, the controller, the client and the mapper — has
/// existed since 2026-07-18 and nothing had ever read or written it, so the spectrogram, its colour
/// ramp, its resolution, the mel scale and the timeline were all reset on every open. For somebody
/// working through a two-hour recording that is several times an hour (2026-09-06 audio walk,
/// finding L).
/// </remarks>
public sealed class AudioViewStateTests
{
    [Fact]
    public void A_recording_nobody_has_set_up_reads_as_the_defaults()
    {
        var view = AudioViewState.From(null);

        Assert.False(view.SpectrogramVisible);
        Assert.True(view.SpectrogramLabels);
        Assert.Equal(512, view.FftSamples);
        Assert.Equal("jet", view.Colormap);
        Assert.False(view.MelScale);
        Assert.True(view.TimelineVisible);
    }

    [Fact]
    public void A_saved_view_comes_back_as_it_was_left()
    {
        var view = new AudioViewState
        {
            SpectrogramVisible = true,
            SpectrogramLabels  = false,
            FftSamples         = 2048,
            Colormap           = "viridis",
            MelScale           = true,
            TimelineVisible    = false,
        };

        var round = AudioViewState.From(Saved(view));

        Assert.Equal(view, round);
    }

    /// <summary>
    /// A row written before this shape grew a colour ramp keeps the settings it does have.
    /// </summary>
    /// <remarks>
    /// Field by field rather than all-or-nothing: losing an FFT size because a colormap is missing
    /// would make every upgrade of this shape silently reset everybody's view.
    /// </remarks>
    [Fact]
    public void A_row_from_an_older_shape_keeps_what_it_does_have()
    {
        var record = new UploadFileAudioConfigRecord
        {
            EnableSpectrogram      = true,
            EnableTimeline         = true,
            SpectrogramOptionsJson = """{"fftSamples":1024,"labels":false}""",
        };

        var view = AudioViewState.From(record);

        Assert.Equal(1024, view.FftSamples);
        Assert.False(view.SpectrogramLabels);
        Assert.Equal("jet", view.Colormap);        // never chosen — the default, not null
        Assert.False(view.MelScale);
    }

    [Fact]
    public void Json_that_cannot_be_read_falls_back_rather_than_throwing()
    {
        var view = AudioViewState.From(new UploadFileAudioConfigRecord
        {
            EnableSpectrogram      = true,
            SpectrogramOptionsJson = "not json at all",
        });

        Assert.True(view.SpectrogramVisible);      // the column that is not JSON is still read
        Assert.Equal(512, view.FftSamples);
        Assert.Equal("jet", view.Colormap);
    }

    /// <summary>
    /// The upsert replaces the whole row, so a save must carry every setting it does not own.
    /// </summary>
    /// <remarks>
    /// Otherwise turning the spectrogram on would quietly wipe the wave colour, the zoom bounds and
    /// the player's height — settings this panel has no control for and would give no sign of
    /// having destroyed.
    /// </remarks>
    [Fact]
    public void Saving_the_view_carries_every_setting_it_does_not_own()
    {
        var existing = new UploadFileAudioConfigRecord
        {
            WaveColor     = "#FF6358",
            ProgressColor = "#D9534F",
            InitialHeight = "250px",
            MinZoom       = 5,
            MaxZoom       = 2000,
            EnableMinimap = true,
        };

        var request = new AudioViewState { SpectrogramVisible = true }.ToRequest(existing);

        Assert.Equal("#FF6358", request.WaveColor);
        Assert.Equal("#D9534F", request.ProgressColor);
        Assert.Equal("250px",   request.InitialHeight);
        Assert.Equal(5,    request.MinZoom);
        Assert.Equal(2000, request.MaxZoom);
        Assert.True(request.EnableMinimap);
        Assert.True(request.EnableSpectrogram);
    }

    [Fact]
    public void Saving_the_first_time_invents_nothing_it_was_not_given()
    {
        var request = new AudioViewState { Colormap = "magma" }.ToRequest(null);

        Assert.Null(request.WaveColor);
        Assert.Contains("magma", request.SpectrogramOptionsJson);
    }

    /// <summary>
    /// The three height fields are not nullable on the request, and a null fails model binding.
    /// </summary>
    /// <remarks>
    /// The whole save failed with a 400 that the client drops as unreadable — a ProblemDetails blob
    /// is not prose — so the editor said only "these settings aren't yours to change" and the real
    /// reason took an explicit PUT of the same body to find (2026-09-06 audio audit, phase 5).
    /// </remarks>
    [Fact]
    public void Saving_supplies_the_fields_the_request_will_not_accept_as_null()
    {
        var request = AudioViewState.Default.ToRequest(null);

        Assert.False(string.IsNullOrEmpty(request.InitialHeight));
        Assert.False(string.IsNullOrEmpty(request.MinHeight));
        Assert.False(string.IsNullOrEmpty(request.MaxHeight));
    }

    [Fact]
    public void A_height_already_saved_is_kept_rather_than_replaced_by_a_default()
    {
        var request = AudioViewState.Default.ToRequest(new UploadFileAudioConfigRecord
        {
            InitialHeight = "250px", MinHeight = "100px", MaxHeight = "900px",
        });

        Assert.Equal("250px", request.InitialHeight);
        Assert.Equal("100px", request.MinHeight);
        Assert.Equal("900px", request.MaxHeight);
    }

    [Fact]
    public void Two_identical_views_are_the_same_view()
    {
        var a = new AudioViewState { Colormap = "viridis", FftSamples = 1024 };
        var b = new AudioViewState { Colormap = "viridis", FftSamples = 1024 };

        Assert.True(a.SameAs(b));
        Assert.False(a.SameAs(b with { MelScale = true }));
    }

    /// <summary>Builds the row a save of <paramref name="view"/> would produce.</summary>
    private static UploadFileAudioConfigRecord Saved(AudioViewState view)
    {
        var request = view.ToRequest(null);
        return new UploadFileAudioConfigRecord
        {
            EnableSpectrogram      = request.EnableSpectrogram,
            EnableTimeline         = request.EnableTimeline,
            SpectrogramOptionsJson = request.SpectrogramOptionsJson,
        };
    }

    // ── The listening chain rides with the view (phase 5b) ────────────────────

    [Fact]
    public void The_listening_chain_survives_the_round_trip()
    {
        var view = new AudioViewState
        {
            SpectrogramVisible = true,
            Chain = AudioListeningChain.Default with
            {
                HighPassOn = true, HighPassHz = 300,
                NoiseGateOn = true, NoiseGateThresholdDb = -55,
                EqGains = [0, 0, 6, 0, 0, 0, 0, 0, 0, 0],
            },
        };

        var record = new UploadFileAudioConfigRecord
        {
            EnableSpectrogram      = view.SpectrogramVisible,
            EnableTimeline         = view.TimelineVisible,
            SpectrogramOptionsJson = view.ToRequest(null).SpectrogramOptionsJson,
            EditStateJson          = view.ToRequest(null).EditStateJson,
        };

        var round = AudioViewState.From(record);

        Assert.True(round.Chain.HighPassOn);
        Assert.Equal(300, round.Chain.HighPassHz);
        Assert.Equal(-55, round.Chain.NoiseGateThresholdDb);
        Assert.Equal(6, round.Chain.EqGains[2]);
    }

    /// <summary>
    /// Two views with equal chains are the same view.
    /// </summary>
    /// <remarks>
    /// The equaliser is a list, and a list compares by reference — so without comparing element by
    /// element, two identical chains read as different and every control would send a save on
    /// every touch (2026-09-06 audio audit, phase 5b).
    /// </remarks>
    [Fact]
    public void Two_views_with_equal_chains_are_the_same_view()
    {
        var a = new AudioViewState { Chain = AudioListeningChain.Default with { EqGains = [1, 2, 3, 0, 0, 0, 0, 0, 0, 0] } };
        var b = new AudioViewState { Chain = AudioListeningChain.Default with { EqGains = [1, 2, 3, 0, 0, 0, 0, 0, 0, 0] } };

        Assert.True(a.SameAs(b));
    }

    [Fact]
    public void A_view_whose_chain_differs_is_a_different_view()
    {
        var a = new AudioViewState();
        var b = new AudioViewState { Chain = AudioListeningChain.Default with { HighPassOn = true } };

        Assert.False(a.SameAs(b));
    }

    [Fact]
    public void A_view_whose_equaliser_moved_is_a_different_view()
    {
        var a = new AudioViewState();
        var b = new AudioViewState { Chain = AudioListeningChain.Default with { EqGains = [0, 0, 6, 0, 0, 0, 0, 0, 0, 0] } };

        Assert.False(a.SameAs(b));
    }

    [Fact]
    public void Saving_carries_the_chain_into_its_own_column()
    {
        var request = new AudioViewState
        {
            Chain = AudioListeningChain.Default with { LowPassOn = true, LowPassHz = 4_000 },
        }.ToRequest(null);

        Assert.NotNull(request.EditStateJson);
        Assert.Contains("lowPassOn", request.EditStateJson);
        Assert.Contains("4000", request.EditStateJson);

        // And not into the spectrogram's column, which is about what you SEE.
        Assert.DoesNotContain("lowPass", request.SpectrogramOptionsJson ?? "");
    }
}
