# grow.IT Permissions Matrix

This matrix documents the current role intent for UI visibility and backend API authorization.

## Roles

- `Owner`: Full organization control (same access as Admin, plus ownership semantics)
- `Admin`: Full operational/admin control
- `Manager`: Team operations, reporting, financial configuration, approvals
- `Case Manager`: Service documentation and growth planning, no approvals/admin workspace
- `Analyst`: Read-heavy access (report consumption, no write controls)
- `Member`: Basic workspace access with read-only experience on protected modules

## Core Capabilities

| Capability | Owner | Admin | Manager | Case Manager | Analyst | Member |
| --- | --- | --- | --- | --- | --- | --- |
| Dashboard / Clients / Households (view) | Yes | Yes | Yes | Yes | Yes | Yes |
| Investments (create / reassign) | Yes | Yes | Yes | Yes | No | No |
| Investments (approve / disburse / delete) | Yes | Yes | Yes | No | No | No |
| Imprints (create) | Yes | Yes | Yes | Yes | No | No |
| Growth Plans (create / update / delete) | Yes | Yes | Yes | Yes | No | No |
| Funds / Programs (configure) | Yes | Yes | Yes | No | No | No |
| Reports / Insights | Yes | Yes | Yes | No | No | No |
| Settings / Admin Workspace | Yes | Yes | Yes | No | No | No |
| Seed Demo Data | Yes | Yes | No | No | No | No |

## Backend Policies (source of truth)

- `AdminOnly`: `Owner`, `Admin`
- `AdminOrManager`: `Owner`, `Admin`, `Manager`
- `ServiceWriter`: `Owner`, `Admin`, `Manager`, `Case Manager`

## Frontend Notes

- The frontend uses `RoleAccessService` to hide or disable actions based on capabilities.
- Backend policy checks still enforce authorization even if a UI action is exposed accidentally.
- Role checks are case-insensitive and support legacy values (for example lowercase `admin`).

## Recommended Next Hardening

- Add endpoint-by-endpoint policy annotations audit (all write routes).
- Add a permissions admin page to preview effective access per role.
- Add integration tests for all policy-protected endpoints as coverage grows.
