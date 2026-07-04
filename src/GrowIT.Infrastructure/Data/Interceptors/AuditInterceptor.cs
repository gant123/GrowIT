using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace GrowIT.Infrastructure.Data.Interceptors;

public class AuditInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentTenantService _tenantService;

    public AuditInterceptor(ICurrentUserService currentUserService, ICurrentTenantService tenantService)
    {
        _currentUserService = currentUserService;
        _tenantService = tenantService;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, 
        InterceptionResult<int> result, 
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context == null) return await base.SavingChangesAsync(eventData, result, cancellationToken);

        var userId = _currentUserService.UserId;
        var tenantId = _tenantService.TenantId;

        var entries = context.ChangeTracker.Entries<object>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted)
            .Where(e => !(e.Entity is AuditLog)) 
            .ToList();

        var auditEntries = new List<AuditLog>();

        foreach (var entry in entries)
        {
            var actionType = entry.State switch
            {
                EntityState.Added => "Create",
                EntityState.Modified => "Update",
                EntityState.Deleted => "Delete",
                _ => "Unknown"
            };

            var oldValues = entry.State == EntityState.Modified || entry.State == EntityState.Deleted
                ? SerializeAuditValues(entry, useOriginalValues: true)
                : null;

            var newValues = entry.State == EntityState.Added || entry.State == EntityState.Modified
                ? SerializeAuditValues(entry, useOriginalValues: false)
                : null;

            var audit = new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId ?? TryGetEntityTenantId(entry.Entity) ?? Guid.Empty,
                UserId = userId ?? Guid.Empty,
                ActionType = actionType,
                TableName = entry.Metadata.GetTableName() ?? entry.Entity.GetType().Name,
                RecordId = GetPrimaryKey(entry) ?? Guid.Empty, 
                PreviousData = oldValues,
                NewData = newValues,
                CreatedAt = DateTime.UtcNow
            };

            auditEntries.Add(audit);
        }

        if (auditEntries.Any())
        {
            await context.Set<AuditLog>().AddRangeAsync(auditEntries, cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static string SerializeAuditValues(EntityEntry entry, bool useOriginalValues)
    {
        var values = new Dictionary<string, object?>();

        foreach (var property in entry.Properties)
        {
            if (entry.State == EntityState.Modified &&
                !property.IsModified &&
                !property.Metadata.IsPrimaryKey())
            {
                continue;
            }

            var name = property.Metadata.Name;
            values[name] = IsSensitiveProperty(name)
                ? "[REDACTED]"
                : useOriginalValues ? property.OriginalValue : property.CurrentValue;
        }

        return JsonSerializer.Serialize(values);
    }

    private static bool IsSensitiveProperty(string propertyName)
    {
        var name = propertyName.ToLowerInvariant();
        return name.Contains("password", StringComparison.Ordinal)
            || name.Contains("token", StringComparison.Ordinal)
            || name.Contains("secret", StringComparison.Ordinal)
            || name.Contains("ssn", StringComparison.Ordinal)
            || name.Contains("hash", StringComparison.Ordinal)
            || name.Contains("securitystamp", StringComparison.Ordinal)
            || name.Contains("apikey", StringComparison.Ordinal)
            || name.Contains("api_key", StringComparison.Ordinal)
            || name.Contains("stripe", StringComparison.Ordinal)
            || name.Contains("card", StringComparison.Ordinal);
    }

    private Guid? GetPrimaryKey(EntityEntry entry)
    {
        var idProperty = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey());
        if (idProperty != null && idProperty.CurrentValue is Guid guidVal)
        {
            return guidVal;
        }
        return null; 
    }

    private static Guid? TryGetEntityTenantId(object entity)
    {
        if (entity is IMustHaveTenant tenantEntity)
        {
            return tenantEntity.TenantId;
        }

        return null;
    }
}
