#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <backup-file.dump>"
  exit 1
fi

backup_file="$1"
if [[ ! -f "${backup_file}" ]]; then
  echo "Backup file not found: ${backup_file}"
  exit 1
fi

PGHOST="${PGHOST:-127.0.0.1}"
PGPORT="${PGPORT:-5433}"
PGUSER="${PGUSER:-postgres}"
PGPASSWORD="${PGPASSWORD:-postgres}"
KEEP_DRILL_DB="${KEEP_DRILL_DB:-false}"

drill_db="GrowIT_restore_drill_$(date -u +%Y%m%d%H%M%S)"

echo "Creating restore-drill database: ${drill_db}"
PGPASSWORD="${PGPASSWORD}" createdb \
  --host="${PGHOST}" \
  --port="${PGPORT}" \
  --username="${PGUSER}" \
  "${drill_db}"

echo "Restoring backup into drill database..."
PGPASSWORD="${PGPASSWORD}" pg_restore \
  --host="${PGHOST}" \
  --port="${PGPORT}" \
  --username="${PGUSER}" \
  --dbname="${drill_db}" \
  --no-owner \
  --no-privileges \
  "${backup_file}"

echo "Verifying migration history exists..."
migration_count="$(
  PGPASSWORD="${PGPASSWORD}" psql \
    --host="${PGHOST}" \
    --port="${PGPORT}" \
    --username="${PGUSER}" \
    --dbname="${drill_db}" \
    --tuples-only \
    --no-align \
    --command='SELECT COUNT(*) FROM "__EFMigrationsHistory";'
)"

echo "Restore drill passed. __EFMigrationsHistory rows: ${migration_count}"

if [[ "${KEEP_DRILL_DB}" != "true" ]]; then
  echo "Dropping restore-drill database: ${drill_db}"
  PGPASSWORD="${PGPASSWORD}" dropdb \
    --host="${PGHOST}" \
    --port="${PGPORT}" \
    --username="${PGUSER}" \
    "${drill_db}"
else
  echo "KEEP_DRILL_DB=true, leaving ${drill_db} in place."
fi
