using Ben.Data.Common.Enums;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Ben.Data.Common.Services;

public class CryptoFileService
{
    private readonly string _filePath = Directory.GetCurrentDirectory();

    public string SourceFileName { get; set; } = string.Empty;
    public string DestinationFileName { get; set; } = string.Empty;
    public CryptoModes Mode { get; set; } = CryptoModes.Encrypt;
    public byte[]? Key { get; private set; }
    public string Password { get; set; } = string.Empty;
    private byte[] Salt
    {
        get
        {
            //TODO: Change where this comes from and make it more secure
            return Convert.FromBase64String("BenKellyAveryPeytonBuddyBella");
        }
    }
    private byte[]? RSAEncrypt(byte[] dataToEncrypt, RSAParameters rsaKeyInfo, bool doOAEPPadding)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportParameters(rsaKeyInfo);
            var padding = doOAEPPadding ? RSAEncryptionPadding.OaepSHA1 : RSAEncryptionPadding.Pkcs1;
            return rsa.Encrypt(dataToEncrypt, padding);
        }
        catch (CryptographicException e)
        {
            Console.WriteLine(e.Message);
            return Array.Empty<byte>();
        }
    }

    public static byte[] RSADecrypt(byte[] dataToDecrypt, RSAParameters rsaKeyInfo, bool doOAEPPadding)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportParameters(rsaKeyInfo);
            var padding = doOAEPPadding ? RSAEncryptionPadding.OaepSHA1 : RSAEncryptionPadding.Pkcs1;
            return rsa.Decrypt(dataToDecrypt, padding);
        }
        catch (CryptographicException e)
        {
            Console.WriteLine(e.ToString());
            return Array.Empty<byte>();
        }
    }


    [SupportedOSPlatform("windows")]
    public void MachineKeyStorage()
    {
#if WINDOWS
        // Set the static UseMachineKeyStore property to use the machine key
        // store instead of the user profile key store. All CSP instances not
        // initialized with CspParameters will use this setting.
        RSACryptoServiceProvider.UseMachineKeyStore = true;
        try
        {
            // This CSP instance will use the Machine Store as set above and is
            // initialized with no parameters.
            using (RSACryptoServiceProvider RSAalg = new RSACryptoServiceProvider())
            {
                ShowContainerInfo(RSAalg.CspKeyContainerInfo);
                RSAalg.PersistKeyInCsp = false;
            }

            var cspParams = new CspParameters
            {
                KeyContainerName = "MyKeyContainer"
            };

            // This CSP instance will use the User Store since cspParams are used.
            using (RSACryptoServiceProvider RSAalg = new RSACryptoServiceProvider(cspParams))
            {
                ShowContainerInfo(RSAalg.CspKeyContainerInfo);
                RSAalg.PersistKeyInCsp = false;
            }

            cspParams.Flags |= CspProviderFlags.UseMachineKeyStore;

            // This CSP instance will use the Machine Store. Although cspParams are used,
            // the cspParams.Flags is set to CspProviderFlags.UseMachineKeyStore.
            using (RSACryptoServiceProvider RSAalg = new RSACryptoServiceProvider(cspParams))
            {
                ShowContainerInfo(RSAalg.CspKeyContainerInfo);
                RSAalg.PersistKeyInCsp = false;
            }
        }
        catch (CryptographicException e)
        {
            Console.WriteLine("Exception: {0}", e.GetType().FullName);
            Console.WriteLine(e.Message);
        }
#else
        throw new PlatformNotSupportedException("CspParameters is only supported on Windows.");
#endif
    }

    [SupportedOSPlatform("windows")]
    public static void ShowContainerInfo(CspKeyContainerInfo containerInfo)
    {
        string keyStore;

        Console.WriteLine();
#if WINDOWS
        if (containerInfo.MachineKeyStore)
        {
            keyStore = "Machine Store";
        }
        else
        {
            keyStore = "User Store";
        }
#else
        keyStore = "User Store";
#endif
        Console.WriteLine("Key Store:     {0}", keyStore);
        Console.WriteLine("Key Provider:  {0}", containerInfo.ProviderName);
        Console.WriteLine("Key Container: \"{0}\"", containerInfo.KeyContainerName);
        Console.WriteLine("Generated:     {0}", containerInfo.RandomlyGenerated);
        Console.WriteLine("Key Nubmer:    {0}", containerInfo.KeyNumber);
        Console.WriteLine("Removable Key: {0}", containerInfo.Removable);

    }
}
