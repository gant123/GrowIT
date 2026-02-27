using GrowIT.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GrowIT.Infrastructure.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        var connectionString = Environment.GetEnvironmentVariable("GROWIT_CONNECTION_STRING")
            ?? "Host=localhost;Port=5433;Database=GrowIT;Username=postgres;Password=password";

        optionsBuilder.UseNpgsql(connectionString);
        return new ApplicationDbContext(optionsBuilder.Options, new DesignTimeTenantService());
    }

    private sealed class DesignTimeTenantService : ICurrentTenantService
    {
        public Guid? TenantId => null;
        public string? ConnectionString => null;
    }
}
