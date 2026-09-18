using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Core.Templates;

/// <summary>
/// What each template lays out.
/// </summary>
/// <remarks>
/// <para>Coordinates are world units — the same ones a drag produces — and start at a comfortable margin
/// rather than the origin, so a board never opens flush against its own corner.</para>
///
/// <para>Panels are at least the registry's group minimum (300 x 200): anything smaller cannot be made
/// smaller by dragging, so it reads as a mistake rather than a choice.</para>
///
/// <para>The words in a blank template are the FRAME's words — what a section is for — never the
/// content. "What we already know" belongs to the template; what is actually known does not.</para>
/// </remarks>
public static partial class BoardTemplates
{
    private const double Margin = 80;
    private const double Pad = 24;

    /// <summary>
    /// The vertical gap between one panel and the one below it.
    /// </summary>
    /// <remarks>
    /// A group's name is drawn as a chip ABOVE its top edge, about thirty pixels tall, so a gap sized
    /// only for breathing room puts the lower panel's name on the upper panel's bottom border. The
    /// pictures from the templates walk showed exactly that on the research plan's quadrant and, more
    /// narrowly, on the deck (2026-09-18) — which is the whole reason that walk takes pictures.
    /// </remarks>
    private const double LabelRoom = 56;

    // ── Moodboard ─────────────────────────────────────────────────────────
    //
    // Four coloured sections and a cluster of theme bubbles, as in the picture: each section takes a
    // palette hue of its own and holds one note, and the bubbles are a centre with three satellites
    // joined to it, because a theme with nothing attached is just a circle.
    private static void Moodboard(BoardBuilder b)
    {
        const double w = 380, h = 300;

        var sections = new (string Label, string Prompt, string Colour)[]
        {
            ("Feel", "What should this give somebody? Drop in pictures and a line each.", "1"),
            ("Places", "Rooms, buildings, weather, time of day.", "2"),
            ("Sound", "What is heard here, and what silence sounds like.", "3"),
            ("References", "Anything already out there that gets it right.", "4"),
        };

        for (var i = 0; i < sections.Length; i++)
        {
            var (label, prompt, colour) = sections[i];
            var x = Margin + i % 2 * (w + Pad * 2);
            var y = Margin + i / 2 * (h + LabelRoom);

            var group = b.Group(label, x, y, w, h, colour);
            b.Note(x + Pad, y + Pad * 2, prompt, w - Pad * 2, h - Pad * 3, colour, group);
        }

        // The cluster: a centre and three satellites, off to the right of the sections.
        const double cx = 1000, cy = 260;
        var centre = b.Node(CanvasNodeType.Shape, cx, cy,
            new ShapeData { Kind = ShapeKind.Ellipse, Text = "Theme" }, 180, 180, "5");

        var satellites = new (string Text, double X, double Y)[]
        {
            ("Why now", cx - 40, cy - 220),
            ("Who cares", cx + 220, cy - 60),
            ("What is odd", cx - 20, cy + 260),
        };

        foreach (var (text, x, y) in satellites)
        {
            var bubble = b.Node(CanvasNodeType.Shape, x, y,
                new ShapeData { Kind = ShapeKind.Ellipse, Text = text }, 140, 140, "6");
            b.Edge(centre, bubble, toMarker: EdgeMarker.None);
        }
    }

    // ── Research plan ─────────────────────────────────────────────────────
    //
    // Titled tiles at the top, a two-by-two of what matters underneath, and a grid for when — the three
    // things the picture showed. The grid is a Table block because a calendar typed into a note is a
    // calendar nobody can read back.
    private static void ResearchPlan(BoardBuilder b)
    {
        var questions = b.Group("Questions", Margin, Margin, 380, 260, "1");
        b.Note(Margin + Pad, Margin + Pad * 2, "What are we actually trying to find out? One question per line.",
            380 - Pad * 2, 260 - Pad * 3, "1", questions);

        var sources = b.Group("Where to look", Margin + 428, Margin, 380, 260, "2");
        b.Note(Margin + 428 + Pad, Margin + Pad * 2, "Records, people, places, papers. Note who holds each one.",
            380 - Pad * 2, 260 - Pad * 3, "2", sources);

        // What matters most: effort across, impact down, so the top-left corner is where to start.
        var quadrant = new (string Label, string Colour)[]
        {
            ("Worth it, quick", "3"),
            ("Worth it, slow", "4"),
            ("Small, quick", "5"),
            ("Small, slow", "6"),
        };

        const double qw = 300, qh = 200, qy = 420;
        for (var i = 0; i < quadrant.Length; i++)
        {
            var (label, colour) = quadrant[i];
            b.Group(label, Margin + i % 2 * (qw + Pad), qy + i / 2 * (qh + LabelRoom), qw, qh, colour, GroupFill.Outline);
        }

        // When: a week of work, header row and four rows to fill in.
        //
        // Sized from BlockFit rather than by hand. It WAS a hand-typed 210, and when a grid row grew
        // from 29 pixels to its real 38.5 the template kept the old number and published a calendar
        // with its last week cut off — caught by looking at the board, 2026-09-18. Asking the same
        // function the editor asks means the two cannot drift again.
        var week = new TableData
        {
            HasHeaderRow = true,
            Rows =
            [
                ["Week", "Question", "Where", "Who"],
                ["1", "", "", ""],
                ["2", "", "", ""],
                ["3", "", "", ""],
                ["4", "", "", ""],
            ],
        };

        var needs = BlockFit.For(week)!.Value;
        b.Node(CanvasNodeType.Table, Margin + 700, qy, week, Math.Max(440, needs.Width), needs.Height, "2");
    }

    // ── Family tree ───────────────────────────────────────────────────────
    //
    // Right angles, because a family tree drawn in curves is unreadable, and a legend, because
    // dashes-mean-siblings is not guessable. Ben's picture had one; a tree without it is a diagram only
    // its author can read.
    //
    // Ben, 2026-09-18: "include a place to put a photo of the person - if they want to do that or even
    // just using a male and female icon. It should be up to the end user." So every person is a PAIR: an
    // empty picture frame above a name box. Empty is the shipped state and reads as a portrait frame —
    // the block draws "No picture yet. Paste or drop one here." — so a photo, a drawn icon or nothing at
    // all are all equally finished, which is the choice Ben asked to leave open. Fit is Cover so whatever
    // is dropped in fills the frame at the same size as every other person's, rather than each portrait
    // being as tall as its own file.
    //
    // The lines join the NAME boxes, not the frames: a line into a photo would move when somebody
    // decides to go without one.
    private static void FamilyTree(BoardBuilder b)
    {
        const double nw = 200, nh = 92;

        // The frame is the image block's own MINIMUM, not the width of the name under it.
        //
        // Ben asked the right question mid-build (2026-09-18): "If they don't provide it, it doesn't
        // take up space for it, right?" It does — a frame is a real block whether or not a photograph
        // ever lands in it — so the shipped size is the smallest one the editor allows, centred over
        // the name. An unused frame then reads as a small placeholder tile rather than a large empty
        // box, and the tree is about a quarter shorter. Ben's call, of three offered.
        const double photoW = 120, photoH = 90;

        // A person: a frame, a name under it, and the id of the name box, which is what lines join.
        Guid Person(double x, double y, string colour)
        {
            b.Node(CanvasNodeType.Image, x + (nw - photoW) / 2, y, new ImageData { Fit = ImageFit.Cover },
                photoW, photoH, colour);
            return b.Note(x, y + photoH, "Name\nb. — d. —", nw, nh, colour);
        }

        var grandfather = Person(420, Margin, "1");
        var grandmother = Person(700, Margin, "1");

        // A partnership: one line, a diamond at each end, and no arrow, because neither end came first.
        b.Edge(grandfather, grandmother, EdgeRoute.Elbow,
            fromMarker: EdgeMarker.Diamond, toMarker: EdgeMarker.Diamond, label: "m. —");

        var children = new[]
        {
            Person(180, 340, "2"),
            Person(460, 340, "2"),
            Person(740, 340, "2"),
        };

        foreach (var child in children)
            b.Edge(grandfather, child, EdgeRoute.Elbow, toMarker: EdgeMarker.Arrow);

        // Siblings: dashed, so a sibling line is never mistaken for a parent line.
        b.Edge(children[0], children[1], EdgeRoute.Elbow, EdgeLine.Dashed, toMarker: EdgeMarker.None);
        b.Edge(children[1], children[2], EdgeRoute.Elbow, EdgeLine.Dashed, toMarker: EdgeMarker.None);

        var grandchild = Person(460, 600, "3");
        b.Edge(children[1], grandchild, EdgeRoute.Elbow, toMarker: EdgeMarker.Arrow);

        // Beside the tree rather than under it, and tall enough for every line. Under it, the board was
        // half as tall again and opened at 58% — small enough that the diamonds on the marriage line
        // could not be made out, which is the one thing the legend is there to explain. The first
        // pictures also cut this note off after "The frame above each name takes a photo", which is the
        // half that says the photograph is optional.
        const double legendX = 1060, legendY = Margin;
        var legend = b.Group("Legend", legendX, legendY, 360, 360, "6");
        b.Note(legendX + Pad, legendY + Pad * 2,
            "Solid line: parent to child.\nDashed line: siblings.\nDiamond at both ends: a marriage or partnership.\n\n"
            + "The frame above each name takes a photo — paste or drop one in. Leave it empty, or drop in "
            + "any picture you like instead; it is yours either way.",
            360 - Pad * 2, 360 - Pad * 3, "6", legend);
    }

    // ── Presentation deck ─────────────────────────────────────────────────
    //
    // Slides are PANEL GROUPS, not a new kind of block, which is the whole reason presenting needed no
    // new model: SlideOrder already walks a board's groups, top to bottom and left to right, so a deck
    // laid out as a reading-order grid presents in the order it is written with nothing else built.
    private static void Deck(BoardBuilder b)
    {
        const double w = 480, h = 300;

        var slides = new (string Label, string Prompt)[]
        {
            ("1 · Title", "What this is, and whose it is."),
            ("2 · What we know", "The findings, one line each."),
            ("3 · What we asked", "The questions, and what answered them."),
            ("4 · What comes next", "What would settle it."),
        };

        for (var i = 0; i < slides.Length; i++)
        {
            var (label, prompt) = slides[i];
            var x = Margin + i % 2 * (w + Pad * 2);
            var y = Margin + i / 2 * (h + LabelRoom);

            var slide = b.Group(label, x, y, w, h, "1");
            b.Note(x + Pad, y + Pad * 2, prompt, w - Pad * 2, 96, "1", slide);
            b.Note(x + Pad, y + Pad * 2 + 120, "•\n•\n•", w - Pad * 2, h - Pad * 3 - 120, null, slide);
        }
    }
}
