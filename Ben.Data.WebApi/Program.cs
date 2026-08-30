using Ben.Data.Common;
using Microsoft.AspNetCore.HttpOverrides;
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

// Everything about levels and sinks now comes from configuration — nothing is pinned here.
//
// What was here before: a rolling file sink writing into the working tree (`.vscode/webapi-.log`,
// a repo-relative path that follows the code to wherever it is deployed) and three
// MinimumLevel.Override calls forcing the auth namespaces to Debug. Because those were code, no
// environment could turn them down: a measured 394 KB log was 654 lines of EF Core dumping every
// SQL statement with its parameters, plus ~400 lines of token-handler internals, on a machine
// nobody was even using. Auth debug output also carries token and claims detail, which is not
// something to write to disk by default.
//
// The Development config still turns those namespaces up — that is where the setting belongs, and
// where it is switched off by simply running in another environment.
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}  {Message:lj}{NewLine}{Exception}")
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

    // Schema ids come from the full type name, not the short one. Swashbuckle's default is the
    // short name, so two types that merely share a name in different namespaces collide and it
    // throws while generating — which took the whole of /swagger/v1/swagger.json to a 500 and left
    // the API docs page dead. There are exactly such a pair today: CaseReportSummary exists in
    // both Controllers and Controllers.Entities. Keying on the full name makes that structurally
    // impossible rather than something the next duplicated record name breaks again.
    options.CustomSchemaIds(type => type.FullName?.Replace('+', '.'));

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
// Separates a media file's metadata from the bytes that get served — see IMediaSanitizationService.
builder.Services.AddSingleton<Ben.Data.WebApi.Services.IMediaSanitizationService, Ben.Data.WebApi.Services.MediaSanitizationService>();
// The one place an uploaded media file is taken in — see IMediaIngestService.
builder.Services.AddSingleton<Ben.Data.WebApi.Services.IMediaIngestService, Ben.Data.WebApi.Services.MediaIngestService>();
// Author-written page markup is cleaned at the point it is stored, so what is in the database is
// what will be rendered — see ICmsMarkupSanitizer for why provenance alone is not enough.
builder.Services.AddSingleton<Ben.Data.WebApi.Services.ICmsMarkupSanitizer, Ben.Data.WebApi.Services.CmsMarkupSanitizer>();
// Scoped: it opens its own DbContext per call and holds no state between them.
builder.Services.AddScoped<Ben.Data.WebApi.Services.SiteSettingsService>();
// Stateless apart from its keys, so one instance serves every request.
builder.Services.AddSingleton<Ben.Data.WebApi.Services.SupportFormGuard>();
builder.Services.Configure<Ben.Data.WebApi.Services.SmtpOptions>(builder.Configuration.GetSection("Smtp"));
// What the site is called, in one place — see SiteIdentity for why it is not a literal.
builder.Services.Configure<Ben.Data.Common.SiteIdentity>(builder.Configuration.GetSection("SiteIdentity"));
builder.Services.AddSingleton<Ben.Data.Common.Interfaces.IEmailService, Ben.Data.WebApi.Services.SmtpEmailService>();
builder.Services.AddHostedService<Ben.Data.WebApi.Services.FileMigrationService>();

// ── @names ───────────────────────────────────────────────────────────────────
// Every account has one, and it is what makes an @mention in the feed resolve to exactly one
// person. The backfill service gives one to any account that predates the column and then does
// nothing on every subsequent start.
builder.Services.AddScoped<Ben.Data.WebApi.Services.UserHandleService>();

// Sign in with Apple. The validator holds a cached, self-refreshing copy of Apple's signing keys,
// so it is a singleton — one key fetch for the process, not one per sign-in.
builder.Services.AddHttpClient<Ben.Data.WebApi.Controllers.IAppleIdentityTokenValidator,
                               Ben.Data.WebApi.Controllers.AppleIdentityTokenValidator>();
builder.Services.AddHostedService<Ben.Data.WebApi.Services.UserHandleBackfillService>();
builder.Services.AddHostedService<Ben.Data.WebApi.Services.UserNameBackfillService>();

// ── Scheduled background work ────────────────────────────────────────────────
// Jobs are Scoped: the scheduler resolves them from a fresh scope on every pass, so they may take
// scoped dependencies exactly as a controller does, and nothing holds a database connection open
// between passes. Registering a job here is all it takes to have it run — see IScheduledJob.
builder.Services.AddScoped<Ben.Data.WebApi.Services.Scheduling.IScheduledJob,
                           Ben.Data.WebApi.Services.Scheduling.EventReminderJob>();
builder.Services.AddScoped<Ben.Data.WebApi.Services.Scheduling.IScheduledJob,
                           Ben.Data.WebApi.Services.Scheduling.TierChangeNoticeJob>();
builder.Services.AddScoped<Ben.Data.WebApi.Services.Scheduling.IScheduledJob,
                           Ben.Data.WebApi.Services.Scheduling.SubscriptionLapseJob>();
// Charges saved cards as periods run out. Registered BEFORE the lapse job in source as a hint of
// the intended order, though each pass runs every job regardless: renew first, lapse what
// renewal could not save.
builder.Services.AddScoped<Ben.Data.WebApi.Services.Scheduling.IScheduledJob,
                           Ben.Data.WebApi.Services.Billing.StripeIntegration.StripeRenewalJob>();
builder.Services.AddScoped<Ben.Data.WebApi.Services.PlatformMessageService>();
builder.Services.AddScoped<Ben.Data.WebApi.Services.RequestReviewNotifier>();
builder.Services.AddScoped<Ben.Data.WebApi.Services.OrganizationMergeService>();
builder.Services.AddScoped<Ben.Data.WebApi.Services.CasePrivacyRetrofit>();
// Deleting your own account — required by App Review 5.1.1(v). See AccountClosureService
// for why it anonymises rather than deletes, and why an organization's owner is refused.
builder.Services.AddScoped<Ben.Data.WebApi.Services.AccountClosureService>();
// Item 181: audio/video metadata stripping. No configured ffmpeg means the feature reports
// itself unavailable rather than failing an upload.
builder.Services.Configure<Ben.Data.WebApi.Services.MediaToolOptions>(
    builder.Configuration.GetSection("MediaTools"));
builder.Services.AddSingleton<Ben.Data.WebApi.Services.IAvMetadataStripper,
                              Ben.Data.WebApi.Services.AvMetadataStripper>();
builder.Services.AddScoped<Ben.Data.WebApi.Services.Billing.TierChangeNotifier>();
// Stripe: the card-charging arm of the billing engine. Gateway is a singleton (a thin client
// around config); fulfillment is scoped like every other database writer. With no SecretKey the
// gateway reports itself unconfigured and the checkout endpoint says so in a sentence.
builder.Services.Configure<Ben.Data.WebApi.Services.Billing.StripeIntegration.StripeOptions>(
    builder.Configuration.GetSection("Stripe"));
builder.Services.AddSingleton<Ben.Data.WebApi.Services.Billing.StripeIntegration.IStripeGateway,
                              Ben.Data.WebApi.Services.Billing.StripeIntegration.StripeGateway>();
builder.Services.AddScoped<Ben.Data.WebApi.Services.Billing.StripeIntegration.StripeFulfillmentService>();
builder.Services.AddScoped<Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard>();
builder.Services.AddScoped<Ben.Data.WebApi.Services.Billing.IncludedAreasResolver>();
builder.Services.AddHostedService<Ben.Data.WebApi.Services.Scheduling.ScheduledWorkService>();

builder.Services.AddAutoMapper(_ => { }, typeof(AppUserProfile).Assembly);
builder.Services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, Ben.Data.WebApi.Services.EntraClaimsTransformation>();

// Bearer tokens issued by MapIdentityApi are protected with Data Protection, so the key ring has
// to outlive the process — without this, every restart of this API silently invalidates every
// access and refresh token ever issued. See DataProtectionSetup for the full account.
builder.Services.AddBenDataProtection(builder.Configuration, Log.Logger);

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
       // Every password check funnels through this, which is the only place both outcomes pass —
       // /login is mapped by MapIdentityApi and has no action of ours to add a line to.
       .AddSignInManager<Ben.Data.WebApi.Services.RecordingSignInManager>()
       .AddDefaultTokenProviders();

// ── Microsoft Entra JWT bearer (optional — active only when ClientId is configured) ──
const string EntraScheme = AuthPolicyNames.EntraScheme;
var entraConfig = builder.Configuration.GetSection("AzureAd");
// Entra is on only when ClientId is a real registration id: GUID-shaped AND not one of the
// checked-in placeholders, which are themselves GUIDs. The shape test alone used to live here and
// let the placeholder through, standing this JWT handler up against an authority that cannot
// exist while the website - using a different rule - correctly stayed off. One rule now, in
// EntraConfig, shared by both hosts.
bool entraEnabled = EntraConfig.IsConfigured(entraConfig["ClientId"]);

if (entraEnabled)
{
    var clientId = entraConfig["ClientId"]!;
    var tenantId = EntraConfig.TenantOrCommon(entraConfig["TenantId"]);
    bool multiTenant = EntraConfig.IsMultiTenant(tenantId);

    builder.Services.AddAuthentication()
        .AddJwtBearer(EntraScheme, jwt =>
        {
            jwt.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = true,
                // Personal Microsoft accounts (MSA) issue tokens with audience = just the GUID.
                // Work/school accounts (AAD) issue tokens with audience = api://<clientId>.
                // Accept both formats so either account type works.
                ValidAudiences   = new[] { $"api://{clientId}", clientId },
                // False only on the multi-tenant authorities, where each user's token carries
                // their own tenant's issuer URL and there is nothing single to compare against.
                // Pointed at one tenant it must be true, or a token from any Microsoft tenant
                // anywhere clears every remaining check. ValidIssuer is left unset on purpose so
                // the value comes from the authority's discovery document, which is right whether
                // TenantId is configured as a GUID or as a domain.
                ValidateIssuer   = !multiTenant,
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
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    Ben.Data.WebApi.Authorization.AppAdministratorHandler>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    Ben.Data.WebApi.Authorization.ModeratorHandler>();

// Feed media screening (item 186 F5/F5b). The automatic ONNX classifier registers when its model
// file is present (fetched by scripts/get-screener-model.sh — 87 MB, deliberately not in git);
// without it the manual screener approves nothing by itself and routes every upload to the
// moderation queue — fail-closed, and honest about not being automatic. Which one is live is
// logged at startup below and shown on /admin/feed-reports via IsAutomatic.
var nsfwModelPresent = File.Exists(Path.Combine(
    builder.Environment.ContentRootPath, Ben.Data.WebApi.Services.Feed.OnnxNsfwScreener.ModelRelativePath));
if (nsfwModelPresent)
{
    builder.Services.AddSingleton<Ben.Data.WebApi.Services.Feed.IFeedMediaScreener,
        Ben.Data.WebApi.Services.Feed.OnnxNsfwScreener>();
}
else
{
    builder.Services.AddSingleton<Ben.Data.WebApi.Services.Feed.IFeedMediaScreener,
        Ben.Data.WebApi.Services.Feed.ManualReviewScreener>();
}
// The recovery path for anything stuck Pending: screener was down, ffmpeg missing, the process
// died mid-create, or the F4→F5b backlog. No-ops under the manual screener.
builder.Services.AddScoped<Ben.Data.WebApi.Services.Scheduling.IScheduledJob,
                           Ben.Data.WebApi.Services.Scheduling.PendingMediaScreeningJob>();
// The learning loop (item 186 F6): feature extraction + category-match scoring at post time,
// labelled examples from every human judgment, and the nightly re-fit that closes the loop.
builder.Services.AddScoped<Ben.Data.WebApi.Services.Feed.FeedLearningService>();
builder.Services.AddScoped<Ben.Data.WebApi.Services.Scheduling.IScheduledJob,
                           Ben.Data.WebApi.Services.Scheduling.WeightRefitJob>();

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder(schemes)
        .RequireAuthenticatedUser()
        .Build();

    // Named "SuperAdmin" policy used by [Authorize(Policy = RoleNames.SuperAdmin)]
    // on all admin controllers. Delegates to SuperAdminHandler for DB-based role check.
    //
    // ADMIN CONTROLLERS MUST USE THIS POLICY, NOT [Authorize(Roles = ...)]. The difference is not
    // stylistic. A bare Roles attribute names no authentication scheme, so it re-authenticates
    // with the DEFAULT scheme only - the local Identity bearer handler. A caller holding a valid
    // Entra JWT therefore comes back not as "authenticated but lacking the role" but as
    // unauthenticated, and the endpoint answers 401 rather than 403. Eight endpoints were written
    // that way and every one of them was closed to Entra sign-ins, including the dashboard's
    // /api/admin/stats. The policy avoids it by pinning the schemes explicitly below, which is
    // also what lets SuperAdminHandler resolve the role from the database by OID.
    options.AddPolicy(RoleNames.SuperAdmin, policy =>
        policy
            .AddAuthenticationSchemes(schemes)
            .RequireAuthenticatedUser()
            .AddRequirements(new Ben.Data.WebApi.Authorization.SuperAdminRequirement()));

    // The same arrangement for the endpoints that accept either app-wide role.
    options.AddPolicy(AuthPolicyNames.AppAdministrator, policy =>
        policy
            .AddAuthenticationSchemes(schemes)
            .RequireAuthenticatedUser()
            .AddRequirements(new Ben.Data.WebApi.Authorization.AppAdministratorRequirement()));

    // Moderation (item 186 F5): the Moderator role, or SuperAdmin implicitly.
    options.AddPolicy(AuthPolicyNames.Moderator, policy =>
        policy
            .AddAuthenticationSchemes(schemes)
            .RequireAuthenticatedUser()
            .AddRequirements(new Ben.Data.WebApi.Authorization.ModeratorRequirement()));

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

// Screening posture, said plainly in the log every start. A deploy that forgot the model file
// finds out here and on /admin/feed-reports — not from a moderator asking why the queue grew.
if (nsfwModelPresent)
    app.Logger.LogInformation(
        "Feed media screening is AUTOMATIC (ONNX model at {Path}).",
        Ben.Data.WebApi.Services.Feed.OnnxNsfwScreener.ModelRelativePath);
else
    app.Logger.LogWarning(
        "Feed media screening is MANUAL-ONLY: no model at {Path}. Every feed photo/video waits " +
        "for a moderator. Run scripts/get-screener-model.sh (or .ps1) and restart to enable " +
        "automatic screening.",
        Ben.Data.WebApi.Services.Feed.OnnxNsfwScreener.ModelRelativePath);

// ── Behind a reverse proxy ──────────────────────────────────────────────────
//
// Same reasoning as the website's: a proxy terminates TLS and forwards over plain HTTP, so
// without this the app sees IsHttps == false and UseHttpsRedirection below sends the caller to
// https://, which the proxy fetches, which loops. Runs before anything that reads the scheme.
//
// Trusted from loopback only, by ASP.NET Core's default, which is left alone on purpose — these
// headers are client-spoofable, and cloudflared connects from this machine. X-Forwarded-For also
// restores the real caller IP, which the audit log would otherwise record as the proxy for every
// request — worse here than on the website, since this is where security decisions are logged.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor,
});

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
        var logger  = context.RequestServices.GetRequiredService<ILogger<Program>>();

        // A stored file that is not on disk is a 404, not a fault. IFileStorageService.OpenReadAsync
        // throws FileNotFoundException, and none of its ~20 call sites catch it, so a row whose
        // bytes are missing answered 500 and logged a stack trace. That is wrong twice over: the
        // caller is told the server broke when the correct answer is "this is gone", and a routine
        // data gap fills the error log with noise that hides real faults. Mapped here rather than
        // at each call site for the same reason the log entry is written here - one place, and it
        // covers the next endpoint to serve a file as well.
        //
        // Logged at Warning, because it is worth knowing about: it means the database and the disk
        // disagree, which is a real condition even though it is not a crash.
        if (feature?.Error is FileNotFoundException or DirectoryNotFoundException)
        {
            logger.LogWarning(feature.Error,
                "Stored file missing at {Path} - the database row exists but the bytes do not", feature.Path);

            context.Response.StatusCode  = 404;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"That file is no longer available.\"}");
            return;
        }

        if (feature?.Error is not null)
        {
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

// Startup seeding talks to the database before the host begins listening, so an unreachable
// database means the process dies rather than starting in a broken state — which is the right
// behaviour for a real deployment and is left alone.
//
// It does make the app unbootable anywhere a database is deliberately absent. CI is the case in
// hand: it verifies the app can *start* (a class of failure no unit test sees — a malformed
// logging config once threw here while all 4,137 tests passed), and standing up SQL Server plus
// 73 migrations to prove that would be a lot of moving parts for the question being asked. So
// seeding can be switched off, following the flag the dev-data seeder already had.
if (app.Configuration.GetValue("SeedData:Enabled", true))
{
    await Ben.Data.WebApi.SeedData.SuperAdminSeeder.SeedAsync(app.Services, app.Configuration);
    await Ben.Data.WebApi.SeedData.OrganizationSeeder.SeedAsync(app.Services, app.Configuration);
    await Ben.Data.WebApi.SeedData.UploadFileTypeSeeder.SeedAsync(app.Services, app.Configuration);
    await Ben.Data.WebApi.SeedData.ExperienceTaxonomySeeder.SeedAsync(app.Services, app.Configuration);
    await Ben.Data.WebApi.SeedData.EquipmentTaxonomySeeder.SeedAsync(app.Services, app.Configuration);
    await Ben.Data.WebApi.SeedData.ContactTypeSeeder.SeedAsync(app.Services, app.Configuration);
    await Ben.Data.WebApi.SeedData.SubscriptionTierSeeder.SeedAsync(app.Services, app.Configuration);
    await Ben.Data.WebApi.SeedData.MemberLevelSeeder.SeedAsync(app.Services, app.Configuration);
    await Ben.Data.WebApi.SeedData.InvestigationDutySeeder.SeedAsync(app.Services, app.Configuration);
    await Ben.Data.WebApi.SeedData.OrgRoleSeeder.SeedAsync(app.Services, app.Configuration);
    // DevelopmentDataSeeder runs late — depends on all users/orgs above being present.
    // Enable via SeedData:DevData:Enabled = true in appsettings.Development.json.
    await Ben.Data.WebApi.SeedData.DevelopmentDataSeeder.SeedAsync(app.Services, app.Configuration);
    // ...and the roster seeder widens what it built: more people, the third group, more cases,
    // investigations and gear. Same flag, must run after DevelopmentDataSeeder.
    await Ben.Data.WebApi.SeedData.DevelopmentRosterSeeder.SeedAsync(app.Services, app.Configuration);
    // Last: needs the tiers, the groups and the past public event all to exist already.
    await Ben.Data.WebApi.SeedData.BillingDemoSeeder.SeedAsync(app.Services, app.Configuration);

    // ── The backfills run a SECOND time, and have to ─────────────────────────
    //
    // MemberLevelSeeder, InvestigationDutySeeder and OrgRoleSeeder above give every organization
    // its default ladder, duties and roles — every organization that exists WHEN THEY RUN. The
    // development and roster seeders below them then create more organizations, and those got
    // nothing: no title ladder, no duty board, no named roles.
    //
    // The first run cannot simply move later, because the roster seeder assigns those very roles
    // to the members it creates and needs them to exist already. So they run at both ends. All
    // three are backfills that skip any organization which already has the thing, so the second
    // pass is free for everything the first pass covered and is the only pass the late-created
    // groups ever get.
    //
    // Found 2026-08-27 by running the suite against a FRESH database: six tests failed with
    // "Role 'Case Manager Role' not found — the default-role seed is missing", and the same for
    // the ladder and the duty board. It never showed on a long-lived database because the NEXT
    // startup backfills what the previous one missed — so the bug was invisible to anybody whose
    // database had been started twice, which is everybody, which is why it survived this long.
    //
    // Only seeded organizations were ever affected: every real creation path
    // (OrganizationController, AdminOrganizationController, OrganizationSecurityService) adds all
    // three itself, so no group Ben has or will have is missing anything.
    await Ben.Data.WebApi.SeedData.MemberLevelSeeder.SeedAsync(app.Services, app.Configuration);
    await Ben.Data.WebApi.SeedData.InvestigationDutySeeder.SeedAsync(app.Services, app.Configuration);
    await Ben.Data.WebApi.SeedData.OrgRoleSeeder.SeedAsync(app.Services, app.Configuration);
}
else
{
    Log.Information("SeedData:Enabled is false — startup seeding skipped");
}

// ── Is the database actually current? ────────────────────────────────────────
//
// Nothing applies migrations at startup and nothing should: auto-migrating a live database on
// every deploy means an unreviewed schema change runs unattended, and several instances starting
// at once race each other. docs/deploy-production.md says to apply them by hand — which is
// correct, and is also a step that gets forgotten under pressure.
//
// So the app does not fix it, it SAYS it. A missing migration otherwise surfaces as "Invalid
// object name" from whichever feature happens to touch the new table first, which reads as a
// broken feature rather than an unapplied migration and sends somebody debugging the wrong thing.
// One line at startup names the real cause.
//
// Deliberately not fatal: refusing to start would turn a partly-degraded site into an outage, and
// most of the site works fine while one new table is missing.
try
{
    await using var schemaScope = app.Services.CreateAsyncScope();
    var schemaFactory = schemaScope.ServiceProvider
        .GetRequiredService<IDbContextFactory<Ben.Data.Source.Context.BenDataContext>>();
    await using var schemaCheck = await schemaFactory.CreateDbContextAsync();

    var pendingMigrations = (await schemaCheck.Database.GetPendingMigrationsAsync()).ToList();
    if (pendingMigrations.Count > 0)
    {
        Log.Warning(
            "DATABASE IS BEHIND: {Count} migration(s) have not been applied — {Names}. Features "
            + "using them will fail with \"Invalid object name\" until somebody runs: dotnet ef "
            + "database update --project Ben.Data.Source --startup-project Ben.Data.WebApi",
            pendingMigrations.Count, string.Join(", ", pendingMigrations));
    }
}
catch (Exception ex)
{
    // A check that cannot run must not stop the app: the database may simply be unreachable yet,
    // and that failure announces itself loudly enough elsewhere.
    Log.Warning(ex, "Could not check whether the database schema is current.");
}

app.Run();

/// <summary>
/// Drops entity navigation properties from generated schemas, so an entity that references another
/// entity does not drag the whole object graph — or a cycle — into the documentation.
/// </summary>
/// <remarks>
/// Decided by the property's TYPE, not the spelling of its name. The previous version removed
/// anything whose name ended in "s", meaning to catch plural collections like
/// <c>UserAddresses</c>; it also removed <c>Status</c>, <c>Address</c>, <c>Notes</c> and
/// <c>Radius</c>, which are ordinary scalars that happen to end in that letter. The documentation
/// therefore described an API subtly different from the real one. That went unnoticed because the
/// document did not generate at all until the duplicate-schema-id fix — nobody could read it to
/// see what was missing.
/// </remarks>
internal class CircularReferenceSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema.Properties is null || schema.Properties.Count == 0 || context.Type is null)
            return;

        // Match on the CLR properties of the type being described, case-insensitively: OpenAPI
        // keys are camelCased by the serializer while the CLR names are PascalCase.
        var clrProperties = context.Type
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .ToDictionary(p => p.Name, p => p.PropertyType, StringComparer.OrdinalIgnoreCase);

        var toRemove = schema.Properties.Keys
            .Where(key => clrProperties.TryGetValue(key, out var type) && IsEntityReference(type))
            .ToList();

        foreach (var key in toRemove)
            schema.Properties.Remove(key);
    }

    /// <summary>
    /// True for a property that points at another entity — either one directly, or a collection of
    /// them. Scalars, strings, enums, and collections of scalars are left alone.
    /// </summary>
    private static bool IsEntityReference(Type type)
    {
        if (type == typeof(string)) return false;

        var elementType = CollectionElementType(type) ?? type;
        elementType = Nullable.GetUnderlyingType(elementType) ?? elementType;

        if (elementType.IsPrimitive || elementType.IsEnum) return false;
        if (elementType == typeof(string) || elementType == typeof(decimal)
            || elementType == typeof(DateTime) || elementType == typeof(DateTimeOffset)
            || elementType == typeof(Guid) || elementType == typeof(TimeSpan)
            || elementType == typeof(byte[])) return false;

        // What's left is a class from the model — an entity or a nested record. Those are the
        // navigations this filter exists to cut.
        return elementType.IsClass || (elementType.IsValueType && !elementType.IsPrimitive
                                       && elementType.Namespace?.StartsWith("System") != true);
    }

    private static Type? CollectionElementType(Type type)
    {
        if (type.IsArray) return type.GetElementType();

        return type.GetInterfaces().Append(type)
            .FirstOrDefault(i => i.IsGenericType
                              && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }
}
