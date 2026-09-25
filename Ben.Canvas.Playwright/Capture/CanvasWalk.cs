using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Persistence;
using Ben.Canvas.Playwright.Support;
using Ben.Canvas.Playwright.Touch;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Ben.Canvas.Playwright.Capture;

/// <summary>
/// The milestone walk: one board photographed at three sizes in both themes, with a record of what each
/// picture is meant to show and what the page was doing when it was taken.
/// </summary>
/// <remarks>
/// <para>Opt-in with BEN_CANVAS_WALK=1. The pictures and report.md land in BEN_CANVAS_WALK_OUT, or in
/// canvas-walk beside the test results.</para>
///
/// <para>The board is opened through the Import file input rather than built by clicking the rail, so all
/// three sizes photograph exactly the same content and the shots differ only by layout. The three server
/// pictures (saved to case, the newer-copy choice, the publish question) need the API and BEN_USER_PASSWORD;
/// without them those rows say so in the report instead of failing the walk.</para>
///
/// <para>Each row records the console errors, the responses of 500 or worse, and the hosts contacted since
/// the previous picture. Apple's map script is the only outside host the canvas may reach, and only once a
/// map box has been opened - which this walk never does, so the expected answer is none.</para>
/// </remarks>
[Category("Capture")]
[NonParallelizable]
[TestFixture("desktop", 1440, 900, "dark")]
[TestFixture("desktop", 1440, 900, "light")]
[TestFixture("ipad", 768, 1024, "dark")]
[TestFixture("ipad", 768, 1024, "light")]
[TestFixture("iphone", 390, 844, "dark")]
[TestFixture("iphone", 390, 844, "light")]
public sealed class CanvasWalk(string device, int width, int height, string theme) : PageTest
{
    private static string CanvasUrl => (Environment.GetEnvironmentVariable("BEN_CANVAS_URL") ?? "http://localhost:5125").TrimEnd('/');

    private static string ApiUrl => (Environment.GetEnvironmentVariable("BEN_API_URL") ?? "http://localhost:5252").TrimEnd('/');

    private bool IsPhone => device == "iphone";

    private bool IsTouch => device != "desktop";

    private readonly List<string> _consoleErrors = [];
    private readonly List<string> _serverErrors = [];
    private readonly List<string> _thirdParty = [];

    private IPage _page = null!;
    private TouchDriver? _touch;
    private int _shot;

    [Test]
    public async Task Capture_the_board()
    {
        if (Environment.GetEnvironmentVariable("BEN_CANVAS_WALK") != "1")
            Assert.Ignore("Set BEN_CANVAS_WALK=1 to capture the walk.");

        WalkReport.Reset(device, theme);

        await using var context = await Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = height },
            DeviceScaleFactor = 1,
            HasTouch = IsTouch,
            IsMobile = IsTouch,
            ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
        });

        // The theme boot reads these before the first paint, so seeding them avoids a flash of the other theme.
        await context.AddInitScriptAsync(
            $"localStorage.setItem('ben-theme', '{theme}');" +
            $"localStorage.setItem('layoutSettings', JSON.stringify({{ theme: '{theme}' }}));" +
            "localStorage.setItem('bc-persist-told', '1');");

        _page = await context.NewPageAsync();
        _page.Console += (_, m) => { if (m.Type == "error") _consoleErrors.Add(Trim(m.Text)); };
        _page.PageError += (_, e) => _consoleErrors.Add(Trim(e));
        _page.Response += (_, r) =>
        {
            if (r.Status >= 500) _serverErrors.Add($"{r.Status} {Trim(r.Url)}");
            var host = new Uri(r.Url).Host;
            if (host != new Uri(CanvasUrl).Host && host != new Uri(ApiUrl).Host) _thirdParty.Add(host);
        };

        if (IsTouch) _touch = await TouchDriver.StartAsync(_page, Browser);

        await OpenAsync("/");
        await ShotAsync("empty", "An untouched board says it is empty and offers a first card.");

        await ImportAsync();
        await FitAsync();
        await ShotAsync("node-types", "Every block type, a labelled connector and a group, drawn at this size.");

        await SelectFirstAsync("card");
        await ShotAsync("selected-with-handles", "A selected card carries resize handles and connect ports.");

        await OpenPropertiesAsync();
        await ShotAsync("properties", IsPhone
            ? "Properties open as a sheet from the bottom, with the board still visible above it."
            : "Properties sit in their own column beside the board.");
        await ScrollPropertiesToColourAsync();
        await ShotAsync("colour-swatches", "The six board colours, with the current one marked by more than colour alone.");
        await ClosePropertiesAsync();

        await SelectEdgeAsync();
        await ShotAsync("connector-selected", "A selected connector shows its ends, its arrow and its label.");

        await SelectGroupAsync();
        await ShotAsync("group", "A group frames its members and carries its name on a chip.");

        await ContextMenuAsync();
        await ShotAsync("context-menu", IsTouch
            ? "A press and hold opens the same menu a right-click gives on a desktop."
            : "Right-clicking a block opens its menu.");
        await DismissAsync();

        await FitAsync();
        await ShotAsync("zoom-and-minimap", IsPhone
            ? "The zoom controls sit above the bottom bar, and the minimap is hidden at this width."
            : "The zoom controls and the minimap sit clear of the board's corners.");

        await HelpAsync();
        await ShotAsync("shortcut-help", IsTouch
            ? "The help panel leads with the gestures, not the keyboard."
            : "The help panel lists the keyboard shortcuts.");
        await DismissAsync();

        await RefuseAHeicAsync();
        await ShotAsync("paste-refusal", "An iPhone photo that browsers cannot show is refused with the fix, not an error code.");
        await DismissAsync();

        await EditSomethingAsync();
        await ShotAsync("save-editing", "The header says the board has changes that are not saved yet.");

        await WaitForSavedLocallyAsync();
        await ShotAsync("save-saved-local", "Two seconds later the header says the board is saved on this device.");

        await OpenOverflowAsync();
        await ShotAsync("export-and-import", "Export and Import are one tap away at every size.");
        await DismissAsync();

        await OpenAsync("/login");
        await ShotAsync("sign-in", "The sign-in card carries the site's look and names the product.");

        await ServerShotsAsync();

        await context.CloseAsync();
    }

    // ── The board the walk photographs ────────────────────────────────

    private static CanvasDocument WalkBoard()
    {
        var card = new CanvasNode
        {
            Type = CanvasNodeType.Card,
            X = 0, Y = 0, Width = 300, Height = 220, Z = 0,
            Data = new CardData
            {
                Title = "Cold spot by the stairs",
                Fields =
                {
                    ["description"] = "Second floor landing, held for about four minutes.",
                    ["date"] = "2026-09-12",
                    ["category"] = "Witness",
                    ["verified"] = "true",
                },
            },
        };

        var note = new CanvasNode
        {
            Type = CanvasNodeType.Text,
            X = 360, Y = 0, Width = 240, Height = 140, Z = 1,
            ColorKey = "4",
            Data = new TextData { Text = "Recorder left running from 11pm. Battery died at 2:40am." },
        };

        var message = new CanvasNode
        {
            Type = CanvasNodeType.Message,
            X = 640, Y = 0, Width = 320, Height = 170, Z = 2,
            Data = new MessageData
            {
                Html = "<p>Heard footsteps above the kitchen, <strong>twice</strong>.</p>",
                Author = "Sarah Mitchell",
                TimestampUtc = new DateTime(2026, 9, 12, 23, 40, 0, DateTimeKind.Utc),
            },
        };

        var map = new CanvasNode
        {
            Type = CanvasNodeType.Map,
            X = 0, Y = 300, Width = 360, Height = 260, Z = 3,
            Data = new MapData { Address = "Belmont Mansion, Nashville", Latitude = 36.1340, Longitude = -86.7962 },
        };

        var picture = new CanvasNode
        {
            Type = CanvasNodeType.Image,
            X = 400, Y = 300, Width = 300, Height = 220, Z = 4,
            Data = new ImageData { Caption = "Landing, looking north" },
        };

        var link = new CanvasNode
        {
            Type = CanvasNodeType.Link,
            X = 740, Y = 300, Width = 320, Height = 140, Z = 5,
            Data = new LinkData { Url = "https://example.com/reports/belmont", SiteName = "example.com", Title = "Belmont: night two", Tier = LinkPreviewTier.HostOnly },
        };

        var file = new CanvasNode
        {
            Type = CanvasNodeType.File,
            X = 740, Y = 480, Width = 280, Height = 72, Z = 6,
            Data = new FileData { FileName = "belmont-night-two.pdf", Size = 2_411_000, ContentType = "application/pdf" },
        };

        var group = new CanvasGroup { Label = "Grounds", X = -40, Y = 250, Width = 780, Height = 340, Z = 0, ColorKey = "5" };
        map.GroupId = group.Id;
        picture.GroupId = group.Id;

        return new CanvasDocument
        {
            Title = "Belmont, night two",
            NextZ = 7,
            Nodes = [card, note, message, map, picture, link, file],
            Groups = [group],
            Edges =
            [
                new CanvasEdge { FromNodeId = card.Id, ToNodeId = map.Id, Label = "same night", Arrow = EdgeArrow.End },
                new CanvasEdge { FromNodeId = note.Id, ToNodeId = message.Id, Arrow = EdgeArrow.Both },
            ],
        };
    }

    // ── Steps ─────────────────────────────────────────────────────────

    private async Task OpenAsync(string route)
    {
        await _page.GotoAsync(CanvasUrl + route);
        await Expect(_page.Locator("#app .loading-progress")).ToHaveCountAsync(0, new() { Timeout = 60_000 });
        await Expect(_page.Locator(".bc-editor, .bwc-login").First).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }

    private async Task ImportAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"walk-{Guid.NewGuid():N}.ishcanvas");
        await File.WriteAllBytesAsync(path, CanvasPackage.Write(WalkBoard(), []));
        await _page.SetInputFilesAsync("#bc-import", path);
        await Expect(_page.Locator(".bc-node")).ToHaveCountAsync(7, new() { Timeout = 15_000 });
        File.Delete(path);
    }

    private async Task FitAsync()
    {
        await _page.ClickAsync("[data-bc-action='fit']");
        await _page.WaitForTimeoutAsync(400);
    }

    private async Task SelectFirstAsync(string kind)
    {
        var node = _page.Locator($".bc-node--{kind}").First;
        var box = (await node.BoundingBoxAsync())!;
        await TapAsync(box.X + box.Width / 2, box.Y + 14);
        await Expect(_page.Locator(".bc-node[data-bc-selected]")).ToHaveCountAsync(1);
    }

    private async Task SelectEdgeAsync()
    {
        var hit = _page.Locator(".bc-edge__hit").First;
        if (await hit.CountAsync() == 0) return;
        var box = (await hit.BoundingBoxAsync())!;
        await TapAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await _page.WaitForTimeoutAsync(200);
    }

    private async Task SelectGroupAsync()
    {
        var chip = _page.Locator(".bc-group__label").First;
        if (await chip.CountAsync() == 0) return;
        var box = (await chip.BoundingBoxAsync())!;
        await TapAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await _page.WaitForTimeoutAsync(200);
    }

    private async Task OpenPropertiesAsync()
    {
        if (IsPhone)
        {
            await _page.ClickAsync(".bc-bar [data-bc-action='more']");
            await _page.ClickAsync(".bc-sheet--open [data-bc-action='edit']");
            await Expect(_page.Locator(".bc-sheet--open")).ToBeVisibleAsync();
        }
        else if (!await _page.Locator("#bc-props").IsVisibleAsync())
        {
            await _page.ClickAsync("[data-bc-action='properties']");
            await Expect(_page.Locator("#bc-props")).ToBeVisibleAsync();
        }

        await _page.WaitForTimeoutAsync(300);
    }

    private async Task ScrollPropertiesToColourAsync()
    {
        var swatches = _page.Locator("#bc-props .bc-swatches").First;
        if (await swatches.CountAsync() == 0) return;
        await swatches.ScrollIntoViewIfNeededAsync();
        await _page.WaitForTimeoutAsync(250);
    }

    private async Task ClosePropertiesAsync()
    {
        if (IsPhone && await _page.Locator(".bc-sheet--open [data-bc-action='sheet-close']").CountAsync() > 0)
        {
            await _page.ClickAsync(".bc-sheet--open [data-bc-action='sheet-close']");
            await Expect(_page.Locator(".bc-sheet--open")).ToHaveCountAsync(0);
        }

        await _page.WaitForTimeoutAsync(200);
    }

    private async Task ContextMenuAsync()
    {
        var node = _page.Locator(".bc-node--card").First;
        var box = (await node.BoundingBoxAsync())!;
        var x = box.X + box.Width / 2;
        var y = box.Y + 14;

        if (_touch is not null) await _touch.LongPressAsync(x, y, 650);
        else await _page.Mouse.ClickAsync(x, y, new() { Button = MouseButton.Right });

        await Expect(_page.Locator("[role='menu']")).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await _page.WaitForTimeoutAsync(200);
    }

    private async Task HelpAsync()
    {
        if (IsPhone)
        {
            await _page.ClickAsync(".bc-bar [data-bc-action='more']");
            await _page.ClickAsync(".bc-sheet--open [data-bc-action='help']");
        }
        else
        {
            await _page.ClickAsync("[data-bc-action='help']");
        }

        await _page.WaitForTimeoutAsync(400);
    }

    private async Task RefuseAHeicAsync()
    {
        await _page.Locator(".bc-board").FocusAsync();
        await _page.PasteAsync(files: [new MadeFile("heic", "IMG_0042.HEIC", "image/heic")]);
        await Expect(_page.Locator(".bc-toast, [role='status'], [role='alert']").First).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await _page.WaitForTimeoutAsync(300);
    }

    private async Task EditSomethingAsync()
    {
        await SelectFirstAsync("text");
        await _page.Locator(".bc-board").FocusAsync();
        await _page.Keyboard.PressAsync("ArrowRight");
        await _page.WaitForTimeoutAsync(200);
    }

    private async Task WaitForSavedLocallyAsync()
    {
        await Expect(_page.Locator(".bc-savestate")).ToContainTextAsync("Saved on this device", new() { Timeout = 15_000 });
        await _page.WaitForTimeoutAsync(200);
    }

    private async Task OpenOverflowAsync()
    {
        if (IsPhone)
        {
            await _page.ClickAsync(".bc-bar [data-bc-action='more']");
            await Expect(_page.Locator(".bc-sheet--open")).ToBeVisibleAsync();
        }
        else
        {
            await Expect(_page.Locator("[data-bc-action='export']").First).ToBeVisibleAsync();
        }

        await _page.WaitForTimeoutAsync(300);
    }

    private async Task DismissAsync()
    {
        await _page.Keyboard.PressAsync("Escape");
        await _page.WaitForTimeoutAsync(250);
    }

    private async Task TapAsync(double x, double y)
    {
        if (_touch is not null) await _touch.TapAsync(x, y);
        else await _page.Mouse.ClickAsync((float)x, (float)y);
    }

    // ── The three pictures that need the server ───────────────────────

    private async Task ServerShotsAsync()
    {
        var password = Environment.GetEnvironmentVariable("BEN_USER_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            SkipRow("save-saved-server", "BEN_USER_PASSWORD is not set, so the walk did not sign in.");
            SkipRow("newer-copy-choice", "BEN_USER_PASSWORD is not set, so the walk did not sign in.");
            SkipRow("publish-question", "BEN_USER_PASSWORD is not set, so the walk did not sign in.");
            return;
        }

        ApiSession api;
        Guid org, caseId;
        try
        {
            api = await ApiSession.SignInAsync(ApiUrl, Environment.GetEnvironmentVariable("BEN_USER_EMAIL") ?? "sarah.mitchell@benco.dev", password);
            if (!await api.CanvasIsOnAsync())
            {
                SkipAllServerRows("the API refused api/canvas-documents for this account.");
                return;
            }

            (org, caseId) = await api.CaseAsync();
        }
        catch (Exception ex)
        {
            SkipAllServerRows($"the API could not be used: {Trim(ex.Message)}");
            return;
        }

        await using var _ = api;

        await _page.FillAsync("#email", Environment.GetEnvironmentVariable("BEN_USER_EMAIL") ?? "sarah.mitchell@benco.dev");
        await _page.FillAsync("#password", password);
        await _page.ClickAsync("button[type='submit']");
        await Expect(_page.Locator(".bc-editor")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        await _page.GotoAsync($"{CanvasUrl}/#case={caseId}&org={org}");
        await _page.ReloadAsync();
        await Expect(_page.Locator(".bc-editor[data-bc-ready='true']")).ToHaveCountAsync(1, new() { Timeout = 60_000 });

        await ImportAsync();
        await FitAsync();
        await _page.ClickAsync("[data-bc-action='save-server']");
        await Expect(_page.Locator(".bc-savestate")).ToContainTextAsync("Saved to case", new() { Timeout = 20_000 });
        await ShotAsync("save-saved-server", "The header says the board is saved to the case, not only to this device.");

        var boards = await api.BoardsAsync(caseId);
        var id = boards.Select(b => b.GetProperty("id").GetGuid()).LastOrDefault();
        if (id != Guid.Empty)
        {
            await api.SaveAsSomebodyElseAsync(id);
            api.CreatedBoards.Add(id);
            await _page.Locator(".bc-board").FocusAsync();
            await _page.Keyboard.PressAsync("ArrowRight");
            await _page.ClickAsync("[data-bc-action='save-server']");
            await Expect(_page.Locator("[role='dialog']")).ToBeVisibleAsync(new() { Timeout = 20_000 });
            await ShotAsync("newer-copy-choice", "A newer copy saved by somebody else becomes a choice, never a silent overwrite.");
            await _page.ClickAsync("[data-bc-action='conflict-mine']");
            await Expect(_page.Locator(".bc-savestate")).ToContainTextAsync("Saved to case", new() { Timeout = 20_000 });
        }
        else
        {
            SkipRow("newer-copy-choice", "the saved board could not be found on the server.");
        }

        await _page.ClickAsync("[data-bc-action='publish']");
        await Expect(_page.Locator("[role='dialog']")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await ShotAsync("publish-question", "Publishing asks first, and says the picture stays inside the case.");
        await DismissAsync();
    }

    private void SkipAllServerRows(string because)
    {
        SkipRow("save-saved-server", because);
        SkipRow("newer-copy-choice", because);
        SkipRow("publish-question", because);
    }

    private void SkipRow(string name, string because)
    {
        _shot++;
        WalkReport.Append(new WalkRow(device, theme, $"{device}-{theme}-{_shot:00}-{name}", $"NOT TAKEN: {because}", [], [], []));
        TestContext.Progress.WriteLine($"{device}/{theme}: skipped {name} - {because}");
    }

    // ── Pictures ──────────────────────────────────────────────────────

    private async Task ShotAsync(string name, string proves)
    {
        _shot++;
        var file = $"{device}-{theme}-{_shot:00}-{name}";

        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await _page.EvaluateAsync("async () => { await document.fonts.ready; await Promise.all([...document.images].map(i => i.decode().catch(() => {}))); }");
        Directory.CreateDirectory(WalkReport.Folder);
        await _page.ScreenshotAsync(new() { Path = Path.Combine(WalkReport.Folder, file + ".png") });

        WalkReport.Append(new WalkRow(
            device, theme, file, proves,
            Drain(_consoleErrors), Drain(_serverErrors), Drain(_thirdParty)));
    }

    private static string[] Drain(List<string> seen)
    {
        var values = seen.Distinct(StringComparer.Ordinal).ToArray();
        seen.Clear();
        return values;
    }

    private static string Trim(string value)
    {
        var line = value.ReplaceLineEndings(" ").Trim();
        return line.Length <= 160 ? line : string.Concat(line.AsSpan(0, 157), "...");
    }
}
