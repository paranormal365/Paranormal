using System.Reflection;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The public hosted-event writes that cost something carry the booking limit, not only the site-wide one
/// (item 235 phase 17a, audit finding A2).
/// </summary>
/// <remarks>
/// The plan put a 30-a-minute limit on the public booking writes; the first build put it on holds alone. Asking for a
/// place, changing a booking, posting to the room (every photo is screened), sending a photo on, reporting a post,
/// reviewing and signing up for a session are the writes a script would hammer. Named here, so one quietly losing
/// its attribute fails the build rather than a Saturday.
/// </remarks>
public sealed class HostedPublicWritesAreRateLimitedTests
{
    public static TheoryData<Type, string> Writes => new()
    {
        { typeof(PublicHostedEventBookingController), nameof(PublicHostedEventBookingController.HoldPlaces) },
        { typeof(PublicHostedEventBookingController), nameof(PublicHostedEventBookingController.RequestAPlace) },
        { typeof(PublicHostedEventBookingController), nameof(PublicHostedEventBookingController.UpdateMyBooking) },
        { typeof(PublicHostedEventRoomController), nameof(PublicHostedEventRoomController.Post) },
        { typeof(PublicHostedEventRoomController), nameof(PublicHostedEventRoomController.SendToHosts) },
        { typeof(PublicHostedEventRoomController), nameof(PublicHostedEventRoomController.Report) },
        { typeof(PublicHostedEventReviewController), nameof(PublicHostedEventReviewController.Upsert) },
        { typeof(PublicHostedEventProgrammeController), nameof(PublicHostedEventProgrammeController.SignUp) },
    };

    [Theory]
    [MemberData(nameof(Writes))]
    public void The_write_carries_the_hosted_booking_limit(Type controller, string action)
    {
        var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        var limit = method!.GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.True(limit is not null, $"{controller.Name}.{action} has no rate-limit policy of its own.");
        Assert.Equal(RateLimiting.HostedBookingPolicy, limit!.PolicyName);
    }
}
