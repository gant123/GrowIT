# Morning TODO (Next Work Session)

## First 15 Minutes (stability checks)

- [ ] Pull latest changes and run:
  - `dotnet build GrowIT.slnx`
- [ ] Apply any pending migrations:
  - `dotnet ef database update --project src/GrowIT.Infrastructure --startup-project src/GrowIT.Client`
- [ ] Start app host + DB and confirm both boot cleanly
- [ ] Hard refresh browser cache before UI review

## QA Pass (high value)

- [ ] `Profile`:
  - [ ] upload photo
  - [ ] remove photo
  - [ ] confirm header/sidebar avatar updates
- [ ] `Settings -> Security`:
  - [ ] Email diagnostics loads
  - [ ] Test email sends (SMTP or dev file fallback)
  - [ ] Seed Demo Data works without FK errors
- [ ] `Settings -> Users & Roles`:
  - [ ] role edit save
  - [ ] deactivate/reactivate
- [ ] `Settings -> Invites`:
  - [ ] create / resend / revoke
  - [ ] accept invite flow
  - [ ] invite activity feed updates

## Role / Permissions Verification (manual)

- [ ] Test `Owner`
- [ ] Test `Admin`
- [ ] Test `Manager`
- [ ] Test `Case Manager`
- [ ] Test `Member`

Check these screens for correct access (visible/hidden actions + backend enforcement):

- [ ] `/settings`
- [ ] `/reports`
- [ ] `/funds`
- [ ] `/programs`
- [ ] `/investments` (create vs approve/disburse/delete)
- [ ] `/imprints`
- [ ] `/growth-plans`

## Tests

- [ ] Run integration tests:
  - `dotnet test tests/GrowIT.API.Tests/GrowIT.API.Tests.csproj -m:1 --disable-build-servers`
- [ ] Confirm new authorization tests pass (`403` checks)

## Next Engineering Work (recommended order)

1. **Upload productionization**
   - [ ] move file storage behind env-based config (Local/S3/Azure Blob)
   - [ ] add thumbnail/size variants (avatar + profile)
   - [ ] add file retention cleanup strategy

2. **Email delivery hardening**
   - [ ] add provider integration option (SendGrid/Postmark)
   - [ ] add retry/backoff + structured error logging
   - [ ] add “copy fallback file path” UX in Settings (dev mode)

3. **Permission coverage expansion**
   - [ ] audit every write endpoint for policy annotations
   - [ ] add more `403` integration tests (Financials/Admin edge cases)
   - [ ] document any role exceptions in `docs/PERMISSIONS_MATRIX.md`

4. **Admin experience polish**
   - [ ] user detail drawer (last seen / created / role history)
   - [ ] force password reset
   - [ ] invite filters/search in grid

## Cleanup / Nice-to-Have

- [ ] Address known nullable warnings (`FinancialsController`, some client Razor pages)
- [ ] Add a startup health check page (DB + migrations status + SMTP diagnostics)
- [ ] Add release checklist doc for staging/prod config

## Notes For Tomorrow

- `GrowIT.Client` is now the single host (UI + backend controllers/services)
- If anything looks weird in UI after code changes: hard refresh first
- If build output paths recurse again: clean `bin/obj` folders before debugging deeper
