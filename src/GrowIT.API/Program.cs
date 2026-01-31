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


// AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 2. REGISTER SERVICES
// ==========================================

// A. Core Services & Accessors
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenantService, CurrentTenantService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>(); // NEW: Needed for Audit
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<IEmailService, EmailService>();

// B. Register the Interceptor itself
builder.Services.AddScoped<AuditInterceptor>(); 

// C. Database (Updated to use the Interceptor)
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    // 1. Get the interceptor from the service provider
    var auditInterceptor = sp.GetRequiredService<AuditInterceptor>();
    
    // 2. Configure Npgsql and attach the interceptor
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection") 
                      ?? "Host=127.0.0.1;Port=5433;Database=GrowIT;Username=postgres;Password=password")
           .AddInterceptors(auditInterceptor);
});

builder.Services.AddControllers();

// D. CORS (The Bridge for Blazor)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorOrigin", policy =>
    {
        policy.WithOrigins("http://localhost:5245", "https://localhost:5001") 
              .AllowAnyMethod()
              .AllowAnyHeader();
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

// F. Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo { Title = "GrowIT API", Version = "v1" });
});

var app = builder.Build();

// ==========================================
// 3. PIPELINE
// ==========================================
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowBlazorOrigin"); 
app.UseAuthentication();          
app.UseAuthorization();           

app.MapControllers();

app.Run();