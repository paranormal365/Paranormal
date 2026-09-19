import SwiftUI
import AVKit
import BenKit

/// One post. Mirrors the website's card anatomy: author, body (linkified), media, the
/// marks (category chip, attribution, badges), the author-only nudge, then the actions.
/// Every control is gated on `canAct` — the server's own CanPost — so the card never
/// offers a button the API would refuse.
struct FeedCardView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(Router.self) private var router

    let post: FeedPostRecord

    /// Whether THIS reader may act — the server's own CanPost, passed down rather than
    /// guessed, so a control never appears that the API would refuse.
    var canAct: Bool = false
    var onLike: (() -> Void)?
    var onReply: (() -> Void)?
    var onFollow: (() -> Void)?
    var onReport: (() -> Void)?
    var onBlock: (() -> Void)?
    var onRecategorize: (() -> Void)?

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            header
            bodyText
            media
            marks
            nudge
            counts
        }
        .padding(14)
        .background(Theme.mist, in: RoundedRectangle(cornerRadius: 14))
        // One VoiceOver element per card, summarized; the thread opens on activate.
        .accessibilityElement(children: .combine)
        .accessibilityLabel(accessibilitySummary)
        .accessibilityAddTraits(.isButton)
        .contentShape(Rectangle())
        .onTapGesture { router.push(.feedPost(post.id), in: .feed) }
    }

    private var header: some View {
        HStack(spacing: 10) {
            InitialsAvatar(displayName: post.authorDisplayName, size: 38)
            VStack(alignment: .leading, spacing: 1) {
                Text(post.authorDisplayName)
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(Theme.bone)
                Text(post.dateCreated, format: .relative(presentation: .named))
                    .font(.caption)
                    .foregroundStyle(Theme.fog)
            }
            Spacer()
        }
        .onTapGesture { router.push(.feedProfile(post.authorAppUserId), in: .feed) }
    }

    private var bodyText: some View {
        Text(FeedText.attributed(body: post.body, mentions: post.mentions))
            .font(.body)
            .foregroundStyle(Theme.bone)
            .tint(Theme.ecto)
    }

    @ViewBuilder
    private var media: some View {
        if post.hasMedia, let url = mediaURL {
            switch post.mediaKind {
            case .video:
                // The ONE anonymous, Range-enabled route — safe to stream directly.
                VideoPlayer(player: AVPlayer(url: url))
                    .frame(height: 220)
                    .clipShape(RoundedRectangle(cornerRadius: 10))
            default:
                AsyncImage(url: url) { phase in
                    switch phase {
                    case .success(let image):
                        image.resizable().scaledToFit()
                    case .failure:
                        // Never a broken-image glyph: the post reads as text.
                        EmptyView()
                    default:
                        RoundedRectangle(cornerRadius: 10)
                            .fill(Theme.ink)
                            .frame(height: 160)
                            .overlay(ProgressView())
                    }
                }
                .frame(maxHeight: 340)
                .clipShape(RoundedRectangle(cornerRadius: 10))
            }
        } else if post.mediaAwaitingReview && post.isOwnPost {
            // Author-only, and said as a wait, not a failure — same words as the website.
            Label("Your photo is being checked before it appears.", systemImage: "clock")
                .font(.caption)
                .foregroundStyle(Theme.fog)
        }
    }

    @ViewBuilder
    private var marks: some View {
        if post.experienceTypeName != nil || post.attributedOrgName != nil
            || post.groupVerified || post.moderatorReviewed {
            HStack(spacing: 8) {
                if let typeName = post.experienceTypeName, let typeId = post.experienceTypeId {
                    Button {
                        router.push(.feedFiltered(.experienceType(typeId, name: typeName)), in: .feed)
                    } label: {
                        Chip(text: typeName, tint: Theme.haunt)
                    }
                    .buttonStyle(.plain)
                }
                if let orgName = post.attributedOrgName {
                    // The claiming group (F7) — named only because it chose to be.
                    Text(orgName)
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(Theme.ecto)
                }
                if post.groupVerified {
                    Chip(text: "Group verified", tint: Theme.success)
                }
                if post.moderatorReviewed {
                    Chip(text: "Moderator reviewed", tint: Theme.fog)
                }
                Spacer()
            }
        }
    }

    /// AUTHOR-ONLY (item 186 F6), phrased as help rather than accusation: an honest
    /// mislabel is the common case, and nothing about the post is blocked either way.
    @ViewBuilder
    private var nudge: some View {
        if post.categoryMatchDegraded && post.isOwnPost && canAct {
            Button {
                onRecategorize?()
            } label: {
                HStack(spacing: 6) {
                    Image(systemName: "tag")
                    Text("This doesn't look like \(post.experienceTypeName ?? "that") to us — change it?")
                        .multilineTextAlignment(.leading)
                    Spacer()
                }
                .font(.caption)
                .foregroundStyle(Theme.warning)
                .padding(8)
                .background(Theme.warning.opacity(0.12), in: RoundedRectangle(cornerRadius: 8))
            }
            .buttonStyle(.plain)
        }
    }

    /// The actions row. A visitor sees the COUNTS — the social proof is the invitation —
    /// but no dead controls: every button here is one the server would honour.
    private var counts: some View {
        HStack(spacing: 18) {
            if canAct {
                Button {
                    onLike?()
                } label: {
                    Label("\(post.likeCount)", systemImage: post.likedByCurrentUser ? "heart.fill" : "heart")
                        .foregroundStyle(post.likedByCurrentUser ? Theme.danger : Theme.fog)
                }
                .buttonStyle(.plain)
                .accessibilityLabel(post.likedByCurrentUser ? "Unlike" : "Like")

                Button { onReply?() } label: {
                    Label("\(post.replyCount)", systemImage: "bubble.right")
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Reply")

                Spacer()

                if post.isOwnPost {
                    // Nothing to offer an author about their own post here — following and
                    // reporting yourself are both nonsense, and the nudge lives above.
                    EmptyView()
                } else {
                    Menu {
                        Button {
                            onFollow?()
                        } label: {
                            Label(post.authorIsFollowedByCurrentUser ? "Unfollow" : "Follow",
                                  systemImage: post.authorIsFollowedByCurrentUser ? "person.badge.minus" : "person.badge.plus")
                        }
                        if post.reportedByCurrentUser {
                            Label("Reported", systemImage: "checkmark")
                        } else {
                            Button(role: .destructive) {
                                onReport?()
                            } label: {
                                Label("Report", systemImage: "flag")
                            }
                        }
                        // Report asks a moderator to act eventually; Block acts now, for this
                        // reader (App Review 1.2 wants both on the content itself).
                        if onBlock != nil {
                            Button(role: .destructive) {
                                onBlock?()
                            } label: {
                                Label("Block \(post.authorDisplayName)", systemImage: "hand.raised")
                            }
                        }
                    } label: {
                        Image(systemName: "ellipsis")
                            .foregroundStyle(Theme.fog)
                            .frame(width: 44, height: 30, alignment: .trailing)
                    }
                    .accessibilityLabel("More actions")
                }
            } else {
                Label("\(post.likeCount)", systemImage: "heart")
                Label("\(post.replyCount)", systemImage: "bubble.right")
                Spacer()
            }
        }
        .font(.caption)
        .foregroundStyle(Theme.fog)
    }

    private var mediaURL: URL? {
        dependencies.environment.url(for: Endpoint(
            .get, "api/feed/posts/\(post.id.uuidString.lowercased())/media", requiresAuth: false))
    }

    private var accessibilitySummary: String {
        var parts = [post.authorDisplayName, post.body]
        if let type = post.experienceTypeName { parts.append("Categorized \(type)") }
        if post.groupVerified, let org = post.attributedOrgName { parts.append("Verified by \(org)") }
        if post.hasMedia { parts.append(post.mediaKind == .video ? "Has video" : "Has photo") }
        parts.append("\(post.likeCount) likes, \(post.replyCount) replies")
        return parts.joined(separator: ". ")
    }
}

/// Initials in a circle — identifies the person without pretending to be a
/// photo that failed to load. The server's viewer-aware avatar route joins in
/// a later slice.
struct InitialsAvatar: View {
    let displayName: String
    let size: CGFloat

    var body: some View {
        Circle()
            .fill(Theme.haunt.opacity(0.35))
            .frame(width: size, height: size)
            .overlay(
                Text(initials)
                    .font(.system(size: size * 0.4, weight: .semibold))
                    .foregroundStyle(Theme.bone))
    }

    private var initials: String {
        let parts = displayName.split(whereSeparator: { " .@-_".contains($0) })
        switch parts.count {
        case 0: return "?"
        case 1: return String(parts[0].prefix(1)).uppercased()
        default: return (parts[0].prefix(1) + parts[1].prefix(1)).uppercased()
        }
    }
}

struct Chip: View {
    let text: String
    let tint: Color

    var body: some View {
        Text(text)
            .font(.caption2.weight(.medium))
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .background(tint.opacity(0.18), in: Capsule())
            .foregroundStyle(tint)
    }
}
