using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Canvas.Playwright.Tests;

/// <summary>
/// The published picture draws a shape as its shape.
/// </summary>
/// <remarks>
/// <para>Every block in the published picture is a titled rounded rectangle, which is right for all
/// of them but one: a shape IS its outline. Until 2026-09-18 the kind never left C#, so a published
/// moodboard's four theme bubbles came out as four grey rectangles and a published diamond came out
/// as the same rectangle. The templates walk found it, because a picture was the only thing that
/// could.</para>
///
/// <para><b>Why this drives the drawing directly.</b> The picture is made by canvas JavaScript, and
/// <c>BoardSnapshotTests</c> can only prove the kind reaches it — the half that was missing before,
/// and the half that would be missing again if the drawer ignored it. So this imports the module the
/// way the editor does, hands it a scene, and reads the pixels back: a diamond's box CORNER must
/// still be the board's own ground, and its middle must be the shape's colour. A rectangle drawn
/// where a diamond belongs fails on the corner; a shape not drawn at all fails in the middle.</para>
/// </remarks>
[TestFixture]
[Category("Blocks")]
public class CanvasPublishedShapeTests : CanvasTestBase
{
    /// <summary>
    /// Draws a one-shape scene and reads back the colour at a fraction across the shape's own box.
    /// </summary>
    private async Task<string> PixelAsync(string shape, double atX, double atY)
    {
        // A scene of one 200 x 200 shape at the origin, with a margin of board around it, drawn at
        // 1:1 so a world coordinate is a pixel.
        var rgba = await Page.EvaluateAsync<int[]>(
            """
            async ([shape, atX, atY]) => {
                const module = await window.benImportCanvasModule('js/snapshotInterop.js');
                const scene = {
                    pixelWidth: 300, pixelHeight: 300, scale: 1, x: -50, y: -50,
                    groups: [], connectors: [],
                    blocks: [{
                        x: 0, y: 0, width: 200, height: 200,
                        kind: 'shape', colorKey: '1', title: 'Why now', lines: [],
                        assetId: null, ext: null, uploadFileId: null, imageUrl: null,
                        filled: false, shape,
                    }],
                };

                const bytes = await module.drawBoard(document.querySelector('.bc-shell') ?? document.body, scene);
                const blob = new Blob([new Uint8Array(bytes)], { type: 'image/png' });
                const bitmap = await createImageBitmap(blob);
                const c = new OffscreenCanvas(bitmap.width, bitmap.height);
                const ctx = c.getContext('2d');
                ctx.drawImage(bitmap, 0, 0);

                // The block sits at world (0,0)-(200,200); the scene's origin is world -50.
                const px = Math.round(50 + 200 * atX);
                const py = Math.round(50 + 200 * atY);
                return [...ctx.getImageData(px, py, 1, 1).data];
            }
            """,
            new object[] { shape, atX, atY });

        return $"{rgba[0]},{rgba[1]},{rgba[2]},{rgba[3]}";
    }

    [Test]
    public async Task A_published_diamond_leaves_its_corners_empty()
    {
        await StartCleanAsync();

        var middle = await PixelAsync("diamond", 0.5, 0.5);
        var corner = await PixelAsync("diamond", 0.06, 0.06);
        var ground = await PixelAsync("diamond", -0.15, -0.15);

        Assert.Multiple(() =>
        {
            Assert.That(middle, Is.Not.EqualTo(ground),
                "the middle of a published diamond is not painted, so the shape was not drawn at all");
            Assert.That(corner, Is.EqualTo(ground),
                "a published diamond's box corner is painted, so it was drawn as a rectangle");
        });
    }

    [Test]
    public async Task A_published_ellipse_leaves_its_corners_empty()
    {
        await StartCleanAsync();

        var middle = await PixelAsync("ellipse", 0.5, 0.5);
        var corner = await PixelAsync("ellipse", 0.03, 0.03);
        var ground = await PixelAsync("ellipse", -0.15, -0.15);

        Assert.Multiple(() =>
        {
            Assert.That(middle, Is.Not.EqualTo(ground), "the middle of a published ellipse is not painted");
            Assert.That(corner, Is.EqualTo(ground),
                "a published ellipse's box corner is painted, so it was drawn as a rectangle");
        });
    }

    /// <summary>And a rectangle shape still fills its box, corners included.</summary>
    [Test]
    public async Task A_published_rectangle_shape_fills_its_box()
    {
        await StartCleanAsync();

        var middle = await PixelAsync("rectangle", 0.5, 0.5);
        var inside = await PixelAsync("rectangle", 0.15, 0.15);
        var ground = await PixelAsync("rectangle", -0.15, -0.15);

        Assert.Multiple(() =>
        {
            Assert.That(middle, Is.Not.EqualTo(ground), "a rectangle shape is not drawn");
            Assert.That(inside, Is.EqualTo(middle), "a rectangle shape does not fill its own box");
        });
    }
}
