using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The photo editor opened from a user's Files tab.
/// </summary>
/// <remarks>
/// Ben, 09/25/2026: "There is no preview and there is no obvious way to use it. The window should
/// be larger to allow for a preview - even if the preview has to be scaled and allow them to zoom
/// in and out." It opened in the smallest dialog size, where the two side panels left the picture a
/// width of zero. Behind that were two more faults, which showed up once the picture was visible:
/// <list type="bullet">
/// <item>every shape landed half its own size up and to the left of the pointer, because the
/// vendored Fabric (7) places new objects by their centre;</item>
/// <item>"Save as New Version" could not have worked, because the picture came back to the server as
/// one SignalR message, which is capped at 128 KB.</item>
/// </list>
/// </remarks>
[TestFixture]
[Category("Admin")]
public class ImageEditorTests : BenTestBase
{
    [SetUp]
    public async Task SignInAsSuperAdmin() => await LoginAsync(SuperAdminEmail, SuperAdminPassword);

    [Test]
    public async Task The_editor_shows_the_photo_large_enough_to_work_on_and_zooms()
    {
        await OpenPhotoEditorOnAFreshUploadAsync();

        var dialog = Page.Locator(".modal-dialog.modal-fullscreen");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The photo loaded, at its real size (the fixture is 1600 x 1000).
        await Expect(dialog.GetByText("1600 × 1000 pixels")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // And the picture area is a real size. In the old dialog this was 0 wide.
        var box = await dialog.Locator("canvas.lower-canvas").BoundingBoxAsync();
        Assert.That(box, Is.Not.Null);
        Assert.That(box!.Width, Is.GreaterThan(500), "The picture area should take most of the screen.");
        Assert.That(box.Height, Is.GreaterThan(300));

        // Every tool says what it is.
        foreach (var label in new[] { "Select", "Pen", "Text", "Rectangle", "Ellipse", "Line", "Redact", "Ruler" })
            await Expect(dialog.Locator($"[data-tool]", new() { HasTextString = label }).First).ToBeVisibleAsync();

        var zoom = dialog.GetByTestId("image-editor-zoom");
        var fitted = await zoom.InnerTextAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Zoom in" }).ClickAsync();
        await Expect(zoom).Not.ToHaveTextAsync(fitted, new() { Timeout = 5_000 });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Fit" }).ClickAsync();
        await Expect(zoom).ToHaveTextAsync(fitted, new() { Timeout = 5_000 });
    }

    [Test]
    public async Task A_rectangle_starts_where_the_pointer_was_pressed()
    {
        await OpenPhotoEditorOnAFreshUploadAsync();
        var dialog = Page.Locator(".modal-dialog.modal-fullscreen");
        await Expect(dialog.GetByText("1600 × 1000 pixels")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await dialog.Locator("[data-tool='rect']").ClickAsync();
        await Expect(dialog.GetByTestId("image-editor-hint")).ToContainTextAsync("rectangle");

        var box = (await dialog.Locator("canvas.upper-canvas").BoundingBoxAsync())!;
        var startX = box.X + box.Width * 0.35f;
        var startY = box.Y + box.Height * 0.35f;
        await Page.Mouse.MoveAsync(startX, startY);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(startX + 120, startY + 80, new() { Steps = 8 });
        await Page.Mouse.UpAsync();

        await Expect(dialog.GetByText("Layers (1)")).ToBeVisibleAsync(new() { Timeout = 5_000 });

        // The rectangle's red corner is under the point that was pressed, not 60 x 40 up and left.
        var redAtThePress = await RedNearAsync(dialog, startX, startY);
        Assert.That(redAtThePress, Is.True,
            "The rectangle's corner should be where the drag began.");
    }

    /// <summary>
    /// "Save State" kept the work and nothing ever loaded it back (09/25/2026). Now a rectangle
    /// saved, closed and reopened is there again, in the same place on the photo.
    /// </summary>
    [Test]
    public async Task Saved_work_is_there_when_the_photo_is_reopened()
    {
        var name = await OpenPhotoEditorOnAFreshUploadAsync();
        var dialog = Page.Locator(".modal-dialog.modal-fullscreen");
        await Expect(dialog.GetByText("1600 × 1000 pixels")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await dialog.Locator("[data-tool='rect']").ClickAsync();
        await Expect(dialog.GetByTestId("image-editor-hint")).ToContainTextAsync("rectangle");
        var box = (await dialog.Locator("canvas.upper-canvas").BoundingBoxAsync())!;
        var startX = box.X + box.Width * 0.35f;
        var startY = box.Y + box.Height * 0.35f;
        await Page.Mouse.MoveAsync(startX, startY);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(startX + 120, startY + 80, new() { Steps = 8 });
        await Page.Mouse.UpAsync();
        await Expect(dialog.GetByText("Layers (1)")).ToBeVisibleAsync(new() { Timeout = 5_000 });

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save State" }).ClickAsync();
        await Expect(Page.GetByText("Edit state saved.")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await dialog.Locator(".modal-footer").GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
        await Expect(dialog).Not.ToBeVisibleAsync(new() { Timeout = 5_000 });

        await Page.Locator("tr", new() { HasTextString = name }).Locator("button[title='Edit image']").ClickAsync();
        await Expect(dialog.GetByTestId("image-editor-restored")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(dialog.GetByText("Layers (1)")).ToBeVisibleAsync(new() { Timeout = 5_000 });

        // Same fit, same place: the corner is still under the point where the drag began.
        Assert.That(await RedNearAsync(dialog, startX, startY), Is.True,
            "The reopened rectangle should be where it was drawn.");
    }

    /// <summary>Whether the picture has a strongly red pixel within a few pixels of a page point.</summary>
    private static Task<bool> RedNearAsync(ILocator dialog, double pageX, double pageY)
        => dialog.Locator("canvas.lower-canvas").EvaluateAsync<bool>(@"(c, p) => {
            const r = c.getBoundingClientRect();
            const sx = c.width / r.width, sy = c.height / r.height;
            const x = Math.round((p.x - r.left) * sx), y = Math.round((p.y - r.top) * sy);
            const d = c.getContext('2d').getImageData(x - 5, y - 5, 11, 11).data;
            for (let i = 0; i < d.length; i += 4)
                if (d[i] > 200 && d[i + 1] < 70 && d[i + 2] < 70) return true;
            return false;
        }", new { x = pageX, y = pageY });
}
