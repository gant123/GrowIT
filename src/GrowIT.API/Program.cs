using System.Text;
using GrowIT.API.Services;
using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;


var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 1. REGISTER SERVICES
// ==========================================

// A. Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Host=127.0.0.1;Port=5433;Database=GrowIT;Username=postgres;Password=password"));

// B. Core Services
builder.Services.AddScoped<ICurrentTenantService, CurrentTenantService>();
builder.Services.AddScoped<TokenService>(); 
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();

// C. CORS (The Bridge for Blazor)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorOrigin", policy =>
    {
        policy.WithOrigins("http://localhost:5245", "https://localhost:5001") 
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// D. Security (The Engine) - WE KEEP THIS!
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

// E. Swagger (Simplified - No Lock Button)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo { Title = "GrowIT API", Version = "v1" });
});

var app = builder.Build();

// ==========================================
// 2. PIPELINE
// ==========================================

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowBlazorOrigin"); // Bridge
app.UseAuthentication();          // Identity Check
app.UseAuthorization();           // Access Check

app.MapControllers();

app.Run();