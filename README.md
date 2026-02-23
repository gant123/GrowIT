# grow.IT

Plant the seed. Measure the growth. Build the future.

grow.IT is a founder survivability and funding-readiness platform for early-stage nonprofit leaders. It helps organizations document service delivery, measure real impact, track cost and capacity, and build the operational clarity needed to sustain and scale.

## What This Repository Contains

- `src/GrowIT.API` — ASP.NET Core API (`.NET 10`)
- `src/GrowIT.Client` — Blazor WebAssembly frontend
- `src/GrowIT.Infrastructure` — EF Core + PostgreSQL + migrations
- `src/GrowIT.Core` — domain entities and core interfaces
- `src/GrowIT.Shared` — shared DTOs/contracts
- `tests/GrowIT.API.Tests` — integration tests (tenant isolation + core flow coverage)
- `docs/` — project docs (including permissions matrix)

## Current Product Scope (Implemented)

- Multi-tenant org/workspace model
- Client / household / family member management
- Funds + programs
- Investments (create, approve, disburse, delete, reassign)
- Imprints (impact milestones / outcomes)
- Growth plans (DB-backed)
- Reports (DB-backed schedules/history)
- Admin workspace:
  - organization settings
  - users + roles
  - invites + invite activity audit feed
  - audit log viewer
  - seed demo data
  - email diagnostics + test email
- Profile:
  - name/password updates
  - notification preferences
  - profile photo upload/remove

## Prerequisites

- .NET SDK `10.x`
- PostgreSQL
- Node is not required for the current Blazor workflow

## Quick Start (Local Development)

## 1. Build the solution

```bash
dotnet build GrowIT.slnx
```

## 2. Run the API

From repo root:

```bash
dotnet run --project src/GrowIT.API
```

Default dev API URL is typically:

- `http://localhost:5286`

## 3. Run the Client

From repo root:

```bash
dotnet run --project src/GrowIT.Client
```

Default dev client URLs:

- `http://localhost:5245`
- `https://localhost:7234`

## Dev Tip (important for profile images)

If your API is running on `http://localhost:5286`, use the client at `http://localhost:5245` while testing image uploads to avoid mixed-content/image loading issues.

## Database / EF Core Workflow (Important)

Run EF commands from the repository root and always target:

- `--project src/GrowIT.Infrastructure` (contains `DbContext` + migrations)
- `--startup-project src/GrowIT.API` (provides runtime config/services)

## Apply migrations to the database

```bash
dotnet ef database update --project src/GrowIT.Infrastructure --startup-project src/GrowIT.API
```

## Add a new migration

```bash
dotnet ef migrations add <MigrationName> --project src/GrowIT.Infrastructure --startup-project src/GrowIT.API
```

## Testing

Run API integration tests:

```bash
dotnet test tests/GrowIT.API.Tests/GrowIT.API.Tests.csproj -m:1 --disable-build-servers
```

Coverage currently includes:

- tenant isolation checks
- family member cross-tenant access checks
- investment + fund balance integrity
- imprint validation
- authorization policy `403` checks for restricted endpoints

## Security + Tenancy Notes

- Tenant-scoped entities are protected via query filters and controller validation.
- Avoid any writes using `Guid.Empty` tenant IDs.
- Prefer tenant-filtered queries (`FirstOrDefaultAsync`, `AnyAsync`) over `FindAsync(...)` in tenant-sensitive paths.
- Backend authorization policies are the source of truth (`AdminOnly`, `AdminOrManager`, `ServiceWriter`).
- Frontend role-based UI gating is implemented for major workflows, but backend policies remain authoritative.

See:

- `/Users/robertgant/workspace/GrowIT/docs/PERMISSIONS_MATRIX.md`

## Admin + Invite Operations

Settings now includes:

- user role management + deactivate/reactivate
- invite creation/resend/revoke
- invite activity notifications
- audit log grid
- demo data seeding
- email diagnostics + test email tool

If invite emails fail in Development:

- grow.IT can fall back to writing email HTML files locally (`dev-emails/`) depending on config.
- Use the Settings `Security` tab email diagnostics panel to confirm SMTP vs dev fallback behavior.

## Common Dev Troubleshooting

## API build path recursion (`bin/.../bin/...`)

If you hit long path/copy errors, clean output folders:

```bash
rm -rf src/**/bin src/**/obj tests/**/bin tests/**/obj
```

The API project is configured to exclude `bin/**` and `obj/**` from content items to prevent recursive copies.

## “Upload succeeded but image is broken”

Checklist:

1. Restart API (static file provider + `wwwroot` ready)
2. Hard refresh browser (`Cmd+Shift+R` / `Ctrl+F5`)
3. Use `http://localhost:5245` client if API is HTTP
4. Re-upload once after restart if needed

## Working Agreements (project-specific)

- Keep DTOs in `GrowIT.Shared` as the source of truth
- Keep migrations in `GrowIT.Infrastructure`
- Use typed client services (avoid raw `HttpClient` in Razor pages/components)
- Prefer tenant-safe validation in controllers for all cross-entity relationships

## Next Docs

- Permission matrix: `/Users/robertgant/workspace/GrowIT/docs/PERMISSIONS_MATRIX.md`
- Morning task list: `/Users/robertgant/workspace/GrowIT/TODO_AM.md`
