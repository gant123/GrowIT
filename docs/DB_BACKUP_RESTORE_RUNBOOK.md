# grow.IT Database Backup & Restore Runbook

This runbook defines the minimum backup/restore standard required for beta operations.

## Scope
- Database engine: PostgreSQL
- Default beta database: `GrowIT`
- Default local/docker connection: `127.0.0.1:5433`

## Prerequisites
- `pg_dump`, `pg_restore`, `psql`, `createdb`, `dropdb` available on operator machine
- Database credentials with backup/restore permissions
- Access to repository scripts:
  - `/Users/robertgant/workspace/GrowIT/scripts/db/backup_postgres.sh`
  - `/Users/robertgant/workspace/GrowIT/scripts/db/restore_postgres.sh`
  - `/Users/robertgant/workspace/GrowIT/scripts/db/restore_drill.sh`

## 1. Create a backup

```bash
cd /Users/robertgant/workspace/GrowIT
PGHOST=10.0.0.6 \
PGPORT=5433 \
PGDATABASE=GrowIT \
PGUSER=postgres \
PGPASSWORD=postgres \
BACKUP_DIR=./backups \
./scripts/db/backup_postgres.sh
```

Expected output:
- `.dump` file in `./backups`
- optional `.sha256` checksum file

## 2. Restore into target database

```bash
cd /Users/robertgant/workspace/GrowIT
PGHOST=10.0.0.6 \
PGPORT=5433 \
PGDATABASE=GrowIT \
PGUSER=postgres \
PGPASSWORD=postgres \
./scripts/db/restore_postgres.sh ./backups/<backup-file>.dump
```

Note:
- Restore uses `--clean --if-exists` and will replace existing objects in the target DB.

## 3. Run a restore drill (required)

Run this at least once per week and before each beta milestone cut:

```bash
cd /Users/robertgant/workspace/GrowIT
PGHOST=10.0.0.6 \
PGPORT=5433 \
PGUSER=postgres \
PGPASSWORD=postgres \
./scripts/db/restore_drill.sh ./backups/<backup-file>.dump
```

This command:
- creates a temporary drill DB
- restores the backup
- validates `__EFMigrationsHistory` exists
- drops the drill DB unless `KEEP_DRILL_DB=true`

## 4. Scheduling policy (beta)
- Full DB backup: nightly
- Retention:
  - 7 daily backups
  - 4 weekly backups
- Restore drill: weekly

## 5. Incident restore checklist
- Stop writes to the app (maintenance window)
- Capture final emergency backup before restore
- Restore from last known good backup
- Run application smoke test:
  - `/healthz` returns 200
  - login succeeds
  - blog/contact pages load
  - one protected route redirect works (`/` when anonymous)
- Document incident in release notes
