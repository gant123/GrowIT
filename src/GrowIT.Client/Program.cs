using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using GrowIT.Client;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using GrowIT.Client.Auth;
using Syncfusion.Blazor;
using Syncfusion.Licensing;
using GrowIT.Client.Services;
var builder = WebAssemblyHostBuilder.CreateDefault(args);
var config = builder.Configuration;
var syncfusionKey = config["SyncfusionLicense"];

if (!string.IsNullOrEmpty(syncfusionKey))
{
    SyncfusionLicenseProvider.RegisterLicense(syncfusionKey);
}
else
{
    Console.WriteLine("WARNING: Syncfusion License Key not found in appsettings.json");
}
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// 1. Storage: Lets us save the token in the browser
builder.Services.AddBlazoredLocalStorage();

// 2. Auth Core: Enables the <AuthorizeView> tags
builder.Services.AddAuthorizationCore();

// 3. The Security Guard: Uses our Custom provider to check the token
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
builder.Services.AddScoped<IClientService, ClientService>(); // New!
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
// 4. API Connection: Points specifically to your Backend API
//    Make sure this matches the port your API is running on (likely 5286 or 5000)
var apiBaseAddress = ResolveApiBaseAddress(builder);
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = apiBaseAddress
});

builder.Services.AddSyncfusionBlazor();
await builder.Build().RunAsync();

static Uri ResolveApiBaseAddress(WebAssemblyHostBuilder builder)
{
    var configured = builder.Configuration["ApiBaseUrl"]?.Trim();
    if (!string.IsNullOrWhiteSpace(configured))
    {
        if (Uri.TryCreate(configured, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        return new Uri(new Uri(builder.HostEnvironment.BaseAddress), configured);
    }

    // Local dev fallback when running the WASM dev server without a reverse proxy.
    if (builder.HostEnvironment.BaseAddress.Contains("localhost:5245", StringComparison.OrdinalIgnoreCase))
    {
        return new Uri("http://localhost:5286");
    }

    // Container/proxy fallback: same-origin (/api/* proxied by nginx).
    return new Uri(builder.HostEnvironment.BaseAddress);
}
