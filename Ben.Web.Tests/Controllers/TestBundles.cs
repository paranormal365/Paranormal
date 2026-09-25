using Ben.Data.Common.Interfaces;
using Ben.Data.WebApi.Services.FieldSessions;
using Microsoft.Extensions.Caching.Memory;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The real bundle store over whatever storage a test is already using.
/// </summary>
/// <remarks>
/// A real one rather than a mock: these tests exercise the sessions that arrive as a document plus
/// loose recordings, which never touch a bundle at all — so the only thing a fake would prove is
/// that the constructor takes an argument. Handed the test's own storage, it behaves exactly as it
/// does in the app if a test ever does reach for it.
/// </remarks>
internal static class TestBundles
{
    public static IBenBundleStore Store(IFileStorageService storage)
        => new BenBundleStore(storage, new MemoryCache(new MemoryCacheOptions()));
}
