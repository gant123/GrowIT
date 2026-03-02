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

var defaultConnectionString = GetRequiredConnectionString(builder.Configuration, "DefaultConnection");
var jwtKey = GetRequiredConfigurationValue(builder.Configuration, "Jwt:Key");
var jwtIssuer = GetRequiredConfigurationValue(builder.Configuration, "Jwt:Issuer");
var jwtAudience = GetRequiredConfigurationValue(builder.Configuration, "Jwt:Audience");

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
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "growit.csrf";
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<ApiAuthorizationHandler>();

// Backend services now run in-process with the Blazor Web App host.
builder.Services.AddScoped<ICurrentTenantService, CurrentTenantService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
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
    options.AddPolicy("SuperAdminOnly", policy => policy.RequireAssertion(context =>
        HasAnyRole(context.User, "SuperAdmin", "Owner")));
    options.AddPolicy("AdminOnly", policy => policy.RequireAssertion(context =>
        HasAnyRole(context.User, "Admin", "Owner")));
    options.AddPolicy("AdminOrManager", policy => policy.RequireAssertion(context =>
        HasAnyRole(context.User, "Admin", "Manager", "Owner")));
    options.AddPolicy("ServiceWriter", policy => policy.RequireAssertion(context =>
        HasAnyRole(context.User, "Admin", "Manager", "Owner", "Case Manager")));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo { Title = "GrowIT API (Hosted)", Version = "v1" });
});

builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<IInvestmentService, InvestmentService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IImprintService, ImprintService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IGrowthPlanService, GrowthPlanService>();
builder.Services.AddScoped<IHouseholdService, HouseholdService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IFinancialService, FinancialService>();
builder.Services.AddScoped<IRoleAccessService, RoleAccessService>();
builder.Services.AddScoped<IFeedbackService, FeedbackService>();
builder.Services.AddScoped<IContentService, ContentService>();

builder.Services.AddHttpClient("GrowITApi", (sp, client) =>
    {
        var httpContext = sp.GetService<IHttpContextAccessor>()?.HttpContext;
        var baseUri = httpContext is not null
            ? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}/"
            : builder.Configuration["ClientUrl"]
                ?? (builder.Environment.IsDevelopment() ? "http://localhost:5245/" : "http://localhost/");

        client.BaseAddress = ResolveApiBaseAddress(builder.Configuration, builder.Environment, baseUri);
    })
    .AddHttpMessageHandler<ApiAuthorizationHandler>();

builder.Services.AddScoped(sp =>
{
    var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("GrowITApi");
    string? baseUri = null;

    if (string.IsNullOrWhiteSpace(baseUri))
    {
        var httpContext = sp.GetService<IHttpContextAccessor>()?.HttpContext;
        if (httpContext is not null)
        {
            baseUri = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}/";
        }
        else
        {
            baseUri = builder.Configuration["ClientUrl"]
                ?? (builder.Environment.IsDevelopment() ? "http://localhost:5245/" : "http://localhost/");
        }
    }

    client.BaseAddress = ResolveApiBaseAddress(builder.Configuration, builder.Environment, baseUri);
    return client;
});

builder.Services.AddSyncfusionBlazor();

var app = builder.Build();

if (args.Contains("--bootstrap-identity", StringComparer.OrdinalIgnoreCase))
{
    app.Logger.LogInformation("Running explicit Identity bootstrap.");
    await EnsureIdentityBootstrapAsync(app.Services);
    app.Logger.LogInformation("Identity bootstrap completed.");
    return;
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

        var desiredRole = string.IsNullOrWhiteSpace(user.Role) ? "Member" : user.Role.Trim();
        if (!await userManager.IsInRoleAsync(user, desiredRole))
        {
            var currentRoles = await userManager.GetRolesAsync(user);
            if (currentRoles.Count > 0)
            {
                var removeRolesResult = await userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!removeRolesResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to remove roles for '{user.Id}': {string.Join(" ", removeRolesResult.Errors.Select(e => e.Description))}");
                }
            }

            var addRoleResult = await userManager.AddToRoleAsync(user, desiredRole);
            if (!addRoleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to add role '{desiredRole}' for '{user.Id}': {string.Join(" ", addRoleResult.Errors.Select(e => e.Description))}");
            }
        }
    }
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

static Uri ResolveApiBaseAddress(IConfiguration config, IWebHostEnvironment env, string baseUri)
{
    static bool IsHttpScheme(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    var fallbackBase = config["ClientUrl"]
        ?? (env.IsDevelopment() ? "http://localhost:5245/" : "http://localhost/");

    if (!Uri.TryCreate(baseUri, UriKind.Absolute, out var candidateBase) || !IsHttpScheme(candidateBase))
    {
        baseUri = fallbackBase;
    }

    var configured = config["ApiBaseUrl"]?.Trim();
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

    return new Uri(fallbackBase);
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
