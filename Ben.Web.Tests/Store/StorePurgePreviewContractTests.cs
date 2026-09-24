using System.Text.Json;
using Ben.Web.Website.Library.SuperAdmin;
using Xunit;
using ServiceRecord = Ben.Data.WebApi.Services.Admin.AppUserPurgePreview;
using PageRecord = Ben.Service.Models.Admin.AppUserPurgePreview;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The delete-a-person screen says how many store orders are kept, and how many keep an address
/// because they are still on their way (storefront S0.12).
/// </summary>
/// <remarks>
/// <para>Two records share the name <c>AppUserPurgePreview</c>: the API returns the service's and
/// the page reads the DTO's. A count added to one and misspelt in the other would not fail to
/// compile — it would arrive as 0 and the screen would promise nothing is kept. So the contract is
/// checked the way it travels, through JSON.</para>
///
/// <para>Not an HtmlRenderer fact: the page loads its preview in <c>OnAfterRenderAsync</c>, which
/// a static render never runs, so a render test could only ever see the empty page.</para>
/// </remarks>
public sealed class StorePurgePreviewContractTests
{
    [Fact]
    public void The_kept_order_counts_survive_the_trip_from_the_API_to_the_page()
    {
        var sent = new ServiceRecord(
            Guid.NewGuid(), "Sarah Hollow", "sarah@example.com", false,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0,
            StoreOrdersKept: 3, StoreOrdersStillShipping: 1,
            OtherAuthoredRecords: 0,
            RowWillSurvive: false, OwnedOrganizations: [], PaidSubscriptions: [], Refusal: null);

        var received = JsonSerializer.Deserialize<PageRecord>(
            JsonSerializer.Serialize(sent, JsonSerializerOptions.Web), JsonSerializerOptions.Web)!;

        Assert.Equal((3, 1), (received.StoreOrdersKept, received.StoreOrdersStillShipping));
    }

    [Theory]
    [InlineData(1, "1 store order is still on its way, so it keeps its delivery address until it is delivered.")]
    [InlineData(3, "3 store orders are still on their way, so they keep their delivery address until they are delivered.")]
    public void The_screen_says_which_orders_keep_an_address(int orders, string sentence)
        => Assert.Equal(sentence, AdminDeleteUser.StillShippingSentence(orders));
}
