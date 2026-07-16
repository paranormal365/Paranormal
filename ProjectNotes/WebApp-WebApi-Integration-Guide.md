# WebApp ↔ WebApi Integration Guide

Complete guide to implementing calls and CRUD operations between the Blazor WebApp and ASP.NET WebApi.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Dependency Injection](#dependency-injection)
3. [Model/DTO Structure](#modeldto-structure)
4. [Generic HTTP Methods](#generic-http-methods)
5. [Creating Typed Endpoints](#creating-typed-endpoints)
6. [CRUD Operations](#crud-operations)
7. [Using AutoMapper](#using-automapper)
8. [Error Handling](#error-handling)
9. [Complete Example](#complete-example)

---

## Architecture Overview

### Three-Layer Model Structure

```
WebApp (Blazor Server)
    ↓ (calls via typed HttpClient)
WebApi (ASP.NET Core)
    ↓ (maps DTOs ↔ Entities)
Entity Models (EF Core)
    ↓
Database (SQL Server)
```

### Project Structure

| Project | Purpose | Key Files |
|---------|---------|-----------|
| `Ben.Service.Models` | DTOs/Records (read/write contracts) | `*Record.cs`, `*AdminRecord.cs`, `*Request.cs`, `*Response.cs` |
| `Ben.Service.Mappings` | AutoMapper profiles | `*Profile.cs` |
| `Ben.Data.Source` | EF Core entities | `Entities/*`, `Context/BenDataContext.cs` |
| `Ben.Data.WebApi` | API endpoints | `Controllers/*` |
| `Ben.Web.WebApp` | Blazor components | `Services/WebApi/*`, `Components/*` |

### Data Flow

```
WebApp Component
    ↓
IWebApiClient.PostAsync<UserCreateRequest, UserRecord>()
    ↓ (HTTP POST /api/users)
WebApi UserController.CreateAsync(UserCreateRequest)
    ↓ (AutoMapper: Request → Entity)
BenDataContext.Users.AddAsync(entity)
    ↓
Database INSERT
    ↓ (AutoMapper: Entity → Response)
HTTP 200: UserRecord { Id, Email, Name, ... }
    ↓
WebApp receives UserRecord
```

---

## Dependency Injection

### Registering WebApi Services in WebApp

Located in `Ben.Web.WebApp/Program.cs`:

```csharp
// 1. Configure WebApi options from appsettings
builder.Services.Configure<WebApiOptions>(builder.Configuration.GetSection("WebApi"));

// 2. Register token store (circuit-scoped)
builder.Services.AddScoped<IWebApiTokenStore, WebApiTokenStore>();

// 3. Register identity client (login/register/refresh — no auth header needed)
builder.Services.AddHttpClient<IWebApiIdentityClient, WebApiIdentityClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<WebApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});

// 4. Register API client.
// IMPORTANT: Do NOT use AddHttpMessageHandler for bearer token injection.
// IHttpClientFactory resolves DelegatingHandlers from the ROOT DI scope, so any
// circuit-scoped IWebApiTokenStore injected there is always an empty, unrelated
// instance. WebApiClient injects IWebApiTokenStore in its constructor instead.
builder.Services.AddHttpClient<IWebApiClient, WebApiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<WebApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});

// 5. Register auth service
builder.Services.AddScoped<IWebApiAuthService, WebApiAuthService>();
```

### Injecting into Blazor Component

```csharp
@page "/users"
@inject IWebApiClient WebApiClient
@inject IWebApiAuthService AuthService

<h1>Users</h1>

@code {
    private List<AppUserRecord> users = [];

    protected override async Task OnInitializedAsync()
    {
        // WebApiClient is automatically injected
        users = (await WebApiClient.GetUsersAsync()).ToList();
    }
}
```

---

## Model/DTO Structure

### Naming Conventions

| Use Case | Naming | Location | Purpose |
|----------|--------|----------|---------|
| **Read** (single) | `{Entity}Record` | `Ben.Service.Models/Entities/` | Query responses (read-only properties) |
| **Read** (list/summary) | `{Entity}SummaryResponse` | `Ben.Service.Models/` | Optimized for list views |
| **Create/Admin** | `{Entity}AdminRecord` | `Ben.Service.Models/Admin/` | Full properties for admin CRUD |
| **Request** (POST/PUT) | `{Entity}Request` or `Create{Entity}Request` | `Ben.Service.Models/` | Input contract for mutations |
| **Response** (POST/PUT result) | `{Entity}Response` | `Ben.Service.Models/` | Output contract for mutations |

### Example Models

#### Read Model (Read-Only)
```csharp
// Ben.Service.Models/Entities/OrganizationRecord.cs
public record OrganizationRecord
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string UrlName { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
```

#### Admin Model (Full CRUD)
```csharp
// Ben.Service.Models/Admin/OrganizationAdminRecord.cs
public record OrganizationAdminRecord
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string UrlName { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
```

#### Request Model (Input Contract)
```csharp
// Ben.Service.Models/CreateOrganizationRequest.cs
public record CreateOrganizationRequest
{
    public required string Name { get; init; }
    public required string UrlName { get; init; }
}
```

#### Response Model (Output Contract)
```csharp
// Ben.Service.Models/OrganizationSummaryResponse.cs
public record OrganizationSummaryResponse
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string UrlName { get; init; }
    public DateTime DateCreated { get; init; }
}
```

---

## Generic HTTP Methods

The `IWebApiClient` provides four generic methods that work with any DTO type.

### GET (Read)

```csharp
// Generic: Fetch any type from any endpoint
Task<TResponse?> GetAsync<TResponse>(string relativeUrl, CancellationToken token = default)

// Usage:
var user = await WebApiClient.GetAsync<AppUserRecord>("/api/users/me");
var organizations = await WebApiClient.GetAsync<List<OrganizationRecord>>("/api/organizations");

// What happens:
// 1. Makes HTTP GET to relativeUrl
// 2. Deserializes response JSON to TResponse
// 3. Returns deserialized object (or null if 4xx/5xx)
// 4. Bearer token automatically injected
```

### POST (Create)

```csharp
// Generic: Send request, get response
Task<TResponse?> PostAsync<TRequest, TResponse>(
    string relativeUrl, 
    TRequest payload, 
    CancellationToken token = default)

// Usage:
var request = new CreateOrganizationRequest 
{ 
    Name = "Acme Corp", 
    UrlName = "acme" 
};
var result = await WebApiClient.PostAsync<CreateOrganizationRequest, OrganizationSummaryResponse>(
    "/api/organizations", 
    request);

// What happens:
// 1. Serializes TRequest to JSON
// 2. Makes HTTP POST with JSON body
// 3. Deserializes response to TResponse
// 4. Bearer token automatically injected
```

### PUT (Update)

```csharp
// Generic: Send update request, get response
Task<TResponse?> PutAsync<TRequest, TResponse>(
    string relativeUrl, 
    TRequest payload, 
    CancellationToken token = default)

// Usage:
var updateRequest = new UpdateOrganizationRequest 
{ 
    Name = "Acme Corp Updated", 
    UrlName = "acme-updated" 
};
var result = await WebApiClient.PutAsync<UpdateOrganizationRequest, OrganizationRecord>(
    $"/api/organizations/{organizationId}", 
    updateRequest);

// What happens:
// 1. Serializes TRequest to JSON
// 2. Makes HTTP PUT with JSON body
// 3. Deserializes response to TResponse
// 4. Bearer token automatically injected
```

### DELETE (Remove)

```csharp
// Generic: Delete resource
Task<bool> DeleteAsync(string relativeUrl, CancellationToken token = default)

// Usage:
bool success = await WebApiClient.DeleteAsync($"/api/organizations/{organizationId}");

// What happens:
// 1. Makes HTTP DELETE
// 2. Returns true if 200-299, false if 4xx/5xx
// 3. Bearer token automatically injected
```

---

## Creating Typed Endpoints

Instead of using generic methods with hardcoded URLs, create typed convenience methods in `IWebApiClient`.

### Step 1: Add Interface Method

```csharp
// Ben.Web.WebApp/Services/WebApi/IWebApiClient.cs
public interface IWebApiClient
{
    // ... existing methods ...

    Task<OrganizationRecord?> GetOrganizationAsync(Guid organizationId, CancellationToken token = default);
    Task<OrganizationSummaryResponse?> CreateOrganizationAsync(CreateOrganizationRequest request, CancellationToken token = default);
    Task<OrganizationRecord?> UpdateOrganizationAsync(Guid organizationId, UpdateOrganizationRequest request, CancellationToken token = default);
    Task<bool> DeleteOrganizationAsync(Guid organizationId, CancellationToken token = default);
}
```

### Step 2: Implement in WebApiClient

```csharp
// Ben.Web.WebApp/Services/WebApi/WebApiClient.cs
public sealed class WebApiClient : IWebApiClient
{
    // ... existing methods ...

    public Task<OrganizationRecord?> GetOrganizationAsync(Guid organizationId, CancellationToken token = default)
    {
        return GetAsync<OrganizationRecord>($"/api/organizations/{organizationId}", token);
    }

    public Task<OrganizationSummaryResponse?> CreateOrganizationAsync(
        CreateOrganizationRequest request, 
        CancellationToken token = default)
    {
        return PostAsync<CreateOrganizationRequest, OrganizationSummaryResponse>(
            "/api/organizations", 
            request, 
            token);
    }

    public Task<OrganizationRecord?> UpdateOrganizationAsync(
        Guid organizationId, 
        UpdateOrganizationRequest request, 
        CancellationToken token = default)
    {
        return PutAsync<UpdateOrganizationRequest, OrganizationRecord>(
            $"/api/organizations/{organizationId}", 
            request, 
            token);
    }

    public Task<bool> DeleteOrganizationAsync(Guid organizationId, CancellationToken token = default)
    {
        return DeleteAsync($"/api/organizations/{organizationId}", token);
    }
}
```

### Step 3: Use in WebApp Component

```csharp
@page "/organizations/{OrganizationId:guid}"
@inject IWebApiClient WebApiClient

<h1>@organization?.Name</h1>

@code {
    [Parameter]
    public Guid OrganizationId { get; set; }

    private OrganizationRecord? organization;

    protected override async Task OnInitializedAsync()
    {
        // Cleaner: specific method vs. generic
        organization = await WebApiClient.GetOrganizationAsync(OrganizationId);
    }
}
```

---

## CRUD Operations

### CREATE (POST)

#### WebApp Component

```csharp
@page "/organizations/create"
@inject IWebApiClient WebApiClient
@inject NavigationManager Nav

<h1>Create Organization</h1>

<EditForm Model="@model" OnValidSubmit="@HandleSubmit">
    <DataAnnotationsValidator />
    
    <InputText @bind-Value="@model.Name" placeholder="Organization Name" />
    <InputText @bind-Value="@model.UrlName" placeholder="URL Name (slug)" />
    
    <button type="submit" disabled="@isSubmitting">Create</button>
</EditForm>

<div style="color: red;">@errorMessage</div>

@code {
    private CreateOrganizationRequest model = new();
    private bool isSubmitting = false;
    private string? errorMessage;

    private async Task HandleSubmit()
    {
        isSubmitting = true;
        try
        {
            // Call WebApi to create
            var result = await WebApiClient.CreateOrganizationAsync(model);
            
            if (result is not null)
            {
                // Success: navigate to detail page
                Nav.NavigateTo($"/organizations/{result.Id}");
            }
            else
            {
                // Failed (4xx/5xx response)
                errorMessage = "Failed to create organization. Please try again.";
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            isSubmitting = false;
        }
    }
}
```

#### WebApi Controller

```csharp
// Ben.Data.WebApi/Controllers/Entities/OrganizationController.cs
[ApiController]
[Route("api/organizations")]
[Authorize]
public sealed class OrganizationController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;

    public OrganizationController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
    }

    [HttpPost]
    public async Task<ActionResult<OrganizationSummaryResponse>> CreateAsync(
        CreateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        // 1. Create entity from request
        var entity = new Organization
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            UrlName = request.UrlName,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = new Guid(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
        };

        // 2. Save to database
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Organizations.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        // 3. Map entity to response DTO
        var response = _mapper.Map<OrganizationSummaryResponse>(entity);

        // 4. Return created response
        return CreatedAtAction(nameof(GetByIdAsync), new { id = entity.Id }, response);
    }
}
```

#### AutoMapper Profile

```csharp
// Ben.Service.Mappings/OrganizationProfile.cs
public class OrganizationProfile : Profile
{
    public OrganizationProfile()
    {
        // Entity → Read DTO
        CreateMap<Organization, OrganizationRecord>();
        
        // Entity → Summary Response
        CreateMap<Organization, OrganizationSummaryResponse>();
        
        // Entity → Admin DTO
        CreateMap<Organization, OrganizationAdminRecord>();

        // Create Request → Entity (handled in controller)
        // (CreateMap not needed here; controller does it manually)
    }
}
```

### READ (GET)

#### WebApp Component

```csharp
@page "/organizations"
@inject IWebApiClient WebApiClient

<h1>Organizations</h1>

@if (organizations == null)
{
    <p>Loading...</p>
}
else if (organizations.Count == 0)
{
    <p>No organizations found.</p>
}
else
{
    <ul>
        @foreach (var org in organizations)
        {
            <li>
                <a href="/organizations/@org.Id">@org.Name</a>
                <small>Created: @org.DateCreated.ToShortDateString()</small>
            </li>
        }
    </ul>
}

@code {
    private List<OrganizationRecord>? organizations;

    protected override async Task OnInitializedAsync()
    {
        // WebApiClient makes HTTP GET to /api/organizations
        // Bearer token automatically attached
        // Response deserialized to List<OrganizationRecord>
        var result = await WebApiClient.GetAsync<List<OrganizationRecord>>("/api/organizations");
        organizations = result ?? [];
    }
}
```

#### WebApi Controller

```csharp
[HttpGet]
public async Task<ActionResult<IEnumerable<OrganizationRecord>>> GetAllAsync(CancellationToken cancellationToken)
{
    await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    var entities = await dbContext.Organizations
        .AsNoTracking()
        .ToListAsync(cancellationToken);

    var records = _mapper.Map<IEnumerable<OrganizationRecord>>(entities);
    return Ok(records);
}

[HttpGet("{id:guid}")]
public async Task<ActionResult<OrganizationRecord>> GetByIdAsync(
    Guid id,
    CancellationToken cancellationToken)
{
    await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    var entity = await dbContext.Organizations
        .AsNoTracking()
        .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    if (entity is null)
        return NotFound();

    var record = _mapper.Map<OrganizationRecord>(entity);
    return Ok(record);
}
```

### UPDATE (PUT)

#### WebApp Component

```csharp
@page "/organizations/{OrganizationId:guid}/edit"
@inject IWebApiClient WebApiClient
@inject NavigationManager Nav

<h1>Edit Organization</h1>

@if (model == null)
{
    <p>Loading...</p>
}
else
{
    <EditForm Model="@model" OnValidSubmit="@HandleSubmit">
        <DataAnnotationsValidator />
        
        <InputText @bind-Value="@model.Name" />
        <InputText @bind-Value="@model.UrlName" />
        
        <button type="submit" disabled="@isSubmitting">Save</button>
    </EditForm>
}

<div style="color: red;">@errorMessage</div>

@code {
    [Parameter]
    public Guid OrganizationId { get; set; }

    private UpdateOrganizationRequest? model;
    private bool isSubmitting = false;
    private string? errorMessage;

    protected override async Task OnInitializedAsync()
    {
        // 1. Load existing organization
        var org = await WebApiClient.GetOrganizationAsync(OrganizationId);
        if (org is not null)
        {
            // 2. Convert to request model
            model = new UpdateOrganizationRequest
            {
                Name = org.Name,
                UrlName = org.UrlName
            };
        }
    }

    private async Task HandleSubmit()
    {
        if (model == null) return;

        isSubmitting = true;
        try
        {
            // Call WebApi to update
            var result = await WebApiClient.UpdateOrganizationAsync(OrganizationId, model);
            
            if (result is not null)
            {
                // Success: navigate back to detail
                Nav.NavigateTo($"/organizations/{OrganizationId}");
            }
            else
            {
                errorMessage = "Failed to update organization.";
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            isSubmitting = false;
        }
    }
}
```

#### WebApi Controller

```csharp
[HttpPut("{id:guid}")]
public async Task<ActionResult<OrganizationRecord>> UpdateAsync(
    Guid id,
    UpdateOrganizationRequest request,
    CancellationToken cancellationToken)
{
    await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    
    // 1. Load existing entity
    var entity = await dbContext.Organizations.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    if (entity is null)
        return NotFound();

    // 2. Update properties
    entity.Name = request.Name;
    entity.UrlName = request.UrlName;
    entity.DateUpdated = DateTime.UtcNow;
    entity.UpdatedByAppUserId = new Guid(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // 3. Save changes
    await dbContext.SaveChangesAsync(cancellationToken);

    // 4. Map to response
    var record = _mapper.Map<OrganizationRecord>(entity);
    return Ok(record);
}
```

### DELETE (DELETE)

#### WebApp Component

```csharp
@page "/organizations/{OrganizationId:guid}/delete"
@inject IWebApiClient WebApiClient
@inject NavigationManager Nav

<h1>Delete Organization</h1>

<p>Are you sure you want to delete this organization?</p>

<button @onclick="@HandleDelete" disabled="@isDeleting">Delete</button>
<button @onclick="@(() => Nav.NavigateTo($"/organizations/{OrganizationId}"))">Cancel</button>

<div style="color: red;">@errorMessage</div>

@code {
    [Parameter]
    public Guid OrganizationId { get; set; }

    private bool isDeleting = false;
    private string? errorMessage;

    private async Task HandleDelete()
    {
        isDeleting = true;
        try
        {
            // Call WebApi to delete
            bool success = await WebApiClient.DeleteOrganizationAsync(OrganizationId);
            
            if (success)
            {
                // Success: navigate to list
                Nav.NavigateTo("/organizations");
            }
            else
            {
                errorMessage = "Failed to delete organization.";
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            isDeleting = false;
        }
    }
}
```

#### WebApi Controller

```csharp
[HttpDelete("{id:guid}")]
public async Task<IActionResult> DeleteAsync(
    Guid id,
    CancellationToken cancellationToken)
{
    await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    
    // 1. Load entity
    var entity = await dbContext.Organizations.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    if (entity is null)
        return NotFound();

    // 2. Delete
    dbContext.Organizations.Remove(entity);
    await dbContext.SaveChangesAsync(cancellationToken);

    return NoContent();
}
```

---

## Using AutoMapper

### Why AutoMapper?

Keeps entity models separate from DTOs sent over the wire. Enables:
- Different properties in read vs. write models
- Nested object flattening/expansion
- Automatic type conversions
- Centralized transformation logic

### Creating a Profile

```csharp
// Ben.Service.Mappings/OrganizationProfile.cs
using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models;

public class OrganizationProfile : Profile
{
    public OrganizationProfile()
    {
        // Entity → DTOs (read direction)
        CreateMap<Organization, OrganizationRecord>();
        CreateMap<Organization, OrganizationSummaryResponse>();
        CreateMap<Organization, OrganizationAdminRecord>();

        // DTOs → Entity (write direction) - less common, usually done in controller
        // CreateMap<CreateOrganizationRequest, Organization>()
        //     .ForMember(dest => dest.Id, opt => opt.MapFrom(_ => Guid.NewGuid()))
        //     .ForMember(dest => dest.DateCreated, opt => opt.MapFrom(_ => DateTime.UtcNow));
    }
}
```

### Using in Controller

```csharp
public sealed class OrganizationController : ControllerBase
{
    private readonly IMapper _mapper;

    public OrganizationController(IMapper mapper)
    {
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganizationRecord>>> GetAllAsync(...)
    {
        var entities = await dbContext.Organizations.ToListAsync(cancellationToken);
        
        // AutoMapper maps each entity to DTO
        var records = _mapper.Map<IEnumerable<OrganizationRecord>>(entities);
        
        return Ok(records);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrganizationRecord>> GetByIdAsync(...)
    {
        var entity = await dbContext.Organizations.FirstOrDefaultAsync(...);
        if (entity is null)
            return NotFound();

        // Map single entity to DTO
        var record = _mapper.Map<OrganizationRecord>(entity);
        
        return Ok(record);
    }
}
```

### Custom Mapping Logic

```csharp
public class UserProfile : Profile
{
    public UserProfile()
    {
        // Simple mapping
        CreateMap<AppUser, AppUserRecord>();

        // Custom transformation
        CreateMap<AppUser, UserSearchResultResponse>()
            .ForMember(dest => dest.FullName, opt => opt.MapFrom(src => $"{src.DisplayName}"))
            .ForMember(dest => dest.OrganizationCount, opt => opt.Ignore()); // Set elsewhere

        // Nested mapping
        CreateMap<AppUser, UserDetailResponse>()
            .ForMember(dest => dest.Addresses, opt => opt.MapFrom(src => src.UserAddresses));

        CreateMap<UserAddress, UserAddressResponse>();
    }
}
```

---

## Error Handling

### In WebApp Component

```csharp
@page "/organizations"
@inject IWebApiClient WebApiClient

<h1>Organizations</h1>

@if (errorMessage != null)
{
    <div style="color: red; border: 1px solid red; padding: 10px;">
        <strong>Error:</strong> @errorMessage
    </div>
}

@if (isLoading)
{
    <p>Loading...</p>
}
else if (organizations == null || organizations.Count == 0)
{
    <p>No organizations found.</p>
}
else
{
    <ul>
        @foreach (var org in organizations)
        {
            <li>@org.Name</li>
        }
    </ul>
}

@code {
    private List<OrganizationRecord>? organizations;
    private bool isLoading = true;
    private string? errorMessage;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var result = await WebApiClient.GetAsync<List<OrganizationRecord>>("/api/organizations");
            organizations = result ?? [];
        }
        catch (HttpRequestException ex)
        {
            errorMessage = "Network error: Unable to reach the server.";
        }
        catch (JsonException ex)
        {
            errorMessage = "Server returned invalid data format.";
        }
        catch (Exception ex)
        {
            errorMessage = $"Unexpected error: {ex.Message}";
        }
        finally
        {
            isLoading = false;
        }
    }
}
```

### Improved WebApiClient with Diagnostics

```csharp
public sealed class WebApiClient : IWebApiClient
{
    private readonly HttpClient _httpClient;

    public async Task<TResponse?> GetAsync<TResponse>(string relativeUrl, CancellationToken token = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(relativeUrl, token);
            
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(token);
                Console.WriteLine($"[GET {relativeUrl}] {response.StatusCode}: {content}");
                return default;
            }

            return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token);
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[GET {relativeUrl}] Network error: {ex.Message}");
            throw;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[GET {relativeUrl}] JSON parse error: {ex.Message}");
            throw;
        }
    }
}
```

---

## Complete Example

### Scenario: User Management CRUD

#### 1. Models in Ben.Service.Models

```csharp
// Ben.Service.Models/Entities/AppUserRecord.cs
public record AppUserRecord
{
    public Guid Id { get; init; }
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
    public DateTime DateCreated { get; init; }
}

// Ben.Service.Models/Admin/AppUserAdminRecord.cs
public record AppUserAdminRecord
{
    public Guid Id { get; init; }
    public string? Email { get; init; }
    public string? UserName { get; init; }
    public string? DisplayName { get; init; }
    public bool EmailConfirmed { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
}

// Ben.Service.Models/CreateUserRequest.cs
public record CreateUserRequest
{
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required string Password { get; init; }
}

// Ben.Service.Models/UpdateUserRequest.cs
public record UpdateUserRequest
{
    public required string DisplayName { get; init; }
    public string? PhoneNumber { get; init; }
}
```

#### 2. AutoMapper Profile

```csharp
// Ben.Service.Mappings/AppUserProfile.cs
public class AppUserProfile : Profile
{
    public AppUserProfile()
    {
        CreateMap<AppUser, AppUserRecord>()
            .ForMember(dest => dest.Email, opt => opt.MapFrom(src => src.Email));

        CreateMap<AppUser, AppUserAdminRecord>();
    }
}
```

#### 3. WebApi Endpoints

```csharp
// Ben.Data.WebApi/Controllers/Entities/AdminAppUserController.cs
[ApiController]
[Route("api/admin/app-users")]
[Authorize(Roles = "Admin")]
public sealed class AdminAppUserController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;
    private readonly UserManager<AppUser> _userManager;

    [HttpPost]
    public async Task<ActionResult<AppUserAdminRecord>> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            UserName = request.Email,
            DisplayName = request.DisplayName,
            DateCreated = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors);

        var record = _mapper.Map<AppUserAdminRecord>(user);
        return CreatedAtAction(nameof(GetByIdAsync), new { id = user.Id }, record);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AppUserAdminRecord>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        var record = _mapper.Map<AppUserAdminRecord>(user);
        return Ok(record);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AppUserAdminRecord>> UpdateAsync(
        Guid id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        user.DisplayName = request.DisplayName;
        user.PhoneNumber = request.PhoneNumber;
        user.DateUpdated = DateTime.UtcNow;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return BadRequest(result.Errors);

        var record = _mapper.Map<AppUserAdminRecord>(user);
        return Ok(record);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
            return BadRequest(result.Errors);

        return NoContent();
    }
}
```

#### 4. WebApp Client Methods

```csharp
// Ben.Web.WebApp/Services/WebApi/IWebApiClient.cs
public interface IWebApiClient
{
    Task<AppUserAdminRecord?> GetUserAsync(Guid userId, CancellationToken token = default);
    Task<AppUserAdminRecord?> CreateUserAsync(CreateUserRequest request, CancellationToken token = default);
    Task<AppUserAdminRecord?> UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken token = default);
    Task<bool> DeleteUserAsync(Guid userId, CancellationToken token = default);
}

// Ben.Web.WebApp/Services/WebApi/WebApiClient.cs
public sealed class WebApiClient : IWebApiClient
{
    private readonly HttpClient _httpClient;

    public Task<AppUserAdminRecord?> GetUserAsync(Guid userId, CancellationToken token = default)
        => GetAsync<AppUserAdminRecord>($"/api/admin/app-users/{userId}", token);

    public Task<AppUserAdminRecord?> CreateUserAsync(CreateUserRequest request, CancellationToken token = default)
        => PostAsync<CreateUserRequest, AppUserAdminRecord>("/api/admin/app-users", request, token);

    public Task<AppUserAdminRecord?> UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken token = default)
        => PutAsync<UpdateUserRequest, AppUserAdminRecord>($"/api/admin/app-users/{userId}", request, token);

    public Task<bool> DeleteUserAsync(Guid userId, CancellationToken token = default)
        => DeleteAsync($"/api/admin/app-users/{userId}", token);
}
```

#### 5. Blazor Component

```csharp
@page "/admin/users"
@inject IWebApiClient WebApiClient
@inject NavigationManager Nav

<h1>User Management</h1>

<button @onclick="@(() => Nav.NavigateTo("/admin/users/create"))">Create User</button>

@if (isLoading)
{
    <p>Loading...</p>
}
else if (users == null || users.Count == 0)
{
    <p>No users found.</p>
}
else
{
    <table>
        <thead>
            <tr>
                <th>Email</th>
                <th>Display Name</th>
                <th>Created</th>
                <th>Actions</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var user in users)
            {
                <tr>
                    <td>@user.Email</td>
                    <td>@user.DisplayName</td>
                    <td>@user.DateCreated.ToShortDateString()</td>
                    <td>
                        <a href="/admin/users/@user.Id/edit">Edit</a>
                        <button @onclick="@(() => DeleteUser(user.Id))">Delete</button>
                    </td>
                </tr>
            }
        </tbody>
    </table>
}

@code {
    private List<AppUserAdminRecord>? users;
    private bool isLoading = true;
    private string? errorMessage;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var result = await WebApiClient.GetAsync<List<AppUserAdminRecord>>("/api/admin/app-users");
            users = result ?? [];
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task DeleteUser(Guid userId)
    {
        if (!await JS.InvokeAsync<bool>("confirm", "Delete this user?"))
            return;

        try
        {
            bool success = await WebApiClient.DeleteUserAsync(userId);
            if (success)
            {
                users?.RemoveAll(u => u.Id == userId);
            }
            else
            {
                errorMessage = "Failed to delete user.";
            }
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
    }
}
```

---

## Best Practices Summary

✅ **DO:**
- Use typed methods in `IWebApiClient` for specific endpoints
- Create request/response DTOs for each mutation
- Use `Guid` for IDs, not `int`
- Map entities ↔ DTOs with AutoMapper
- Validate input in EditForm before sending
- Handle errors gracefully in components
- Use `CancellationToken` for async operations
- Load data in `OnInitializedAsync()` not in constructors
- Show loading state while fetching
- Inject services via `@inject`

❌ **DON'T:**
- Expose entities directly to WebApp
- Mix entity and DTO logic
- Ignore HTTP status codes
- Forget to include Bearer token (WebApiClient.Auth() handles this automatically)
- Make blocking calls on UI thread
- Leave errors silently unhandled
- Cache tokens in WebApp (use WebApiTokenStore)
- Bypass authentication for "testing"
