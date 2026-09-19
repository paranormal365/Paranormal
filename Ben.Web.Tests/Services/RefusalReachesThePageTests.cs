using Ben.Service.Models.Admin;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Moq;
using System.Net;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The refusal a controller wrote reaches the person it was written for.
/// </summary>
/// <remarks>
/// <para>Turning on membership applications for a free group is refused with a sentence that
/// explains a plan. What the owner saw was "Save failed. The URL name may already be in use, or
/// you may not have permission." — a guess the page wrote because the client threw the body away
/// on any non-success. It named a clash that did not exist and doubted their permission on their
/// own group, at the exact moment they were willing to pay.</para>
///
/// <para>These pin the plumbing rather than the wording: the sentence survives the trip, and a
/// framework error page does not get shown to anybody.</para>
/// </remarks>
public sealed class RefusalReachesThePageTests
{
    private sealed class CannedHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response);
    }

    /// <summary>
    /// The real client over a canned response, behind the real adapter. Both halves are the point:
    /// the client is what recovers the sentence, and the adapter is what the page actually calls.
    /// </summary>
    private static IBenAdminClient Client(HttpResponseMessage canned)
        => new BenAdminClientAdapter(
            new WebApiClient(
                new HttpClient(new CannedHandler(canned)) { BaseAddress = new Uri("http://unit.test") },
                new WebApiTokenStore()),
            new Mock<IWebApiAuthService>().Object,
            Microsoft.Extensions.Options.Options.Create(new WebApiOptions()));

    private static HttpResponseMessage Refusal(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body) };

    private const string PaidGate =
        "Working with other people is part of a paid plan — a free group is just you. "
        + "Everybody already here stays; adding somebody new needs a plan.";

    private static AdminUpdateOrganizationRequest AnyUpdate()
        => new("A Group", "a-group", IsAcceptingApplications: true);

    [Fact]
    public async Task The_paid_gates_sentence_survives_the_trip_to_the_page()
    {
        var (result, error) = await Client(Refusal(HttpStatusCode.PaymentRequired, PaidGate))
            .UpdateOrganizationAsync(Guid.NewGuid(), AnyUpdate());

        Assert.Null(result);
        Assert.Equal(PaidGate, error);
    }

    [Fact]
    public async Task A_bad_request_sentence_survives_too()
    {
        const string taken = "That web address is already taken.";

        var (result, error) = await Client(Refusal(HttpStatusCode.BadRequest, taken))
            .UpdateOrganizationAsync(Guid.NewGuid(), AnyUpdate());

        Assert.Null(result);
        Assert.Equal(taken, error);
    }

    /// <summary>
    /// A 403 carries no sentence, and the page falls back to its own wording. The point of the
    /// change is not that every refusal now has prose — it is that the page stops inventing one
    /// when the server did supply it.
    /// </summary>
    [Fact]
    public async Task A_refusal_with_no_body_leaves_the_page_to_say_it_its_own_way()
    {
        var (result, error) = await Client(new HttpResponseMessage(HttpStatusCode.Forbidden))
            .UpdateOrganizationAsync(Guid.NewGuid(), AnyUpdate());

        Assert.Null(result);
        Assert.Null(error);
    }

    /// <summary>
    /// An unhandled exception comes back as a ProblemDetails blob or an HTML error page. Showing
    /// either to a person is worse than the guess, so the client keeps them out.
    /// </summary>
    [Fact]
    public async Task A_framework_error_is_not_passed_off_as_an_explanation()
    {
        var problem = Refusal(HttpStatusCode.InternalServerError,
            """{"type":"https://tools.ietf.org/html/rfc9110","title":"An error occurred","status":500}""");

        var (_, error) = await Client(problem).UpdateOrganizationAsync(Guid.NewGuid(), AnyUpdate());

        Assert.Null(error);
    }
}
