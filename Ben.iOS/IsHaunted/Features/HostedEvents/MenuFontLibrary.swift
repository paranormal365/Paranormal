import SwiftUI
import CoreText
import BenKit

/// The fonts the menu designs are set in, fetched the first time a menu needs them (10/13/2026).
///
/// **Fetched, not bundled.** The site has 25 designs set in 23 free families (44 files), and a design
/// added on the server should reach the app without an update. So the catalog says which files exist,
/// each file is downloaded once from `api/public/menu-fonts/{file}`, kept in Caches, and registered for
/// this process. A menu is drawn in the system's own serif, sans or monospaced face until its files
/// arrive, then redraws in its own.
@MainActor
@Observable
final class MenuFontLibrary {
    static let shared = MenuFontLibrary()

    private(set) var catalog: MenuDesignCatalog?
    /// File name to the PostScript name SwiftUI needs, once registered.
    private var registered: [String: String] = [:]
    private var inFlight: Set<String> = []
    /// Bumped as fonts arrive, so a menu on screen redraws in them.
    private(set) var revision = 0

    private var folder: URL {
        FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("MenuFonts", isDirectory: true)
    }

    /// The catalog, read once a launch.
    func loadCatalog(_ api: APIClient) async {
        if catalog != nil { return }
        if case .ok(let value) = await HostedEventsStore(api: api).loadMenuDesigns() { catalog = value }
    }

    /// Fetches and registers what a design uses: its heading face and its body's regular, bold and italic.
    func prepare(_ design: MenuDesign, api: APIClient) async {
        guard let catalog else { return }
        let wanted = [
            catalog.closest(design.headingFace, weight: design.headingWeight, italic: false),
            catalog.closest(design.bodyFace, weight: 400, italic: false),
            catalog.closest(design.bodyFace, weight: 700, italic: false),
            catalog.closest(design.bodyFace, weight: 400, italic: true),
        ].compactMap { $0 }
        for file in Set(wanted) where registered[file.file] == nil && !inFlight.contains(file.file) {
            inFlight.insert(file.file)
            if let local = await cached(file.file, api: api), let name = Self.register(local) {
                registered[file.file] = name
                revision += 1
            }
            inFlight.remove(file.file)
        }
    }

    /// The design's face at a size, or the system face of the same kind until it has arrived.
    func font(_ family: String?, weight: Int, italic: Bool, size: CGFloat, kind: MenuFontKind,
              relativeTo style: Font.TextStyle = .body) -> Font {
        _ = revision
        if let file = catalog?.closest(family, weight: weight, italic: italic), let name = registered[file.file] {
            return .custom(name, size: size, relativeTo: style)
        }
        let design: Font.Design = switch kind { case .serif: .serif; case .sans: .default; case .typewriter: .monospaced }
        var font = Font.system(size: size, weight: weight >= 650 ? .bold : .regular, design: design)
        if italic { font = font.italic() }
        return font
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    /// The file in Caches, fetched first if it isn't there yet.
    private func cached(_ file: String, api: APIClient) async -> URL? {
        let local = folder.appendingPathComponent(file)
        if FileManager.default.fileExists(atPath: local.path) { return local }
        guard case .ok(let data) = await api.loadData(HostedEventsStore.menuFontEndpoint(file)) else { return nil }
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        guard (try? data.write(to: local, options: .atomic)) != nil else { return nil }
        return local
    }

    /// Registers a TrueType font for this process and returns its PostScript name. A font already
    /// registered (the same file, another menu) counts as registered.
    private static func register(_ url: URL) -> String? {
        guard let descriptors = CTFontManagerCreateFontDescriptorsFromURL(url as CFURL) as? [CTFontDescriptor],
              let first = descriptors.first,
              let name = CTFontDescriptorCopyAttribute(first, kCTFontNameAttribute) as? String else {
            // Not a font after all (a cut-off download): drop it so the next launch fetches it again.
            try? FileManager.default.removeItem(at: url)
            return nil
        }
        var error: Unmanaged<CFError>?
        if !CTFontManagerRegisterFontsForURL(url as CFURL, .process, &error) {
            // Already registered by an earlier load is fine; anything else leaves the system face in place.
            let code = (error?.takeRetainedValue() as Error?).map { ($0 as NSError).code }
            if code != CTFontManagerError.alreadyRegistered.rawValue { return nil }
        }
        return name
    }
}
