using Ben.Web.Website.Library.Shared;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// A marker may only claim what somebody actually checked.
/// </summary>
/// <remarks>
/// Ben, 2026-09-09, asking for landmarks to look different on the map — and, separately: "I don't
/// think I would add the public locations to an organization's map unless they have done an
/// investigation at the location". The map has always plotted investigations rather than places,
/// so that already held; what changed is only how an existing pin is drawn.
///
/// The three-state field is the part worth guarding. Two of the five callers know the place kind;
/// the other three do not, and an unknown must keep the marker it had rather than be drawn as
/// somebody's home.
/// </remarks>
public class InvestigationMapPinTests
{
    private static InvestigationMapPin Pin(bool? isPublicPlace = null) =>
        new(Guid.NewGuid(), "Bell Witch Cave — autumn survey", 36.58m, -87.06m, IsPast: false, isPublicPlace);

    [Fact]
    public void A_caller_that_says_nothing_leaves_the_place_unknown()
        => Assert.Null(new InvestigationMapPin(Guid.NewGuid(), "t", 1m, 1m, IsPast: false).IsPublicPlace);

    [Fact]
    public void Unknown_is_not_the_same_as_private()
    {
        Assert.Null(Pin(null).IsPublicPlace);
        Assert.False(Pin(false).IsPublicPlace);
        Assert.NotEqual(Pin(null), Pin(false));
    }

    [Fact]
    public void A_landmark_is_distinguishable_from_a_home()
        => Assert.NotEqual(Pin(true), Pin(false));

    [Fact]
    public void Nothing_else_about_the_pin_moved()
    {
        var pin = Pin(true);

        Assert.Equal("Bell Witch Cave — autumn survey", pin.Title);
        Assert.Equal(36.58m, pin.Latitude);
        Assert.False(pin.IsPast);
        Assert.True(pin.IsPublicPlace);
    }
}
