using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace GrowIT.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    private readonly ICurrentTenantService _tenantService;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentTenantService tenantService) : base(options)
    {
        _tenantService = tenantService;
    }

    // ========================================================================
    // 1. THE TABLES (19 Entities)
    // ========================================================================

    // Domain: SaaS & Business
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }
    public DbSet<Subscription> Subscriptions { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<Payment> Payments { get; set; }

    // Domain: Security & Users
    public DbSet<User> Users { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<UserRole> UserRoles { get; set; }

    // Domain: Case Management
    public DbSet<Household> Households { get; set; }
    public DbSet<Client> Clients { get; set; }
    public DbSet<Document> Documents { get; set; }
    public DbSet<AppTask> Tasks { get; set; } // Uses the 'AppTask' class we created
    public DbSet<Notification> Notifications { get; set; }

    // Domain: Financial Core & Impact
    public DbSet<Program> Programs { get; set; }
    public DbSet<Fund> Funds { get; set; }
    public DbSet<Investment> Investments { get; set; }
    public DbSet<Imprint> Imprints { get; set; }

    // Domain: Compliance
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<BillingEvent> BillingEvents { get; set; }

    // ========================================================================
    // 2. THE CONFIGURATION (The Rules)
    // ========================================================================
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // A. Global Query Filter (Multi-Tenancy Security)
        // -------------------------------------------------------------
        // Automatically finds any entity that implements IMustHaveTenant
        // and applies the ".Where(x => x.TenantId == CurrentTenant)" filter.
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (typeof(IMustHaveTenant).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(ApplicationDbContext)
                    .GetMethod(nameof(SetGlobalQueryFilter), BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.MakeGenericMethod(entityType.ClrType);

                method?.Invoke(this, new object[] { builder });
            }
        }

        // B. Money Precision (Financial Safety)
        // -------------------------------------------------------------
        // By default, decimal in SQL is (18,2), but it's safer to be explicit.
        // This finds every decimal property in your app and locks it.
        foreach (var property in builder.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetColumnType("decimal(18,2)");
        }
        
        // C. Enums as Strings (Readability)
        // -------------------------------------------------------------
        // Optional: Uncomment this if you want columns to say "Active" instead of "0"
        // builder.Entity<Subscription>().Property(s => s.Status).HasConversion<string>();
    }

    // Helper method for the reflection loop above
    private void SetGlobalQueryFilter<T>(ModelBuilder builder) where T : class, IMustHaveTenant
    {
        builder.Entity<T>().HasQueryFilter(e => e.TenantId == _tenantService.TenantId);
    }
}