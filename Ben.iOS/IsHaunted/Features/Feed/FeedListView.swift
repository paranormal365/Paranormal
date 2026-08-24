import SwiftUI
import BenKit

/// The feed (iOS Slice 3, read-only): For You / Latest / Following, infinite
/// scroll with the For You de-dupe handled in the store, and the honest states —
/// including "the feed isn't available", which is a fact, not an error.
struct FeedListView: View {
    @Environment(AppDependencies.self) private var dependencies

    /// Non-nil pins the list to one filter (a tag page, a type page, an author).
    let fixedFilter: FeedFilter?

    @State private var store: FeedStore?
    @State private var mode: FeedFilter = .forYou

    init(fixedFilter: FeedFilter? = nil) {
        self.fixedFilter = fixedFilter
    }

    var body: some View {
        Group {
            if let store {
                content(store)
            } else {
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .navigationTitle(fixedFilter?.title ?? "Feed")
        .toolbar {
            if fixedFilter == nil {
                ToolbarItem(placement: .principal) { modePicker }
            }
        }
        .task(id: taskIdentity) { await reload() }
        .refreshable { await store?.load() }
    }

    private var taskIdentity: FeedFilter { fixedFilter ?? mode }

    private var modePicker: some View {
        Picker("Mode", selection: $mode) {
            Text("For You").tag(FeedFilter.forYou)
            Text("Latest").tag(FeedFilter.latest)
            if dependencies.session.me != nil {
                Text("Following").tag(FeedFilter.following)
            }
        }
        .pickerStyle(.segmented)
        .frame(maxWidth: 320)
    }

    @ViewBuilder
    private func content(_ store: FeedStore) -> some View {
        switch store.state {
        case .loading:
            ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
        case .featureUnavailable:
            // A switched-off feature, said as such — never an error, never "nothing here".
            ContentUnavailableView {
                Label("The feed isn't available right now", systemImage: "moon.zzz")
            } description: {
                Text("It's switched off sitewide. Everything else still works.")
            }
        case .failed(let reason):
            ContentUnavailableView {
                Label("Couldn't load the feed", systemImage: "exclamationmark.triangle")
                    .foregroundStyle(Theme.warning)
            } description: {
                Text(reason ?? "The server couldn't be reached.")
            } actions: {
                Button("Try again") { Task { await store.load() } }
                    .buttonStyle(.borderedProminent)
            }
        case .rateLimited(let retryAfter):
            ContentUnavailableView {
                Label("Slow down a moment", systemImage: "hourglass")
            } description: {
                Text(retryAfter.map { "Too many requests — try again in \(Int($0.rounded(.up))) seconds." }
                     ?? "Too many requests — try again shortly.")
            }
        case .loaded where store.posts.isEmpty:
            ContentUnavailableView {
                Label("Nothing here yet", systemImage: "sparkles")
            } description: {
                Text("Be the first — or follow some people.")
            }
        case .loaded:
            ScrollView {
                LazyVStack(spacing: 10) {
                    ForEach(store.posts) { post in
                        FeedCardView(post: post)
                    }
                    if store.hasMore {
                        ProgressView()
                            .padding()
                            .task { await store.loadMore() }
                    }
                }
                .padding(.horizontal, 12)
                .padding(.vertical, 8)
                .frame(maxWidth: 640)          // iPad: reading width, not wall-to-wall
                .frame(maxWidth: .infinity)
            }
            .background(Theme.ink)
        }
    }

    private func reload() async {
        let store = FeedStore(filter: taskIdentity, api: dependencies.api)
        self.store = store
        await store.load()
    }
}

/// One thread: the root and its replies, indented once — the feed is shallow by design.
struct FeedThreadView: View {
    @Environment(AppDependencies.self) private var dependencies
    let postId: UUID

    @State private var store: FeedThreadStore?

    var body: some View {
        Group {
            if let store {
                switch store.state {
                case .loading:
                    ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
                case .featureUnavailable:
                    ContentUnavailableView {
                        Label("That post isn't there", systemImage: "moon.zzz")
                    } description: {
                        Text("It may have been removed, or the feed is switched off.")
                    }
                case .failed(let reason):
                    ContentUnavailableView {
                        Label("Couldn't load this", systemImage: "exclamationmark.triangle")
                    } description: {
                        Text(reason ?? "The server couldn't be reached.")
                    } actions: {
                        Button("Try again") { Task { await store.load() } }
                    }
                case .rateLimited:
                    ContentUnavailableView {
                        Label("Slow down a moment", systemImage: "hourglass")
                    }
                case .loaded:
                    ScrollView {
                        LazyVStack(spacing: 10) {
                            ForEach(Array(store.posts.enumerated()), id: \.element.id) { index, post in
                                FeedCardView(post: post)
                                    .padding(.leading, index == 0 ? 0 : 20)
                            }
                        }
                        .padding(12)
                        .frame(maxWidth: 640)
                        .frame(maxWidth: .infinity)
                    }
                    .background(Theme.ink)
                }
            } else {
                ProgressView()
            }
        }
        .navigationTitle("Post")
        .task {
            let store = FeedThreadStore(postId: postId, api: dependencies.api)
            self.store = store
            await store.load()
        }
    }
}

/// A person's feed presence: counts up top, their posts below.
struct FeedProfileView: View {
    @Environment(AppDependencies.self) private var dependencies
    let appUserId: UUID

    @State private var profile: FeedProfileRecord?

    var body: some View {
        VStack(spacing: 0) {
            if let profile {
                HStack(spacing: 14) {
                    InitialsAvatar(displayName: profile.displayName, size: 56)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(profile.displayName)
                            .font(.title3.weight(.semibold))
                            .foregroundStyle(Theme.bone)
                        Text("\(profile.postCount) posts · \(profile.followerCount) followers · \(profile.followingCount) following")
                            .font(.caption)
                            .foregroundStyle(Theme.fog)
                    }
                    Spacer()
                }
                .padding(14)
            }
            FeedListView(fixedFilter: .author(appUserId))
        }
        .background(Theme.ink)
        .navigationTitle(profile?.displayName ?? "Profile")
        .task {
            let result = await dependencies.api.load(
                Endpoint(.get, "api/feed/profile/\(appUserId.uuidString.lowercased())"),
                as: FeedProfileRecord.self)
            if case .ok(let record) = result { profile = record }
            // A failed header fetch degrades to the list alone — the list carries
            // its own honest states, and a profile without counts still works.
        }
    }
}
