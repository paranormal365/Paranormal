# Ben.Data.Common — Services

---

## `CryptoFileService`

**Namespace:** `Ben.Data.Common.Services`  
**File:** [`Ben.Data.Common/Services/CryptoFileService.cs`](../../../Ben.Data.Common/Services/CryptoFileService.cs)  
**Type:** Class (not static — has state)

### Summary

Encrypts and decrypts files using RSA and AES cryptography.  
Configured via properties before calling the operation method.  
The `Mode` property (see [`CryptoModes`](Enums.md#cryptomodes)) determines whether the next operation encrypts or decrypts.

> **Note:** The salt in this class is currently hardcoded as a placeholder (`"BenKellyAveryPeytonBuddyBella"`). This is marked with a TODO comment and should be replaced with a proper secret before production use.

### Properties

| Property | Type | Description |
|---|---|---|
| `SourceFileName` | `string` | Path to the input file. |
| `DestinationFileName` | `string` | Path where the output file will be written. |
| `Mode` | [`CryptoModes`](Enums.md#cryptomodes) | `Encrypt` or `Decrypt`. Defaults to `Encrypt`. |
| `Key` | `byte[]?` | The generated/derived symmetric key (set internally). |
| `Password` | `string` | Password used to derive the AES key via PBKDF2. |

### Key Methods

| Method | Description |
|---|---|
| `RSADecrypt(byte[], RSAParameters, bool)` | *(static)* Decrypts data using an RSA private key. |
| `MachineKeyStorage()` | *(Windows only, `[SupportedOSPlatform("windows")]`)* Stores/retrieves the derived key using the Windows Data Protection API. |

---

## `JsonConvertService`

**Namespace:** `Ben.Data.Common.Services`  
**File:** [`Ben.Data.Common/Services/JsonConvertService.cs`](../../../Ben.Data.Common/Services/JsonConvertService.cs)  
**Type:** Static class

### Summary

Thin wrapper around `System.Text.Json` providing consistent serialisation settings across the application.  
Centralising JSON options here ensures all components use the same casing, null handling, and depth settings.

### Key Responsibilities

- Provides a shared `JsonSerializerOptions` instance.
- Serialises objects to JSON strings.
- Deserialises JSON strings to typed objects.
