using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using GrowIT.Backend.Controllers;
using GrowIT.Backend.Middleware;
using GrowIT.Backend.Services;
using GrowIT.Client;
using GrowIT.Client.Auth;
using GrowIT.Client.Services;
using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Infrastructure.Data.Interceptors;
using GrowIT.Infrastructure.Services;
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
    options.UseNpgsql(
            builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5433;Database=GrowIT;Username=postgres;Password=password")
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

var jwtKey = builder.Configuration["Jwt:Key"] ?? "ThisIsMySuperSecretKeyForGrowITLocalDevelopment123!";
const string CompositeAuthScheme = "GrowITCompositeAuth";
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

            return CookieAuthenticationDefaults.AuthenticationScheme;
        };
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
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
        options.AccessDeniedPath = "/login";
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
            ValidateIssuer = false,
            ValidateAudience = false
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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

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
            "font-src 'self' data: https://fonts.gstatic.com; " +
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
            "script-src 'self' 'unsafe-inline'; " +
            "connect-src 'self' ws: wss:;";
    }
}

public partial class Program;
