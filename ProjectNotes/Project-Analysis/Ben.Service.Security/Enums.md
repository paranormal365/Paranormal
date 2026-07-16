# Ben.Service.Security — Enums

---

## `OrganizationSecurityTable`

**Namespace:** `Ben.Service.Security.Enums`  
**File:** [`Ben.Service.Security/Enums/OrganizationSecurityTable.cs`](../../../Ben.Service.Security/Enums/OrganizationSecurityTable.cs)

### Summary
Identifies an organisation-related domain table for which per-user access grants can be configured.  
Used as the `table` parameter in `IOrganizationSecurityService` methods and as the constructor argument for `OrganizationSecurityAuthorizeAttribute`.

> **Note:** A parallel enum `Ben.Data.Common.Enums.OrganizationSecurityTable` exists with **different integer values** and is used at the entity/database layer. A cast is applied inside `OrganizationSecurityService` when querying the DB. Do not mix the two without the explicit cast.

### Values

| Value | Int | Domain Area |
|---|---|---|
| `None` | 0 | Sentinel — not used in grants. |
| `Organization` | 1 | Core org record. |
| `OrganizationAddress` | 2 | Org mailing addresses. |
| `OrganizationEmail` | 3 | Org email addresses. |
| `OrganizationPhone` | 4 | Org phone numbers. |
| `OrganizationLink` | 5 | Org links / URLs. |
| `OrganizationNote` | 6 | Org notes. |
| `OrganizationPage` | 7 | Org pages / content. |
| `OrganizationAddressType` | 8 | Lookup type for addresses. |
| `OrganizationEmailType` | 9 | Lookup type for emails. |
| `OrganizationPhoneType` | 10 | Lookup type for phones. |
| `OrganizationLinkType` | 11 | Lookup type for links. |
| `OrganizationNoteType` | 12 | Lookup type for notes. |
| `User` | 13 | AppUser account. |
| `UserAddress` | 14 | User mailing addresses. |
| `UserEmail` | 15 | User email addresses. |
| `UserPhone` | 16 | User phone numbers. |
| `UserLink` | 17 | User links / URLs. |
| `UserNote` | 18 | User notes. |
| `UserMessage` | 19 | User messages. |
| `UserAddressType` | 20 | Lookup type for user addresses. |
| `UserEmailType` | 21 | Lookup type for user emails. |
| `UserPhoneType` | 22 | Lookup type for user phones. |
| `UserLinkType` | 23 | Lookup type for user links. |
| `UserNoteType` | 24 | Lookup type for user notes. |
| `UserMessageType` | 25 | Lookup type for user messages. |

---

## `OrganizationSecurityAction`

`OrganizationSecurityAction` was originally defined here as a `[Flags]` enum.  
It has been **consolidated** into `Ben.Data.Common.Enums` and is now imported via a `global using` alias in `GlobalUsings.cs`.  

See [`Ben.Data.Common/Enums.md#organizationsecurityaction`](../Ben.Data.Common/Enums.md#organizationsecurityaction) for full documentation.

```csharp
// GlobalUsings.cs in Ben.Service.Security
global using OrganizationSecurityAction = Ben.Data.Common.Enums.OrganizationSecurityAction;
```

---
