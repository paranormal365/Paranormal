using Ben.Web.Services.WebApi;

namespace Ben.Data.WebApi.Client.External;

/// <summary>
/// Takes on a session that arrived from an external provider rather than from a password form.
/// </summary>
/// <remarks>
/// <para>The two front ends keep a session in completely different places — a desktop app has one
/// signed-in person for the life of the process, a Blazor Server app has one per circuit — but they
/// do exactly the same thing with an Apple sign-in, because the endpoint answers with our own
/// Identity tokens. This is the seam that lets them share the client rather than write it twice and
/// drift.</para>
///
/// <para>An implementation must also resolve identity afterwards. Identity's bearer tokens are
/// opaque, so roles only ever come from <c>GET api/me</c>, and a session adopted without asking
/// looks like an ordinary member to somebody who is not one.</para>
/// </remarks>
public interface IExternalSignInAdopter
{
    Task AdoptExternalSignInAsync(WebApiTokenResponse response, CancellationToken token = default);
}
