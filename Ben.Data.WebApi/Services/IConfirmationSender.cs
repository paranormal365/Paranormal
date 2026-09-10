using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Sends an account its confirmation link. What <see cref="AccountCreationService"/> does for a
/// website sign-up, offered to any other path that creates an account whose address is not yet
/// proved.
/// </summary>
/// <remarks>
/// An interface rather than the class itself because the other caller is
/// <see cref="ExternalSignInService"/>, whose decisions are tested from mocks of the two Identity
/// managers alone; dragging the mailer, the site identity and configuration into that harness
/// would test the plumbing rather than the rule. The contract is the one method those paths need,
/// with the same promise: true means the message left this machine.
/// </remarks>
public interface IConfirmationSender
{
    /// <summary>
    /// Builds and sends the confirmation link for <paramref name="user"/>, stamping
    /// <c>DateConfirmationSent</c> on a real send. Never throws: a failure is logged and reported
    /// as false, because by the time this runs the account already exists.
    /// </summary>
    Task<bool> SendConfirmationAsync(AppUser user, string? returnUrl, CancellationToken ct);
}
