# Ben.Service.RepositoryService.Tests — Summary

**Type:** xUnit Test Project  
**Test count:** 119 tests (all passing)  
**Framework:** xUnit with `Microsoft.EntityFrameworkCore.InMemory`

## Purpose

Unit and integration tests for `Ben.Service.RepositoryService` services.  
Uses an in-memory EF Core provider so tests run without a database.

## Test Files

| File | Tests | Coverage |
|---|---|---|
| `RepositoryManagerTests.cs` | Caching tests | `RepositoryManager` — verifies repositories are cached after first access |
| `UserRepositoryManagerTests.cs` | User repo tests | `AppUserRepositoryManager` — add, get, delete |
| `OrganizationRepositoryManagerTests.cs` | Org repo tests | `OrganizationRepositoryManager` — add, get, delete |
| `OrganizationSecurityServiceTests.cs` | ~40 tests | `OrganizationSecurityService`: `IsMemberAsync`, `IsOwnerAsync`, `HasAccessAsync`, `RegisterOrganizationAsync`, `GetOrganizationUsersAsync`, `UpsertMembershipAsync`, `SetAccessGrantAsync`, `AddMemberAsync`, `RemoveMemberAsync`, `GetOrganizationMembersAsync` |
| `UploadFileEntityTests.cs` | Upload entity tests | `UploadFile`, `UploadFileOrganizationShare`, `UploadFilePermissionRequest` |
| `UploadFileTypeExtensionTests.cs` | 38 tests | `UploadFileType.AllowAllExtensions`, extension CRUD, cascade delete, pattern matching integration, `FileExtensionPatternMatcher` |
| `AuditChangeTrackerTests.cs` | 16 tests | `AuditChangeTracker.GetChanges`, `ToPropertySnapshot`, scalar type coverage, navigation property exclusion |
| `AuditLogServiceTests.cs` | 15 tests | `AuditLogService.LogCreateAsync`, `LogUpdateAsync`, `LogDeleteAsync` — JSON shape, action values, source, uniqueness |

## InMemory Constraints

- **Cascade deletes** only occur when child entities are loaded (tracked) before removing the parent. Tests that need cascade must `Include` children before deletion.
- **Unique index constraints** are not enforced at runtime — tests verify EF model metadata instead.

### `LogoFileTypeTests` *(added 2026-07-18)*

**File:** [`Ben.Service.RepositoryService.Tests/LogoFileTypeTests.cs`](../../../Ben.Service.RepositoryService.Tests/LogoFileTypeTests.cs)  
**Test count:** 14 tests  
**Purpose:** Validates the design decisions implemented by `UploadFileTypeSeeder` — the "Logo" file type's `AllowAllExtensions=false` + specific image extension patterns work correctly with `FileExtensionPatternMatcher`.

| Test group | Coverage |
|---|---|
| DB persistence | `AllowAllExtensions=false`, 6 extensions stored, name round-trip |
| Allowed extensions | `.jpg`, `.jpeg`, `.png`, `.gif`, `.webp`, `.svg` (Theory) |
| Case insensitivity | `.JPG`, `.PNG`, `.Svg` all accepted |
| Blocked extensions | `.pdf`, `.exe`, `.txt`, `.docx`, `.mp4`, `.zip` (Theory) |
| Array shape | Exactly 6 entries, all lowercase, all dot-prefixed |
| AllowAllExtensions bypass | `AllowAllExtensions=true` short-circuits pattern check |
