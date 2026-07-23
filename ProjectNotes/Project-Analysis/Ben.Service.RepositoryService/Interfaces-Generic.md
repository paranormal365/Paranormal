# Ben.Service.RepositoryService — Generic Interfaces

---

## `IRepositoryBase<T>`

**Namespace:** `Ben.Service.RepositoryService.GenericInterfaces`  
**File:** [`Ben.Service.RepositoryService/GenericInterfaces/IRepositoryBase.cs`](../../../Ben.Service.RepositoryService/GenericInterfaces/IRepositoryBase.cs)

### Summary
Generic repository contract providing standard **read-only** query operations over a typed entity.  
All query methods accept a `trackChanges` flag — pass `false` for read-only operations (better performance) and `true` when the result will be modified.

> **Write operations are not part of this interface.** All Create / Update / Delete work is performed directly in controllers via `IDbContextFactory<BenDataContext>`.

### Methods

| Method | Returns | Description |
|---|---|---|
| `GetAllAsync(bool, CancellationToken)` | `IEnumerable<T>` | All entities, no includes. |
| `GetAllAsync(Expression[]?, bool, CancellationToken)` | `IEnumerable<T>` | All entities with specified navigation properties eagerly loaded. |
| `GetAllAsync(bool includes, bool track, CancellationToken)` | `IEnumerable<T>` | All entities with option to load every navigation property. |
| `FindListAsync(predicate, bool, CancellationToken)` | `IEnumerable<T>` | Entities matching predicate. |
| `FindListAsync(predicate, Expression[]?, bool, CancellationToken)` | `IEnumerable<T>` | Matching entities with specified includes. |
| `FindListAsync(predicate, bool includes, bool track, CancellationToken)` | `IEnumerable<T>` | Matching entities with all-navigations option. |
| `GetByIdAsync(Guid, bool, CancellationToken)` | `T?` | Single entity by PK, or null. |
| `GetByIdAsync(Guid, Expression[]?, bool, CancellationToken)` | `T?` | Single entity with specified includes. |
| `GetByIdAsync(Guid, bool includes, bool, CancellationToken)` | `T?` | Single entity with all-navigations option. |
| `FindOneAsync(predicate, bool, CancellationToken)` | `T?` | First entity matching predicate. |
| `FindOneAsync(predicate, Expression[]?, bool, CancellationToken)` | `T?` | First matching entity with includes. |
| `FindOneAsync(predicate, bool, bool, CancellationToken)` | `T?` | First matching with all-navigations. |
| `CountAllAsync(CancellationToken)` | `int` | Total row count. |
| `CountFindAsync(predicate, CancellationToken)` | `int` | Count of rows matching predicate. |

---

## `IRepositoryManager`

**Namespace:** `Ben.Service.RepositoryService.GenericInterfaces`  
**File:** `Ben.Service.RepositoryService/GenericInterfaces/IRepositoryManager.cs`

### Summary
Exposes the two sub-managers. No `SaveChangesAsync` — each repository owns its own short-lived context.

### Properties (repositories)

Each property returns the typed sub-manager:
`Organization` → `IOrganizationRepositoryManager`, `AppUser` → `IAppUserRepositoryManager`.

---

## `IAuditLogService`

**Namespace:** `Ben.Service.RepositoryService.GenericInterfaces`  
**File:** [`Ben.Service.RepositoryService/GenericInterfaces/IAuditLogService.cs`](../../../Ben.Service.RepositoryService/GenericInterfaces/IAuditLogService.cs)  
**Implemented by:** [`AuditLogService`](Services.md#auditlogservice)

### Summary
Records CRUD activity to the `AuditLogs` table.

| Method | Description |
|---|---|
| `LogCreateAsync(entityType, entityId, entity, userId, source, ct)` | Writes a Create entry with a full property snapshot. |
| `LogUpdateAsync(entityType, entityId, before, after, userId, source, ct)` | Writes an Update entry with only changed properties. Uses [`AuditChangeTracker.GetChanges`](../Ben.Data.Common/Helpers.md#auditchangetracker). |
| `LogDeleteAsync(entityType, entityId, entity, userId, source, ct)` | Writes a Delete entry with a full property snapshot captured before removal. |

---

## `IOrganizationSecurityService`

**Namespace:** `Ben.Service.RepositoryService.GenericInterfaces`  
**File:** [`Ben.Service.RepositoryService/GenericInterfaces/IOrganizationSecurityService.cs`](../../../Ben.Service.RepositoryService/GenericInterfaces/IOrganizationSecurityService.cs)  
**Implemented by:** [`OrganizationSecurityService`](Services.md#organizationsecurityservice)  
**See also:** [Ben.Service.Security version](../Ben.Service.Security/Interfaces.md)

### Summary
Organization security and membership operations used by WebApi controllers.

| Method | Description |
|---|---|
| `SearchUsersAsync(actingUserId, query, skip, take, token)` | User search scoped to the acting user's visibility. SuperAdmins see all. |
| `HasAccessAsync(appUserId, organizationId, tableName, actionName, token)` | Returns `true` if the user can perform the action on the table within the org. |
| `GetOrganizationsForUserAsync(appUserId, token)` | All organizations the user is an active member of. SuperAdmins get all. |
| `RegisterOrganizationAsync(appUserId, name, urlName, token)` | Creates org + seeds Owner membership. Throws if name/urlName is blank or urlName is taken. |
| `GetOrganizationUsersAsync(organizationId, actingUserId, token)` | All membership rows for the org (requires Owner/Admin or SuperAdmin). |
| `UpsertMembershipAsync(organizationId, targetUserId, role, isActive, actingUserId, token)` | Creates or updates a membership row. |
| `SetAccessGrantAsync(organizationId, targetUserId, tableName, actions, actingUserId, token)` | Creates or updates an access grant, setting the full `OrganizationSecurityAction` bitmask. Pass `None` to clear all access. Throws if target is not an active member. |

---

## `IAppUserRepositoryManager`

**Namespace:** `Ben.Service.RepositoryService.GenericInterfaces`  
**File:** [`Ben.Service.RepositoryService/GenericInterfaces/IAppUserRepositoryManager.cs`](../../../Ben.Service.RepositoryService/GenericInterfaces/IAppUserRepositoryManager.cs)  
**Implemented by:** [`AppUserRepositoryManager`](RepositoryManager.md#appuserrepositorymanager)

### Summary
Groups all AppUser-domain repositories behind a single interface. Accessed via `IRepositoryManager.AppUser`.

| Property | Type | Entity |
|---|---|---|
| `AppUserRepository` | `IAppUserRepository` | `AppUser` |
| `AddressRepository` | `IUserAddressRepository` | `UserAddress` |
| `AddressTypeRepository` | `IUserAddressTypeRepository` | `UserAddressType` |
| `EmailRepository` | `IUserEmailRepository` | `UserEmail` |
| `EmailTypeRepository` | `IUserEmailTypeRepository` | `UserEmailType` |
| `LinkRepository` | `IUserLinkRepository` | `UserLink` |
| `LinkTypeRepository` | `IUserLinkTypeRepository` | `UserLinkType` |
| `MessageRepository` | `IUserMessageRepository` | `UserMessage` |
| `MessageToRepository` | `IUserMessageToRepository` | `UserMessageTo` |
| `MessageTypeRepository` | `IUserMessageTypeRepository` | `UserMessageType` |
| `NoteRepository` | `IUserNoteRepository` | `UserNote` |
| `NoteTypeRepository` | `IUserNoteTypeRepository` | `UserNoteType` |
| `PhoneRepository` | `IUserPhoneRepository` | `UserPhone` |
| `PhoneTypeRepository` | `IUserPhoneTypeRepository` | `UserPhoneType` |

---

## `IOrganizationRepositoryManager`

**Namespace:** `Ben.Service.RepositoryService.GenericInterfaces`  
**File:** [`Ben.Service.RepositoryService/GenericInterfaces/IOrganizationRepositoryManager.cs`](../../../Ben.Service.RepositoryService/GenericInterfaces/IOrganizationRepositoryManager.cs)  
**Implemented by:** [`OrganizationRepositoryManager`](RepositoryManager.md#organizationrepositorymanager)

### Summary
Groups all Organization-domain repositories behind a single interface. Accessed via `IRepositoryManager.Organization`.

| Property | Type | Entity |
|---|---|---|
| `OrganizationRepository` | `IOrganizationRepository` | `Organization` |
| `AddressRepository` | `IOrganizationAddressRepository` | `OrganizationAddress` |
| `AddressTypeRepository` | `IOrganizationAddressTypeRepository` | `OrganizationAddressType` |
| `EmailRepository` | `IOrganizationEmailRepository` | `OrganizationEmail` |
| `EmailTypeRepository` | `IOrganizationEmailTypeRepository` | `OrganizationEmailType` |
| `LinkRepository` | `IOrganizationLinkRepository` | `OrganizationLink` |
| `LinkTypeRepository` | `IOrganizationLinkTypeRepository` | `OrganizationLinkType` |
| `NoteRepository` | `IOrganizationNoteRepository` | `OrganizationNote` |
| `NoteTypeRepository` | `IOrganizationNoteTypeRepository` | `OrganizationNoteType` |
| `PageRepository` | `IOrganizationPageRepository` | `OrganizationPage` |
| `PhoneRepository` | `IOrganizationPhoneRepository` | `OrganizationPhone` |
| `PhoneTypeRepository` | `IOrganizationPhoneTypeRepository` | `OrganizationPhoneType` |

---
