# grow.IT Database Backup & Restore Runbook

How to back up, restore and drill-test the grow.IT database.

## Where the database is

- **Live app:** PostgreSQL in the home server's self-hosted Supabase, in the `supabase-db`
  container. Database `growit`, owned by the `growit` role. It is not exposed on the
  network: the scripts reach it over SSH and run the Postgres tools inside the container.
  Set `DB_SSH_HOST` to your SSH host alias for the server (keep the address in
  `~/.ssh/config`, not in this repo). See [DEPLOY_SERVER.md](DEPLOY_SERVER.md).
- **Local development:** the `db` service in `docker-compose.yml` (container `growit-db`),
  database `GrowIT`. Leave `DB_SSH_HOST` unset; the scripts run the tools inside that
  container.

All settings and their defaults are listed at the top of
[scripts/db/_pg.sh](../scripts/db/_pg.sh). Run the commands from the repository root.

## Prerequisites

- Live app: SSH access to the server. Local: Docker. No local Postgres tools are needed:
  the tools run inside the database's container, so their version always matches it.
- To use your own Postgres tools against a host and port instead, set `DB_CONTAINER=`
  (empty) plus `PGHOST`, `PGPORT` and `PGPASSWORD`.
- Backups contain user data. `backups/` and `*.dump` are gitignored; keep copies private.

## 1. Create a backup

```bash
DB_SSH_HOST=<server> scripts/db/backup_postgres.sh
```

Writes `backups/growit_growit_<timestamp>.dump` and a `.sha256` checksum next to it.
Local database: `scripts/db/backup_postgres.sh`.

## 2. Run a restore drill

Proves a backup actually restores, without touching the live database. Run it at least
weekly and before each release:

```bash
DB_SSH_HOST=<server> scripts/db/restore_drill.sh backups/<backup-file>.dump
```

It creates a temporary database, restores the backup the same way a real restore would,
checks `__EFMigrationsHistory` and the user count, then drops the temporary database
(set `KEEP_DRILL_DB=true` to keep it).

## 3. Restore the live database

This **replaces** the live data. Stop the app first:

```bash
ssh <server> 'cd ~/docker/growit && docker compose stop client'
DB_SSH_HOST=<server> CONFIRM_DB=growit scripts/db/restore_postgres.sh backups/<backup-file>.dump
ssh <server> 'cd ~/docker/growit && docker compose start client'
```

The script drops and recreates the `growit` database, owned by the `growit` role, and
restores as that role so the app keeps access to every table. Without
`CONFIRM_DB=growit` it stops before changing anything.

Local database: `scripts/db/restore_postgres.sh backups/<backup-file>.dump` (restores
over the existing objects with `--clean --if-exists`).

## 4. Schedule and retention

- Full backup: nightly
- Keep 7 daily and 4 weekly backups
- Restore drill: weekly

Nothing runs this automatically yet; backups are taken by hand with the commands above.

## 5. Incident restore checklist

- Stop the app (section 3) so nothing writes during the restore
- Take a final emergency backup before restoring
- Restore from the last known good backup
- Start the app and smoke test:
  - `/healthz` returns 200
  - sign-in works
  - blog and contact pages load
  - `/` redirects when signed out
- Record what happened and when
