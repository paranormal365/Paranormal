namespace Ben.Service.RepositoryService.Services;

/// <summary>
/// A group rule refused the change, in a sentence the person can be shown (a taken web address, the last owner).
/// </summary>
/// <remarks>
/// An <see cref="InvalidOperationException"/> still, so every existing catch keeps working. Its own type is
/// what lets the API answer 400 with the sentence: as a plain InvalidOperationException it reached the
/// server's error handler and every refusal answered 500 (site audit, 10/09/2026).
/// </remarks>
public sealed class OrganizationRuleException(string message) : InvalidOperationException(message);

/// <summary>A group rule says this person may not make the change. Answered 403 with the sentence.</summary>
public sealed class OrganizationRuleDeniedException(string message) : UnauthorizedAccessException(message);
