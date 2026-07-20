using GrowIT.Infrastructure.Data;
using GrowIT.Infrastructure.Data.Interceptors;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GrowIT.Backend.Tests.Infrastructure;

public sealed class GrowItApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"growit-tests-{Guid.NewGuid():N}";
    private readonly Dictionary<string, string?> _configOverrides;
    private readonly string _environment;

    public GrowItApiFactory(IDictionary<string, string?>? configOverrides = null, string environment = "Development")
    {
        _environment = environment;
        _configOverrides = configOverrides is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(configOverrides);

        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Host=localhost;Port=5432;Database=growit-tests;Username=test;Password=test");
        Environment.SetEnvironmentVariable("Jwt__Key", "integration-test-signing-key-fixed-nonplaceholder-secret-value");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "growit-local");
        Environment.SetEnvironmentVariable("Jwt__Audience", "growit-internal");
        Environment.SetEnvironmentVariable("ClientUrl", "https://localhost");
        Environment.SetEnvironmentVariable("Reports__Scheduler__Enabled", "false");
        // Boot-time validation (Program.cs, before builder.Build()) only sees env vars, not the
        // in-memory config the factory adds later — so Production-environment tests (CSP) need this
        // as an env var to skip the prod email-provider requirement. Tests don't send email.
        Environment.SetEnvironmentVariable("Email__RequireProviderInProduction", "false");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            // Pin Stripe to "not configured" by default so billing tests are deterministic
            // regardless of any user-secrets/env on the host. Tests that need the
            // Stripe-configured path pass overrides via the constructor.
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=growit-tests;Username=test;Password=test",
                ["Jwt:Key"] = "integration-test-signing-key-fixed-nonplaceholder-secret-value",
                ["Jwt:Issuer"] = "growit-local",
                ["Jwt:Audience"] = "growit-internal",
                ["ClientUrl"] = "https://localhost",
                ["Reports:Scheduler:Enabled"] = "false",
                ["Stripe:SecretKey"] = "",
                ["Stripe:WebhookSecret"] = "",
                // Tests that boot in the Production environment (e.g. CSP header tests) don't send
                // email, so opt out of the prod email-provider requirement rather than configure Resend.
                ["Email:RequireProviderInProduction"] = "false"
            };

            foreach (var kvp in _configOverrides)
            {
                settings[kvp.Key] = kvp.Value;
            }

            configBuilder.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            // Remove the app's Npgsql registration so the test host only has one EF provider.
            for (var i = services.Count - 1; i >= 0; i--)
            {
                var descriptor = services[i];
                var serviceAssembly = descriptor.ServiceType.Assembly.GetName().Name ?? string.Empty;
                var implAssembly = descriptor.ImplementationType?.Assembly.GetName().Name ?? string.Empty;

                if (serviceAssembly.Contains("Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal) ||
                    implAssembly.Contains("Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
                {
                    services.RemoveAt(i);
                }
            }

            services.RemoveAll(typeof(IDbContextOptionsConfiguration<ApplicationDbContext>));
            services.RemoveAll(typeof(DbContextOptions));
            services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
            services.RemoveAll<ApplicationDbContext>();

            services.AddDbContext<ApplicationDbContext>((sp, options) =>
            {
                options.UseInMemoryDatabase(_dbName)
                    .AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
            });

            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                    options.DefaultScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();
        });
    }

    public HttpClient CreateTenantClient(Guid tenantId, Guid? userId = null, string role = "Admin")
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, (userId ?? Guid.NewGuid()).ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        return client;
    }

    public async Task SeedAsync(Func<ApplicationDbContext, Task> seed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await seed(db);
        await db.SaveChangesAsync();
    }
}
