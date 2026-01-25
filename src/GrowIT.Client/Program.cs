using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using GrowIT.Client;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using GrowIT.Client.Auth;
using Syncfusion.Blazor;
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// 1. Storage: Lets us save the token in the browser
builder.Services.AddBlazoredLocalStorage();

// 2. Auth Core: Enables the <AuthorizeView> tags
builder.Services.AddAuthorizationCore();

// 3. The Security Guard: Uses our Custom provider to check the token
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();

// 4. API Connection: Points specifically to your Backend API
//    Make sure this matches the port your API is running on (likely 5286 or 5000)
builder.Services.AddScoped(sp => new HttpClient 
{ 
    BaseAddress = new Uri("http://localhost:5286") 
});
builder.Services.AddSyncfusionBlazor();
await builder.Build().RunAsync();