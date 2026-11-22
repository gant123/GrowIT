#!/usr/bin/env bash
# Back up the grow.IT database to a local .dump file (custom format) plus a .sha256.
#
#   Live app:  DB_SSH_HOST=<server> scripts/db/backup_postgres.sh
#   Local:     scripts/db/backup_postgres.sh
#
# Connection settings: see scripts/db/_pg.sh. Output: BACKUP_DIR (default ./backups,
# gitignored). Backups contain user data: keep them private.
set -euo pipefail
source "$(dirname "$0")/_pg.sh"

BACKUP_DIR="${BACKUP_DIR:-./backups}"
BACKUP_PREFIX="${BACKUP_PREFIX:-growit}"

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "${BACKUP_DIR}"
backup_file="${BACKUP_DIR}/${BACKUP_PREFIX}_${PGDATABASE}_${timestamp}.dump"
partial="${backup_file}.partial"
trap 'rm -f "${partial}"' EXIT

echo "Backing up ${PGDATABASE} on ${DB_WHERE}"
pg pg_dump --dbname="${PGDATABASE}" --format=custom --no-owner --no-privileges > "${partial}"
mv "${partial}" "${backup_file}"

if command -v sha256sum >/dev/null 2>&1; then
  (cd "${BACKUP_DIR}" && sha256sum "$(basename "${backup_file}")" > "$(basename "${backup_file}").sha256")
elif command -v shasum >/dev/null 2>&1; then
  (cd "${BACKUP_DIR}" && shasum -a 256 "$(basename "${backup_file}")" > "$(basename "${backup_file}").sha256")
fi

echo "Backup completed: ${backup_file} ($(du -h "${backup_file}" | cut -f1))"
