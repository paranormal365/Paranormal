# Ben.Service.RepositoryService — Services

---

## `OrganizationSecurityService`

**Namespace:** `Ben.Service.RepositoryService.Services`  
**File:** [`Ben.Service.RepositoryService/Services/OrganizationSecurityService.cs`](../../../Ben.Service.RepositoryService/Services/OrganizationSecurityService.cs)  
**Implements:** `Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService` AND `Ben.Service.Security.Services.IOrganizationSecurityService`

### Summary
The concrete organisation security and membership implementation.  
Registered **twice** in DI — once for each interface — so both the controller layer and the attribute/middleware layer can resolve it independently.

**DI registration in `Ben.Data.WebApi/Program.cs`:**
```csharp
builder.Services.AddScoped<Ben.Service.Security.Services.IOrganizationSecurityService,
    Ben.Service.RepositoryService.Services.OrganizationSecurityService>();
builder.Services.AddScoped<Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService,
    Ben.Service.RepositoryService.Services.OrganizationSecurityService>();
```

### Key method implementations

#### `SearchUsersAsync`
- SuperAdmins: queries all `AppUsers`.
- Others: joins `OrganizationUserMemberships` to find users in shared organisations, then applies the text filter.
- Result ordered by `DisplayName` then `Email`.

#### `HasAccessAsync`
1. Returns `true` for SuperAdmins (Identity role join query).
2. Returns `true` for `Owner` or `Administrator` role members.
3. Queries `OrganizationAccessGrants` using a bitwise check: `(g.Actions & actionName) != None`.

#### `RegisterOrganizationAsync`
1. Validates name/urlName (throws `InvalidOperationException` if blank or duplicate).
2. Creates `Organization` entity.
3. Adds an `OrganizationUserMembership` with `Role = Owner`.

#### `GetOrganizationUsersAsync`
Calls `EnsureCanManageOrganizationAsync` first.  
Returns all memberships ordered by `Role` ascending (Owner first).

#### `UpsertMembershipAsync`
Calls `EnsureCanManageOrganizationAsync`, validates org and target user exist, then upserts the membership row.

#### `EnsureCanManageOrganizationAsync` *(private)*
Throws `UnauthorizedAccessException` unless the acting user is a SuperAdmin, Owner, or Administrator.

#### `IsSuperAdminAsync` *(private static)*
Joins `AspNetUserRoles` and `AspNetRoles` to check the `"SuperAdmin"` role.

---

## `AuditLogService`

**Namespace:** `Ben.Service.RepositoryService.Services`  
**File:** [`Ben.Service.RepositoryService/Services/AuditLogService.cs`](../../../Ben.Service.RepositoryService/Services/AuditLogService.cs)  
**Implements:** [`IAuditLogService`](Interfaces-Generic.md#iauditlogservice)

### Summary
Persists CRUD audit entries to the `AuditLogs` table.  
Creates a new `BenDataContext` per call to avoid tracking conflicts with any context that may already be tracking the entity being audited.

### Implementation Detail

All three public methods (`LogCreateAsync`, `LogUpdateAsync`, `LogDeleteAsync`) share a private `WriteAsync` that:
1. Creates a new context via `IDbContextFactory`.
2. Adds a new `AuditLog` entity with a new `Guid.NewGuid()` PK.
3. Calls `SaveChangesAsync`.

JSON serialisation uses `System.Text.Json` with `WriteIndented = false` and `JsonIgnoreCondition.WhenWritingNull`.

---

## `AddressGeocodingService`

**Namespace:** `Ben.Service.RepositoryService.Services`  
**File:** `Ben.Service.RepositoryService/Services/AddressGeocodingService.cs`

### Summary
Converts a postal address to latitude/longitude coordinates using an external geocoding API.  
Called when address entities are created or updated to populate the `Latitude` and `Longitude` columns.

### Key Method

| Method | Description |
|---|---|
| `GeocodeAddressAsync(streetAddress1, city, state, zipCode, country, token)` | Calls the geocoding API and returns `(double Lat, double Lng)?`. Returns `null` if any required field is blank or the API call fails. |
