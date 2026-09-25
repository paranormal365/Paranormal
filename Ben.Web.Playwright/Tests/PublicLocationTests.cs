using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A public location, from naming it to what its evidence says (item 250).
/// </summary>
/// <remarks>
/// <para>Walked as Wren, who belongs to no group, has no case and is nobody's client. That is the
/// point of the whole feature: the person who knows what Cragfont is, and who has a photograph of
/// it, is rarely a member of a paranormal group. Anything she can reach here she reached with an
/// account and nothing else.</para>
///
/// <para>Each test founds its OWN place. These walks write rows that outlive the run, and sharing
/// one would make them depend on each other's order — the mistake the guest-code walks made
/// before they were given a code each.</para>
/// </remarks>
[TestFixture]
public sealed class PublicLocationTests : BenTestBase
{
    /// <summary>A name no other run will have used.</summary>
    private static string ANewName() => $"Walk House {Guid.NewGuid():N}"[..24];

    /// <summary>Founds a public location and returns the page's address.</summary>
    private async Task<string> ANewPlaceAsync(string name)
    {
        await Page.GotoAsync($"{BaseUrl}/places/new");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();

        await Page.Locator("#newplace-name").FillAsync(name);
        await Page.Locator("#newplace-street").FillAsync($"{Random.Shared.Next(1, 9999)} Walk Lane");
        await Page.Locator("#newplace-city").FillAsync("Castalian Springs");
        await Page.Locator("#newplace-state").FillAsync("TN");

        await ClickUntilUrlAsync(Page.Locator("#newplace-add"), @"/places/[0-9a-f\-]{36}");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();

        Assert.That(Page.Url, Does.Match(@"/places/[0-9a-f\-]{36}"),
            $"adding a place should land on its page, not {Page.Url}");
        return new Uri(Page.Url).AbsolutePath;
    }

    private async Task AddAsync(string caption, bool asEvidence)
    {
        await Page.Locator("#place-evidence-file").SetInputFilesAsync(new FilePayload
        {
            Name = "corridor.png",
            MimeType = "image/png",
            // A one-pixel PNG. The ingest re-encodes it, so what matters is that it decodes.
            Buffer = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII="),
        });
        await Page.Locator("#place-evidence-caption").FillAsync(caption);
        await Page.Locator("#place-evidence-kind")
            .SelectOptionAsync(asEvidence ? "Evidence" : "AboutThePlace");

        await ClickUntilAsync(Page.Locator("#place-evidence-add"), Page.Locator("#place-evidence-says"));
        await Assertions.Expect(Page.Locator("#place-evidence-says")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Somebody_in_no_group_can_put_a_place_on_the_map()
    {
        await LoginAsync(SoloEmail, SoloPassword);

        var name = ANewName();
        var address = await ANewPlaceAsync(name);

        await Assertions.Expect(Main).ToContainTextAsync(name);

        // The same address again is not a second place. Two rows for one building split its
        // evidence in half and make the merge screen somebody's afternoon.
        await Page.GotoAsync($"{BaseUrl}{address}");
        await WaitUntilLoadedAsync();
        Assert.That(new Uri(Page.Url).AbsolutePath, Is.EqualTo(address));
    }

    [Test]
    public async Task The_add_door_is_reachable_from_whats_near_you()
    {
        await LoginAsync(SoloEmail, SoloPassword);

        await Page.GotoAsync($"{BaseUrl}/");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();

        // Reachable with NO location search having run, which is the whole point. The link lived
        // inside the Places tab until this walk was written, and those tabs only render once a
        // nearby search has succeeded — so anybody who declined the browser's location prompt
        // could not reach the one door that lets them add a place. A link to "put a place on the
        // map" must not require already having found places on the map.
        var link = Page.Locator("[data-testid=add-public-place]");
        await Assertions.Expect(link).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(link).ToHaveAttributeAsync("href", "/places/new");
    }

    [Test]
    public async Task What_the_place_is_can_be_written_and_read_back()
    {
        await LoginAsync(SoloEmail, SoloPassword);
        await ANewPlaceAsync(ANewName());

        await ClickUntilAsync(Page.Locator("#place-about-edit"), Page.Locator("#place-about-text"));
        await Page.Locator("#place-about-text")
            .FillAsync("Built in 1802 on the Cumberland, and empty since the war.");
        await ClickUntilAsync(Page.Locator("#place-about-save"), Page.Locator("#place-about-edit"));

        await Assertions.Expect(Page.Locator("#place-about"))
            .ToContainTextAsync("empty since the war");
    }

    [Test]
    public async Task A_picture_of_the_building_is_not_counted_as_evidence()
    {
        await LoginAsync(SoloEmail, SoloPassword);
        await ANewPlaceAsync(ANewName());

        await AddAsync("The upstairs corridor, about 11pm", asEvidence: true);
        await AddAsync("The frontage from the drive", asEvidence: false);

        await Page.ReloadAsync();
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();

        // The rule the whole slice is built around. One piece of evidence, and the photograph of
        // the building listed apart from it — mixed together every figure on the page is wrong.
        await Assertions.Expect(Page.Locator("#place-figures-evidence")).ToHaveTextAsync("1");
        await Assertions.Expect(Main).ToContainTextAsync("The place itself");
        await Assertions.Expect(Main).ToContainTextAsync("The frontage from the drive");
    }

    [Test]
    public async Task A_stranger_reads_the_evidence_without_signing_in()
    {
        await LoginAsync(SoloEmail, SoloPassword);
        var address = await ANewPlaceAsync(ANewName());
        await AddAsync("Something on the stairs", asEvidence: true);

        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}{address}");
        await WaitUntilLoadedAsync();

        await Assertions.Expect(Main).ToContainTextAsync("Something on the stairs");

        // A visitor may read it and may not add to it. An account is required because evidence
        // nobody stands behind is worth nothing to read and impossible to moderate.
        Assert.That(await Page.Locator("#place-add-evidence").CountAsync(), Is.Zero,
            "a signed-out visitor should not be offered the add form");
    }
}
