import SwiftUI
import AVKit
import BenKit

/// One post. Mirrors the website's card anatomy: author, body (linkified),
/// media, then the marks — category chip, attribution, badges — then counts.
/// Slice 3 is read-only: counts display, actions arrive in Slice 4.
struct FeedCardView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(Router.self) private var router

    let post: FeedPostRecord

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            header
            bodyText
            media
            marks
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

    private var counts: some View {
        HStack(spacing: 16) {
            Label("\(post.likeCount)", systemImage: post.likedByCurrentUser ? "heart.fill" : "heart")
            Label("\(post.replyCount)", systemImage: "bubble.right")
            Spacer()
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
