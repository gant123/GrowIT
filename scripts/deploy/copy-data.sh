#!/usr/bin/env bash
# Copy grow.IT's database, uploaded files and login keys from the local Docker stack
# (docker-compose.yml) to a server running deploy/server/docker-compose.yml.
#
# Usage: scripts/deploy/copy-data.sh <ssh-host> [--replace]
#
# - Creates the target role and database if they don't exist. Creating the role needs
#   DST_DB_PASSWORD (the same password as in the server's connection string).
# - If the target database already exists, stops unless --replace is given, which drops
#   and recreates it. Never use --replace once the server is the live copy.
# - For the final copy, stop the source app first (docker stop growit-client): anything
#   written after the dump is not copied. Works with the source app stopped.
# - Login keys are copied so people stay signed in across the move.
# - The growit-client image must already be on the server (scripts/deploy/ship-image.sh).
#
# Defaults can be overridden with environment variables:
set -euo pipefail

SRC_DB_CONTAINER="${SRC_DB_CONTAINER:-growit-db}"
SRC_DB="${SRC_DB:-GrowIT}"
SRC_DB_USER="${SRC_DB_USER:-postgres}"
SRC_APP_CONTAINER="${SRC_APP_CONTAINER:-growit-client}"
DST_DB_CONTAINER="${DST_DB_CONTAINER:-supabase-db}"
DST_DB_ADMIN="${DST_DB_ADMIN:-supabase_admin}"
DST_DB="${DST_DB:-growit}"
DST_DB_USER="${DST_DB_USER:-growit}"
DST_IMAGE="${DST_IMAGE:-growit-client:latest}"
DST_UPLOADS_VOLUME="${DST_UPLOADS_VOLUME:-growit_uploads}"
DST_KEYS_VOLUME="${DST_KEYS_VOLUME:-growit_keys}"

host="${1:?usage: $0 <ssh-host> [--replace]}"
replace="${2:-}"

remote_admin_sql() {
  # SQL arrives on stdin so passwords never appear in a command line.
  ssh "$host" "docker exec -i '$DST_DB_CONTAINER' psql -X -q -v ON_ERROR_STOP=1 -U '$DST_DB_ADMIN' -d postgres" "$@"
}
remote_admin_query() {
  ssh "$host" "docker exec '$DST_DB_CONTAINER' psql -X -At -U '$DST_DB_ADMIN' -d postgres -c \"$1\""
}

echo "== Database role"
if [[ "$(remote_admin_query "select 1 from pg_roles where rolname='$DST_DB_USER'")" != "1" ]]; then
  : "${DST_DB_PASSWORD:?role $DST_DB_USER does not exist yet; set DST_DB_PASSWORD to create it}"
  remote_admin_sql <<SQL
\set pw '$DST_DB_PASSWORD'
CREATE ROLE "$DST_DB_USER" LOGIN PASSWORD :'pw';
SQL
  echo "Created role $DST_DB_USER."
else
  echo "Role $DST_DB_USER exists."
fi

echo "== Database"
if [[ "$(remote_admin_query "select 1 from pg_database where datname='$DST_DB'")" == "1" ]]; then
  if [[ "$replace" != "--replace" ]]; then
    echo "Database $DST_DB already exists on $host. Re-run with --replace to drop and recreate it." >&2
    exit 1
  fi
  echo "Dropping existing $DST_DB (--replace)."
  remote_admin_sql <<SQL
DROP DATABASE "$DST_DB" WITH (FORCE);
SQL
fi
remote_admin_sql <<SQL
CREATE DATABASE "$DST_DB" OWNER "$DST_DB_USER";
SQL
echo "Created database $DST_DB."

echo "== Copying data ($SRC_DB -> $DST_DB)"
docker exec "$SRC_DB_CONTAINER" pg_dump -U "$SRC_DB_USER" -d "$SRC_DB" -Fc --no-owner --no-privileges \
  | ssh "$host" "docker exec -i '$DST_DB_CONTAINER' pg_restore -U '$DST_DB_ADMIN' -d '$DST_DB' --role='$DST_DB_USER' --no-owner --no-privileges --no-comments --exit-on-error"
# --no-comments: Supabase's template database already has pgcrypto (owned by its admin),
# so the dump's "COMMENT ON EXTENSION pgcrypto" fails. The app defines no comments of its own.
echo "Restored. Row counts (source | target):"
count_sql="select 'users', count(*) from \"Users\" union all select 'tenants', count(*) from \"Tenants\" union all select 'clients', count(*) from \"Clients\" union all select 'migrations', count(*) from \"__EFMigrationsHistory\""
paste -d'|' \
  <(docker exec -i "$SRC_DB_CONTAINER" psql -X -At -U "$SRC_DB_USER" -d "$SRC_DB" <<<"$count_sql") \
  <(ssh "$host" "docker exec -i '$DST_DB_CONTAINER' psql -X -At -U '$DST_DB_ADMIN' -d '$DST_DB'" <<<"$count_sql" | cut -d'|' -f2)

copy_dir_to_volume() {
  local src_path="$1" volume="$2"
  docker cp "$SRC_APP_CONTAINER:$src_path/." - \
    | ssh "$host" "docker volume create '$volume' >/dev/null && docker run --rm -i -v '$volume:/dst' --entrypoint tar '$DST_IMAGE' -C /dst -xf -"
}

echo "== Uploaded files"
copy_dir_to_volume /app/wwwroot/uploads "$DST_UPLOADS_VOLUME"
echo "Copied uploads into $DST_UPLOADS_VOLUME."

echo "== Login keys"
# Keys live in /app/keys once DataProtection__KeysPath is set; older containers kept them
# in the root user's home directory.
keys_path=/app/keys
if ! docker cp "$SRC_APP_CONTAINER:$keys_path/." - 2>/dev/null | tar -tf - 2>/dev/null | grep -q '\.xml$'; then
  keys_path=/root/.aspnet/DataProtection-Keys
fi
copy_dir_to_volume "$keys_path" "$DST_KEYS_VOLUME"
echo "Copied keys from $keys_path into $DST_KEYS_VOLUME."

echo "Done."
