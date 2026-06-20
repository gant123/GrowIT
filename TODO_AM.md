# grow.IT — Working TODO

_Last refreshed: 2026-06-20. Ordered by priority. Check the README "Quick Start" for setup commands._

## Recently shipped (PR: super-admin lockdown)

- [x] `SuperAdminOnly` narrowed to `SuperAdmin` only; SuperAdmin made a strict superset of lower policies.
- [x] Single SuperAdmin provisioned from `SuperAdmin:Email` during identity bootstrap.
- [x] Platform endpoints (email/system diagnostics, email test, site content) locked to SuperAdmin.
- [x] `audit-logs` / `security-attempts` tenant-scoped for tenant admins, platform-wide for SuperAdmin.
- [x] Privilege-escalation guards in `UpdateUserRole` / `CreateInvite`.
- [x] Integration tests for the new policy boundaries (14 passing).
- [x] Removed dead `Role` / `UserRole` entities and the unused `UserRole` enum.
- [x] README + permissions matrix updated; `.env.docker.example` added.

## First 15 minutes (stability checks)

- [ ] `dotnet build GrowIT.slnx`
- [ ] Start DB: `docker compose up -d db`
- [ ] Apply migrations: `dotnet ef database update --project src/GrowIT.Infrastructure --startup-project src/GrowIT.Client`
- [ ] Run bootstrap (provisions SuperAdmin): `dotnet run --project src/GrowIT.Client -- --bootstrap-identity`
- [ ] Start the host, log in as SuperAdmin, confirm `Site Content` + diagnostics are visible.

## Identity correctness (the big one)

- [ ] **Consolidate role storage to a single source of truth.** Today roles live in BOTH
      ASP.NET Identity (`AspNetUserRoles`/`AspNetRoles`) and the denormalized `User.Role`
      column, and `GrowITUserClaimsPrincipalFactory` emits both as claims. It works because
      every write path keeps them in sync, but it's fragile. Decide: make Identity authoritative
      and either drop `User.Role` or keep it as a read-only cached display value never used for
      authz. Touch points: `TokenService`, `GrowITUserClaimsPrincipalFactory`, `AdminController`,
      `AuthController`, bootstrap.
- [ ] Reconcile `User.Role` ↔ Identity roles in bootstrap as a one-time repair (detect drift).

## Auth / permissions hardening

- [ ] Route-level gating: switch `Routes.razor` to `AuthorizeRouteView` so protected pages enforce
      roles at the router (defense in depth; today only the API enforces).
- [ ] Endpoint-by-endpoint policy audit — confirm every write route has an explicit policy.
- [ ] Expand `403` integration tests (Financials, Investments approve/disburse, AdminContent).
- [ ] **Product decision:** should `Manager` keep user/org/invite management, or be operational-only?
- [ ] **Product decision:** is beta feedback meant to be per-tenant (current) or platform-wide to the founder/SuperAdmin?

## Operational / correctness

- [ ] `seed-demo-data` is currently a stub returning a success message — either implement real
      seeding or hide the Settings action so it isn't misleading.
- [ ] Decide how migrations + bootstrap run in deployed environments (no auto-migrate on startup
      today). Either add a guarded startup migrate, or document/script it for Docker.
- [ ] Add a startup health check page (DB connectivity + pending migrations + SMTP diagnostics).

## QA pass (manual, per role)

Test `SuperAdmin`, `Owner`, `Admin`, `Manager`, `Case Manager`, `Member` against:

- [ ] `/settings` (org, users & roles, invites, system health tab)
- [ ] `/super-admin/content` (SuperAdmin only)
- [ ] `/reports`, `/insights`
- [ ] `/funds`, `/programs`
- [ ] `/investments` (create vs approve/disburse/delete)
- [ ] `/imprints`, `/growth-plans`
- [ ] Profile photo upload/remove → header/sidebar avatar updates
- [ ] Invites: create / resend / revoke / accept + activity feed

## Next engineering work

1. **Upload productionization** — env-based storage (Local/S3/Azure Blob), thumbnail variants, retention cleanup.
2. **Email delivery hardening** — provider option (SendGrid/Postmark), retry/backoff, structured error logging.
3. **Admin experience** — user detail drawer (last seen / created / role history), force password reset, invite filters/search.

## Cleanup / nice-to-have

- [ ] Address remaining nullable warnings (`FinancialsController`, some Razor pages).
- [ ] Keep `docs/PERMISSIONS_MATRIX.md` in sync when policies change.

## Notes

- `GrowIT.Client` is the single host (UI + backend controllers/services).
- After code changes, hard-refresh the browser before UI review.
- If build output paths recurse, clean `bin/obj` before deeper debugging.
