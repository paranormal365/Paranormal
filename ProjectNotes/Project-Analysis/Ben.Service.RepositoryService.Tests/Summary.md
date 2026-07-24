# Ben.Service.RepositoryService.Tests — Summary

**Type:** xUnit Test Project  
**Test count:** 279 tests (all passing)  
**Framework:** xUnit with `Microsoft.EntityFrameworkCore.InMemory`

## Purpose

Unit and integration tests for `Ben.Service.RepositoryService` services.  
Uses an in-memory EF Core provider so tests run without a database.

## Test Files

| File | Tests | Coverage |
|---|---|---|
| `RepositoryManagerTests.cs` | 2 | `RepositoryManager` — verifies repositories are cached after first access |
| `UserRepositoryManagerTests.cs` | 14 | `AppUserRepositoryManager` — all 14 user-domain repos return cached instances |
| `OrganizationRepositoryManagerTests.cs` | 12 | `OrganizationRepositoryManager` — all 12 org-domain repos return cached instances |
| `OrganizationSecurityServiceTests.cs` | 20 | `Ben.Service.Security.Services.OrganizationSecurityService`: `IsMemberAsync`, `IsOwnerAsync`, `HasPermissionAsync`, `GetUserOrganizations`, `GrantAccessAsync`, `RevokeAccessAsync`, `AddMemberAsync`, `RemoveMemberAsync`, `GetOrganizationMembersAsync` |
| `OrganizationSecurityServiceRepositoryTests.cs` | **30** | `Ben.Service.RepositoryService.Services.OrganizationSecurityService`: `SearchUsersAsync`, `HasAccessAsync` (direct grants + named roles), `GetOrganizationsForUserAsync`, `RegisterOrganizationAsync`, `GetOrganizationUsersAsync`, `UpsertMembershipAsync`, `SetAccessGrantAsync` |
| `OrganizationRoleTests.cs` | **14** | `OrganizationRole`, `OrganizationRolePermission`, `OrganizationRoleMembership` entity persistence, cascade deletes, unique index model config |
| `DeleteGrantTests.cs` | 4 | `OrganizationSecurityService.DeleteGrantAsync` |
| `RepositoryReadPathTests.cs` | **20** | `RepositoryBase<T>` read-path: `GetAllAsync`, `FindListAsync`, `FindOneAsync`, `GetByIdAsync`, `CountAllAsync`, `CountFindAsync` via `UploadFileTypeRepository` + cross-entity smoke via `OrganizationAddressTypeRepository` |
| `AuditChangeTrackerTests.cs` | 17 | `AuditChangeTracker.GetChanges`, `ToPropertySnapshot`, scalar type coverage, navigation property exclusion |
| `AuditLogServiceTests.cs` | 14 | `AuditLogService.LogCreateAsync`, `LogUpdateAsync`, `LogDeleteAsync` — JSON shape, action values, source, uniqueness |
| `UploadFileEntityTests.cs` | 5 | `UploadFile`, `UploadFileOrganizationShare`, `UploadFilePermissionRequest` |
| `UploadFileTypeExtensionTests.cs` | 16 | `UploadFileType.AllowAllExtensions`, extension CRUD, cascade delete, pattern matching, `FileExtensionPatternMatcher` |
| `UploadFileRegionNoteTests.cs` | 9 | `UploadFileRegionNote` entity CRUD, cascade delete, `TimeOffset` null vs value, parent-file tracking |
| `CmsEntityTests.cs` | 19 | CMS entities: logos, pages, sections, member groups, page permissions, `CmsPageAction` flags |
| `LogoFileTypeTests.cs` | 8 | Logo `UploadFileType` seeder design — `AllowAllExtensions=false`, 6 image extensions, `FileExtensionPatternMatcher` |
| `AddressMapConfigTests.cs` | 5 | `OrganizationAddressMapConfig` entity CRUD, cascade delete, unique index model config, nav to map config |
| `OrganizationFilePublishAndDeleteLogTests.cs` | 9 | `OrganizationFile` publish tracking, `OrganizationFileDeleteLog` snapshot persistence |
| `OrganizationMembershipRequestTests.cs` | 11 | `OrganizationMembershipRequest` status transitions, `OrganizationFile` CRUD |
| `SvgSanitizerTests.cs` | 15 | `SvgSanitizer` — removes dangerous attributes/elements, preserves safe SVG content |

## InMemory Constraints

- **Cascade deletes** only occur when child entities are loaded (tracked) before removing the parent. Tests that need cascade must `Include` children before deletion.
- **Unique index constraints** are not enforced at runtime — tests verify EF model metadata instead.
- **Required reference navigations** (e.g. `CreatedByAppUser`) cause EF to filter rows when using `IncludeAllNavigations` if no matching FK entity is seeded. Tests that exercise `includeAllNavigations: true` must seed the referenced `AppUser`.

## Test Infrastructure

### `TestDbFactory`
Returns `IDbContextFactory<BenDataContext>` via `PooledDbContextFactory` over a uniquely-named in-memory database. Each call to `TestDbFactory.Create()` produces an isolated store.

### Standard Pattern
All test classes:
1. Call `private static IDbContextFactory<BenDataContext> CreateFactory() => TestDbFactory.Create()` 
2. Open short-lived scopes with `await using var db = await factory.CreateDbContextAsync()`
3. Seed data in one scope, assert in another (or same scope where entity tracking is needed)

### SuperAdmin Seeding
`OrganizationSecurityServiceRepositoryTests` provides a `SeedSuperAdminAsync` helper that seeds `IdentityRole<Guid>` + `IdentityUserRole<Guid>` to enable SuperAdmin path tests.
