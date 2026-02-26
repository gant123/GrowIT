# grow.IT Staging / Beta Release Checklist

## Configuration
- [ ] `ASPNETCORE_ENVIRONMENT=Production` (or staging-specific environment)
- [ ] `ConnectionStrings:DefaultConnection` set correctly
- [ ] `ClientUrl` points to deployed client URL
- [ ] `Cors:AllowedOrigins` includes deployed client URL only
- [ ] `Email` SMTP values set (non-placeholder)
- [ ] `Reports:Scheduler:Enabled=true`
- [ ] `Reports:Scheduler` poll interval reviewed (`PollSeconds`)

## Database
- [ ] Run migrations:
  - `dotnet ef database update --project src/GrowIT.Infrastructure --startup-project src/GrowIT.Client`
- [ ] Confirm `System Diagnostics -> Pending Migrations = 0`

## Storage / Files
- [ ] `wwwroot/uploads/profile-photos` path is writable on host
- [ ] Backup/retention plan documented for uploaded files (or cloud storage enabled)

## Security
- [ ] HTTPS enabled end-to-end
- [ ] JWT key set via secure secret management
- [ ] Swagger exposure reviewed for staging vs production
- [ ] Admin account rotation plan documented

## Validation
- [ ] Run smoke test checklist (`docs/BETA_SMOKE_TEST_CHECKLIST.md`)
- [ ] Validate invite acceptance with real inbox
- [ ] Validate scheduled report runner produces runs
- [ ] Validate report downloads (PDF/XLSX/CSV)

## Observability
- [ ] Logs captured centrally (or persisted on host)
- [ ] Error alerting path established (email/Slack/Sentry/etc.)
- [ ] Team knows where to review `Audit Trail` and `System Diagnostics`
