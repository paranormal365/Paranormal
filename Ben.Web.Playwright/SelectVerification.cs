using Microsoft.Playwright;
using NUnit.Framework;
using System.Text.RegularExpressions;

namespace Ben.Web.Playwright;

/// <summary>
/// The native selects that replaced TelerikDropDownList. A build proves they compile; only driving
/// them proves the value round-trips — the failure mode is a control that renders perfectly and
/// silently writes back nothing.
/// </summary>
[TestFixture]
public class SelectVerification : BenTestBase
{
    [Test]
    public async Task UploadFiles_FileTypeSelect_IsNativeAndSelectionSticks()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/upload-files");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await ClickUntilAsync(Page.GetByRole(AriaRole.Button, new() { Name = "Upload New File" }),
                              Main.GetByText("New Upload", new() { Exact = false }));

        var select = Main.Locator("select.form-select").First;
        await Expect(select).ToBeVisibleAsync(new() { Timeout = 8_000 });

        // Native, not a Telerik popup: the options are real <option> elements.
        var options = await select.Locator("option").AllInnerTextsAsync();
        Assert.That(options.Count, Is.GreaterThan(1), "expected the file types to render as options");

        // Round-trip: pick the last option and confirm the control holds it. This is what a
        // mis-converted value would break — the list renders, the selection does not stick.
        var last = options[^1];
        await select.SelectOptionAsync(new SelectOptionValue { Label = last });

        var chosen = await select.EvaluateAsync<string>("el => el.options[el.selectedIndex].text");
        Assert.That(chosen, Is.EqualTo(last), "the select did not keep the chosen option");
    }

    [Test]
    public async Task NoTelerikDropdownPopupsRemainOnConvertedPages()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        foreach (var path in new[] { "/upload-files", "/organizations", "/admin/users" })
        {
            await Page.GotoAsync($"{BaseUrl}{path}");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await WaitUntilLoadedAsync();

            // Scoped to dropdowns outside a Telerik grid. The grid stays Telerik by design and
            // renders its own dropdowns for paging and filtering; counting those made the check
            // fail on pages that had been converted correctly.
            var strayDropdowns = await Page.EvaluateAsync<int>(@"() =>
                [...document.querySelectorAll('.k-dropdownlist')]
                    .filter(el => !el.closest('.k-grid') && !el.closest('.k-pager'))
                    .length");

            Assert.That(strayDropdowns, Is.Zero,
                $"{path} still renders a Telerik dropdown outside a grid; it should be a native select");
        }
    }
}
