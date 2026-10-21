import SwiftUI
import BenKit

/// One published menu, drawn in its own design (10/13/2026), as the website and its PDF draw it: the
/// design's paper and inks, its frame and ornament, its heading and body faces, and the same words
/// (`EventMenuWording`). The menu's colors are its own paper, never the app's theme.
struct DesignedMenuView: View {
    let menu: EventMenu
    let design: MenuDesign?

    @Environment(AppDependencies.self) private var dependencies
    private var fonts: MenuFontLibrary { MenuFontLibrary.shared }

    var body: some View {
        let d = design
        VStack(spacing: 0) {
            VStack(spacing: 6) {
                if let logo = menu.logoUploadFileId {
                    MenuPicture(uploadFileId: logo, height: 64, fill: false)
                }
                MenuOrnamentView(ornament: d?.ornament ?? .flourish, colour: accent, paper: paper)
                    .frame(width: 120, height: 22)
                Text(menu.eventName.uppercased())
                    .font(body(size: 11))
                    .tracking(1.4)
                    .foregroundStyle(muted)
                    .multilineTextAlignment(.center)
                Text(menu.shownHeading)
                    .font(heading(size: 30))
                    .foregroundStyle(accent)
                    .multilineTextAlignment(.center)
                ForEach(EventMenuWording.whenLines(menu), id: \.self) { line in
                    Text(line).font(body(size: 13, italic: true)).foregroundStyle(muted).multilineTextAlignment(.center)
                }
                if let intro = menu.intro, !intro.isEmpty {
                    Text(intro).font(body(size: 15, italic: true)).foregroundStyle(ink).multilineTextAlignment(.center)
                        .padding(.top, 4)
                }
                if let prices = EventMenuWording.priceLine(menu) {
                    Text(prices).font(body(size: 15, weight: 700)).foregroundStyle(accent).multilineTextAlignment(.center)
                        .padding(.top, 4)
                }
                if let included = EventMenuWording.includedLine(menu) {
                    Text(included).font(body(size: 15, weight: 700)).foregroundStyle(accent)
                }
                if let note = menu.priceNote, !note.isEmpty {
                    Text(note).font(body(size: 11)).foregroundStyle(muted)
                }
                Rectangle().fill(accent).frame(width: 140, height: 1).padding(.top, 10)
            }
            .frame(maxWidth: .infinity)
            .padding(.bottom, 18)

            VStack(alignment: .leading, spacing: 20) {
                ForEach(menu.sections) { section in
                    VStack(alignment: .leading, spacing: 10) {
                        VStack(spacing: 4) {
                            Text(d?.upperHeadings == true ? section.title.uppercased() : section.title)
                                .font(heading(size: 17))
                                .tracking(d?.upperHeadings == true ? 2 : 0)
                                .foregroundStyle(accent)
                            Rectangle().fill(accent).frame(width: 40, height: 1)
                        }
                        .frame(maxWidth: .infinity)

                        ForEach(section.entries) { entry in entryRow(entry) }
                    }
                }
            }

            if let key = EventMenuWording.key(menu) {
                Text(key).font(body(size: 11)).foregroundStyle(muted).multilineTextAlignment(.center)
                    .padding(.top, 16)
            }
            // The small site mark beside the credit, as the website and the PDF draw it (event-scenario-gaps).
            HStack(spacing: 4) {
                Image("MenuMark").resizable().frame(width: 9, height: 9)
                    .clipShape(RoundedRectangle(cornerRadius: 2))
                    .accessibilityHidden(true)
                Text(EventMenuWording.credit(year: Calendar.current.component(.year, from: .now)))
                    .font(.system(size: 9))
                    .foregroundStyle(muted.opacity(0.85))
            }
            .padding(.top, 18)
        }
        .padding(.horizontal, 22)
        .padding(.vertical, 26)
        .background(paper)
        .overlay { frame }
        .clipShape(RoundedRectangle(cornerRadius: 10))
        .environment(\.colorScheme, (d?.isDark ?? false) ? .dark : .light)
        .accessibilityElement(children: .contain)
        .task(id: menu.designKey) {
            if let design { await fonts.prepare(design, api: dependencies.api) }
        }
    }

    // ── one item ─────────────────────────────────────────────────────────────

    @ViewBuilder
    private func entryRow(_ entry: EventMenuEntry) -> some View {
        HStack(alignment: .top, spacing: 10) {
            if let picture = entry.item.imageUploadFileId {
                MenuPicture(uploadFileId: picture, height: 48, fill: true)
                    .frame(width: 48, height: 48)
                    .clipShape(RoundedRectangle(cornerRadius: 6))
            }
            VStack(alignment: .leading, spacing: 2) {
                HStack(alignment: .firstTextBaseline, spacing: 6) {
                    // The name and marks keep their width; the leader takes only what is left over.
                    Text(entry.item.name).font(body(size: 16, weight: 700)).foregroundStyle(ink)
                        .layoutPriority(2)
                    if let letters = EventMenuWording.letters(entry.item.dietary) {
                        Text(letters).font(body(size: 11, weight: 700)).foregroundStyle(muted)
                            .fixedSize().layoutPriority(2)
                    }
                    if let price = EventMenuWording.entryPrice(menu, entry) {
                        if design?.dottedLeaders == true {
                            Line().stroke(muted, style: StrokeStyle(lineWidth: 1, dash: [1, 3]))
                                .frame(minWidth: 12, maxHeight: 1).alignmentGuide(.firstTextBaseline) { _ in 0 }
                        } else {
                            Spacer(minLength: 8)
                        }
                        Text(price).font(body(size: 16, weight: 700)).foregroundStyle(accent)
                            .fixedSize().layoutPriority(3)
                    }
                }
                if let description = entry.item.description, !description.isEmpty {
                    Text(description).font(body(size: 13, italic: true)).foregroundStyle(muted)
                }
                if let allergens = EventMenuWording.allergenLine(entry.item) {
                    Text(allergens).font(body(size: 11)).foregroundStyle(muted)
                }
                if let note = entry.note, !note.isEmpty {
                    Text(note).font(body(size: 12, italic: true)).foregroundStyle(accent)
                }
            }
        }
        .accessibilityElement(children: .combine)
    }

    // ── the design ───────────────────────────────────────────────────────────

    private var paper: Color { Color(menuHex: design?.paper ?? "#FBF6EC") }
    private var ink: Color { Color(menuHex: design?.ink ?? "#2B2B2B") }
    private var accent: Color { Color(menuHex: design?.accent ?? "#7B2D26") }
    private var muted: Color { Color(menuHex: design?.muted ?? "#6B6460") }

    private func heading(size: CGFloat) -> Font {
        fonts.font(design?.headingFace, weight: design?.headingWeight ?? 700, italic: false,
                   size: size * CGFloat(design?.headingScale ?? 1), kind: design?.font ?? .serif, relativeTo: .title)
    }

    private func body(size: CGFloat, weight: Int = 400, italic: Bool = false) -> Font {
        fonts.font(design?.bodyFace, weight: weight, italic: italic, size: size, kind: design?.font ?? .serif)
    }

    @ViewBuilder
    private var frame: some View {
        switch design?.frame ?? .single {
        case .none:
            EmptyView()
        case .single:
            RoundedRectangle(cornerRadius: 4).stroke(accent, lineWidth: 1.2).padding(8)
        case .double:
            ZStack {
                RoundedRectangle(cornerRadius: 4).stroke(accent, lineWidth: 1.6).padding(6)
                RoundedRectangle(cornerRadius: 3).stroke(accent, lineWidth: 0.7).padding(12)
            }
        case .corners:
            CornerMarks().stroke(accent, lineWidth: 1.2).padding(8)
        }
    }
}

/// A menu's photo or logo, once its menu is published (`api/public/menu-image`).
private struct MenuPicture: View {
    let uploadFileId: UUID
    let height: CGFloat
    let fill: Bool

    @Environment(AppDependencies.self) private var dependencies
    @State private var image: UIImage?

    var body: some View {
        Group {
            if let image {
                Image(uiImage: image).resizable().aspectRatio(contentMode: fill ? .fill : .fit)
            } else {
                Color.clear
            }
        }
        .frame(height: height)
        .task(id: uploadFileId) {
            if case .ok(let data) = await dependencies.api.loadData(HostedEventsStore.menuImageEndpoint(uploadFileId)) {
                image = UIImage(data: data)
            }
        }
    }
}

private struct Line: Shape {
    func path(in rect: CGRect) -> Path {
        var path = Path()
        path.move(to: CGPoint(x: rect.minX, y: rect.midY))
        path.addLine(to: CGPoint(x: rect.maxX, y: rect.midY))
        return path
    }
}

private struct CornerMarks: Shape {
    func path(in rect: CGRect) -> Path {
        var path = Path()
        let arm: CGFloat = 34
        for (x, y, dx, dy) in [(rect.minX, rect.minY, 1.0, 1.0), (rect.maxX, rect.minY, -1.0, 1.0),
                               (rect.minX, rect.maxY, 1.0, -1.0), (rect.maxX, rect.maxY, -1.0, -1.0)] {
            path.move(to: CGPoint(x: x + dx * arm, y: y))
            path.addLine(to: CGPoint(x: x, y: y))
            path.addLine(to: CGPoint(x: x, y: y + dy * arm))
            path.addEllipse(in: CGRect(x: x + dx * 7 - 2, y: y + dy * 7 - 2, width: 4, height: 4))
        }
        return path
    }
}

/// The design's small drawing, the same shapes the website's SVG and the PDF draw.
struct MenuOrnamentView: View {
    let ornament: MenuOrnament
    let colour: Color
    let paper: Color

    var body: some View {
        Canvas { context, size in
            let s = min(size.width / 120, size.height / 24)
            let cx = size.width / 2, cy = size.height / 2
            func p(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: cx + x * s, y: cy + y * s) }
            func line(_ a: CGPoint, _ b: CGPoint) {
                var path = Path(); path.move(to: a); path.addLine(to: b)
                context.stroke(path, with: .color(colour), lineWidth: 1)
            }
            switch ornament {
            case .none:
                break
            case .flourish:
                var curve = Path()
                curve.move(to: p(-60, 0)); curve.addCurve(to: p(-7, 0), control1: p(-30, -8), control2: p(-18, 6))
                curve.move(to: p(60, 0)); curve.addCurve(to: p(7, 0), control1: p(30, -8), control2: p(18, 6))
                context.stroke(curve, with: .color(colour), lineWidth: 1.2)
                var diamond = Path()
                diamond.move(to: p(0, -5)); diamond.addLine(to: p(5, 0)); diamond.addLine(to: p(0, 5)); diamond.addLine(to: p(-5, 0)); diamond.closeSubpath()
                context.fill(diamond, with: .color(colour))
            case .crescent:
                context.fill(Path(ellipseIn: CGRect(x: cx - 8 * s, y: cy - 8 * s, width: 16 * s, height: 16 * s)), with: .color(colour))
                context.fill(Path(ellipseIn: CGRect(x: cx - 3.5 * s, y: cy - 9 * s, width: 14 * s, height: 14 * s)), with: .color(paper))
                for (x, y) in [(-22.0, -3.0), (24.0, 2.0)] {
                    context.fill(Path(ellipseIn: CGRect(x: cx + x * s - 1.2 * s, y: cy + y * s - 1.2 * s, width: 2.4 * s, height: 2.4 * s)), with: .color(colour))
                }
            case .stars:
                for (x, r) in [(-20.0, 3.5), (0.0, 6.0), (20.0, 3.5)] { context.fill(star(p(x, 0), r * s), with: .color(colour)) }
            case .candle:
                var flame = Path()
                flame.move(to: p(0, -12)); flame.addLine(to: p(3, -5)); flame.addLine(to: p(0, -3)); flame.addLine(to: p(-3, -5)); flame.closeSubpath()
                context.fill(flame, with: .color(colour))
                context.fill(Path(CGRect(x: cx - 3 * s, y: cy - 2 * s, width: 6 * s, height: 12 * s)), with: .color(colour))
                line(p(-40, 6), p(-10, 6)); line(p(10, 6), p(40, 6))
            case .planchette:
                var heart = Path()
                heart.move(to: p(0, -10))
                heart.addCurve(to: p(0, 10), control1: p(14, -8), control2: p(12, 10))
                heart.addCurve(to: p(0, -10), control1: p(-12, 10), control2: p(-14, -8))
                context.stroke(heart, with: .color(colour), lineWidth: 1.2)
                context.stroke(Path(ellipseIn: CGRect(x: cx - 3.2 * s, y: cy - 3.2 * s, width: 6.4 * s, height: 6.4 * s)), with: .color(colour), lineWidth: 1.2)
                line(p(-46, 0), p(-18, 0)); line(p(18, 0), p(46, 0))
            case .waveform:
                for (i, h) in [1, 2, 4, 3, 7, 10, 5, 12, 6, 3, 9, 4, 2, 5, 1].enumerated() {
                    let x = CGFloat(i - 7) * 5
                    line(p(x, -CGFloat(h)), p(x, CGFloat(h)))
                }
            case .leaf:
                for dir in [-1.0, 1.0] {
                    var leaf = Path()
                    leaf.move(to: p(8 * dir, 0))
                    leaf.addCurve(to: p(22 * dir, 0), control1: p(12 * dir, -8), control2: p(20 * dir, -6))
                    leaf.addCurve(to: p(8 * dir, 0), control1: p(20 * dir, 6), control2: p(12 * dir, 8))
                    context.fill(leaf, with: .color(colour))
                }
                line(p(-50, 0), p(-24, 0)); line(p(24, 0), p(50, 0))
            case .stamp:
                let box = CGRect(x: cx - 52 * s, y: cy - 9 * s, width: 104 * s, height: 18 * s)
                context.stroke(Path(box), with: .color(colour), lineWidth: 1.4)
                context.draw(Text("EVIDENCE · MENU").font(.system(size: 8 * s, weight: .bold, design: .monospaced)).foregroundStyle(colour),
                             at: CGPoint(x: cx, y: cy))
            case .ghost:
                var ghost = Path()
                ghost.move(to: p(-9, 10)); ghost.addLine(to: p(-9, -2))
                ghost.addCurve(to: p(9, -2), control1: p(-9, -14), control2: p(9, -14))
                ghost.addLine(to: p(9, 10))
                ghost.addCurve(to: p(3, 10), control1: p(7, 6), control2: p(5, 6))
                ghost.addCurve(to: p(-3, 10), control1: p(1, 6), control2: p(-1, 6))
                ghost.addCurve(to: p(-9, 10), control1: p(-5, 6), control2: p(-7, 6))
                ghost.closeSubpath()
                context.fill(ghost, with: .color(colour))
                for x in [-3.2, 3.2] {
                    context.fill(Path(ellipseIn: CGRect(x: cx + x * s - 1.6 * s, y: cy - 4.6 * s, width: 3.2 * s, height: 3.2 * s)), with: .color(paper))
                }
                line(p(-46, 4), p(-16, 4)); line(p(16, 4), p(46, 4))
            case .bat:
                let points: [(CGFloat, CGFloat)] = [(0, -3), (3, -6), (6, -3), (16, -7), (22, -2), (16, 0), (13, 4), (8, 1), (4, 5), (0, 2),
                                                    (-4, 5), (-8, 1), (-13, 4), (-16, 0), (-22, -2), (-16, -7), (-6, -3), (-3, -6)]
                var bat = Path()
                bat.move(to: p(points[0].0, points[0].1))
                for (x, y) in points.dropFirst() { bat.addLine(to: p(x, y)) }
                bat.closeSubpath()
                context.fill(bat, with: .color(colour))
            }
        }
        .accessibilityHidden(true)
    }

    private func star(_ c: CGPoint, _ r: CGFloat) -> Path {
        var path = Path()
        for i in 0..<10 {
            let radius = i % 2 == 0 ? r : r * 0.42
            let angle = Double.pi / 2 + Double(i) * Double.pi / 5
            let point = CGPoint(x: c.x + radius * cos(angle), y: c.y - radius * sin(angle))
            if i == 0 { path.move(to: point) } else { path.addLine(to: point) }
        }
        path.closeSubpath()
        return path
    }
}

extension Color {
    /// "#2A0E12" as a color, for a menu design's own inks.
    init(menuHex hex: String) {
        let digits = hex.trimmingCharacters(in: CharacterSet(charactersIn: "#"))
        let value = UInt64(digits, radix: 16) ?? 0
        self.init(red: Double((value >> 16) & 0xFF) / 255, green: Double((value >> 8) & 0xFF) / 255, blue: Double(value & 0xFF) / 255)
    }
}
