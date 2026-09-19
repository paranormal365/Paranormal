import SwiftUI
import BenKit

/// The feed: For You / Latest / Following, infinite scroll with the For You de-dupe
/// handled in the store, participation gated on the server's own CanPost, and the honest
/// states — including "the feed isn't available", which is a fact, not an error.
struct FeedListView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(Router.self) private var router

    /// Non-nil pins the list to one filter (a tag page, a type page, an author).
    let fixedFilter: FeedFilter?

    @State private var store: FeedStore?
    @State private var mode: FeedFilter = .forYou
    @State private var composing = false
    @State private var replyingTo: FeedPostRecord?
    @State private var recategorizing: FeedPostRecord?
    @State private var reporting: FeedPostRecord?
    @State private var blocking: FeedPostRecord?
    @State private var toast: String?

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
            // The bell. On iPhone there is no notifications tab, so without this the screen
            // is reachable only by deep link; the iPad sidebar carries its own entry.
            if dependencies.session.me != nil {
                ToolbarItem(placement: .topBarLeading) {
                    Button { router.push(.notifications, in: .feed) } label: {
                        Image(systemName: dependencies.notifications.badgeCount > 0
                              ? "bell.badge" : "bell")
                    }
                    // The NAME is the name; the count is a value. Baking the number into the
                    // label made the control rename itself as the count changed — VoiceOver
                    // announced a different control, and nothing could refer to it by name.
                    // The iPad sidebar's own row uses .badge, which is the same idea.
                    .accessibilityLabel("Notifications")
                    .accessibilityValue(dependencies.notifications.badgeCount > 0
                        ? "\(dependencies.notifications.badgeCount) waiting" : "")
                }
            }
            // CanPost is the SERVER's answer, so the button never invites a refusal.
            if store?.canPost == true {
                ToolbarItem(placement: .primaryAction) {
                    Button { composing = true } label: {
                        Image(systemName: "square.and.pencil")
                    }
                    .accessibilityLabel("New post")
                    .keyboardShortcut("n", modifiers: .command)   // iPad
                }
            }
        }
        .sheet(isPresented: $composing) {
            ComposerView(parentPostId: nil) { post in
                // Straight to the top of what they are looking at — but not onto a
                // filtered list it may not belong to.
                if fixedFilter == nil { store?.prepend(post) }
                toast = post.hasMedia || post.mediaAwaitingReview
                    ? "Posted — your media appears once it's checked."
                    : "Posted."
            }
            .environment(dependencies)
        }
        .sheet(item: $replyingTo) { parent in
            ComposerView(parentPostId: parent.id) { _ in
                toast = "Reply posted."
                Task { await store?.load() }   // reply counts moved
            }
            .environment(dependencies)
        }
        .sheet(item: $recategorizing) { post in
            RecategorizeSheet(post: post) { updated in
                store?.replace(updated)
                toast = "Category updated."
            }
            .environment(dependencies)
        }
        .alert("Report this post?", isPresented: Binding(
            get: { reporting != nil }, set: { if !$0 { reporting = nil } })
        ) {
            Button("Cancel", role: .cancel) { reporting = nil }
            Button("Report", role: .destructive) {
                if let post = reporting, let store {
                    Task {
                        let ok = await store.report(post, reason: nil, actions: dependencies.feedActions)
                        toast = ok ? "Reported. Thank you." : "Couldn't report that — try again."
                        reporting = nil
                    }
                }
            }
        } message: {
            Text("An administrator will look at it. Reporting twice is one report.")
        }
        .alert("Block \(blocking?.authorDisplayName ?? "this person")?", isPresented: Binding(
            get: { blocking != nil }, set: { if !$0 { blocking = nil } })
        ) {
            Button("Cancel", role: .cancel) { blocking = nil }
            Button("Block", role: .destructive) {
                if let post = blocking, let store {
                    Task {
                        let ok = await store.blockAuthor(of: post, actions: dependencies.feedActions)
                        toast = ok ? "Blocked. You won't see their posts."
                                   : "Couldn't block them — try again."
                        blocking = nil
                    }
                }
            }
        } message: {
            Text("Their posts and replies stop being shown to you. They aren't told, and you can undo this under Profile → Blocked accounts.")
        }
        .overlay(alignment: .bottom) {
            if let toast {
                Text(toast)
                    .font(.callout)
                    .padding(.horizontal, 16).padding(.vertical, 10)
                    .background(Theme.mist, in: Capsule())
                    .foregroundStyle(Theme.bone)
                    .shadow(radius: 6)
                    .padding(.bottom, 16)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
                    .task {
                        try? await Task.sleep(for: .seconds(3))
                        self.toast = nil
                    }
            }
        }
        .animation(.default, value: toast)
        .task(id: taskIdentity) { await reload() }
        // Sign-in resolves a beat AFTER the first page fetch, so that fetch asked the
        // server "may this reader post?" as a visitor and was told no. The website learned
        // this the same way (its _participationKnown flag); here the answer simply gets
        // asked again the moment the session changes, in either direction.
        .onChange(of: dependencies.session.me?.userId) { _, _ in
            Task { await store?.load() }
        }
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
                        FeedCardView(
                            post: post,
                            canAct: store.canPost,
                            onLike: { Task { await store.toggleLike(post, actions: dependencies.feedActions) } },
                            onReply: { replyingTo = post },
                            onFollow: { Task { await store.toggleFollow(post, actions: dependencies.feedActions) } },
                            onReport: { reporting = post },
                            onBlock: { blocking = post },
                            onRecategorize: { recategorizing = post })
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
    @State private var replyingTo: FeedPostRecord?
    /// The server's own CanPost, learned from a one-page feed fetch — the thread endpoint
    /// does not carry it, and guessing from "is signed in" would offer a reply box to
    /// somebody whose post the API will refuse.
    @State private var canAct = false

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
                                FeedCardView(
                                    post: post,
                                    canAct: canAct,
                                    onLike: { Task {
                                        _ = await dependencies.feedActions.setLiked(
                                            !post.likedByCurrentUser, postId: post.id)
                                        await store.load()
                                    } },
                                    onReply: { replyingTo = post })
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
        .sheet(item: $replyingTo) { parent in
            ComposerView(parentPostId: parent.id) { _ in
                Task { await store?.load() }
            }
            .environment(dependencies)
        }
        .task {
            let store = FeedThreadStore(postId: postId, api: dependencies.api)
            self.store = store
            await store.load()

            let page = await dependencies.api.load(
                Endpoint(.get, "api/feed", query: [URLQueryItem(name: "mode", value: "all")]),
                as: FeedPageRecord.self)
            canAct = page.value?.canPost ?? false
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
