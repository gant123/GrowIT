# Shared by the scripts in scripts/db/ (sourced, not run). Decides where the database is
# and provides `pg`, which runs a Postgres client tool against it. The tools run inside
# the database's own container, so their version always matches the server and nothing
# has to be installed locally or exposed on the network.
#
# Server (the live app): set DB_SSH_HOST to the server's SSH host alias.
#     DB_CONTAINER  container name               (default supabase-db)
#     PGDATABASE    database                     (default growit)
#     PGUSER        admin role in the container  (default supabase_admin)
#     DB_OWNER      role that owns the app's tables (default growit)
#
# Local (DB_SSH_HOST unset): the docker-compose database on this machine.
#     DB_CONTAINER  container name               (default growit-db)
#     PGDATABASE    database                     (default GrowIT)
#     PGUSER        role                         (default postgres)
#   With DB_CONTAINER set to empty, uses locally installed tools against
#   PGHOST:PGPORT instead (defaults 127.0.0.1:5433, PGPASSWORD postgres).

DB_SSH_HOST="${DB_SSH_HOST:-}"

if [[ -n "${DB_SSH_HOST}" ]]; then
  DB_CONTAINER="${DB_CONTAINER:-supabase-db}"
  PGDATABASE="${PGDATABASE:-growit}"
  PGUSER="${PGUSER:-supabase_admin}"
  DB_OWNER="${DB_OWNER:-growit}"
  DB_WHERE="${DB_CONTAINER} on ${DB_SSH_HOST}"
else
  DB_CONTAINER="${DB_CONTAINER-growit-db}"
  PGDATABASE="${PGDATABASE:-GrowIT}"
  PGUSER="${PGUSER:-postgres}"
  if [[ -n "${DB_CONTAINER}" ]]; then
    DB_WHERE="${DB_CONTAINER} (local Docker)"
  else
    PGHOST="${PGHOST:-127.0.0.1}"
    PGPORT="${PGPORT:-5433}"
    PGPASSWORD="${PGPASSWORD:-postgres}"
    DB_WHERE="${PGHOST}:${PGPORT}"
  fi
fi

# pg <tool> [args...]: run pg_dump, pg_restore, psql, createdb or dropdb against the
# database server. Standard input and output pass through, so dumps stream to and from
# local files in every mode.
pg() {
  local tool="$1"
  shift
  if [[ -n "${DB_SSH_HOST}" ]]; then
    # shellcheck disable=SC2029  # arguments are quoted with %q for the remote shell
    ssh "${DB_SSH_HOST}" "docker exec -i $(printf '%q' "${DB_CONTAINER}") $(printf '%q ' "${tool}" "--username=${PGUSER}" "$@")"
  elif [[ -n "${DB_CONTAINER}" ]]; then
    docker exec -i "${DB_CONTAINER}" "${tool}" --username="${PGUSER}" "$@"
  else
    PGPASSWORD="${PGPASSWORD}" "${tool}" --host="${PGHOST}" --port="${PGPORT}" --username="${PGUSER}" "$@"
  fi
}
