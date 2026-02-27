# grow.IT Beta Readiness & Launch Gate

Use this file as the single go/no-go checklist before inviting beta testers.

## Release intent
- Beta is invite-only and focused on stability, workflow validation, and operational readiness.
- Priority is reliability over new feature velocity.

## Must-pass gate

### 1) Security baseline
- [ ] HTTPS enforced in deployed environment.
- [ ] Cookie auth uses secure flags in production.
- [ ] No active secrets in git history or tracked config.
- [ ] Rate limiting active on high-risk endpoints:
  - [ ] `POST /bff/auth/login`
  - [ ] `POST /api/content/contact`
- [ ] Unauthorized access attempts are logged and visible in super admin tooling.

### 2) Data safety
- [ ] Latest migrations applied successfully.
- [ ] Nightly backup job configured.
- [ ] Backup retention policy configured.
- [ ] Weekly restore drill completed (see `/Users/robertgant/workspace/GrowIT/docs/DB_BACKUP_RESTORE_RUNBOOK.md`).

### 3) Observability
- [ ] `/healthz` monitored.
- [ ] 5xx and startup failures alert to an operator channel.
- [ ] Team has documented first-response owner for incidents.

### 4) Test gate
- [ ] `dotnet build` succeeds.
- [ ] Integration tests pass (`tests/GrowIT.IntegrationTests`).
- [ ] Playwright smoke tests pass:
  - [ ] `/welcome` loads and demo CTA is visible.
  - [ ] anonymous access to `/` redirects to `/access-denied`.
  - [ ] `/blog` loads.
  - [ ] `/contact` submission succeeds.

### 5) Beta operations
- [ ] Support channel and response SLA are published to testers.
- [ ] Daily bug triage owner assigned.
- [ ] Rollback procedure reviewed by the release owner.
- [ ] Known limitations communicated to beta testers.

## Known limitations to communicate during beta
- Browser coverage is smoke-tested for Chromium in CI; manual Safari/Edge checks remain required.
- Feature surface may evolve during beta; data model changes are migration-driven.
- Scheduler and email delivery depend on environment configuration quality.

## Support and triage protocol
- Collect bug reports with:
  - reproduction steps
  - URL/page
  - timestamp (UTC)
  - screenshot/video
- Severity targets:
  - P0/P1: same-day mitigation
  - P2: next release window

## Rollback outline
1. Stop external traffic (maintenance mode / upstream route pause).
2. Roll back application container/image to prior known-good version.
3. Restore DB only if data integrity requires rollback.
4. Validate `/healthz`, login, and public routes.
5. Resume traffic and post incident summary.
