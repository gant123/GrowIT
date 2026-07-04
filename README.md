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

By default the app does **not** migrate the database on startup, so for local dev the
database must exist and be migrated before the first run (follow these steps in order).
For container/beta deploys you can instead set `Database__AutoMigrate=true` to apply
migrations and bootstrap roles + the SuperAdmin automatically — see
[Beta / Production deployment](#beta--production-deployment).

### 1. Start PostgreSQL

Use the bundled container (exposes Postgres on host port `5433`):

```bash
docker compose up -d db
```

Or point at any local PostgreSQL 16 instance — just match the connection string below.

### 2. Configure local secrets

The host reads config from `src/GrowIT.Client/appsettings.Development.json` plus
environment variables / user-secrets. Defaults exist for local dev, but keep real
credentials out of git via user-secrets:

```bash
dotnet user-secrets --project src/GrowIT.Client set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5433;Database=GrowIT;Username=postgres;Password=your-password"
dotnet user-secrets --project src/GrowIT.Client set "Jwt:Key" "your-long-random-secret-key"
dotnet user-secrets --project src/GrowIT.Client set "SuperAdmin:Email" "you@example.com"
dotnet user-secrets --project src/GrowIT.Client set "Email:ResendApiKey" "re_your-resend-api-key"
dotnet user-secrets --project src/GrowIT.Client set "Email:FromEmail" "info@moemedia.cloud"
```

> `SuperAdmin:Email` designates the single platform SuperAdmin (see
> [Roles & Identity](#roles--identity)). A dev default is set in
> `appsettings.Development.json`.

### 3. Apply database migrations

```bash
dotnet ef database update --project src/GrowIT.Infrastructure --startup-project src/GrowIT.Client
```

### 4. Build and run

```bash
dotnet build GrowIT.slnx
dotnet run --project src/GrowIT.Client
```

Default dev URLs:
- `http://localhost:5245`
- `https://localhost:7234`

### 5. Create the SuperAdmin

1. Register an account through the UI using the same email as `SuperAdmin:Email`
   (registration creates a tenant + an `Admin` user and sends an email-confirmation link;
   in dev, confirmation emails are written to `src/GrowIT.Client/dev-emails/`).
2. Run the identity bootstrap to elevate that account and normalize Identity state:
   ```bash
   dotnet run --project src/GrowIT.Client -- --bootstrap-identity
   ```
3. Log out and back in so the new `SuperAdmin` role claim is issued.

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

- **Identity bootstrap** (idempotent — seeds roles, normalizes users, elevates the
  configured `SuperAdmin:Email`):
  ```bash
  dotnet run --project src/GrowIT.Client -- --bootstrap-identity
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

## Configuration & Environment Variables

Configuration is read from the `appsettings.*.json` files **and then overridden by environment
variables** (env vars always win). **Secrets are never committed** — the tracked
`appsettings.Production.json` holds only non-secret config with blank placeholders, and real
values are supplied per-environment via env vars (beta/prod) or `dotnet user-secrets` (local).

Use `:` for the config key, or `__` (double underscore) for the environment-variable form —
e.g. `Jwt:Key` ↔ `Jwt__Key`.

| Key | Description | Required for beta? | Default (Dev) |
|-----|-------------|--------------------|---------------|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string | ✅ Yes | local default |
| `Jwt:Key` | **Secret** — JWT signing key (use a long random value) | ✅ Yes | dev placeholder |
| `Jwt:Issuer` / `Jwt:Audience` | JWT metadata | — | `growit-local` / `growit-internal` |
| `SuperAdmin:Email` | Account elevated to SuperAdmin during identity bootstrap (blank = none) | ✅ Yes | set in `appsettings.Development.json` |
| `Database:AutoMigrate` | If true, apply migrations + bootstrap roles/SuperAdmin on startup | recommended | `false` |
| `ClientUrl` | Public URL of the application (used in email links + redirects) | ✅ Yes | `http://localhost:5245` |
| `Email:ResendApiKey` | **Secret** — Resend API key used for transactional email | ✅ Yes | blank |
| `Email:ResendBaseUrl` | Resend API base URL | — | `https://api.resend.com` |
| `Email:FromEmail` | From address (domain must have SPF/DKIM to avoid spam) | ✅ Yes | `dev@growit.local` |
| `Email:FromName` | Friendly sender name | — | `grow.IT` |
| `Email:DevFileFallbackEnabled` | If true, writes emails to disk instead of sending | — | `true` (dev) / `false` (prod) |
| `Stripe:SecretKey` | **Secret** — Stripe API key (paid plans require checkout once set) | for billing | blank |
| `Stripe:WebhookSecret` | **Secret** — Stripe webhook signing secret | for billing | blank |
| `Stripe:Plans:{Pro,Enterprise}:{Monthly,Yearly}PriceId` | Stripe price IDs per plan/interval | for billing | blank |
| `SyncfusionLicense` | License key for Syncfusion components | ✅ Yes | embedded |
| `Reports:Scheduler:Enabled` | Enables the background report runner | — | `true` |

> ⚠️ **Email is onboarding-critical.** Sign-in requires a confirmed email
> (`RequireConfirmedEmail = true`) and prod has no file fallback, so a tester **cannot log in
> until they receive and click a confirmation email**. Verify Resend delivery (and the from-domain's
> SPF/DKIM) before inviting users.

## Beta / Production deployment

The container stack (`docker compose up -d --build`) runs `db` + `client`. For a real beta:

1. **Set secrets via environment variables** (never in committed files). With Docker Compose,
   put these in `.env` (copied from `.env.docker.example`). Minimum set:
   ```bash
   ConnectionStrings__DefaultConnection="Host=...;Port=5432;Database=GrowIT;Username=...;Password=..."
   Jwt__Key="<long-random-secret>"          # generate e.g. with: openssl rand -base64 48
   SuperAdmin__Email="you@example.com"
   ClientUrl="https://your-beta-domain"
   Email__ResendApiKey="re_..."
   Email__FromEmail="info@moemedia.cloud"
   Database__AutoMigrate="true"             # apply migrations + bootstrap on startup
   # When enabling billing:
   Stripe__SecretKey="sk_..."
   Stripe__WebhookSecret="whsec_..."
   ```

2. **First boot** with `Database__AutoMigrate=true` applies pending migrations and seeds roles +
   promotes `SuperAdmin__Email` automatically. (Manual alternative: `dotnet ef database update`
   then `dotnet run --project src/GrowIT.Client -- --bootstrap-identity`.)

3. **Do not** run `--seed-demo` against beta — it creates fake demo organizations/clients.

4. **Verify before inviting testers:** register → confirm email → log in works, and an invite
   email is actually delivered (not spam-filtered).

> 🔐 **Rotate any previously-committed secrets.** Blanking a value in the file does not remove
> it from git history — rotate the Resend credential that was committed earlier and use a fresh
> `Jwt__Key` before going live.

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

- **Multi-Tenancy:** Tenant-scoped entities use EF Core global query filters (`IMustHaveTenant`). Globals (no tenant filter): `BlogPost`, `ContactSubmission`, `BetaFeedback`, `UnauthorizedAccessAttempt`, `UserSignInEvent`.
- **Authorization:** Authoritative policies are defined in `Program.cs` (`SuperAdminOnly`, `AdminOnly`, `AdminOrManager`, `ServiceWriter`) and enforced on backend controllers. Identity (ASP.NET Core) is the authority for roles/claims.
- **Audit Logs:** Tenant-scoped audit logging for sensitive operations (SuperAdmin sees all tenants).
- **Reference:** See `docs/PERMISSIONS_MATRIX.md` for the full role/capability matrix.

### Roles & Identity

Roles form a strict hierarchy. **SuperAdmin is a superset** — it satisfies every lower-tier policy, but only SuperAdmin satisfies `SuperAdminOnly` (platform/site-wide controls, blog & contact submissions, email/system diagnostics, cross-tenant audit/security logs).

| Role | Scope | Notes |
|------|-------|-------|
| `SuperAdmin` | Platform | Single account, set via `SuperAdmin:Email` + identity bootstrap. Not assignable through the UI/API by tenant admins. |
| `Owner` | Tenant | Full control of one organization. **Not** a platform admin. |
| `Admin` | Tenant | Organization administration (users, invites, org settings). |
| `Manager` | Tenant | Operational service workflow, reporting, and approvals. No user/org/invite/billing/platform controls. |
| `Case Manager` | Tenant | Service documentation and growth planning. |
| `Analyst` | Tenant | Read-heavy access. |
| `Member` | Tenant | Basic read-only workspace. |

Elevated roles (`SuperAdmin`, `Owner`) can only be granted by an existing SuperAdmin.

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
