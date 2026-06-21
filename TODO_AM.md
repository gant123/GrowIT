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

## Identity correctness

- [x] **Consolidated role storage to a single source of truth.** Removed the denormalized
      `User.Role` column (migration `RemoveUserRoleColumn`); ASP.NET Identity
      (`AspNetUserRoles`/`AspNetRoles`) is now authoritative everywhere. Claims factory and
      `TokenService` emit roles only from Identity.
- [ ] Apply the migration in each environment (`dotnet ef database update`) and run
      `--bootstrap-identity` afterward (ensures every user has ≥1 Identity role).

## Auth / permissions hardening

- [x] Route-level gating: `Routes.razor` now uses `AuthorizeRouteView` (defense in depth);
      `/super-admin/content` carries `[Authorize(Policy = "SuperAdminOnly")]`. Add the same
      attribute to other sensitive pages (Settings, BetaFeedbackAdmin) if redirect-on-deny is
      preferred over their current in-page messages.
- [x] Endpoint-by-endpoint policy audit — every write route now has an explicit policy.
      Closed gaps: Clients (create / add-member / edit-member / delete-member) and Households
      (create / add-member) writes now require `ServiceWriter` (were authenticated-only).
- [x] Expanded `403` integration tests (Clients/Households create + add-member). 18 passing.
- [ ] **Product decision:** should `Manager` keep user/org/invite management, or be operational-only?
- [ ] **Product decision:** is beta feedback meant to be per-tenant (current) or platform-wide to the founder/SuperAdmin?

## Operational / correctness

- [x] `seed-demo-data` now returns an honest `501 Not Implemented` instead of fake success
      (and uses `AdminOnly` so SuperAdmin is included). Implement real seeding when needed.
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

- [x] Nullable warnings — solution builds clean (0 warnings) as of this pass.
- [ ] Keep `docs/PERMISSIONS_MATRIX.md` in sync when policies change.

## Notes

- `GrowIT.Client` is the single host (UI + backend controllers/services).
- After code changes, hard-refresh the browser before UI review.
- If build output paths recurse, clean `bin/obj` before deeper debugging.
