# Ben.Service.RepositoryService — Entity Repository Interfaces

All interfaces are in the `Ben.Service.RepositoryService.EntityInterfaces` namespace.  
Each interface extends [`IRepositoryBase<T>`](Interfaces-Generic.md#irepositoryt) and adds no extra methods — they exist to provide a distinct named type for DI registration and `IRepositoryManager` / `IAppUserRepositoryManager` / `IOrganizationRepositoryManager` property access.

---

## AppUser Repositories

| Interface | Entity | Accessed via |
|---|---|---|
| `IAppUserRepository` | `AppUser` | `IAppUserRepositoryManager.AppUserRepository` |
| `IUserAddressRepository` | `UserAddress` | `IAppUserRepositoryManager.AddressRepository` |
| `IUserAddressTypeRepository` | `UserAddressType` | `IAppUserRepositoryManager.AddressTypeRepository` |
| `IUserEmailRepository` | `UserEmail` | `IAppUserRepositoryManager.EmailRepository` |
| `IUserEmailTypeRepository` | `UserEmailType` | `IAppUserRepositoryManager.EmailTypeRepository` |
| `IUserLinkRepository` | `UserLink` | `IAppUserRepositoryManager.LinkRepository` |
| `IUserLinkTypeRepository` | `UserLinkType` | `IAppUserRepositoryManager.LinkTypeRepository` |
| `IUserMessageRepository` | `UserMessage` | `IAppUserRepositoryManager.MessageRepository` |
| `IUserMessageToRepository` | `UserMessageTo` | `IAppUserRepositoryManager.MessageToRepository` |
| `IUserMessageTypeRepository` | `UserMessageType` | `IAppUserRepositoryManager.MessageTypeRepository` |
| `IUserNoteRepository` | `UserNote` | `IAppUserRepositoryManager.NoteRepository` |
| `IUserNoteTypeRepository` | `UserNoteType` | `IAppUserRepositoryManager.NoteTypeRepository` |
| `IUserPhoneRepository` | `UserPhone` | `IAppUserRepositoryManager.PhoneRepository` |
| `IUserPhoneTypeRepository` | `UserPhoneType` | `IAppUserRepositoryManager.PhoneTypeRepository` |

---

## Organisation Repositories

| Interface | Entity | Accessed via |
|---|---|---|
| `IOrganizationRepository` | `Organization` | `IOrganizationRepositoryManager.OrganizationRepository` |
| `IOrganizationAddressRepository` | `OrganizationAddress` | `IOrganizationRepositoryManager.AddressRepository` |
| `IOrganizationAddressTypeRepository` | `OrganizationAddressType` | `IOrganizationRepositoryManager.AddressTypeRepository` |
| `IOrganizationEmailRepository` | `OrganizationEmail` | `IOrganizationRepositoryManager.EmailRepository` |
| `IOrganizationEmailTypeRepository` | `OrganizationEmailType` | `IOrganizationRepositoryManager.EmailTypeRepository` |
| `IOrganizationLinkRepository` | `OrganizationLink` | `IOrganizationRepositoryManager.LinkRepository` |
| `IOrganizationLinkTypeRepository` | `OrganizationLinkType` | `IOrganizationRepositoryManager.LinkTypeRepository` |
| `IOrganizationNoteRepository` | `OrganizationNote` | `IOrganizationRepositoryManager.NoteRepository` |
| `IOrganizationNoteTypeRepository` | `OrganizationNoteType` | `IOrganizationRepositoryManager.NoteTypeRepository` |
| `IOrganizationPageRepository` | `OrganizationPage` | `IOrganizationRepositoryManager.PageRepository` |
| `IOrganizationPhoneRepository` | `OrganizationPhone` | `IOrganizationRepositoryManager.PhoneRepository` |
| `IOrganizationPhoneTypeRepository` | `OrganizationPhoneType` | `IOrganizationRepositoryManager.PhoneTypeRepository` |

---

## Interface Pattern

Every interface follows the same pattern — no additional methods beyond `IRepositoryBase<T>`:

```csharp
namespace Ben.Service.RepositoryService.EntityInterfaces;

/// <summary>Repository for <c>Organization</c>. Inherits all methods from IRepositoryBase.</summary>
public interface IOrganizationRepository : IRepositoryBase<Organization>
{
}
```

All generic CRUD methods are inherited from [`IRepositoryBase<T>`](Interfaces-Generic.md#irepositoryt).

---
