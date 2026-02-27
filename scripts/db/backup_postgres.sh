#!/usr/bin/env bash
set -euo pipefail

BACKUP_DIR="${BACKUP_DIR:-./backups}"
BACKUP_PREFIX="${BACKUP_PREFIX:-growit}"
PGHOST="${PGHOST:-127.0.0.1}"
PGPORT="${PGPORT:-5433}"
PGDATABASE="${PGDATABASE:-GrowIT}"
PGUSER="${PGUSER:-postgres}"
PGPASSWORD="${PGPASSWORD:-postgres}"

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "${BACKUP_DIR}"

backup_file="${BACKUP_DIR}/${BACKUP_PREFIX}_${PGDATABASE}_${timestamp}.dump"

echo "Creating backup: ${backup_file}"
PGPASSWORD="${PGPASSWORD}" pg_dump \
  --host="${PGHOST}" \
  --port="${PGPORT}" \
  --username="${PGUSER}" \
  --dbname="${PGDATABASE}" \
  --format=custom \
  --no-owner \
  --no-privileges \
  --file="${backup_file}"

if command -v sha256sum >/dev/null 2>&1; then
  sha256sum "${backup_file}" > "${backup_file}.sha256"
elif command -v shasum >/dev/null 2>&1; then
  shasum -a 256 "${backup_file}" > "${backup_file}.sha256"
fi

echo "Backup completed."
echo "File: ${backup_file}"
