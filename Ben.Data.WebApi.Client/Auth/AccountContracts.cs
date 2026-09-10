using System.Text.Json.Serialization;

namespace Ben.Data.WebApi.Client.Auth;

/// <summary>A sign-up. The handle is normalised and checked again on the server whatever the client did.</summary>
public sealed record RegisterRequest(
    string Email, string Password, string DisplayName, string Handle,
    string? FirstName = null, string? LastName = null);

/// <summary>
/// The answer to a sign-up.
/// </summary>
/// <param name="Field">
/// Names the input to point at, or null for a general message. Worth honouring: "that name is
/// taken" shown under the password box helps nobody.
/// </param>
/// <remarks>
/// This endpoint answers a REFUSAL as JSON, not as prose, so the ordinary refusal handling would
/// discard the sentence and substitute a paraphrase of the status. Read the body either way.
/// </remarks>
public sealed record RegisterResponse(bool Succeeded, string Message, string? Field);

/// <param name="Reason">Why not, when it is not available. Shown as-is.</param>
public sealed record HandleAvailabilityResponse(string Handle, bool Available, string? Reason);

public sealed record ResendConfirmationRequest(string? Email);

/// <summary>
/// One field, always the same sentence — deliberately. Whether the address has an account is not
/// something this endpoint reveals.
/// </summary>
public sealed record ResendConfirmationResponse(string Message);

public sealed record ConfirmEmailRequest(Guid UserId, string Code);

/// <param name="Handle">The @name the account carries. Permanent, and often the first the person hears of it.</param>
/// <param name="Waiting">Where a request made before they could sign in stands, or null.</param>
public sealed record ConfirmEmailResponse(bool Succeeded, string Message, string? Handle = null, string? Waiting = null);

/// <summary>
/// What <c>POST api/auth/apple</c> answers with a 409: Apple authenticated the person, but the
/// account still needs a display name and a handle before it can exist.
/// </summary>
/// <param name="Email">
/// May be one of Apple's private relay addresses. When <paramref name="IsPrivateEmail"/> is true,
/// do not show it as though it were the person's own address.
/// </param>
public sealed record AppleNeedsProfileResponse(
    [property: JsonPropertyName("needsProfile")] bool NeedsProfile,
    [property: JsonPropertyName("suggestedDisplayName")] string? SuggestedDisplayName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("isPrivateEmail")] bool IsPrivateEmail,
    [property: JsonPropertyName("handleProblem")] string? HandleProblem);

/// <summary>A Sign in with Apple attempt, in the shape <c>POST api/auth/apple</c> expects.</summary>
public sealed record AppleSignInRequest(
    [property: JsonPropertyName("identityToken")] string IdentityToken,
    [property: JsonPropertyName("displayName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DisplayName = null,
    [property: JsonPropertyName("handle"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Handle = null);
