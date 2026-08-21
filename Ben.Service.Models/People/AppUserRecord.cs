namespace Ben.Service.Models.People;

public record AppUserRecord
{
    public Guid Id { get; init; }
    public string? UserName { get; init; }
    public string? DisplayName { get; init; }

    /// <summary>Legal first name. Required of new accounts; null on ones created before it existed.</summary>
    public string? FirstName { get; init; }

    /// <summary>Legal last name. Required of new accounts; null on ones created before it existed.</summary>
    public string? LastName { get; init; }

    /// <summary>
    /// First and last together, or null when neither is set.
    /// </summary>
    /// <remarks>
    /// Computed here rather than at each call site so a half-filled name — a first name and no
    /// last — renders as the part that exists instead of "Margaret " with a trailing space.
    /// </remarks>
    public string? FullName =>
        string.Join(" ", new[] { FirstName, LastName }.Where(n => !string.IsNullOrWhiteSpace(n)))
              is { Length: > 0 } joined ? joined : null;

    /// <summary>Optional, and never required.</summary>
    public Ben.Data.Common.Enums.ClientGender? Gender { get; init; }

    /// <summary>Year of birth. Optional; never what an age gate depends on. Nothing collects it yet.</summary>
    public int? BirthYear { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public string? Email { get; init; }
    public bool IsEmailConfirmed { get; init; }
    public string? PhoneNumber { get; init; }
    public bool IsPhoneNumberConfirmed { get; init; }
    public bool IsTwoFactorEnabled { get; init; }
}
