using Ben.Service.Models.Store;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The order doors' 404 means "not for you" — the pages must see nothing there, not a failure
/// (storefront S4.13, found by StoreOrderTests.A_stranger_sees_no_order).
/// </summary>
public sealed class StoreOrderDoorClientTests
{
    private static BenAdminClientAdapter Answering(int status, StoreOrderView? body = null)
    {
        var api = new Mock<IWebApiClient>();
        api.Setup(a => a.SendWithStatusAsync<object, StoreOrderView>(HttpMethod.Get, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((body, status >= 400 ? "refused" : null, status));
        return new BenAdminClientAdapter(api.Object, new Mock<IWebApiAuthService>().Object,
            Microsoft.Extensions.Options.Options.Create(new WebApiOptions()));
    }

    [Fact]
    public async Task Not_yours_is_nothing_there()
    {
        var read = await Answering(404).GetStoreOrderAsync(Guid.NewGuid(), null);
        Assert.True(read.IsEmpty);
        Assert.False(read.Failed);
    }

    [Fact]
    public async Task A_server_fault_is_a_failure_with_a_retry()
    {
        var read = await Answering(500).GetStoreOrderAsync(Guid.NewGuid(), null);
        Assert.True(read.Failed);
    }

    [Fact]
    public async Task The_token_rides_the_address()
    {
        var api = new Mock<IWebApiClient>();
        api.Setup(a => a.SendWithStatusAsync<object, StoreOrderView>(HttpMethod.Get, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((StoreOrderView?)null, (string?)null, 404));
        var adapter = new BenAdminClientAdapter(api.Object, new Mock<IWebApiAuthService>().Object,
            Microsoft.Extensions.Options.Options.Create(new WebApiOptions()));
        var id = Guid.NewGuid();

        await adapter.GetStoreOrderAsync(id, "a+b/c");

        api.Verify(a => a.SendWithStatusAsync<object, StoreOrderView>(HttpMethod.Get, $"/api/store/orders/{id}?t=a%2Bb%2Fc", null, It.IsAny<CancellationToken>()));
    }
}
