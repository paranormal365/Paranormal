import SwiftUI
import BenKit

/// One walking tour (item 234).
///
/// The same facts the website's tour page shows: where you meet, how long it runs, who leads it,
/// what it costs to arrange with the business, the nights coming up, and what people made of it.
/// A night here opens the event screen from phase 3, which is where a place is actually asked for.
struct TourDetailView: View {
    let organizationUrlName: String
    let tourSlug: String

    @Environment(AppDependencies.self) private var dependencies

    @State private var store: ToursStore?
    @State private var tour: PublicTourRecord?
    @State private var loading = true

    var body: some View {
        Group {
            if loading {
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if let tour {
                content(tour)
            } else {
                ContentUnavailableView {
                    Label("Couldn't load this tour", systemImage: "exclamationmark.triangle")
                        .foregroundStyle(Theme.warning)
                } description: {
                    Text("It may have stopped running.")
                } actions: {
                    Button("Try again") { Task { await load() } }.buttonStyle(.borderedProminent)
                }
            }
        }
        .navigationTitle(tour?.name ?? "Tour")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            if store == nil { store = ToursStore(api: dependencies.api) }
            await load()
        }
    }

    @ViewBuilder
    private func content(_ tour: PublicTourRecord) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                VStack(alignment: .leading, spacing: 4) {
                    Text(tour.name).font(.title2.weight(.semibold)).foregroundStyle(Theme.bone)
                    Text(tour.organizationName).font(.subheadline).foregroundStyle(Theme.fog)
                    HStack(spacing: 8) {
                        if let length = lengthLabel(tour) { Text(length) }
                        if let rating = tour.ratingLabel { Text("· \(rating)") }
                    }
                    .font(.caption).foregroundStyle(Theme.fog)
                }

                if !tour.isBookable {
                    // A closed season. Dates already set still stand, which is why this is a note
                    // rather than an empty page.
                    Label("Not taking sign-ups just now", systemImage: "pause.circle")
                        .font(.footnote).foregroundStyle(Theme.warning)
                }

                if let description = tour.description, !description.isEmpty {
                    Text(description.strippingTags).font(.body).foregroundStyle(Theme.fog)
                }

                meetingPoint(tour)
                guides(tour)
                dates(tour)

                if let contact = tour.contactLine, !contact.isEmpty {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("Arranging it").font(.headline).foregroundStyle(Theme.bone)
                        Text(contact).font(.body).foregroundStyle(Theme.fog)
                        // Said once, plainly, wherever money is mentioned.
                        Text("IsHaunted doesn't take payment — that's between you and them.")
                            .font(.caption).foregroundStyle(Theme.fog)
                    }
                }
            }
            .padding()
        }
    }

    @ViewBuilder
    private func meetingPoint(_ tour: PublicTourRecord) -> some View {
        if let point = tour.meetingPoint, !point.isEmpty {
            VStack(alignment: .leading, spacing: 8) {
                Text("Where you meet").font(.headline).foregroundStyle(Theme.bone)
                Text(point).font(.body).foregroundStyle(Theme.fog)
                if let url = directionsURL(point) {
                    Link(destination: url) {
                        Label("Directions", systemImage: "arrow.triangle.turn.up.right.circle")
                    }
                    .buttonStyle(.bordered)
                }
            }
        }
    }

    @ViewBuilder
    private func guides(_ tour: PublicTourRecord) -> some View {
        if let guides = tour.guides, !guides.isEmpty {
            VStack(alignment: .leading, spacing: 6) {
                Text(guides.count == 1 ? "Your guide" : "Your guides")
                    .font(.headline).foregroundStyle(Theme.bone)
                ForEach(guides, id: \.displayName) { guide in
                    Label(guide.displayName, systemImage: "person.circle")
                        .font(.body).foregroundStyle(Theme.fog)
                }
            }
        }
    }

    @ViewBuilder
    private func dates(_ tour: PublicTourRecord) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Nights coming up").font(.headline).foregroundStyle(Theme.bone)

            if let dates = tour.upcomingDates, !dates.isEmpty {
                ForEach(dates) { date in
                    NavigationLink(value: AppRoute.eventDetail(date.id)) {
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                // The walk's own clock, not this phone's.
                                Text(EventClock.dayAndTime(date.startDateTime, date.timeZoneId))
                                    .font(.body).foregroundStyle(Theme.bone)
                                if let left = date.spacesLeft {
                                    Text(date.isFull ? "Full" : "\(left) place\(left == 1 ? "" : "s") left")
                                        .font(.caption)
                                        .foregroundStyle(date.isFull ? Theme.warning : Theme.fog)
                                }
                            }
                            Spacer()
                            Image(systemName: "chevron.right").foregroundStyle(Theme.fog)
                        }
                        .padding()
                        .background(Theme.mist, in: RoundedRectangle(cornerRadius: 10))
                    }
                    .buttonStyle(.plain)
                }
            } else {
                Text("Nothing on the calendar yet.").font(.footnote).foregroundStyle(Theme.fog)
            }
        }
    }

    private func lengthLabel(_ tour: PublicTourRecord) -> String? {
        guard let minutes = tour.durationMinutes, minutes > 0 else { return nil }
        let hours = minutes / 60, rest = minutes % 60
        return switch (hours, rest) {
        case (0, let m): "\(m) min"
        case (let h, 0): h == 1 ? "1 hr" : "\(h) hrs"
        case (let h, let m): "\(h) hr \(m) min"
        }
    }

    private func directionsURL(_ address: String) -> URL? {
        guard let encoded = address.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed)
        else { return nil }
        return URL(string: "https://maps.apple.com/?daddr=\(encoded)")
    }

    private func load() async {
        loading = true
        defer { loading = false }
        tour = await store?.loadOne(organizationUrlName: organizationUrlName, tourSlug: tourSlug)
    }
}

private extension String {
    /// The server already sanitized this; the phone has no HTML view here, so the tags come out
    /// rather than being printed at somebody.
    var strippingTags: String {
        replacingOccurrences(of: "<[^>]+>", with: "", options: .regularExpression)
            .replacingOccurrences(of: "&nbsp;", with: " ")
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
