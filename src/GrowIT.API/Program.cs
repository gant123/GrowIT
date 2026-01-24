using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using GrowIT.API.Services; 

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 1. REGISTER SERVICES
// ==========================================

// Enable Controllers (The API endpoints)
builder.Services.AddControllers();

// Enable Swagger (The Dashboard)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Enable HttpContext (To read headers/cookies)
builder.Services.AddHttpContextAccessor();

// Database Connection
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql("Host=127.0.0.1;Port=5433;Database=GrowIT;Username=postgres;Password=password"));

// Multi-Tenancy Service
builder.Services.AddScoped<ICurrentTenantService, CurrentTenantService>();
builder.Services.AddScoped<TokenService>();

var app = builder.Build();

// ==========================================
// 2. CONFIGURE PIPELINE
// ==========================================

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(); 
}

app.UseHttpsRedirection();

// Map the Controllers so the API works
app.MapControllers(); 

app.Run();