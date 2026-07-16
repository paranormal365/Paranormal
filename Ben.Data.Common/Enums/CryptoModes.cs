namespace Ben.Data.Common.Enums;

/// <summary>
/// Specifies the direction of a cryptographic operation performed by
/// <see cref="Ben.Data.Common.Services.CryptoFileService"/>.
/// </summary>
public enum CryptoModes
{
    /// <summary>Encrypt plaintext to produce ciphertext.</summary>
    Encrypt,

    /// <summary>Decrypt ciphertext to recover plaintext.</summary>
    Decrypt
}
