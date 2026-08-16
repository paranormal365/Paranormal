using AutoMapper;
using Ben.Data.WebApi.Services;
using Ben.Service.Mappings;
using Serilog;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

/* LOGGING */

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    // Console + rolling file in addition to whatever the JSON config specifies.
    // Auth/JWT debug messages always go here so failures are always visible.
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}  {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: ".vscode/webapi-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 3,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .MinimumLevel.Override("Microsoft.AspNetCore.Authentication", Serilog.Events.LogEventLevel.Debug)
    .MinimumLevel.Override("Microsoft.AspNetCore.Authorization",  Serilog.Events.LogEventLevel.Debug)
    .MinimumLevel.Override("Microsoft.IdentityModel",             Serilog.Events.LogEventLevel.Debug)
    .CreateLogger();

builder.Host.UseSerilog();

/* END LOGGING */

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddBenRateLimiting(builder.Configuration);

// Audit finding B4, pulled forward from phase 3 because Ben.Wasm.Video needs it: with a WASM host
// the *browser* calls this API, so cross-origin is real whenever the two aren't behind one origin.
// Origins come from configuration — hardcoded localhost values would silently break the first real
// deployment. An EMPTY list is valid and meaningful: it's the same-origin / reverse-proxied case
// (the preferred deployment), where CORS never applies. Wildcards are refused rather than honored:
// this API serves authenticated personal data, and "*" here is always a misconfiguration.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
corsOrigins = corsOrigins.Where(o => !string.IsNullOrWhiteSpace(o) && o != "*").ToArray();

builder.Services.AddCors(options =>
{
    options.AddPolicy("WebAppPolicy", policy =>
    {
        if (corsOrigins.Length == 0) return; // same-origin deployment — no cross-origin grants

        policy
            .WithOrigins(corsOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            // Bearer-token auth, not cookies: credentials (in the CORS sense) are never sent
            // cross-origin, so don't invite them. The Authorization header is covered by
            // AllowAnyHeader above.
            // Range/media headers the editor's scrubbing needs to *read*, not just receive:
            .WithExposedHeaders("Content-Range", "Accept-Ranges", "Content-Length")
            // One preflight per endpoint per 10 minutes instead of one per request — media
            // scrubbing makes many small authenticated GETs and each would otherwise pay a
            // preflight round-trip.
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
});

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title   = "Ben API",
        Version = "v1",
        Description = """
            REST API for the Ben application.

            **Authentication**
            Most endpoints require a Bearer token obtained from `POST /login`
            (ASP.NET Core Identity). After login, call `GET /api/me` to resolve
            the user's role (IsSuperAdmin). Entra (Azure AD) tokens are also
            accepted — the `app_user_id` claim is injected by `EntraClaimsTransformation`.

            **SuperAdmin**
            All `/api/admin/*` routes require the `SuperAdmin` role.

            **Everything else**
            Access is enforced per-route inside each action, not by a blanket filter: the
            shared helpers (`FileAudienceAccess`, `CaseOrgAccess`, `InvestigationAccess`,
            `InvestigationVisibilityFilter`) decide what the caller may see, and org-level
            permission grants are checked through `IOrganizationSecurityService`.

            **File storage**
            Uploaded files are stored on the local filesystem under the configured
            `FileStorage:RootPath`. The `FileData` blob column is being phased out;
            `FileMigrationService` migrates any remaining blobs on startup.
            """
    });

    options.SchemaFilter<CircularReferenceSchemaFilter>();

    // Include XML doc comments from the compiled documentation file
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = System.IO.Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (System.IO.File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);

    // Bearer token security scheme
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "opaque",
        In           = ParameterLocation.Header,
        Description  = "Enter the bearer token returned by POST /login"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            []
        }
    });
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultBufferSize = 256 * 1024;
    options.SerializerOptions.MaxDepth = 256;
    options.SerializerOptions.WriteIndented = false;
});

builder.Services.AddDbContextFactory<Ben.Data.Source.Context.BenDataContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("BenDbConnectionString"));
});
builder.Services.AddScoped<Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService, Ben.Service.RepositoryService.Services.OrganizationSecurityService>();
builder.Services.AddScoped<Ben.Service.RepositoryService.GenericInterfaces.IAuditLogService, Ben.Service.RepositoryService.Services.AuditLogService>();
builder.Services.AddSingleton<Ben.Data.Common.Interfaces.IFileStorageService, Ben.Data.WebApi.Services.LocalFileStorageService>();
builder.Services.AddSingleton<Ben.Data.WebApi.Services.FileMetadataExtractorService>();
// Scoped: it opens its own DbContext per call and holds no state between them.
builder.Services.AddScoped<Ben.Data.WebApi.Services.SiteSettingsService>();
// Stateless apart from its keys, so one instance serves every request.
builder.Services.AddSingleton<Ben.Data.WebApi.Services.SupportFormGuard>();
builder.Services.Configure<Ben.Data.WebApi.Services.SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton<Ben.Data.Common.Interfaces.IEmailService, Ben.Data.WebApi.Services.SmtpEmailService>();
builder.Services.AddHostedService<Ben.Data.WebApi.Services.FileMigrationService>();
builder.Services.AddAutoMapper(_ => { }, typeof(AppUserProfile).Assembly);
builder.Services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, Ben.Data.WebApi.Services.EntraClaimsTransformation>();

// Sends Identity's confirmation / password-reset mail. Registered before AddIdentityApiEndpoints
// so it wins over the framework's silent no-op sender — which, with RequireConfirmedAccount below,
// would otherwise mean every new account is created and then locked out with no error anywhere.
builder.Services.AddTransient<Microsoft.AspNetCore.Identity.IEmailSender<AppUser>,
    Ben.Data.WebApi.Services.IdentityEmailSender>();

builder.Services.AddIdentityApiEndpoints<AppUser>(options =>
       {
           options.Password.RequireDigit           = true;
           options.Password.RequiredLength         = 8;
           options.Password.RequireNonAlphanumeric = false;
           options.Password.RequireUppercase       = true;
           options.Password.RequireLowercase       = true;
           options.User.RequireUniqueEmail         = true;
           // /register is anonymous, so without this anyone can mint a working account at an
           // address they do not own — including someone else's. Confirmation gates sign-in only;
           // Entra sign-up and invite acceptance create their users through their own paths and
           // are unaffected.
           options.SignIn.RequireConfirmedAccount  = true;
       })
       .AddRoles<IdentityRole<Guid>>()
       .AddEntityFrameworkStores<BenDataContext>()
       .AddDefaultTokenProviders();

// ── Microsoft Entra JWT bearer (optional — active only when ClientId is configured) ──
const string EntraScheme = "Entra";
var entraConfig = builder.Configuration.GetSection("AzureAd");
bool entraEnabled = !string.IsNullOrWhiteSpace(entraConfig["ClientId"])
                    && entraConfig["ClientId"] != "YOUR_WEBAPI_CLIENT_ID"
                    && entraConfig["ClientId"] != "YOUR_WEBAPP_CLIENT_ID";

if (entraEnabled)
{
    var clientId = entraConfig["ClientId"]!;
    builder.Services.AddAuthentication()
        .AddJwtBearer(EntraScheme, jwt =>
        {
            jwt.Authority = $"https://login.microsoftonline.com/{entraConfig["TenantId"]}/v2.0";
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = true,
                // Personal Microsoft accounts (MSA) issue tokens with audience = just the GUID.
                // Work/school accounts (AAD) issue tokens with audience = api://<clientId>.
                // Accept both formats so either account type works.
                ValidAudiences   = new[] { $"api://{clientId}", clientId },
                // ValidateIssuer must be false when TenantId = "common".
                // Each user's token carries their own tenant-specific issuer URL.
                ValidateIssuer   = false,
                ValidateLifetime = true,
                NameClaimType    = "preferred_username",
            };
        });
}

// Default authorization policy accepts local Identity bearer OR Entra JWT.
var schemes = entraEnabled
    ? new[] { IdentityConstants.BearerScheme, EntraScheme }
    : new[] { IdentityConstants.BearerScheme };

// SuperAdmin authorization handler — checks role directly in DB via UserManager.
// This works for both local Identity (role claims) and Entra JWT (OID DB lookup),
// bypassing any claim-injection issues with IClaimsTransformation.
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    Ben.Data.WebApi.Authorization.SuperAdminHandler>();

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder(schemes)
        .RequireAuthenticatedUser()
        .Build();

    // Named "SuperAdmin" policy used by [Authorize(Policy = RoleNames.SuperAdmin)]
    // on all admin controllers. Delegates to SuperAdminHandler for DB-based role check.
    options.AddPolicy(RoleNames.SuperAdmin, policy =>
        policy
            .AddAuthenticationSchemes(schemes)
            .RequireAuthenticatedUser()
            .AddRequirements(new Ben.Data.WebApi.Authorization.SuperAdminRequirement()));

    // "EntraOnly" policy used by [Authorize(Policy = AuthPolicyNames.EntraOnly)] on
    // EntraAuthController's Register/Link actions — those need to read the caller's OID/email
    // from a *validated Entra JWT's own claims* rather than trusting the request body, so they
    // must pin the "Entra" scheme specifically rather than accepting the default multi-scheme
    // policy. Referencing the scheme by name only when it's actually registered (entraEnabled)
    // avoids the "no authentication handler registered for scheme 'Entra'" crash that pinning an
    // unregistered scheme name would cause; when Entra isn't configured, deny outright instead —
    // no Entra JWT could ever be presented in that environment anyway.
    options.AddPolicy(AuthPolicyNames.EntraOnly, policy =>
    {
        if (entraEnabled)
            policy.AddAuthenticationSchemes(EntraScheme).RequireAuthenticatedUser();
        else
            policy.RequireAssertion(_ => false);
    });
});

var app = builder.Build();

// Say what the CORS posture is at startup — a misconfigured deploy should fail loudly here, not
// as a mystery in a browser console three layers away.
if (corsOrigins.Length == 0)
    Log.Information("CORS: no cross-origin browser origins allowed (same-origin deployment)");
else
    Log.Information("CORS: allowing browser origins {Origins}", (object)corsOrigins);

// Initialise geocod.io — API key stored in Geocodio:ApiKey in appsettings
Ben.Service.RepositoryService.Services.AddressGeocodingService.Configure(
    builder.Configuration["Geocodio:ApiKey"] ?? string.Empty,
    builder.Configuration["Geocodio:BaseUrl"]);

app.UseExceptionHandler(handler =>
{
    handler.Run(async context =>
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        if (feature?.Error is not null)
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError(feature.Error,
                "Unhandled exception at {Path} — Source: WebApi", feature.Path);
        }
        context.Response.StatusCode  = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"error\":\"An unexpected error occurred.\"}");
    });
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseStaticFiles();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Ben API v1");
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("WebAppPolicy");
app.UseAuthentication();
app.UseAuthorization();
// After authentication, so the limiter can partition signed-in callers by user id rather than
// lumping everyone behind a shared address together.
app.UseRateLimiter();
app.MapControllers();

// Identity's endpoints are mapped by the framework, so the throttle goes on the whole group:
// /login is an unauthenticated password oracle and /register creates accounts.
app.MapIdentityApi<AppUser>().RequireRateLimiting(RateLimiting.AuthPolicy);

await Ben.Data.WebApi.SeedData.SuperAdminSeeder.SeedAsync(app.Services, app.Configuration);
await Ben.Data.WebApi.SeedData.OrganizationSeeder.SeedAsync(app.Services, app.Configuration);
await Ben.Data.WebApi.SeedData.UploadFileTypeSeeder.SeedAsync(app.Services, app.Configuration);
await Ben.Data.WebApi.SeedData.ExperienceTaxonomySeeder.SeedAsync(app.Services, app.Configuration);
await Ben.Data.WebApi.SeedData.ContactTypeSeeder.SeedAsync(app.Services, app.Configuration);
// DevelopmentDataSeeder runs last — depends on all users/orgs above being present.
// Enable via SeedData:DevData:Enabled = true in appsettings.Development.json.
await Ben.Data.WebApi.SeedData.DevelopmentDataSeeder.SeedAsync(app.Services, app.Configuration);

app.Run();

/// <summary>
/// Schema filter to exclude circular reference properties from Swagger schema
/// </summary>
internal class CircularReferenceSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema.Properties == null || schema.Properties.Count == 0)
            return;

        var propertiesToRemove = schema.Properties
            .Where(p =>
                p.Key.EndsWith("s") ||  // Collections (plural names like "UserAddresses")
                p.Key == "CreatedByAppUser" ||
                p.Key == "UpdatedByAppUser" ||
                p.Key.StartsWith("CreatedBy") ||
                p.Key.StartsWith("UpdatedBy"))
            .Select(p => p.Key)
            .ToList();

        foreach (var key in propertiesToRemove)
        {
            schema.Properties.Remove(key);
        }
    }
}
