# grow.IT Beta Smoke Test Checklist

Use this checklist before inviting external beta testers and after each deployment.

## 1. Environment & Health
- [ ] API starts without exceptions.
- [ ] Client loads and authenticates.
- [ ] `/healthz` returns healthy HTTP status (`200`).
- [ ] Public marketing routes load (`/welcome`, `/blog`, `/contact`).
- [ ] Anonymous visit to `/` redirects to `/access-denied` with styled UI.
- [ ] `Settings -> Security -> System Diagnostics` loads.
- [ ] `System Diagnostics` overall status is `Healthy` or only expected `Warning` items.
- [ ] `Pending Migrations` check shows `No pending migrations`.

## 2. Tenant / Security Basics
- [ ] Admin can log in.
- [ ] Invite flow works end-to-end (create invite -> accept invite -> new user can log in).
- [ ] Member role cannot access restricted pages/actions (`Settings`, report admin actions, financial config writes).
- [ ] Deactivated user cannot log in.

## 3. Core Product Flows
- [ ] Create client / household member.
- [ ] Create investment.
- [ ] Approve + disburse investment (admin/manager).
- [ ] Create imprint.
- [ ] Create/update growth plan.
- [ ] Dashboard loads (`/api/dashboard`) without 500 errors.

## 4. Reports (Critical for beta)
- [ ] Generate `PDF` report.
- [ ] Generate `Excel` (`.xlsx`) report.
- [ ] Generate `CSV` report.
- [ ] Download filenames are correct (not `.bin`).
- [ ] `Recent Reports` status column updates.
- [ ] `Details` drawer opens and shows run summary / request payload.
- [ ] Download same report multiple times and verify `Download History` entries increase.
- [ ] Scheduled report can be created and appears in `Scheduled Reports`.
- [ ] Wait for scheduler poll window and verify a scheduled run appears in `Recent Reports`.

## 5. Email / Notifications
- [ ] `Settings -> Security -> Email Delivery Diagnostics` loads.
- [ ] `Send Test Email` succeeds (or expected dev fallback file is written).
- [ ] Invite create/resend/revoke creates notifications in header bell and `/notifications`.

## 6. Public Demo Funnel
- [ ] `/welcome#demo` renders and `Request Demo` CTA navigates to `/contact?demo=1...`.
- [ ] Contact form submission creates a record visible in `Super Admin Content`.

## 7. Uploads / Branding
- [ ] Profile photo upload works.
- [ ] Profile photo displays in header/sidebar.
- [ ] Remove photo works.
- [ ] `grow.IT` wordmark looks correct in light and dark mode.

## 8. Data / Seed
- [ ] `Seed Demo Data` works (or confirms data already exists).
- [ ] Syncfusion grids render correctly (no raw IDs / broken pager/icons).

## 9. Browser Sanity
- [ ] Chrome (latest)
- [ ] Safari (latest)
- [ ] Edge (latest)
- [ ] Mobile viewport sanity check (header/sidebar/nav)

## Suggested Beta Test Accounts (via Invites)
- `Owner/Admin`: full access, can manage org and users.
- `Manager`: can operate reports and service approvals.
- `Case Manager`: can document services (investments/imprints/growth plans), no admin config.
- `Member`: read-only / limited access validation.

Create these through `Settings -> Invites` so invite and role flows are tested at the same time.
