using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
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

        // 1. Identify Changed Entities (excluding AuditLogs themselves to prevent loops)
        var entries = context.ChangeTracker.Entries<object>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted)
            .Where(e => !(e.Entity is AuditLog)) 
            .ToList();

        var auditEntries = new List<AuditLog>();

        foreach (var entry in entries)
        {
            // 2. Determine Action
            var actionType = entry.State switch
            {
                EntityState.Added => "Create",
                EntityState.Modified => "Update",
                EntityState.Deleted => "Delete",
                _ => "Unknown"
            };

            // 3. Serialize Data (Snapshot)
            // Note: For 'Added', OldData is null. For 'Deleted', NewData is null.
            var oldValues = entry.State == EntityState.Modified || entry.State == EntityState.Deleted 
                ? JsonSerializer.Serialize(entry.OriginalValues.ToObject()) 
                : null;

            var newValues = entry.State == EntityState.Added || entry.State == EntityState.Modified 
                ? JsonSerializer.Serialize(entry.CurrentValues.ToObject()) 
                : null;

            // 4. Create Log Record
            var audit = new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId ?? Guid.Empty,
                UserId = (Guid)userId,
                ActionType = actionType,
                TableName = entry.Metadata.GetTableName() ?? entry.Entity.GetType().Name,
                RecordId = (Guid)GetPrimaryKey(entry), 
                PreviousData = oldValues,
                NewData = newValues,
                CreatedAt = DateTime.UtcNow
            };

            auditEntries.Add(audit);
        }

        // 5. Add Audit Logs to Context
        if (auditEntries.Any())
        {
            await context.Set<AuditLog>().AddRangeAsync(auditEntries, cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private Guid? GetPrimaryKey(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        // Simplistic assumption: PK is a single GUID named 'Id'
        // For production, you might want a more robust way to find the PK property
        var idProperty = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey());
        if (idProperty != null && idProperty.CurrentValue is Guid guidVal)
        {
            return guidVal;
        }
        return null; 
    }
}