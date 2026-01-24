using System;
using GrowIT.Core.Interfaces;

namespace GrowIT.API.Services;

public class CurrentTenantService : ICurrentTenantService
{
    // The Tenant ID (using a placeholder for now)
    public Guid? TenantId => Guid.Empty;

    // The Missing Piece: We return null to tell the app 
    // "Use the default connection string from appsettings.json"
    public string? ConnectionString => null;
}