namespace Ben.Data.WebApi.Services.Push;

/// <summary>What Apple issued for sending pushes (item 252): the APNs key, and whose app it is for.</summary>
/// <param name="TeamId">The developer team, the provider token's issuer.</param>
/// <param name="KeyId">The Key ID shown beside the downloaded <c>.p8</c>; the token's <c>kid</c>.</param>
/// <param name="PrivateKeyPem">The <c>.p8</c> contents. A secret: read from a file outside the repository.</param>
/// <param name="BundleId">The app pushes are addressed to — <c>apns-topic</c>.</param>
public sealed record ApnsOptions(string TeamId, string KeyId, string PrivateKeyPem, string BundleId)
{
    public static readonly ApnsOptions Unconfigured = new(string.Empty, string.Empty, string.Empty, string.Empty);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TeamId) && !string.IsNullOrWhiteSpace(KeyId)
        && !string.IsNullOrWhiteSpace(PrivateKeyPem) && !string.IsNullOrWhiteSpace(BundleId);
}
