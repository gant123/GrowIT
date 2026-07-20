using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using GrowIT.Backend.Controllers;
using GrowIT.Backend.Middleware;
using GrowIT.Backend.Services;
using GrowIT.Client;
using GrowIT.Client.Auth;
using GrowIT.Client.Services;
using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Infrastructure.Data.Interceptors;
using GrowIT.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using Syncfusion.Blazor;
using Syncfusion.Licensing;

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Preserve the existing client config model while moving to a server host.
builder.Configuration.AddJsonFile("wwwroot/appsettings.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile($"wwwroot/appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

// Re-apply environment variables LAST so they always win over the JSON files above.
// Secrets (Jwt__Key, Email__ResendApiKey, Stripe__SecretKey, SuperAdmin__Email, ...) are
// supplied per-environment via env vars / user-secrets and are never committed.
builder.Configuration.AddEnvironmentVariables();

var defaultConnectionString = GetRequiredConnectionString(builder.Configuration, "DefaultConnection");
var jwtKey = GetRequiredConfigurationValue(builder.Configuration, "Jwt:Key");
var jwtIssuer = GetRequiredConfigurationValue(builder.Configuration, "Jwt:Issuer");
var jwtAudience = GetRequiredConfigurationValue(builder.Configuration, "Jwt:Audience");
ValidateEmailDeliveryConfiguration(builder.Configuration, builder.Environment);

var syncfusionKey = builder.Configuration["SyncfusionLicense"];
if (!string.IsNullOrWhiteSpace(syncfusionKey))
{
    SyncfusionLicenseProvider.RegisterLicense(syncfusionKey);
}
else
{
    Console.WriteLine("WARNING: Syncfusion License Key not found in configuration.");
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AppNotificationService>();
builder.Services.AddScoped<BreadcrumbState>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "growit.csrf";
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<ApiAuthorizationHandler>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, GrowIT.Client.Infrastructure.CircuitExceptionHandlingHandler>();

// Backend services now run in-process with the Blazor Web App host.
builder.Services.AddScoped<ICurrentTenantService, CurrentTenantService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IPlanLimitService, PlanLimitService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<User>, GrowITUserClaimsPrincipalFactory>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services.Configure<ReportSchedulerOptions>(builder.Configuration.GetSection("Reports:Scheduler"));
builder.Services.AddSingleton<ReportSchedulerState>();
var schedulerEnabled = builder.Configuration.GetValue("Reports:Scheduler:Enabled", !builder.Environment.IsDevelopment());
if (schedulerEnabled)
{
    builder.Services.AddHostedService<ScheduledReportRunnerService>();
}
builder.Services.AddScoped<AuditInterceptor>();

builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    var auditInterceptor = sp.GetRequiredService<AuditInterceptor>();
    options.UseNpgsql(defaultConnectionString)
        .AddInterceptors(auditInterceptor);
});

builder.Services.AddControllers()
    .AddApplicationPart(typeof(AuthController).Assembly);
builder.Services.AddHealthChecks();
// Backs the per-email throttle on server-proxied auth endpoints. The IP-based
// "auth-submit" limiter is useless for /api/auth/* because those are called in-process
// over loopback (every request's RemoteIpAddress is ::1), so it collapses to one global
// bucket. Per-email throttling survives the loopback hop.
builder.Services.AddMemoryCache();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.Headers.TryAdd("Retry-After", "60");

        if (!context.HttpContext.Response.HasStarted)
        {
            await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Title = "Too Many Requests",
                Status = StatusCodes.Status429TooManyRequests,
                Detail = "Rate limit exceeded. Please retry in a minute."
            }, cancellationToken: token);
        }
    };

    options.AddPolicy("auth-submit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientIpPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            }));

    options.AddPolicy("contact-submit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientIpPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 6,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            }));
});

const string CompositeAuthScheme = "GrowITCompositeAuth";
builder.Services.AddIdentityCore<User>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.SignIn.RequireConfirmedAccount = true;
        options.SignIn.RequireConfirmedEmail = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddSignInManager<SignInManager<User>>()
    .AddRoleManager<RoleManager<IdentityRole<Guid>>>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();
builder.Services.AddScoped<IPasswordHasher<User>, LegacyCompatiblePasswordHasher>();
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(5);
});
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CompositeAuthScheme;
        options.DefaultAuthenticateScheme = CompositeAuthScheme;
        options.DefaultChallengeScheme = CompositeAuthScheme;
    })
    .AddPolicyScheme(CompositeAuthScheme, CompositeAuthScheme, options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authHeader = context.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authHeader) &&
                authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return JwtBearerDefaults.AuthenticationScheme;
            }

            // Keep API endpoints bearer-first to avoid opening all existing API writes to browser cookie CSRF.
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                return JwtBearerDefaults.AuthenticationScheme;
            }

            return IdentityConstants.ApplicationScheme;
        };
    })
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.Cookie.Name = "growit.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                if (IsApiOrBffRequest(context.Request))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                if (IsApiOrBffRequest(context.Request))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            }
        };
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    // SuperAdmin is a strict superset: it is implicitly allowed by every lower-tier
    // policy, but only SuperAdmin satisfies SuperAdminOnly (platform/site-wide controls).
    options.AddPolicy("SuperAdminOnly", policy => policy.RequireAssertion(context =>
        HasAnyRole(context.User, "SuperAdmin")));
    options.AddPolicy("AdminOnly", policy => policy.RequireAssertion(context =>
        HasAnyRole(context.User, "SuperAdmin", "Admin", "Owner")));
    options.AddPolicy("AdminOrManager", policy => policy.RequireAssertion(context =>
        HasAnyRole(context.User, "SuperAdmin", "Admin", "Manager", "Owner")));
    options.AddPolicy("ServiceWriter", policy => policy.RequireAssertion(context =>
        HasAnyRole(context.User, "SuperAdmin", "Admin", "Manager", "Owner", "Case Manager")));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo { Title = "GrowIT API (Hosted)", Version = "v1" });
});

builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<IInvestmentService, InvestmentService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IInsightsService, InsightsService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IImprintService, ImprintService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IReportManagementService, ReportManagementService>();
builder.Services.AddScoped<IAscScoreService, AscScoreService>();
builder.Services.AddScoped<IGrowthPlanService, GrowthPlanService>();
builder.Services.AddScoped<IHouseholdService, HouseholdService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IFinancialService, FinancialService>();
builder.Services.AddScoped<IRoleAccessService, RoleAccessService>();
builder.Services.AddScoped<IFeedbackService, FeedbackService>();
builder.Services.AddScoped<IContentService, ContentService>();
builder.Services.AddScoped<IBillingService, BillingService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IOrganizationReadinessService, OrganizationReadinessService>();

builder.Services.AddHttpClient("GrowITApi", (sp, client) =>
    {
        var httpContext = sp.GetService<IHttpContextAccessor>()?.HttpContext;
        var baseUri = httpContext is not null
            ? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}/"
            : builder.Configuration["InternalApiBaseUrl"]
                ?? builder.Configuration["ClientUrl"]
                ?? (builder.Environment.IsDevelopment() ? "http://localhost:5245/" : "http://localhost/");

        client.BaseAddress = ResolveApiBaseAddress(builder.Configuration, builder.Environment, baseUri);
    })
    .AddHttpMessageHandler<ApiAuthorizationHandler>();

// The named "GrowITApi" client already resolves its BaseAddress (and attaches auth)
// in the AddHttpClient configuration above.
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("GrowITApi"));

builder.Services.AddSyncfusionBlazor();

var cultureInfo = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

var app = builder.Build();

if (args.Contains("--bootstrap-identity", StringComparer.OrdinalIgnoreCase))
{
    app.Logger.LogInformation("Running explicit Identity bootstrap.");
    await EnsureIdentityBootstrapAsync(app.Services);
    app.Logger.LogInformation("Identity bootstrap completed.");
    return;
}

if (args.Contains("--seed-demo", StringComparer.OrdinalIgnoreCase))
{
    app.Logger.LogInformation("Seeding demo data...");
    using var seedScope = app.Services.CreateScope();
    var seeder = new DemoDataSeeder(
        seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
        seedScope.ServiceProvider.GetRequiredService<UserManager<User>>(),
        seedScope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
        seedScope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DemoDataSeeder"));
    await seeder.SeedAsync();
    app.Logger.LogInformation("Demo data seeding completed.");
    return;
}

// Opt-in startup database setup for container/beta deploys (off by default). When
// Database:AutoMigrate is true, apply pending EF migrations and ensure roles + the
// configured SuperAdmin exist — so a fresh environment is ready without remembering
// separate CLI steps. Leave it off where migrations are applied out-of-band.
if (app.Configuration.GetValue("Database:AutoMigrate", false))
{
    app.Logger.LogInformation("Database:AutoMigrate enabled — applying migrations and bootstrapping identity...");
    using (var migrateScope = app.Services.CreateScope())
    {
        var migrateDb = migrateScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await migrateDb.Database.MigrateAsync();
    }
    await EnsureIdentityBootstrapAsync(app.Services);
    app.Logger.LogInformation("Startup database setup complete.");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// API middleware (also handles controller exceptions when backend runs in this host).
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
app.UseForwardedHeaders();
var enforceContentSecurityPolicy = !app.Environment.IsDevelopment();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        ApplySecurityHeaders(context, enforceContentSecurityPolicy);
        return Task.CompletedTask;
    });

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/healthz");
app.MapGet("/robots.txt", (HttpContext context) =>
{
    var origin = GetOrigin(context);
    var content =
        "User-agent: *\n" +
        "Allow: /\n" +
        "Disallow: /api/\n" +
        "Disallow: /bff/\n" +
        "Disallow: /home\n" +
        "Disallow: /settings\n" +
        "Disallow: /reports\n" +
        "Disallow: /profile\n" +
        "Disallow: /notifications\n" +
        "Disallow: /clients\n" +
        "Disallow: /people\n" +
        "Disallow: /investments\n" +
        "Disallow: /imprints\n" +
        "Disallow: /households\n" +
        "Disallow: /growth-plans\n" +
        "Disallow: /insights\n" +
        $"Sitemap: {origin}/sitemap.xml\n";

    return Results.Text(content, "text/plain; charset=utf-8");
}).AllowAnonymous();

app.MapGet("/sitemap.xml", (HttpContext context) =>
{
    var origin = GetOrigin(context);
    var publicPaths = new[]
    {
        "/",
        "/demo",
        "/blog",
        "/contact",
        "/about",
        "/careers",
        "/partners",
        "/docs",
        "/webinars",
        "/templates",
        "/changelog",
        "/privacy",
        "/terms",
        "/security"
    };

    var now = DateTime.UtcNow.ToString("yyyy-MM-dd");
    var xml = new StringBuilder();
    xml.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
    xml.AppendLine("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");

    foreach (var path in publicPaths)
    {
        var loc = path == "/" ? $"{origin}/" : $"{origin}{path}";
        var priority = path == "/" ? "1.0" : "0.7";
        xml.AppendLine("  <url>");
        xml.AppendLine($"    <loc>{System.Security.SecurityElement.Escape(loc)}</loc>");
        xml.AppendLine($"    <lastmod>{now}</lastmod>");
        xml.AppendLine("    <changefreq>weekly</changefreq>");
        xml.AppendLine($"    <priority>{priority}</priority>");
        xml.AppendLine("  </url>");
    }

    xml.AppendLine("</urlset>");
    return Results.Text(xml.ToString(), "application/xml; charset=utf-8");
}).AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static async Task EnsureIdentityBootstrapAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("IdentityBootstrap");

    foreach (var roleName in new[] { "SuperAdmin", "Owner", "Admin", "Manager", "Case Manager", "Analyst", "Member" })
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var createRoleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            if (!createRoleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create role '{roleName}': {string.Join(" ", createRoleResult.Errors.Select(e => e.Description))}");
            }
        }
    }

    var userIds = await dbContext.Users
        .IgnoreQueryFilters()
        .Select(u => u.Id)
        .ToListAsync();

    foreach (var userId in userIds)
    {
        var user = await userManager.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null)
        {
            continue;
        }

        var changed = false;

        if (string.IsNullOrWhiteSpace(user.UserName))
        {
            user.UserName = user.Email;
            changed = true;
        }

        var normalizedEmail = (user.Email ?? user.UserName ?? string.Empty).ToUpperInvariant();
        if (!string.Equals(user.NormalizedEmail, normalizedEmail, StringComparison.Ordinal))
        {
            user.NormalizedEmail = normalizedEmail;
            changed = true;
        }

        var normalizedUserName = user.UserName?.ToUpperInvariant();
        if (!string.Equals(user.NormalizedUserName, normalizedUserName, StringComparison.Ordinal))
        {
            user.NormalizedUserName = normalizedUserName;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(user.SecurityStamp))
        {
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(user.ConcurrencyStamp))
        {
            user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            changed = true;
        }

        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            changed = true;
        }

        if (changed)
        {
            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to normalize Identity user '{user.Id}': {string.Join(" ", updateResult.Errors.Select(e => e.Description))}");
            }
        }

        // Identity is the single source of truth for roles. If this trips, the role
        // backfill migration did not preserve an existing user's role and deploy should stop.
        var currentRoles = await userManager.GetRolesAsync(user);
        if (currentRoles.Count == 0)
        {
            logger.LogCritical(
                "Identity user {UserId} ({Email}) has no ASP.NET Identity role after role backfill.",
                user.Id,
                user.Email);
            throw new InvalidOperationException(
                $"Identity bootstrap found role-less user '{user.Id}'. Verify the role backfill migration before continuing.");
        }
    }

    await EnsureConfiguredSuperAdminAsync(userManager, configuration, logger);
}

// Promotes the single configured SuperAdmin (SuperAdmin:Email) to the SuperAdmin role
// exclusively. SuperAdmin is a superset of all other roles, so this user does not also
// need Admin/Owner. No email is hardcoded here — it is read from configuration.
static async Task EnsureConfiguredSuperAdminAsync(
    UserManager<User> userManager,
    IConfiguration configuration,
    ILogger logger)
{
    const string SuperAdminRole = "SuperAdmin";
    var superAdminEmail = configuration["SuperAdmin:Email"]?.Trim();
    if (string.IsNullOrWhiteSpace(superAdminEmail))
    {
        logger.LogWarning("SuperAdmin:Email is not configured. No SuperAdmin was provisioned.");
        return;
    }

    var normalizedEmail = superAdminEmail.ToUpperInvariant();
    var superUser = await userManager.Users
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);

    if (superUser is null)
    {
        logger.LogWarning(
            "Configured SuperAdmin '{Email}' was not found. Register that account, then re-run the identity bootstrap.",
            superAdminEmail);
        return;
    }

    var currentRoles = await userManager.GetRolesAsync(superUser);
    var staleRoles = currentRoles
        .Where(r => !string.Equals(r, SuperAdminRole, StringComparison.OrdinalIgnoreCase))
        .ToList();
    if (staleRoles.Count > 0)
    {
        var removeResult = await userManager.RemoveFromRolesAsync(superUser, staleRoles);
        if (!removeResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to remove stale roles for SuperAdmin '{superUser.Id}': {string.Join(" ", removeResult.Errors.Select(e => e.Description))}");
        }
    }

    if (!await userManager.IsInRoleAsync(superUser, SuperAdminRole))
    {
        var addResult = await userManager.AddToRoleAsync(superUser, SuperAdminRole);
        if (!addResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to add SuperAdmin role for '{superUser.Id}': {string.Join(" ", addResult.Errors.Select(e => e.Description))}");
        }
    }

    await userManager.UpdateSecurityStampAsync(superUser);
    logger.LogInformation("Provisioned SuperAdmin for '{Email}'.", superAdminEmail);
}

static string GetRequiredConnectionString(IConfiguration configuration, string name)
{
    var value = configuration.GetConnectionString(name);
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"Connection string '{name}' is required. Configure it via appsettings, environment variables, or user secrets.");
    }

    return value;
}

static string GetRequiredConfigurationValue(IConfiguration configuration, string key)
{
    var value = configuration[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"Configuration value '{key}' is required. Configure it via appsettings, environment variables, or user secrets.");
    }

    return value;
}

// Fail loud instead of silent. In Production, EmailService has no fallback (the dev file
// fallback is gated to Development), so an unconfigured Resend key means every confirmation
// and password-reset email is silently dropped — registration "succeeds" but the account can
// never be confirmed. Refuse to boot so the misconfiguration is caught at deploy time, not by
// a stuck user. Set Email:RequireProviderInProduction=false to intentionally run without email.
static void ValidateEmailDeliveryConfiguration(IConfiguration configuration, IWebHostEnvironment environment)
{
    if (!environment.IsProduction())
    {
        return;
    }

    if (!configuration.GetValue("Email:RequireProviderInProduction", true))
    {
        return;
    }

    var apiKey = configuration["Email:ResendApiKey"]?.Trim();
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        apiKey = configuration["Resend:ApiKey"]?.Trim();
    }

    static bool IsPlaceholder(string value) =>
        value.Contains("YOUR_", StringComparison.OrdinalIgnoreCase)
        || value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
        || value.Contains("CHANGEME", StringComparison.OrdinalIgnoreCase)
        || value.Contains('<');

    if (string.IsNullOrWhiteSpace(apiKey) || IsPlaceholder(apiKey))
    {
        throw new InvalidOperationException(
            "Email delivery is not configured for Production. Set Email__ResendApiKey (and Email__FromEmail to a Resend-verified sender) so account-confirmation and password-reset emails are actually sent. " +
            "To run without email on purpose, set Email__RequireProviderInProduction=false.");
    }
}

static Uri ResolveApiBaseAddress(IConfiguration config, IWebHostEnvironment env, string baseUri)
{
    static bool IsHttpScheme(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    var fallbackBase = config["InternalApiBaseUrl"]
        ?? config["ClientUrl"]
        ?? (env.IsDevelopment() ? "http://localhost:5245/" : "http://localhost/");

    if (!Uri.TryCreate(baseUri, UriKind.Absolute, out var candidateBase) || !IsHttpScheme(candidateBase))
    {
        baseUri = fallbackBase;
    }

    var configured = config["InternalApiBaseUrl"]?.Trim();
    if (string.IsNullOrWhiteSpace(configured))
    {
        configured = config["ApiBaseUrl"]?.Trim();
    }
    if (!string.IsNullOrWhiteSpace(configured))
    {
        if (Uri.TryCreate(configured, UriKind.Absolute, out var absolute))
        {
            if (IsHttpScheme(absolute))
            {
                return absolute;
            }

            return new Uri(fallbackBase);
        }

        var combined = new Uri(new Uri(baseUri), configured);
        if (IsHttpScheme(combined))
        {
            return combined;
        }
    }

    return new Uri(baseUri);
}

static bool HasAnyRole(ClaimsPrincipal user, params string[] allowedRoles)
{
    var allowed = new HashSet<string>(allowedRoles, StringComparer.OrdinalIgnoreCase);

    return user.Claims.Any(c =>
        (c.Type == ClaimTypes.Role || c.Type == "role") &&
        !string.IsNullOrWhiteSpace(c.Value) &&
        allowed.Contains(c.Value.Trim()));
}

static bool IsApiOrBffRequest(HttpRequest request) =>
    request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
    request.Path.StartsWithSegments("/bff", StringComparison.OrdinalIgnoreCase);

static string GetClientIpPartitionKey(HttpContext context)
{
    var ip = context.Connection.RemoteIpAddress?.ToString();
    return string.IsNullOrWhiteSpace(ip) ? "unknown" : ip;
}

static string GetOrigin(HttpContext context)
{
    var scheme = context.Request.Scheme;
    var host = context.Request.Host.Value;
    var pathBase = context.Request.PathBase.Value?.TrimEnd('/') ?? string.Empty;
    return $"{scheme}://{host}{pathBase}";
}

static void ApplySecurityHeaders(HttpContext context, bool enforceContentSecurityPolicy)
{
    var headers = context.Response.Headers;

    headers.Remove("Server");
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
    headers["Cross-Origin-Opener-Policy"] = "same-origin";
    headers["Cross-Origin-Resource-Policy"] = "same-site";

    if (enforceContentSecurityPolicy &&
        !context.Request.Path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase))
    {
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "base-uri 'self'; " +
            "frame-ancestors 'none'; " +
            "form-action 'self'; " +
            "img-src 'self' data: https:; " +
            "font-src 'self' data: https://fonts.gstatic.com https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; " +
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; " +
            "script-src 'self' 'unsafe-inline'; " +
            "connect-src 'self' ws: wss:;";
    }
}

public partial class Program;
