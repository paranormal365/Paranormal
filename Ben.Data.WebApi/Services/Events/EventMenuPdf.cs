using Ben.Data.Common.Menus;
using Ben.Service.Models.Entities;
using Telerik.Documents.Primitives;
using Telerik.Windows.Documents.Fixed.FormatProviders.Pdf;
using Telerik.Windows.Documents.Fixed.Model;
using Telerik.Windows.Documents.Fixed.Model.ColorSpaces;
using Telerik.Windows.Documents.Fixed.Model.Data;
using Telerik.Windows.Documents.Fixed.Model.Editing;
using Telerik.Windows.Documents.Fixed.Model.Editing.Flow;
using Telerik.Windows.Documents.Fixed.Model.Fonts;
using Telerik.Windows.Documents.Fixed.Model.Graphics;
using Telerik.Windows.Documents.Fixed.Model.Resources;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// A menu as a PDF, in its design (Ben, 10/13/2026: "it will be publishable as a .PDF file. You can
/// use Telerik to generate it").
/// </summary>
/// <remarks>
/// <para><b>US Letter, laid out from <see cref="MenuDesign"/></b>: the same colors, type family, frame,
/// ornament and columns the page uses, and the same words (<see cref="EventMenuPresentation"/>), so
/// what the planner previewed is what prints.</para>
///
/// <para><b>Only the standard PDF fonts</b> (Times, Helvetica, Courier): every reader has them, and the
/// site ships no font files Telerik could embed. They cover the accented letters a menu needs
/// (Séance, Café, Entrées).</para>
///
/// <para><b>Pictures</b> are the fitted JPEGs the site stores; one that can't be read is left out
/// rather than failing the whole menu.</para>
/// </remarks>
public static class EventMenuPdf
{
    private const double PageWidth = 612, PageHeight = 792, Margin = 58, FrameInset = 24;

    public static string FileName(EventMenuRecord menu)
    {
        var name = new string($"{menu.EventName} {menu.ShownHeading}"
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
        while (name.Contains("--")) name = name.Replace("--", "-");
        name = name.Trim('-');
        return $"{(name.Length == 0 ? "menu" : name[..Math.Min(name.Length, 80)])}-menu.pdf";
    }

    public static async Task<byte[]> RenderAsync(EventMenuRecord menu, Func<Guid, Task<byte[]?>> readImage, int year)
    {
        // Every picture first, so drawing never waits.
        var ids = menu.Sections.SelectMany(s => s.Entries).Select(e => e.Item.ImageUploadFileId)
            .Append(menu.LogoUploadFileId).OfType<Guid>().Distinct();
        var images = new Dictionary<Guid, ImageSource>();
        foreach (var id in ids)
        {
            try
            {
                if (await readImage(id) is { Length: > 0 } bytes) images[id] = new ImageSource(new MemoryStream(bytes));
            }
            catch (Exception)
            {
                // Not a picture Telerik can read: drawn without it.
            }
        }

        return new Renderer(menu, MenuDesigns.FindOrDefault(menu.DesignKey), images, year).Render();
    }

    private sealed class Renderer
    {
        private readonly EventMenuRecord _menu;
        private readonly MenuDesign _d;
        private readonly Dictionary<Guid, ImageSource> _images;
        private readonly int _year;
        private readonly RadFixedDocument _doc = new();
        private readonly RgbColor _paper, _ink, _accent, _muted;
        private readonly FontBase _regular, _bold, _italic;

        private RadFixedPage _page = null!;
        private FixedContentEditor _editor = null!;

        // The frames text flows through: one per page, or two (the columns) once the masthead is done.
        private double _x, _y, _width, _bottom;
        private int _column;

        public Renderer(EventMenuRecord menu, MenuDesign design, Dictionary<Guid, ImageSource> images, int year)
        {
            _menu = menu; _d = design; _images = images; _year = year;
            _paper = Rgb(design.Paper); _ink = Rgb(design.Ink); _accent = Rgb(design.Accent); _muted = Rgb(design.Muted);
            (_regular, _bold, _italic) = design.Font switch
            {
                MenuFont.Sans => (FontsRepository.Helvetica, FontsRepository.HelveticaBold, FontsRepository.HelveticaOblique),
                MenuFont.Typewriter => (FontsRepository.Courier, FontsRepository.CourierBold, FontsRepository.CourierOblique),
                _ => (FontsRepository.TimesRoman, FontsRepository.TimesBold, FontsRepository.TimesItalic),
            };
        }

        public byte[] Render()
        {
            NewPage();
            Masthead();

            var columns = _d.TwoColumns && _menu.Sections.Count > 1 ? 2 : 1;
            StartColumns(columns);

            foreach (var section in _menu.Sections)
            {
                // A heading never sits alone at the bottom of a column: it moves with its first item.
                var headingHeight = SectionHeadingHeight(section);
                var firstHeight = section.Entries.Count > 0 ? EntryHeight(section.Entries[0]) : 0;
                if (_y + headingHeight + firstHeight > _bottom) NextFrame(columns);
                SectionHeading(section);

                foreach (var entry in section.Entries)
                {
                    if (_y + EntryHeight(entry) > _bottom) NextFrame(columns);
                    Entry(entry);
                }
                _y += 10;
            }

            if (EventMenuPresentation.Key(_menu) is { } key)
            {
                _y = Math.Max(_lowest, _y);
                StartColumns(1, keepY: true);
                // The key may use the room above the footer rather than start a page of its own.
                var keyHeight = Measure(key, _regular, 7.5, _width);
                if (_y + keyHeight > PageHeight - FrameInset - 20) NextFrame(1);
                Text(key, _regular, 7.5, _muted, HorizontalAlignment.Center);
            }

            Footers();
            return new PdfFormatProvider().Export(_doc, null);
        }

        // ── pages and frames ─────────────────────────────────────────────────

        private void NewPage()
        {
            _page = _doc.Pages.AddPage();
            _page.Size = new Size(PageWidth, PageHeight);
            _editor = new FixedContentEditor(_page);

            using (_editor.SaveGraphicProperties())
            {
                _editor.GraphicProperties.IsStroked = false;
                _editor.GraphicProperties.FillColor = _paper;
                _editor.DrawRectangle(new Rect(0, 0, PageWidth, PageHeight));
            }
            Frame();

            _x = Margin; _width = PageWidth - 2 * Margin; _y = Margin; _bottom = PageHeight - Margin - 18;
        }

        private double _columnTop;

        /// <summary>The lowest point reached in any column of this page, so what follows starts below both.</summary>
        private double _lowest;

        private void StartColumns(int columns, bool keepY = false)
        {
            _column = 0;
            _columnTop = _y;
            SetColumn(columns);
            if (!keepY) _y = _columnTop;
        }

        private void SetColumn(int columns)
        {
            const double gap = 26;
            var full = PageWidth - 2 * Margin;
            _width = columns == 1 ? full : (full - gap) / 2;
            _x = Margin + _column * (_width + gap);
        }

        private void NextFrame(int columns)
        {
            _lowest = Math.Max(_lowest, _y);
            if (columns == 2 && _column == 0)
            {
                _column = 1;
                SetColumn(columns);
                _y = _columnTop;
                return;
            }
            NewPage();
            _lowest = _y;
            _column = 0;
            _columnTop = _y;
            SetColumn(columns);
        }

        private void Frame()
        {
            using (_editor.SaveGraphicProperties())
            {
                _editor.GraphicProperties.IsFilled = false;
                _editor.GraphicProperties.IsStroked = true;
                _editor.GraphicProperties.StrokeColor = _accent;
                _editor.GraphicProperties.StrokeThickness = 1.2;

                switch (_d.Frame)
                {
                    case MenuFrame.Single:
                        _editor.DrawRectangle(new Rect(FrameInset, FrameInset, PageWidth - 2 * FrameInset, PageHeight - 2 * FrameInset));
                        break;
                    case MenuFrame.Double:
                        _editor.DrawRectangle(new Rect(FrameInset - 4, FrameInset - 4, PageWidth - 2 * (FrameInset - 4), PageHeight - 2 * (FrameInset - 4)));
                        _editor.GraphicProperties.StrokeThickness = 0.6;
                        _editor.DrawRectangle(new Rect(FrameInset + 2, FrameInset + 2, PageWidth - 2 * (FrameInset + 2), PageHeight - 2 * (FrameInset + 2)));
                        break;
                    case MenuFrame.Corners:
                        const double arm = 46;
                        foreach (var (cx, cy, dx, dy) in new[]
                                 {
                                     (FrameInset, FrameInset, 1.0, 1.0),
                                     (PageWidth - FrameInset, FrameInset, -1.0, 1.0),
                                     (FrameInset, PageHeight - FrameInset, 1.0, -1.0),
                                     (PageWidth - FrameInset, PageHeight - FrameInset, -1.0, -1.0),
                                 })
                        {
                            _editor.DrawLine(new Point(cx, cy), new Point(cx + dx * arm, cy));
                            _editor.DrawLine(new Point(cx, cy), new Point(cx, cy + dy * arm));
                            _editor.DrawCircle(new Point(cx + dx * 8, cy + dy * 8), 2.2);
                        }
                        break;
                }
            }
        }

        private void Footers()
        {
            var credit = MenuDesigns.Credit(_year);
            foreach (var page in _doc.Pages)
            {
                var editor = new FixedContentEditor(page);
                var block = MakeBlock(credit, _regular, 6, _muted, HorizontalAlignment.Center);
                var size = block.Measure(new Size(PageWidth - 2 * Margin, double.PositiveInfinity), CancellationToken.None);
                editor.Position = At(Margin, PageHeight - FrameInset - 14);
                editor.DrawBlock(block, new Size(PageWidth - 2 * Margin, size.Height));
            }
        }

        // ── the masthead ─────────────────────────────────────────────────────

        private void Masthead()
        {
            if (_menu.LogoUploadFileId is { } logo && _images.TryGetValue(logo, out var picture))
            {
                const double maxH = 72, maxW = 220;
                var scale = Math.Min(maxH / picture.Height, maxW / picture.Width);
                var w = picture.Width * scale; var h = picture.Height * scale;
                _editor.Position = At(Margin + (_width - w) / 2, _y);
                _editor.DrawImage(picture, w, h);
                _y += h + 12;
            }

            Ornament(PageWidth / 2, _y + 8, 1.0);
            _y += _d.Ornament == MenuOrnament.None ? 0 : 24;

            Text(Spaced(_menu.EventName.ToUpperInvariant()), _regular, 8.5, _muted, HorizontalAlignment.Center);
            _y += 4;
            Text(_menu.ShownHeading, _bold, 28, _accent, HorizontalAlignment.Center);
            _y += 2;

            foreach (var line in EventMenuPresentation.WhenLines(_menu))
                Text(line, _italic, 10, _muted, HorizontalAlignment.Center);

            if (_menu.Intro is { Length: > 0 } intro)
            {
                _y += 6;
                Text(intro, _italic, 10.5, _ink, HorizontalAlignment.Center);
            }

            if (EventMenuPresentation.PriceLine(_menu) is { } prices)
            {
                _y += 8;
                Text(prices, _bold, 12, _accent, HorizontalAlignment.Center);
            }
            if (EventMenuPresentation.IncludedLine(_menu) is { } included)
            {
                _y += 6;
                Text(included, _bold, 11, _accent, HorizontalAlignment.Center);
            }
            if (_menu.PriceNote is { Length: > 0 } note)
                Text(note, _regular, 8.5, _muted, HorizontalAlignment.Center);

            _y += 10;
            Rule(Margin + _width * 0.3, Margin + _width * 0.7, _y);
            _y += 18;
        }

        // ── sections and items ───────────────────────────────────────────────

        private string SectionTitle(EventMenuSectionRecord s) => _d.UpperHeadings ? Spaced(s.Title.ToUpperInvariant()) : s.Title;

        private double SectionHeadingHeight(EventMenuSectionRecord s)
            => Measure(SectionTitle(s), _bold, 13, _width) + 16;

        private void SectionHeading(EventMenuSectionRecord s)
        {
            Text(SectionTitle(s), _bold, 13, _accent, HorizontalAlignment.Center);
            _y += 3;
            var mid = _x + _width / 2;
            Rule(mid - 22, mid + 22, _y);
            _y += 12;
        }

        private const double Thumb = 40;

        private double EntryHeight(EventMenuEntryRecord e)
        {
            var textW = _width - (HasPicture(e) ? Thumb + 10 : 0) - 60;
            var h = Measure(NameLine(e), _bold, 11, textW);
            if (e.Item.Description is { Length: > 0 } d) h += Measure(d, _italic, 9.5, textW) + 1;
            if (EventMenuPresentation.AllergenLine(e.Item) is { } a) h += Measure(a, _regular, 8, textW);
            if (e.Note is { Length: > 0 } n) h += Measure(n, _italic, 8.5, textW);
            return Math.Max(h, HasPicture(e) ? Thumb : 0) + 9;
        }

        private bool HasPicture(EventMenuEntryRecord e) => e.Item.ImageUploadFileId is { } id && _images.ContainsKey(id);

        private static string NameLine(EventMenuEntryRecord e)
            => EventMenuPresentation.Letters(e.Item.Dietary) is { } letters ? $"{e.Item.Name}  {letters}" : e.Item.Name;

        private void Entry(EventMenuEntryRecord e)
        {
            var top = _y;
            var left = _x;
            if (HasPicture(e))
            {
                var picture = _images[e.Item.ImageUploadFileId!.Value];
                var scale = Math.Max(Thumb / picture.Width, Thumb / picture.Height);
                _editor.Position = At(0, 0);
                using (_editor.PushClipping(new Rect(_x, _y, Thumb, Thumb)))
                {
                    _editor.Position = At(_x + (Thumb - picture.Width * scale) / 2, _y + (Thumb - picture.Height * scale) / 2);
                    _editor.DrawImage(picture, picture.Width * scale, picture.Height * scale);
                }
                left += Thumb + 10;
            }

            var price = EventMenuPresentation.EntryPrice(_menu, e);
            const double priceW = 56;
            var textW = _x + _width - left - (price is null ? 0 : priceW + 4);

            var nameH = TextAt(NameLine(e), _bold, 11, _ink, left, _y, textW, HorizontalAlignment.Left);
            if (price is not null)
            {
                TextAt(price, _bold, 11, _accent, _x + _width - priceW, _y, priceW, HorizontalAlignment.Right);
                if (_d.DottedLeaders)
                {
                    var nameW = Math.Min(textW, Width(NameLine(e), _bold, 11));
                    var from = left + nameW + 6;
                    var to = _x + _width - Width(price, _bold, 11) - 6;
                    if (to - from > 12) Leaders(from, to, _y + 9);
                }
            }
            _y += nameH;

            if (e.Item.Description is { Length: > 0 } d) _y += TextAt(d, _italic, 9.5, _muted, left, _y, textW, HorizontalAlignment.Left) + 1;
            if (EventMenuPresentation.AllergenLine(e.Item) is { } a) _y += TextAt(a, _regular, 8, _muted, left, _y, textW, HorizontalAlignment.Left);
            if (e.Note is { Length: > 0 } n) _y += TextAt(n, _italic, 8.5, _accent, left, _y, textW, HorizontalAlignment.Left);

            _y = Math.Max(_y, top + (HasPicture(e) ? Thumb : 0)) + 9;
        }

        // ── drawing helpers ──────────────────────────────────────────────────

        private static SimplePosition At(double x, double y)
        {
            var p = new SimplePosition();
            p.Translate(x, y);
            return p;
        }

        private Block MakeBlock(string text, FontBase font, double size, RgbColor color, HorizontalAlignment align)
        {
            var block = new Block { HorizontalAlignment = align };
            block.TextProperties.Font = font;
            block.TextProperties.FontSize = size;
            // Letter-spaced capitals for an all-caps line, the way the page sets them.
            if (text.Length > 1 && text == text.ToUpperInvariant() && text.Any(char.IsLetter))
                block.TextProperties.CharacterSpacing = size * 0.12;
            block.GraphicProperties.FillColor = color;
            block.InsertText(text);
            return block;
        }

        private double Measure(string text, FontBase font, double size, double width)
            => MakeBlock(text, font, size, _ink, HorizontalAlignment.Left).Measure(new Size(width, double.PositiveInfinity), CancellationToken.None).Height;

        private double Width(string text, FontBase font, double size)
            => MakeBlock(text, font, size, _ink, HorizontalAlignment.Left).Measure(new Size(double.PositiveInfinity, double.PositiveInfinity), CancellationToken.None).Width;

        /// <summary>Draws text across the current frame and moves down past it.</summary>
        private void Text(string text, FontBase font, double size, RgbColor color, HorizontalAlignment align)
            => _y += TextAt(text, font, size, color, _x, _y, _width, align);

        private double TextAt(string text, FontBase font, double size, RgbColor color, double x, double y, double width, HorizontalAlignment align)
        {
            var block = MakeBlock(text, font, size, color, align);
            var measured = block.Measure(new Size(width, double.PositiveInfinity), CancellationToken.None);
            _editor.Position = At(x, y);
            _editor.DrawBlock(block, new Size(width, measured.Height));
            return measured.Height;
        }

        private void Rule(double from, double to, double y)
        {
            _editor.Position = At(0, 0);
            using (_editor.SaveGraphicProperties())
            {
                _editor.GraphicProperties.IsStroked = true;
                _editor.GraphicProperties.StrokeColor = _accent;
                _editor.GraphicProperties.StrokeThickness = 0.8;
                _editor.DrawLine(new Point(from, y), new Point(to, y));
            }
        }

        private void Leaders(double from, double to, double y)
        {
            _editor.Position = At(0, 0);
            using (_editor.SaveGraphicProperties())
            {
                _editor.GraphicProperties.IsStroked = false;
                _editor.GraphicProperties.FillColor = _muted;
                for (var x = from; x < to; x += 4) _editor.DrawCircle(new Point(x, y), 0.45);
            }
        }

        /// <summary>The design's small drawing, centered on (cx, cy).</summary>
        private void Ornament(double cx, double cy, double s)
        {
            // Shapes are drawn through the editor's position like text, so it goes back to the corner first.
            _editor.Position = At(0, 0);
            using (_editor.SaveGraphicProperties())
            {
                _editor.GraphicProperties.StrokeColor = _accent;
                _editor.GraphicProperties.FillColor = _accent;
                _editor.GraphicProperties.StrokeThickness = 1;

                switch (_d.Ornament)
                {
                    case MenuOrnament.Flourish:
                        _editor.GraphicProperties.IsFilled = false;
                        _editor.GraphicProperties.IsStroked = true;
                        Curve(cx - 60 * s, cy, cx - 30 * s, cy - 8 * s, cx - 18 * s, cy + 6 * s, cx - 7 * s, cy);
                        Curve(cx + 60 * s, cy, cx + 30 * s, cy - 8 * s, cx + 18 * s, cy + 6 * s, cx + 7 * s, cy);
                        _editor.GraphicProperties.IsFilled = true;
                        Polygon((cx, cy - 5 * s), (cx + 5 * s, cy), (cx, cy + 5 * s), (cx - 5 * s, cy));
                        break;
                    case MenuOrnament.Crescent:
                        _editor.GraphicProperties.IsStroked = false;
                        _editor.GraphicProperties.IsFilled = true;
                        _editor.DrawCircle(new Point(cx, cy), 8 * s);
                        _editor.GraphicProperties.FillColor = _paper;
                        _editor.DrawCircle(new Point(cx + 3.5 * s, cy - 2 * s), 7 * s);
                        _editor.GraphicProperties.FillColor = _accent;
                        _editor.DrawCircle(new Point(cx - 22 * s, cy - 3 * s), 1.2 * s);
                        _editor.DrawCircle(new Point(cx + 24 * s, cy + 2 * s), 1.2 * s);
                        break;
                    case MenuOrnament.Stars:
                        _editor.GraphicProperties.IsStroked = false;
                        _editor.GraphicProperties.IsFilled = true;
                        Star(cx, cy, 6 * s);
                        Star(cx - 20 * s, cy + 1 * s, 3.5 * s);
                        Star(cx + 20 * s, cy + 1 * s, 3.5 * s);
                        break;
                    case MenuOrnament.Candle:
                        _editor.GraphicProperties.IsStroked = false;
                        _editor.GraphicProperties.IsFilled = true;
                        _editor.DrawRectangle(new Rect(cx - 3 * s, cy - 2 * s, 6 * s, 12 * s));
                        Polygon((cx, cy - 12 * s), (cx + 3 * s, cy - 5 * s), (cx, cy - 3 * s), (cx - 3 * s, cy - 5 * s));
                        _editor.GraphicProperties.IsFilled = false;
                        _editor.GraphicProperties.IsStroked = true;
                        _editor.DrawLine(new Point(cx - 40 * s, cy + 6 * s), new Point(cx - 10 * s, cy + 6 * s));
                        _editor.DrawLine(new Point(cx + 10 * s, cy + 6 * s), new Point(cx + 40 * s, cy + 6 * s));
                        break;
                    case MenuOrnament.Planchette:
                        _editor.GraphicProperties.IsFilled = false;
                        _editor.GraphicProperties.IsStroked = true;
                        Curve(cx, cy - 10 * s, cx + 14 * s, cy - 8 * s, cx + 12 * s, cy + 10 * s, cx, cy + 10 * s);
                        Curve(cx, cy - 10 * s, cx - 14 * s, cy - 8 * s, cx - 12 * s, cy + 10 * s, cx, cy + 10 * s);
                        _editor.DrawCircle(new Point(cx, cy), 3.2 * s);
                        _editor.DrawLine(new Point(cx - 46 * s, cy), new Point(cx - 18 * s, cy));
                        _editor.DrawLine(new Point(cx + 18 * s, cy), new Point(cx + 46 * s, cy));
                        break;
                    case MenuOrnament.Waveform:
                        _editor.GraphicProperties.IsFilled = false;
                        _editor.GraphicProperties.IsStroked = true;
                        var heights = new[] { 1, 2, 4, 3, 7, 10, 5, 12, 6, 3, 9, 4, 2, 5, 1 };
                        for (var i = 0; i < heights.Length; i++)
                        {
                            var x = cx + (i - heights.Length / 2) * 5 * s;
                            _editor.DrawLine(new Point(x, cy - heights[i] * s), new Point(x, cy + heights[i] * s));
                        }
                        break;
                    case MenuOrnament.Leaf:
                        _editor.GraphicProperties.IsStroked = false;
                        _editor.GraphicProperties.IsFilled = true;
                        Leaf(cx - 8 * s, cy, -1, s);
                        Leaf(cx + 8 * s, cy, 1, s);
                        _editor.GraphicProperties.IsFilled = false;
                        _editor.GraphicProperties.IsStroked = true;
                        _editor.DrawLine(new Point(cx - 50 * s, cy), new Point(cx - 22 * s, cy));
                        _editor.DrawLine(new Point(cx + 22 * s, cy), new Point(cx + 50 * s, cy));
                        break;
                    case MenuOrnament.Stamp:
                        _editor.GraphicProperties.IsFilled = false;
                        _editor.GraphicProperties.IsStroked = true;
                        _editor.GraphicProperties.StrokeThickness = 1.4;
                        _editor.DrawRectangle(new Rect(cx - 52 * s, cy - 9 * s, 104 * s, 18 * s));
                        var block = MakeBlock("EVIDENCE · MENU", _bold, 8.5, _accent, HorizontalAlignment.Center);
                        var h = block.Measure(new Size(104 * s, 30), CancellationToken.None).Height;
                        _editor.Position = At(cx - 52 * s, cy - h / 2);
                        _editor.DrawBlock(block, new Size(104 * s, h));
                        break;
                }
            }
        }

        private void Curve(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3)
        {
            var path = new PathGeometry();
            var figure = path.Figures.AddPathFigure();
            figure.StartPoint = new Point(x0, y0);
            figure.Segments.AddBezierSegment(new Point(x1, y1), new Point(x2, y2), new Point(x3, y3));
            _editor.DrawPath(path);
        }

        private void Polygon(params (double X, double Y)[] points)
        {
            var path = new PathGeometry();
            var figure = path.Figures.AddPathFigure();
            figure.StartPoint = new Point(points[0].X, points[0].Y);
            foreach (var (x, y) in points.Skip(1)) figure.Segments.AddLineSegment(new Point(x, y));
            figure.IsClosed = true;
            _editor.DrawPath(path);
        }

        private void Star(double cx, double cy, double r)
        {
            var points = new (double, double)[10];
            for (var i = 0; i < 10; i++)
            {
                var radius = i % 2 == 0 ? r : r * 0.42;
                var angle = Math.PI / 2 + i * Math.PI / 5;
                points[i] = (cx + radius * Math.Cos(angle), cy - radius * Math.Sin(angle));
            }
            Polygon(points);
        }

        private void Leaf(double cx, double cy, int dir, double s)
        {
            var path = new PathGeometry();
            var figure = path.Figures.AddPathFigure();
            figure.StartPoint = new Point(cx, cy);
            figure.Segments.AddBezierSegment(new Point(cx + dir * 4 * s, cy - 8 * s), new Point(cx + dir * 12 * s, cy - 6 * s), new Point(cx + dir * 14 * s, cy));
            figure.Segments.AddBezierSegment(new Point(cx + dir * 12 * s, cy + 6 * s), new Point(cx + dir * 4 * s, cy + 8 * s), new Point(cx, cy));
            figure.IsClosed = true;
            _editor.DrawPath(path);
        }

        /// <summary>Capitals are spaced by the block (see <see cref="MakeBlock"/>), not by inserted spaces,
        /// which the standard fonts would print as gaps between words only.</summary>
        private static string Spaced(string text) => text;

        private static RgbColor Rgb(string hex)
        {
            var h = hex.TrimStart('#');
            return new RgbColor(Convert.ToByte(h[..2], 16), Convert.ToByte(h.Substring(2, 2), 16), Convert.ToByte(h.Substring(4, 2), 16));
        }
    }
}
