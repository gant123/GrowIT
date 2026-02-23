# GrowIT

GrowIT is a .NET 10 solution with:
- `src/GrowIT.API` (ASP.NET Core API)
- `src/GrowIT.Client` (Blazor WebAssembly client)
- `src/GrowIT.Infrastructure` (EF Core + PostgreSQL)
- `src/GrowIT.Shared` (shared DTOs/contracts)

## Requirements

- .NET SDK `10.x`
- PostgreSQL (connection string configured for `GrowIT.API`)

## Build (Repo Root)

```bash
dotnet build GrowIT.slnx
```

## Database Changes (Important)

When you make EF Core model changes, run commands from the repo root and always target the Infrastructure project with the API as the startup project.

### Apply migrations to the database

```bash
dotnet ef database update --project src/GrowIT.Infrastructure --startup-project src/GrowIT.API
```

### Add a new migration (recommended pattern)

```bash
dotnet ef migrations add <MigrationName> --project src/GrowIT.Infrastructure --startup-project src/GrowIT.API
```

Notes:
- Run the commands from the repository root (`GrowIT/`)
- `GrowIT.Infrastructure` contains the `DbContext` and migrations
- `GrowIT.API` provides the runtime configuration/startup services used by EF

## Tenant Hardening Reminder

- Avoid writing records with `Guid.Empty` tenant IDs
- Prefer tenant-filtered queries over `FindAsync(...)` in API controllers
- `FamilyMember` is tenant-scoped and should stay aligned with the parent `Client`
