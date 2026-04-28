# grow.IT

Plant the seed. Measure the growth. Build the future.

grow.IT is a founder survivability and funding-readiness platform for early-stage nonprofit leaders. It helps organizations document service delivery, measure real impact, track cost and capacity, and build the operational clarity needed to sustain and scale.

## Technology Stack

- **Framework:** .NET 10 (ASP.NET Core / Blazor Interactive Server)
- **Database:** PostgreSQL 16 (EF Core)
- **UI:** Blazor + Syncfusion + Bootstrap 5
- **Reporting:** QuestPDF
- **Testing:** xUnit (Integration) + Playwright (Smoke/E2E)
- **Containerization:** Docker + Docker Compose

## Project Structure

- `src/GrowIT.Client` — Blazor Web App host (UI + embedded backend controllers/services)
- `src/GrowIT.Backend` — Backend controllers/services/middleware/validators (loaded by `GrowIT.Client`)
- `src/GrowIT.Infrastructure` — EF Core + PostgreSQL + migrations
- `src/GrowIT.Core` — Domain entities and core interfaces
- `src/GrowIT.Shared` — Shared DTOs/contracts
- `tests/GrowIT.IntegrationTests` — Integration tests (tenant isolation + core flow coverage)
- `tests/Playwright` — Browser-based smoke tests
- `docs/` — Project documentation and runbooks
- `scripts/` — Automation and maintenance scripts

## Prerequisites

- [.NET SDK 10.x](https://dotnet.microsoft.com/download/dotnet/10.0)
- [PostgreSQL 16+](https://www.postgresql.org/)
- [Node.js 20+](https://nodejs.org/) (required for Playwright smoke tests)
- [Docker & Docker Compose](https://www.docker.com/) (optional, for containerized dev/prod)

## Quick Start (Local Development)

### 1. Build the solution

```bash
dotnet build GrowIT.slnx
```

### 2. Run the App (single host)

From repo root:

```bash
dotnet run --project src/GrowIT.Client
```

Default dev URLs:
- `http://localhost:5245`
- `https://localhost:7234`

### 3. Configure local secrets

Keep SMTP/API credentials out of git by using user-secrets for the web app host:

```bash
dotnet user-secrets --project src/GrowIT.Client set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5433;Database=GrowIT;Username=postgres;Password=your-password"
dotnet user-secrets --project src/GrowIT.Client set "Jwt:Key" "your-long-random-secret-key"
dotnet user-secrets --project src/GrowIT.Client set "Email:SmtpHost" "your-smtp-host"
dotnet user-secrets --project src/GrowIT.Client set "Email:SmtpUser" "your-user"
dotnet user-secrets --project src/GrowIT.Client set "Email:SmtpPass" "your-password"
```

## Database / EF Core Workflow

Run EF commands from the repository root:

- **Apply migrations:**
  ```bash
  dotnet ef database update --project src/GrowIT.Infrastructure --startup-project src/GrowIT.Client
  ```

- **Add a new migration:**
  ```bash
  dotnet ef migrations add <MigrationName> --project src/GrowIT.Infrastructure --startup-project src/GrowIT.Client
  ```

## Scripts & Automation

The repository includes several scripts for database maintenance and operations:

- **Backup/Restore:** See `scripts/db/`
  - `backup_postgres.sh`: Creates a DB dump.
  - `restore_postgres.sh`: Restores a DB dump.
  - `restore_drill.sh`: Validates backup integrity.
- **Cleanup:**
  ```bash
  # Clean build artifacts if recursion issues occur
  rm -rf src/**/bin src/**/obj tests/**/bin tests/**/obj
  ```

## Docker Compose (Beta)

The container stack includes:
- `db`: PostgreSQL 16
- `client`: ASP.NET Core / Blazor Web App host

### 1. Setup Environment
```bash
cp .env.docker.example .env
# Edit .env with your specific settings (POSTGRES_PASSWORD, JWT_KEY, etc.)
```

### 2. Start Containers
```bash
docker compose up -d --build
```
- App: `http://localhost:5180`
- Postgres: `localhost:5433`

## Environment Variables

Key configuration keys (can be set via `appsettings.json`, environment variables, or secrets):

| Key | Description | Default (Dev) |
|-----|-------------|---------------|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string | - |
| `Jwt:Key` | Secret key for JWT signing | - |
| `Jwt:Issuer` / `Jwt:Audience` | JWT metadata | `growit-local` / `growit-internal` |
| `ClientUrl` | Public URL of the application | `http://localhost:5245` |
| `SyncfusionLicense` | License key for Syncfusion components | - |
| `Email:SmtpHost` | SMTP server address | - |
| `Email:DevFileFallbackEnabled` | If true, writes emails to disk instead of sending | `true` |
| `Reports:Scheduler:Enabled` | Enables the background report runner | `true` |

## Testing

### Integration Tests
API and data isolation checks:
```bash
dotnet test tests/GrowIT.IntegrationTests/GrowIT.IntegrationTests.csproj -m:1 --disable-build-servers
```

### Browser Smoke Tests
Using Playwright:
```bash
cd tests/Playwright
npm install
npx playwright install chromium
npx playwright test --project=chromium
```

## Security & Tenancy

- **Multi-Tenancy:** Tenant-scoped entities implementation uses EF Core global query filters (`IMustHaveTenant`).
- **Authorization:** Authoritative policies are defined in the backend (`AdminOnly`, `AdminOrManager`, etc.).
- **Audit Logs:** System-wide audit logging for sensitive operations.
- **Reference:** See `docs/PERMISSIONS_MATRIX.md` for role details.

## Working Agreements

- Keep DTOs in `GrowIT.Shared` as the source of truth.
- Keep migrations in `GrowIT.Infrastructure`.
- Use typed client services (avoid raw `HttpClient` in Razor pages/components).
- Prefer tenant-safe validation in controllers for all cross-entity relationships.

## License

- **Framework/Code:** TODO: Define project license (e.g., MIT, Proprietary).
- **QuestPDF:** Community License.
- **Syncfusion:** Requires valid license key.
- **Bootstrap:** MIT.

## Documentation Index

- [Beta Readiness Checklist](BETA.md)
- [Database Backup/Restore Runbook](docs/DB_BACKUP_RESTORE_RUNBOOK.md)
- [Permissions Matrix](docs/PERMISSIONS_MATRIX.md)
- [Morning Task List](TODO_AM.md)
