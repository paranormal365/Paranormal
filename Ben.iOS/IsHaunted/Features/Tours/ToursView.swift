import SwiftUI
import CoreLocation
import BenKit

/// Haunted Tours (item 234, Ben 2026-09-10).
///
/// Ben: *"I would also like to add the tours in a Haunted Tours tab in the iPhone and iPad app
/// based on current location or looking up a location on the tab."* Both, and neither needs an
/// account: a walk is something somebody looks for before they have one, so this whole screen is
/// anonymous.
///
/// **Location is asked for, never taken.** The tab opens on a list of everything rather than a
/// permission sheet — somebody who declines still has a working screen and a box to type a place
/// into, which is the same bargain the website's nearby search makes.
struct ToursView: View {
    @Environment(AppDependencies.self) private var dependencies

    @State private var store: ToursStore?
    @State private var locator = TourLocator()
    @State private var place = ""
    @State private var radius = 25
    @State private var searching = false

    private static let radii = [5, 10, 25, 50, 100]

    var body: some View {
        VStack(spacing: 0) {
            searchBar
            list
        }
        .navigationTitle("Haunted Tours")
        .task {
            if store == nil { store = ToursStore(api: dependencies.api) }
            if case .idle = store?.state { await searchEverywhere() }
        }
    }

    // ── Where to look ────────────────────────────────────────────────────────

    private var searchBar: some View {
        VStack(spacing: 8) {
            HStack {
                Image(systemName: "magnifyingglass").foregroundStyle(Theme.fog)
                TextField("A tour, a business, or a city", text: $place)
                    .textInputAutocapitalization(.words)
                    .autocorrectionDisabled()
                    .onSubmit { Task { await searchTyped() } }
                if !place.isEmpty {
                    Button {
                        place = ""
                        Task { await searchEverywhere() }
                    } label: {
                        Image(systemName: "xmark.circle.fill").foregroundStyle(Theme.fog)
                    }
                    .accessibilityLabel("Clear")
                }
            }
            .padding(10)
            .background(Theme.mist, in: RoundedRectangle(cornerRadius: 10))

            HStack {
                Button {
                    Task { await searchNearMe() }
                } label: {
                    Label("Near me", systemImage: "location")
                }
                .buttonStyle(.bordered)
                .disabled(searching)

                Spacer()

                Picker("Within", selection: $radius) {
                    ForEach(Self.radii, id: \.self) { Text("\($0) mi").tag($0) }
                }
                .pickerStyle(.menu)
                .onChange(of: radius) { _, _ in Task { await repeatLastSearch() } }
            }

            if let near = store?.searchedNear {
                Text("Near \(near)").font(.caption).foregroundStyle(Theme.fog)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            if let refusal = locator.refusal {
                // Said out loud rather than left as a button that does nothing.
                Text(refusal).font(.caption).foregroundStyle(Theme.warning)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .padding(.horizontal)
        .padding(.bottom, 8)
    }

    // ── What was found ───────────────────────────────────────────────────────

    @ViewBuilder
    private var list: some View {
        switch store?.state {
        case .none, .idle, .loading:
            ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)

        case .failed(let reason):
            ContentUnavailableView {
                Label("Couldn't look for tours", systemImage: "exclamationmark.triangle")
                    .foregroundStyle(Theme.warning)
            } description: {
                Text(reason ?? "The server couldn't be reached.")
            } actions: {
                Button("Try again") { Task { await repeatLastSearch() } }
                    .buttonStyle(.borderedProminent)
            }

        case .loaded:
            if store?.tours.isEmpty == true {
                ContentUnavailableView {
                    Label("No tours here", systemImage: "figure.walk")
                } description: {
                    Text(place.isEmpty
                         ? "Nothing within \(radius) miles. Try a wider distance, or look up a city."
                         : "Nothing matching “\(place)”. Try a wider distance, or another name.")
                }
            } else {
                List(store?.tours ?? []) { tour in
                    NavigationLink(value: AppRoute.tourDetail(
                        organizationUrlName: tour.organizationUrlName, tourSlug: tour.urlName)) {
                        TourRow(tour: tour)
                    }
                }
                .listStyle(.insetGrouped)
                .refreshable { await repeatLastSearch() }
            }
        }
    }

    // ── Searching ────────────────────────────────────────────────────────────

    private func searchEverywhere() async {
        searching = true
        defer { searching = false }
        await store?.load(radiusMiles: radius, query: place.isEmpty ? nil : place, near: nil)
    }

    private func searchTyped() async {
        searching = true
        defer { searching = false }

        // A typed place is resolved to a point when it looks like one, so "Nashville" sorts by
        // distance rather than only matching tours whose CITY column happens to say Nashville.
        if let point = await locator.geocode(place) {
            await store?.load(latitude: point.latitude, longitude: point.longitude,
                              radiusMiles: radius, near: place)
        } else {
            await store?.load(radiusMiles: radius, query: place, near: nil)
        }
    }

    private func searchNearMe() async {
        searching = true
        defer { searching = false }

        guard let here = await locator.whereAmI() else { return }
        await store?.load(latitude: here.latitude, longitude: here.longitude,
                          radiusMiles: radius, query: place.isEmpty ? nil : place,
                          near: "you")
    }

    /// The same search again — after a distance change, a pull, or a retry.
    private func repeatLastSearch() async {
        if locator.lastPoint != nil && store?.searchedNear != nil {
            searching = true
            defer { searching = false }
            let point = locator.lastPoint!
            await store?.load(latitude: point.latitude, longitude: point.longitude,
                              radiusMiles: radius, query: place.isEmpty ? nil : place,
                              near: store?.searchedNear)
        } else if !place.isEmpty {
            await searchTyped()
        } else {
            await searchEverywhere()
        }
    }
}

/// One tour in the list.
struct TourRow: View {
    let tour: PublicTourListItem

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(tour.name).font(.headline).foregroundStyle(Theme.bone)
            Text(tour.organizationName).font(.subheadline).foregroundStyle(Theme.fog)

            HStack(spacing: 6) {
                if let place = tour.placeLabel {
                    Label(place, systemImage: "mappin.and.ellipse")
                }
                if let distance = tour.distanceLabel {
                    Text("· \(distance)")
                }
            }
            .font(.caption).foregroundStyle(Theme.fog)

            HStack(spacing: 8) {
                if let length = tour.lengthLabel {
                    Text(length)
                }
                if let rating = tour.ratingLabel {
                    Text("· \(rating)")
                }
                if let next = tour.nextDateStartUtc {
                    // The walk's own clock, not this phone's — see EventClock.
                    Text("· next \(EventClock.dayAndTime(next, tour.timeZoneId))")
                } else {
                    Text("· no dates yet")
                }
            }
            .font(.caption).foregroundStyle(Theme.fog)
        }
        .padding(.vertical, 4)
    }
}

/// Where to search from: this phone, or a place somebody typed.
///
/// Permission is asked at the moment somebody presses **Near me**, not when the tab opens. A
/// permission sheet in front of a screen that already works is a sheet people decline, and iOS
/// only ever asks once.
@MainActor
@Observable
final class TourLocator: NSObject, CLLocationManagerDelegate {
    private(set) var refusal: String?
    private(set) var lastPoint: CLLocationCoordinate2D?

    private let manager = CLLocationManager()
    private var waiting: CheckedContinuation<CLLocationCoordinate2D?, Never>?

    override init() {
        super.init()
        manager.delegate = self
        manager.desiredAccuracy = kCLLocationAccuracyKilometer   // a city block is plenty
    }

    /// This phone's position, or nil with a sentence in `refusal`.
    func whereAmI() async -> CLLocationCoordinate2D? {
        refusal = nil

        switch manager.authorizationStatus {
        case .denied, .restricted:
            // Never re-ask: iOS would not show it. Say what to do instead.
            refusal = "Location is off for IsHaunted. Turn it on in Settings, or type a place."
            return nil
        case .notDetermined:
            manager.requestWhenInUseAuthorization()
        default:
            break
        }

        let point = await withCheckedContinuation { (continuation: CheckedContinuation<CLLocationCoordinate2D?, Never>) in
            waiting = continuation
            manager.requestLocation()
        }
        if point == nil && refusal == nil {
            refusal = "Couldn't work out where you are. Type a place instead."
        }
        lastPoint = point ?? lastPoint
        return point
    }

    /// A typed place as a point, or nil when it is not one — "ghost" is a name, not a city.
    func geocode(_ text: String) async -> CLLocationCoordinate2D? {
        guard !text.trimmingCharacters(in: .whitespaces).isEmpty else { return nil }
        let found = try? await CLGeocoder().geocodeAddressString(text)
        let point = found?.first?.location?.coordinate
        lastPoint = point ?? lastPoint
        return point
    }

    nonisolated func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        let point = locations.last?.coordinate
        Task { @MainActor in
            waiting?.resume(returning: point)
            waiting = nil
        }
    }

    nonisolated func locationManager(_ manager: CLLocationManager, didFailWithError error: Error) {
        Task { @MainActor in
            waiting?.resume(returning: nil)
            waiting = nil
        }
    }
}
