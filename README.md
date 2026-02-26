# grow.IT

Plant the seed. Measure the growth. Build the future.

grow.IT is a founder survivability and funding-readiness platform for early-stage nonprofit leaders. It helps organizations document service delivery, measure real impact, track cost and capacity, and build the operational clarity needed to sustain and scale.

## What This Repository Contains

- `src/GrowIT.Client` — Blazor Web App host (UI + embedded backend controllers/services)
- `src/GrowIT.Backend` — backend controllers/services/middleware/validators (loaded by `GrowIT.Client`)
- `src/GrowIT.Infrastructure` — EF Core + PostgreSQL + migrations
- `src/GrowIT.Core` — domain entities and core interfaces
- `src/GrowIT.Shared` — shared DTOs/contracts
- `tests/GrowIT.IntegrationTests` — integration tests (tenant isolation + core flow coverage)
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

## 2. Run the App (single host)

From repo root:

```bash
dotnet run --project src/GrowIT.Client
```

Default dev URLs:

- `http://localhost:5245`
- `https://localhost:7234`

## 3. Configure local secrets (recommended)

Keep SMTP/API credentials out of git by using user-secrets for the web app host:

```bash
dotnet user-secrets --project src/GrowIT.Client set "Email:SmtpHost" "your-smtp-host"
dotnet user-secrets --project src/GrowIT.Client set "Email:SmtpUser" "your-user"
dotnet user-secrets --project src/GrowIT.Client set "Email:SmtpPass" "your-password"
```

## Database / EF Core Workflow (Important)

Run EF commands from the repository root and always target:

- `--project src/GrowIT.Infrastructure` (contains `DbContext` + migrations)
- `--startup-project src/GrowIT.Client` (single host provides runtime config/services)

## Apply migrations to the database

```bash
dotnet ef database update --project src/GrowIT.Infrastructure --startup-project src/GrowIT.Client
```

## Add a new migration

```bash
dotnet ef migrations add <MigrationName> --project src/GrowIT.Infrastructure --startup-project src/GrowIT.Client
```

## Docker Compose (Beta)

This repository includes a container stack for:

- `db` (PostgreSQL)
- `client` (ASP.NET Core / Blazor Web App host serving UI + backend in one process)

Files:

- `docker-compose.yml`
- `docker/client/Dockerfile`
- `.env.docker.example`

## 1. Create your Docker env file

```bash
cp .env.docker.example .env
```

Update at minimum:

- `POSTGRES_PASSWORD`
- `JWT_KEY`
- `CLIENT_URL` (set to `https://beta-growit.ganthome.cloud`)
- SMTP settings (`EMAIL_*`) for real invite delivery

## 2. Start the containers

```bash
docker compose up -d --build
```

Default container ports:

- App host: `http://localhost:5180`
- Postgres: `localhost:5433`

## 3. Apply EF migrations to the containerized database (from host)

Use your normal EF command, but point the connection string at the Docker Postgres port:

```bash
ConnectionStrings__DefaultConnection="Host=localhost;Port=5433;Database=GrowIT;Username=postgres;Password=YOUR_PASSWORD" \
dotnet ef database update --project src/GrowIT.Infrastructure --startup-project src/GrowIT.Client
```

## 4. Cloudflare Tunnel (`beta-growit.ganthome.cloud`)

Your Cloudflare tunnel is already set up, so just point the public hostname service target to:

- `http://localhost:5180`

That routes requests directly to the single `client` container (Blazor Web App host + backend).

## Testing

Run API integration tests:

```bash
dotnet test tests/GrowIT.IntegrationTests/GrowIT.IntegrationTests.csproj -m:1 --disable-build-servers
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

## Legacy API build path recursion (`bin/.../bin/...`)

If you hit long path/copy errors, clean output folders:

```bash
rm -rf src/**/bin src/**/obj tests/**/bin tests/**/obj
```

If you still have an older checkout with the separate API host, clean `bin/obj` and update to the single-host branch changes.

## “Upload succeeded but image is broken”

Checklist:

1. Restart the app host (`GrowIT.Client`) so static file providers reload
2. Hard refresh browser (`Cmd+Shift+R` / `Ctrl+F5`)
3. Re-upload once if the cached image URL is stale
4. Re-upload once after restart if needed

## Working Agreements (project-specific)

- Keep DTOs in `GrowIT.Shared` as the source of truth
- Keep migrations in `GrowIT.Infrastructure`
- Use typed client services (avoid raw `HttpClient` in Razor pages/components)
- Prefer tenant-safe validation in controllers for all cross-entity relationships

## Next Docs

- Permission matrix: `/Users/robertgant/workspace/GrowIT/docs/PERMISSIONS_MATRIX.md`
- Morning task list: `/Users/robertgant/workspace/GrowIT/TODO_AM.md`
