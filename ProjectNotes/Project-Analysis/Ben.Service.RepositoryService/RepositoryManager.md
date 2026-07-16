# Ben.Service.RepositoryService — Repository Manager Implementations

---

## `RepositoryManager`

**Namespace:** `Ben.Service.RepositoryService`  
**File:** [`Ben.Service.RepositoryService/RepositoryManager.cs`](../../../Ben.Service.RepositoryService/RepositoryManager.cs)  
**Implements:** [`IRepositoryManager`](Interfaces-Generic.md#irepositorymanager)  
**Registered as:** `Scoped`

### Summary
The concrete unit-of-work implementation.  
Lazily initialises `AppUserRepositoryManager` and `OrganizationRepositoryManager` on first access using the null-coalescing assignment pattern (`??=`).  
Shares a single `IDbContextFactory<BenDataContext>` that is passed down to every sub-manager and repository.

### Properties

| Property | Interface | Lazy? |
|---|---|---|
| `Organization` | `IOrganizationRepositoryManager` | Yes — creates `OrganizationRepositoryManager` on first access |
| `AppUser` | `IAppUserRepositoryManager` | Yes — creates `AppUserRepositoryManager` on first access |

### Methods

| Method | Notes |
|---|---|
| `SaveChangesAsync(CancellationToken)` | Currently throws `NotImplementedException`. Repositories use their own scoped contexts via `IDbContextFactory`. |

---

## `AppUserRepositoryManager`

**Namespace:** `Ben.Service.RepositoryService`  
**File:** [`Ben.Service.RepositoryService/AppUserRepositoryManager.cs`](../../../Ben.Service.RepositoryService/AppUserRepositoryManager.cs)  
**Implements:** [`IAppUserRepositoryManager`](Interfaces-Generic.md#iappuserrepositorymanager)

### Summary
Aggregates all AppUser-domain repository instances.  
Each property is lazily initialised on first access — only the repositories that are actually needed in a given request are constructed.

### Repository Properties

| Property | Repository | Entity |
|---|---|---|
| `AppUserRepository` | `AppUserRepository` | `AppUser` |
| `AddressRepository` | `UserAddressRepository` | `UserAddress` |
| `AddressTypeRepository` | `UserAddressTypeRepository` | `UserAddressType` |
| `EmailRepository` | `UserEmailRepository` | `UserEmail` |
| `EmailTypeRepository` | `UserEmailTypeRepository` | `UserEmailType` |
| `LinkRepository` | `UserLinkRepository` | `UserLink` |
| `LinkTypeRepository` | `UserLinkTypeRepository` | `UserLinkType` |
| `MessageRepository` | `UserMessageRepository` | `UserMessage` |
| `MessageToRepository` | `UserMessageToRepository` | `UserMessageTo` |
| `MessageTypeRepository` | `UserMessageTypeRepository` | `UserMessageType` |
| `NoteRepository` | `UserNoteRepository` | `UserNote` |
| `NoteTypeRepository` | `UserNoteTypeRepository` | `UserNoteType` |
| `PhoneRepository` | `UserPhoneRepository` | `UserPhone` |
| `PhoneTypeRepository` | `UserPhoneTypeRepository` | `UserPhoneType` |

---

## `OrganizationRepositoryManager`

**Namespace:** `Ben.Service.RepositoryService`  
**File:** [`Ben.Service.RepositoryService/OrganizationRepositoryManager.cs`](../../../Ben.Service.RepositoryService/OrganizationRepositoryManager.cs)  
**Implements:** [`IOrganizationRepositoryManager`](Interfaces-Generic.md#iorganizationrepositorymanager)

### Summary
Aggregates all Organisation-domain repository instances.  
Same lazy-initialisation pattern as `AppUserRepositoryManager`.

### Repository Properties

| Property | Repository | Entity |
|---|---|---|
| `OrganizationRepository` | `OrganizationRepository` | `Organization` |
| `AddressRepository` | `OrganizationAddressRepository` | `OrganizationAddress` |
| `AddressTypeRepository` | `OrganizationAddressTypeRepository` | `OrganizationAddressType` |
| `EmailRepository` | `OrganizationEmailRepository` | `OrganizationEmail` |
| `EmailTypeRepository` | `OrganizationEmailTypeRepository` | `OrganizationEmailType` |
| `LinkRepository` | `OrganizationLinkRepository` | `OrganizationLink` |
| `LinkTypeRepository` | `OrganizationLinkTypeRepository` | `OrganizationLinkType` |
| `NoteRepository` | `OrganizationNoteRepository` | `OrganizationNote` |
| `NoteTypeRepository` | `OrganizationNoteTypeRepository` | `OrganizationNoteType` |
| `PageRepository` | `OrganizationPageRepository` | `OrganizationPage` |
| `PhoneRepository` | `OrganizationPhoneRepository` | `OrganizationPhone` |
| `PhoneTypeRepository` | `OrganizationPhoneTypeRepository` | `OrganizationPhoneType` |

---
