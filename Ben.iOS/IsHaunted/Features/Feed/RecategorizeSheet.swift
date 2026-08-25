import SwiftUI
import BenKit

/// The nudge's one-click answer (item 186 F6): the author changes what their post claims
/// to show. Never a punishment — the post is already up and stays up whatever is chosen,
/// including "no category at all".
struct RecategorizeSheet: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.dismiss) private var dismiss

    let post: FeedPostRecord
    var onChanged: (FeedPostRecord) -> Void

    @State private var selected: UUID?
    @State private var taxonomy: [ExperienceCategoryWithTypes] = []
    @State private var isSaving = false
    @State private var errorMessage: String?

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Picker("Category", selection: $selected) {
                        Text("No category").tag(UUID?.none)
                        ForEach(taxonomy) { group in
                            Section(group.category.name) {
                                ForEach(group.selectableTypes) { type in
                                    Text(type.name).tag(UUID?.some(type.id))
                                }
                            }
                        }
                    }
                    .pickerStyle(.inline)
                    .labelsHidden()
                } header: {
                    Text("What does this show?")
                } footer: {
                    Text("Only you saw the suggestion to change this. Your post stays up "
                         + "either way — the label just helps people find it.")
                }

                if let errorMessage {
                    Section {
                        Label(errorMessage, systemImage: "exclamationmark.triangle")
                            .foregroundStyle(Theme.danger)
                            .font(.callout)
                    }
                }
            }
            .navigationTitle("Change category")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }.disabled(isSaving)
                }
                ToolbarItem(placement: .confirmationAction) {
                    if isSaving {
                        ProgressView()
                    } else {
                        Button("Save") { Task { await save() } }
                            .disabled(selected == post.experienceTypeId)
                    }
                }
            }
            .task {
                selected = post.experienceTypeId
                taxonomy = await dependencies.feedActions.experienceTaxonomy()
            }
        }
    }

    private func save() async {
        isSaving = true
        errorMessage = nil
        defer { isSaving = false }

        switch await dependencies.feedActions.recategorize(postId: post.id, experienceTypeId: selected) {
        case .success(let updated):
            onChanged(updated)
            dismiss()
        case .failure(let error):
            errorMessage = error.message
        }
    }
}
