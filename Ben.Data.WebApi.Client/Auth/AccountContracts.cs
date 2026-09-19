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
/// <param name="ShouldLinkInstead">
/// The address already has an account here. Offer the link door, not the create form: a second
/// account would hold none of their history, and the server will refuse it anyway.
/// </param>
/// <param name="EmailProblem">
/// What is wrong with the ADDRESS. Kept apart from <paramref name="HandleProblem"/> because they
/// belong under different fields, and showing one under the other tells somebody to fix the wrong
/// thing — which is precisely what the server used to do.
/// </param>
public sealed record AppleNeedsProfileResponse(
    [property: JsonPropertyName("needsProfile")] bool NeedsProfile,
    [property: JsonPropertyName("suggestedDisplayName")] string? SuggestedDisplayName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("isPrivateEmail")] bool IsPrivateEmail,
    [property: JsonPropertyName("handleProblem")] string? HandleProblem,
    [property: JsonPropertyName("shouldLinkInstead")] bool ShouldLinkInstead = false,
    [property: JsonPropertyName("emailProblem")] string? EmailProblem = null);

/// <summary>A Sign in with Apple attempt, in the shape <c>POST api/auth/apple</c> expects.</summary>
/// <param name="AuthorizationCode">
/// Apple's one-shot authorization code from the same sign-in, if the client kept it. The server
/// exchanges it for the refresh token that lets the person's Apple tokens be revoked when they
/// delete their account (item 229). Omitted when null; a client that sends none leaves nothing to
/// revoke, which is the state everything was in before.
/// </param>
public sealed record AppleSignInRequest(
    [property: JsonPropertyName("identityToken")] string IdentityToken,
    [property: JsonPropertyName("displayName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DisplayName = null,
    [property: JsonPropertyName("handle"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Handle = null,
    [property: JsonPropertyName("authorizationCode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? AuthorizationCode = null);

/// <summary>Claiming an account that already exists here for a verified Apple identity.</summary>
/// <param name="Email">
/// The account being claimed. Deliberately not tied to whatever address Apple supplied: a Hide My
/// Email relay address matches nothing here, and an Apple ID is often simply a different address
/// from the one somebody signed up with.
/// </param>
/// <remarks>
/// The two-factor fields are omitted from the JSON when null, exactly as the sign-in request's are,
/// and for the same reason: Identity treats an empty string as an attempt with a wrong code rather
/// than as no attempt at all, which spends a failure against an account that may not even have a
/// second factor.
/// </remarks>
public sealed record AppleLinkRequest(
    [property: JsonPropertyName("identityToken")] string IdentityToken,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("twoFactorCode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TwoFactorCode = null,
    [property: JsonPropertyName("twoFactorRecoveryCode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TwoFactorRecoveryCode = null,
    [property: JsonPropertyName("authorizationCode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? AuthorizationCode = null);
