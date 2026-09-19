import Foundation

// The platform-wide experience taxonomy (ExperienceCategory → ExperienceType), the same
// vocabulary cases and evidence use. Feed posts categorize against it so every judgment
// accumulates against the taxonomy of record rather than a feed-only list.

public struct ExperienceCategoryRecord: Sendable, Codable, Equatable, Identifiable, Hashable {
    public var id: UUID
    public var name: String
    public var description: String?
    public var sortOrder: Int
    public var isActive: Bool
    public var isApproved: Bool
}

public struct ExperienceTypeRecord: Sendable, Codable, Equatable, Identifiable, Hashable {
    public var id: UUID
    public var experienceCategoryId: UUID
    public var name: String
    public var description: String?
    public var sortOrder: Int
    public var isActive: Bool
    public var isApproved: Bool
}

/// One category with its types, as `GET api/experience-categories/with-types` returns it.
public struct ExperienceCategoryWithTypes: Sendable, Codable, Equatable, Identifiable {
    public var category: ExperienceCategoryRecord
    public var types: [ExperienceTypeRecord]

    public var id: UUID { category.id }

    /// Only what a person may actually choose. The server refuses a retired or unapproved
    /// type with a sentence; filtering here means the picker never offers one — the refusal
    /// stays a backstop rather than something a user has to discover.
    public var selectableTypes: [ExperienceTypeRecord] {
        types.filter { $0.isActive && $0.isApproved }.sorted { $0.sortOrder < $1.sortOrder }
    }
}

extension Array where Element == ExperienceCategoryWithTypes {
    /// Categories worth showing: active, approved, and with at least one choosable type.
    public var selectable: [ExperienceCategoryWithTypes] {
        filter { $0.category.isActive && $0.category.isApproved && !$0.selectableTypes.isEmpty }
            .sorted { $0.category.sortOrder < $1.category.sortOrder }
    }

    /// Finds a type by id across every category — for resolving a chip's name.
    public func type(_ id: UUID) -> ExperienceTypeRecord? {
        for group in self {
            if let match = group.types.first(where: { $0.id == id }) { return match }
        }
        return nil
    }
}
