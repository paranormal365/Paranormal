using Microsoft.Playwright;

namespace Ben.Canvas.Playwright.Support;

/// <summary>One flavour placed on a synthesised clipboard or drop.</summary>
public sealed record Flavour(string Type, string Text);

/// <summary>A file made in the page: a picture drawn on a canvas, or plain bytes.</summary>
/// <param name="Kind">"png", "jpeg", "jpeg-with-exif", "heic" or "bytes".</param>
public sealed record MadeFile(string Kind, string Name, string Type, string? Text = null, int Width = 40, int Height = 30);

/// <summary>
/// Drives the board's paste and drop the way a browser delivers them.
/// </summary>
/// <remarks>
/// Two drivers. The synthesised one builds a DataTransfer in the page and dispatches a real ClipboardEvent or
/// DragEvent, which is the only way to paste files and several flavours at once in headless Chromium. The real
/// one uses the browser's own clipboard with permission granted, and proves the keyboard path end to end.
/// </remarks>
public static class PasteDriver
{
    private const string MakeFiles = """
        async function makeFiles(files) {
          const made = []
          for (const f of files) {
            if (f.kind === 'png' || f.kind === 'jpeg' || f.kind === 'jpeg-with-exif') {
              const canvas = new OffscreenCanvas(f.width, f.height)
              const ctx = canvas.getContext('2d')
              ctx.fillStyle = '#37508a'; ctx.fillRect(0, 0, f.width, f.height)
              ctx.fillStyle = '#ffc107'; ctx.fillRect(0, 0, f.width / 2, f.height / 2)
              let blob = await canvas.convertToBlob({ type: f.kind === 'png' ? 'image/png' : 'image/jpeg', quality: 0.9 })
              if (f.kind === 'jpeg-with-exif') {
                const bytes = new Uint8Array(await blob.arrayBuffer())
                const tiff = [0x4d, 0x4d, 0x00, 0x2a, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00]
                const body = [0x45, 0x78, 0x69, 0x66, 0x00, 0x00, ...tiff]
                const length = body.length + 2
                const app1 = new Uint8Array([0xff, 0xe1, length >> 8, length & 0xff, ...body])
                const at = bytes[3] === 0xe0 ? 4 + ((bytes[4] << 8) | bytes[5]) : 2
                blob = new Blob([bytes.slice(0, at), app1, bytes.slice(at)], { type: 'image/jpeg' })
              }
              made.push(new File([blob], f.name, { type: f.type }))
            } else if (f.kind === 'heic') {
              made.push(new File([new Uint8Array([0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x63, 1, 2, 3, 4])], f.name, { type: f.type }))
            } else {
              made.push(new File([f.text || ''], f.name, { type: f.type }))
            }
          }
          return made
        }
        """;

    /// <summary>Focuses the board and dispatches a paste carrying the given flavours and files.</summary>
    public static Task PasteAsync(this IPage page, IEnumerable<Flavour>? flavours = null, IEnumerable<MadeFile>? files = null) =>
        page.EvaluateAsync("async ({ flavours, files }) => {\n" + MakeFiles + """
              const dt = new DataTransfer()
              for (const f of flavours) dt.setData(f.type, f.text)
              for (const file of await makeFiles(files)) dt.items.add(file)
              document.querySelector('.bc-board').focus()
              document.dispatchEvent(new ClipboardEvent('paste', { clipboardData: dt, bubbles: true, cancelable: true }))
            }
            """, new { flavours = (flavours ?? []).Select(f => new { type = f.Type, text = f.Text }), files = Files(files) });

    /// <summary>Drags files and flavours over the board and drops them at a point in the viewport.</summary>
    public static Task DropAsync(this IPage page, double clientX, double clientY, IEnumerable<Flavour>? flavours = null, IEnumerable<MadeFile>? files = null, bool stopBeforeDrop = false) =>
        page.EvaluateAsync("async ({ flavours, files, x, y, stop }) => {\n" + MakeFiles + """
              const dt = new DataTransfer()
              for (const f of flavours) dt.setData(f.type, f.text)
              for (const file of await makeFiles(files)) dt.items.add(file)
              const board = document.querySelector('.bc-board')
              const at = { dataTransfer: dt, bubbles: true, cancelable: true, clientX: x, clientY: y }
              board.dispatchEvent(new DragEvent('dragenter', at))
              board.dispatchEvent(new DragEvent('dragover', at))
              if (!stop) board.dispatchEvent(new DragEvent('drop', at))
            }
            """, new { flavours = (flavours ?? []).Select(f => new { type = f.Type, text = f.Text }), files = Files(files), x = clientX, y = clientY, stop = stopBeforeDrop });

    private static object Files(IEnumerable<MadeFile>? files) =>
        (files ?? []).Select(f => new { kind = f.Kind, name = f.Name, type = f.Type, text = f.Text, width = f.Width, height = f.Height }).ToList();

    /// <summary>The bytes behind a picture block's src, read back from the device store.</summary>
    public static Task<int[]> PictureBytesAsync(this ILocator image) =>
        image.EvaluateAsync<int[]>("async img => [...new Uint8Array(await (await fetch(img.src)).arrayBuffer())]");
}
