using Ben.Web.Website.Library.Kit;
using Xunit;
using static Ben.Web.Website.Library.Kit.WizardModel;
using static Ben.Web.Website.Library.Kit.TourModel;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Item 166 W0: the wizard and tour primitives' decisions, held where xUnit can reach them.
/// The components only render what these classes answer, so a passing suite here is the bulk
/// of the primitives' correctness — the Playwright halves prove the rendering.
/// </summary>
public sealed class WizardAndTourModelTests
{
    private static WizardModel ThreeSteps(Func<Task<string?>>? gate2 = null) => new(
    [
        new WizardStep("One"),
        new WizardStep("Two", gate2),
        new WizardStep("Three"),
    ]);

    [Fact]
    public async Task A_wizard_walks_forward_and_finishes_only_from_the_last_step()
    {
        var w = ThreeSteps();
        Assert.True(w.IsFirst);
        Assert.False(await w.FinishAsync());          // not on the last step yet

        Assert.True(await w.NextAsync());
        Assert.True(await w.NextAsync());
        Assert.True(w.IsLast);
        Assert.False(await w.NextAsync());            // no step beyond the last

        Assert.True(await w.FinishAsync());
        Assert.True(w.IsFinished);
        Assert.False(await w.FinishAsync());          // finishing is once
    }

    [Fact]
    public async Task A_refusing_step_blocks_next_with_its_sentence_and_back_clears_it()
    {
        var allow = false;
        var w = ThreeSteps(() => Task.FromResult<string?>(allow ? null : "Pick a name first."));
        await w.NextAsync();                          // onto the gated step

        Assert.False(await w.NextAsync());
        Assert.Equal("Pick a name first.", w.Refusal);
        Assert.Equal(1, w.CurrentIndex);              // still standing on the refused step

        Assert.True(w.Back());
        Assert.Null(w.Refusal);                       // retreating clears the complaint

        await w.NextAsync();
        allow = true;
        Assert.True(await w.NextAsync());
        Assert.Null(w.Refusal);
    }

    [Fact]
    public async Task Back_never_validates_and_stops_at_the_first_step()
    {
        var w = ThreeSteps(() => Task.FromResult<string?>("Never leaves forward."));
        await w.NextAsync();

        Assert.True(w.Back());                        // the gate refuses FORWARD, not back
        Assert.False(w.Back());                       // already at the first step
    }

    [Fact]
    public async Task A_draft_restore_clamps_rather_than_throws()
    {
        var w = ThreeSteps();
        w.Restore(99);                                // a draft from a longer, older wizard
        Assert.Equal(2, w.CurrentIndex);
        w.Restore(-5);
        Assert.Equal(0, w.CurrentIndex);

        w.Restore(1);
        Assert.Equal(1, w.CurrentIndex);
        Assert.True(await w.NextAsync());
    }

    [Fact]
    public void A_tour_runs_forward_and_completing_differs_from_skipping()
    {
        var t = new TourModel(
        [
            new TourStep("#a", "A", "a"),
            new TourStep("#b", "B", "b"),
        ]);

        t.Start();
        Assert.True(t.IsRunning);
        t.Next();
        Assert.True(t.IsLast);
        t.Next();                                     // next on the last step = completion
        Assert.False(t.IsRunning);
        Assert.True(t.Completed);

        var s = new TourModel([new TourStep("#a", "A", "a"), new TourStep("#b", "B", "b")]);
        s.Start();
        s.Skip();
        Assert.False(s.IsRunning);
        Assert.False(s.Completed);                    // ended, but not seen through
    }

    [Fact]
    public void A_tour_backs_up_but_not_past_the_start_and_ignores_input_when_idle()
    {
        var t = new TourModel([new TourStep("#a", "A", "a"), new TourStep("#b", "B", "b")]);

        t.Next();                                     // not started: inert
        Assert.False(t.IsRunning);

        t.Start();
        t.Back();                                     // at the first step: stays
        Assert.Equal(0, t.CurrentIndex);
        t.Next();
        t.Back();
        Assert.Equal(0, t.CurrentIndex);
    }
}
