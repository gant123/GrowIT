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

## 4. Automatic nightly backups

The server backs itself up with [deploy/server/backup.sh](../deploy/server/backup.sh),
installed as `~/docker/growit/backup.sh` and run by the user crontab:

```
40 * * * * $HOME/docker/growit/backup.sh 2>&1 | logger -t growit-backup
```

- **What:** the `growit` database (custom-format dump, checked to read back) and a
  tarball of the `growit_uploads`, `growit_keys` and `growit_documents` volumes. Files are
  mode 600.
- **When:** cron runs it hourly, but it only backs up once per night after 03:45. A night
  the PC slept through is made up within an hour of waking; a failed run retries hourly.
- **Kept:** 7 daily backups in `~/backups/growit/daily/` and 4 weekly in
  `~/backups/growit/weekly/` (the first backup of each week, hard-linked).
- **Drill:** every new weekly backup is restored into a temporary database, checked
  (migration history and users) and dropped. A failed drill fails the backup.
- **Logs:** `ssh <server> journalctl -t growit-backup`. Back up now:
  `ssh <server> '~/docker/growit/backup.sh --now'`.
- **Alerts:** run `~/docker/growit/hc-set-url.sh` on the server once and paste a
  healthchecks.io ping URL (a separate check from the Supabase one). Until then a failed
  backup is only visible in the log.
- **Off-site copy:** off by default. To copy each backup to Cloudflare R2 (checked by size
  and MD5, same retention), put the rclone destination in
  `~/.config/growit-backup/r2-dest` on the server, for example
  `r2:<bucket>/growit`. Without it, backups exist only on the server.

To restore from a server backup, copy it here first:

```bash
scp <server>:backups/growit/daily/db-<stamp>.dump backups/
DB_SSH_HOST=<server> scripts/db/restore_drill.sh backups/db-<stamp>.dump
```

then restore it as in section 3. To restore the files tarball (uploads, login keys and
uploaded documents), with the app stopped:

```bash
ssh <server> 'docker run --rm -v growit_uploads:/dst/uploads -v growit_keys:/dst/keys \
  -v growit_documents:/dst/documents \
  -v ~/backups/growit/daily:/in:ro alpine:3.20 tar -C /dst -xzf /in/files-<stamp>.tar.gz'
```

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
