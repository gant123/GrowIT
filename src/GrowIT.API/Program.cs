using System.Security.Claims;
using System.Text;
using GrowIT.API.Middleware;
using GrowIT.API.Services;
using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Infrastructure.Data.Interceptors; // Added for AuditInterceptor
using GrowIT.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.FileProviders;
using QuestPDF.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;


// AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = LicenseType.Community;

// ==========================================
// 2. REGISTER SERVICES
// ==========================================

// A. Core Services & Accessors
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenantService, CurrentTenantService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>(); // NEW: Needed for Audit
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services.Configure<ReportSchedulerOptions>(builder.Configuration.GetSection("Reports:Scheduler"));
builder.Services.AddSingleton<ReportSchedulerState>();
builder.Services.AddHostedService<ScheduledReportRunnerService>();

// B. Register the Interceptor itself
builder.Services.AddScoped<AuditInterceptor>(); 

// C. Database (Updated to use the Interceptor)
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    // 1. Get the interceptor from the service provider
    var auditInterceptor = sp.GetRequiredService<AuditInterceptor>();
    
    // 2. Configure Npgsql and attach the interceptor
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection") 
                      ?? "Host=10.0.0.6;Port=5433;Database=GrowIT;Username=postgres;Password=password")
           .AddInterceptors(auditInterceptor);
});

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Docker/nginx reverse proxy addresses are dynamic in local networks.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// D. CORS (The Bridge for Blazor)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorOrigin", policy =>
    {
        var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        var origins = configuredOrigins is { Length: > 0 }
            ? configuredOrigins
            : new[] { "http://localhost:5245", "https://localhost:5001" };

        policy.WithOrigins(origins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("Content-Disposition");
    });
});

// E. Security (The Engine)
var jwtKey = builder.Configuration["Jwt:Key"] ?? "ThisIsMySuperSecretKeyForGrowITLocalDevelopment123!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
    options.AddPolicy("AdminOnly", policy => policy.RequireAssertion(context =>
        HasAnyRole(context.User, "Admin", "Owner")));
    options.AddPolicy("AdminOrManager", policy => policy.RequireAssertion(context =>
        HasAnyRole(context.User, "Admin", "Manager", "Owner")));
    options.AddPolicy("ServiceWriter", policy => policy.RequireAssertion(context =>
        HasAnyRole(context.User, "Admin", "Manager", "Owner", "Case Manager")));
});

// F. Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo { Title = "GrowIT API", Version = "v1" });
});

var app = builder.Build();

var webRootPath = app.Environment.WebRootPath;
if (string.IsNullOrWhiteSpace(webRootPath))
{
    webRootPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
}

Directory.CreateDirectory(webRootPath);
app.Environment.WebRootPath = webRootPath;
app.Environment.WebRootFileProvider = new PhysicalFileProvider(webRootPath);

// ==========================================
// 3. PIPELINE
// ==========================================
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
app.UseForwardedHeaders();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseCors("AllowBlazorOrigin"); 
app.UseAuthentication();          
app.UseAuthorization();           

app.MapControllers();
app.MapHealthChecks("/healthz");

app.Run();

public partial class Program
{
    internal static bool HasAnyRole(ClaimsPrincipal user, params string[] allowedRoles)
    {
        var allowed = new HashSet<string>(allowedRoles, StringComparer.OrdinalIgnoreCase);

        return user.Claims.Any(c =>
            (c.Type == ClaimTypes.Role || c.Type == "role") &&
            !string.IsNullOrWhiteSpace(c.Value) &&
            allowed.Contains(c.Value.Trim()));
    }
}
