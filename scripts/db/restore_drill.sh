#!/usr/bin/env bash
# Prove a backup restores: load it into a temporary database, check the EF migration
# history is there, then drop the temporary database (unless KEEP_DRILL_DB=true).
# The live database is not touched.
#
#   Live app:  DB_SSH_HOST=<server> scripts/db/restore_drill.sh <file.dump>
#   Local:     scripts/db/restore_drill.sh <file.dump>
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

KEEP_DRILL_DB="${KEEP_DRILL_DB:-false}"
drill_db="growit_restore_drill_$(date -u +%Y%m%d%H%M%S)"

cleanup() {
  if [[ "${KEEP_DRILL_DB}" == "true" ]]; then
    echo "KEEP_DRILL_DB=true, leaving ${drill_db} in place."
  else
    echo "Dropping restore-drill database: ${drill_db}"
    pg dropdb --if-exists "${drill_db}"
  fi
}

echo "Creating restore-drill database ${drill_db} on ${DB_WHERE}"
if [[ -n "${DB_SSH_HOST}" ]]; then
  # Same path as a real restore on the server: owned by the app's role, restored as it.
  pg createdb --owner="${DB_OWNER}" "${drill_db}"
  trap cleanup EXIT
  pg pg_restore --dbname="${drill_db}" --role="${DB_OWNER}" \
    --no-owner --no-privileges --no-comments --exit-on-error < "${backup_file}"
else
  pg createdb "${drill_db}"
  trap cleanup EXIT
  pg pg_restore --dbname="${drill_db}" --no-owner --no-privileges < "${backup_file}"
fi

echo "Verifying migration history exists..."
migration_count="$(pg psql --dbname="${drill_db}" -X --tuples-only --no-align \
  --command='SELECT COUNT(*) FROM "__EFMigrationsHistory";')"
user_count="$(pg psql --dbname="${drill_db}" -X --tuples-only --no-align \
  --command='SELECT COUNT(*) FROM "Users";')"

echo "Restore drill passed. __EFMigrationsHistory rows: ${migration_count}, users: ${user_count}"
