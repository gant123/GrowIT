# grow.IT Permissions Matrix

This matrix documents the current role intent for UI visibility and backend API authorization.

## Roles

- `SuperAdmin`: **Platform** administrator. Strict superset of all roles. Single account, provisioned via `SuperAdmin:Email` + identity bootstrap. Exclusive access to platform/site-wide controls. Not assignable through the UI/API by tenant admins.
- `Owner`: Full control of one organization (tenant). **Not** a platform admin.
- `Admin`: Full operational/admin control within a tenant.
- `Manager`: Team operations, reporting, financial configuration, approvals.
- `Case Manager`: Service documentation and growth planning, no approvals/admin workspace.
- `Analyst`: Read-heavy access (report consumption, no write controls).
- `Member`: Basic workspace access with read-only experience on protected modules.

> **SuperAdmin is a superset:** it implicitly passes every lower-tier capability below, but only SuperAdmin passes `SuperAdminOnly`.

## Core Capabilities

| Capability | SuperAdmin | Owner | Admin | Manager | Case Manager | Analyst | Member |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Dashboard / Clients / Households (view) | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| Case files / Households (create / edit / members) | Yes | Yes | Yes | Yes | Yes | No | No |
| Investments (create / reassign) | Yes | Yes | Yes | Yes | Yes | No | No |
| Investments (approve / disburse / delete) | Yes | Yes | Yes | Yes | No | No | No |
| Imprints (create) | Yes | Yes | Yes | Yes | Yes | No | No |
| Growth Plans (create / update / delete) | Yes | Yes | Yes | Yes | Yes | No | No |
| Funds / Programs (configure) | Yes | Yes | Yes | Yes | No | No | No |
| Reports / Insights | Yes | Yes | Yes | Yes | No | No | No |
| Settings / Admin Workspace (org, users, invites, audit) | Yes | Yes | Yes | Yes | No | No | No |
| Security attempts / Audit logs | Platform-wide | Own tenant | Own tenant | Own tenant | No | No | No |
| Seed Demo Data | Yes | Yes | Yes | No | No | No | No |
| **Site Content (blog / contact submissions)** | Yes | No | No | No | No | No | No |
| **Email & System Diagnostics, Email Test** | Yes | No | No | No | No | No | No |
| **Assign elevated roles (SuperAdmin / Owner)** | Yes | No | No | No | No | No | No |

## Backend Policies (source of truth)

Defined in `src/GrowIT.Client/Program.cs`. SuperAdmin is included as a superset in every policy.

- `SuperAdminOnly`: `SuperAdmin`
- `AdminOnly`: `SuperAdmin`, `Admin`, `Owner`
- `AdminOrManager`: `SuperAdmin`, `Admin`, `Manager`, `Owner`
- `ServiceWriter`: `SuperAdmin`, `Admin`, `Manager`, `Owner`, `Case Manager`

`SuperAdminOnly` is applied to `AdminContentController` (blog + contact submissions) and to the
`email-diagnostics`, `email-test`, and `system-diagnostics` endpoints on `AdminController`.
`audit-logs` and `security-attempts` stay on `AdminOrManager` but are tenant-scoped for tenant
admins and platform-wide only for SuperAdmin.

## Frontend Notes

- The frontend uses `RoleAccessService` to hide or disable actions based on capabilities (SuperAdmin is a superset there too).
- Backend policy checks still enforce authorization even if a UI action is exposed accidentally.
- Role checks are case-insensitive and read both `ClaimTypes.Role` and the `role` claim type.

## Identity / Role Storage

- ASP.NET Core Identity (`AspNetRoles` / `AspNetUserRoles`) is the authority for authorization.
- `User.Role` is a denormalized convenience column kept in sync at every write point
  (registration, invite acceptance, `UpdateUserRole`, bootstrap). Treat Identity as the source
  of truth; see the backlog item to consolidate to a single source.

## Recommended Next Hardening

- ~~Consolidate role storage to a single source of truth~~ — done (Identity only).
- ~~Endpoint-by-endpoint policy audit (all write routes)~~ — done; service-data writes
  on Clients/Households now require `ServiceWriter` (previously only authenticated).
- ~~Route-level role gating (`AuthorizeRouteView`)~~ — done (defense in depth).
- Add a permissions admin page to preview effective access per role.
