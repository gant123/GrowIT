#!/usr/bin/env bash
# Restore a backup made by backup_postgres.sh, REPLACING the target database's contents.
#
#   Live app:  DB_SSH_HOST=<server> CONFIRM_DB=growit scripts/db/restore_postgres.sh <file.dump>
#   Local:     scripts/db/restore_postgres.sh <file.dump>
#
# Live app (DB_SSH_HOST set): stop the app first. The database is dropped and recreated,
# owned by DB_OWNER, and the backup is restored as that role so the app keeps access.
# CONFIRM_DB must equal the database name, as a guard against restoring over the wrong one.
#
# Local: restores into the existing database with --clean --if-exists.
#
# Connection settings: see scripts/db/_pg.sh.
set -euo pipefail
source "$(dirname "$0")/_pg.sh"

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <backup-file.dump>" >&2
  exit 1
fi
backup_file="$1"
if [[ ! -f "${backup_file}" ]]; then
  echo "Backup file not found: ${backup_file}" >&2
  exit 1
fi

if [[ -n "${DB_SSH_HOST}" ]]; then
  if [[ "${CONFIRM_DB:-}" != "${PGDATABASE}" ]]; then
    echo "This replaces database '${PGDATABASE}' on ${DB_WHERE}." >&2
    echo "Stop the app, then re-run with CONFIRM_DB=${PGDATABASE} to continue." >&2
    exit 1
  fi
  echo "Recreating ${PGDATABASE} on ${DB_WHERE} (owner ${DB_OWNER})"
  pg psql --dbname=postgres -X -q -v ON_ERROR_STOP=1 \
    -c "DROP DATABASE IF EXISTS \"${PGDATABASE}\" WITH (FORCE)" \
    -c "CREATE DATABASE \"${PGDATABASE}\" OWNER \"${DB_OWNER}\""
  echo "Restoring ${backup_file}"
  # --no-comments: Supabase databases already have pgcrypto, owned by the admin role, so
  # the dump's COMMENT ON EXTENSION would fail. The app defines no comments of its own.
  pg pg_restore --dbname="${PGDATABASE}" --role="${DB_OWNER}" \
    --no-owner --no-privileges --no-comments --exit-on-error < "${backup_file}"
else
  echo "Restoring ${backup_file} into ${PGDATABASE} on ${DB_WHERE}"
  pg pg_restore --dbname="${PGDATABASE}" --clean --if-exists \
    --no-owner --no-privileges < "${backup_file}"
fi

echo "Restore completed."
