#!/bin/bash
# Nightly backup of grow.IT on the home server. Installed as ~/docker/growit/backup.sh.
# Database: pg_dump (custom format) of the `growit` database in the supabase-db container.
# Files: the growit_uploads, growit_keys and growit_documents volumes (keys = login cookie
# encryption keys; documents = privately stored uploaded documents).
#
# Keeps 7 daily backups in daily/ and 4 weekly ones in weekly/ (the first backup of each
# ISO week, hard-linked, so it costs no extra space while both copies exist). Every new
# weekly backup is restore-drilled: loaded into a temporary database, checked, dropped.
#
# Schedule: cron runs this every hour. It backs up only when nothing has succeeded since
# the most recent 03:45, so a night the PC slept through is made up within an hour of
# waking, and a failed run is retried the next hour. --now backs up regardless.
#   40 * * * * $HOME/docker/growit/backup.sh 2>&1 | logger -t growit-backup
#   Logs: journalctl -t growit-backup
#
# Alerts (optional): reports start / success / failure to healthchecks.io if
# ~/.config/growit-backup/healthcheck-url exists (write it with hc-set-url.sh).
# Off-site copy (optional, off until set up): if ~/.config/growit-backup/r2-dest exists,
# it names an rclone destination such as r2:<bucket>/growit, and each backup is
# copied there and checked by size + MD5. Uses the rclone config in R2_CONF.
#
# Restore: docs/DB_BACKUP_RESTORE_RUNBOOK.md in the grow.IT repo.
set -euo pipefail
DEST=${DEST:-$HOME/backups/growit}             # override only to test
DB_CONTAINER=supabase-db
DB_NAME=growit
DB_OWNER=growit
VOLUMES=(growit_uploads growit_keys growit_documents)
KEEP_DAILY=7
KEEP_WEEKLY=4
SLOT_TIME="03:45"
CONF_DIR="$HOME/.config/growit-backup"
HC_FILE="$CONF_DIR/healthcheck-url"
R2_DEST_FILE="$CONF_DIR/r2-dest"
R2_CONF=${R2_CONF:-$HOME/.config/rclone/moemedia-r2.conf}

mkdir -p "$DEST/daily" "$DEST/weekly" && chmod 700 "$DEST" "$DEST/daily" "$DEST/weekly"

slot=$(date -d "today $SLOT_TIME" +%s)
(( $(date +%s) >= slot )) || slot=$(date -d "yesterday $SLOT_TIME" +%s)
last=$(stat -c %Y "$DEST/.last-success" 2>/dev/null || echo 0)
if [[ ${1:-} != --now ]] && (( last >= slot )); then
  exit 0
fi
exec 9>"$DEST/.lock"
if ! flock -n 9; then
  echo "skipped: another backup.sh is still running"
  exit 0
fi

# healthchecks.io ping. $1: "" (success), /start or /<exit status>; $2: file sent as the body.
# The URL reaches curl on stdin (-K -), not in its arguments. Never fails the backup.
hc() {
  [[ -r $HC_FILE ]] || return 0
  printf 'url = "%s%s"\n' "$(<"$HC_FILE")" "$1" |
    curl -fsS -m 10 --retry 5 -o /dev/null -K - --data-binary @"${2:-/dev/null}" ||
    echo "warning: healthchecks.io ping '$1' failed"
}
[[ -r $HC_FILE ]] || echo "warning: $HC_FILE missing, so no alert can fire; run ~/docker/growit/hc-set-url.sh"

# Keep the newest $2 files matching $1 (names sort by time). $1 is a glob.
prune() {
  local pattern=$1 keep=$2
  # shellcheck disable=SC2086
  ls -1 $pattern 2>/dev/null | sort -r | tail -n +"$((keep + 1))" | xargs -r rm -f --
}

# Load a dump into a temporary database the way a real restore would, check it, drop it.
drill() {
  local dump=$1 db="growit_restore_drill_$(date +%Y%m%d%H%M%S)"
  docker exec "$DB_CONTAINER" createdb -U supabase_admin --owner="$DB_OWNER" "$db"
  local rc=0
  docker exec -i "$DB_CONTAINER" pg_restore -U supabase_admin -d "$db" --role="$DB_OWNER" \
    --no-owner --no-privileges --no-comments --exit-on-error < "$dump" || rc=$?
  local migrations=0 users=0
  if (( rc == 0 )); then
    migrations=$(docker exec "$DB_CONTAINER" psql -X -At -U supabase_admin -d "$db" -c 'SELECT COUNT(*) FROM "__EFMigrationsHistory"') || rc=$?
    users=$(docker exec "$DB_CONTAINER" psql -X -At -U supabase_admin -d "$db" -c 'SELECT COUNT(*) FROM "Users"') || rc=$?
  fi
  docker exec "$DB_CONTAINER" dropdb -U supabase_admin --if-exists "$db"
  if (( rc != 0 )) || (( migrations == 0 )); then
    echo "drill FAILED for $(basename "$dump") (exit $rc, migrations=$migrations)"
    return 1
  fi
  echo "drill ok $(basename "$dump") migrations=$migrations users=$users"
}

# Copy files to the off-site destination and check size + MD5 there (see the Supabase
# backup.sh for why: R2's ETag is the MD5 of what it stored for single-PUT uploads).
offsite() {
  local subdir=$1; shift
  local dest r2dir
  dest=$(<"$R2_DEST_FILE")
  dest=${dest//[[:space:]]/}
  r2dir="$dest/$subdir"
  if [[ ! -r $R2_CONF ]]; then
    echo "r2 FAILED: $R2_CONF missing"
    return 1
  fi
  r2() { rclone --config "$R2_CONF" --s3-upload-cutoff 4G --s3-no-head "$@"; }
  local f local_size local_md5 remote
  for f in "$@"; do
    r2 copyto "$f" "$r2dir/$(basename "$f")"
    local_size=$(stat -c %s "$f")
    local_md5=$(md5sum "$f" | cut -d' ' -f1)
    remote=$(r2 lsf --format sh --separator ' ' --hash MD5 "$r2dir/$(basename "$f")")
    if [[ ${remote%% *} != "$local_size" || ${remote#* } != "$local_md5" ]]; then
      echo "r2 FAILED: $(basename "$f") local=$local_size/$local_md5 r2=${remote:-missing}"
      return 1
    fi
    echo "r2 ok $subdir/$(basename "$f") size=$local_size"
  done
  # Same retention off-site (with a day of slack).
  if [[ $subdir == daily ]]; then
    r2 delete --min-age "$((KEEP_DAILY + 1))d" "$r2dir"
  else
    r2 delete --min-age "$((KEEP_WEEKLY * 7 + 3))d" "$r2dir"
  fi
}

backup() {
  local stamp week db files
  stamp=$(date +%Y%m%d-%H%M)
  week=$(date +%G-W%V)
  db="$DEST/daily/db-$stamp.dump"
  files="$DEST/daily/files-$stamp.tar.gz"

  docker exec "$DB_CONTAINER" pg_dump -U supabase_admin -d "$DB_NAME" -Fc --no-owner --no-privileges > "$db.tmp"
  # Cheap integrity check: the archive's table of contents must read back.
  docker exec -i "$DB_CONTAINER" pg_restore --list < "$db.tmp" > /dev/null
  mv "$db.tmp" "$db"

  local mounts=() v
  for v in "${VOLUMES[@]}"; do mounts+=(-v "$v:/src/${v#growit_}:ro"); done
  docker run --rm "${mounts[@]}" -v "$DEST/daily:/out" alpine:3.20 \
    sh -c "tar -C /src -czf /out/files-$stamp.tar.gz.tmp . && chown $(id -u):$(id -g) /out/files-$stamp.tar.gz.tmp"
  mv "$files.tmp" "$files"
  chmod 600 "$db" "$files"
  echo "ok db=$(du -h "$db" | cut -f1) files=$(du -h "$files" | cut -f1)"

  local new_weekly=()
  if [[ ! -e "$DEST/weekly/.week-$week" ]]; then
    ln "$db" "$DEST/weekly/" && ln "$files" "$DEST/weekly/"
    if ! drill "$DEST/weekly/$(basename "$db")"; then
      # Don't keep an unverified weekly; the next hourly run tries again with a new backup.
      rm -f "$DEST/weekly/$(basename "$db")" "$DEST/weekly/$(basename "$files")"
      return 1
    fi
    rm -f "$DEST/weekly"/.week-*
    touch "$DEST/weekly/.week-$week"
    new_weekly=("$DEST/weekly/$(basename "$db")" "$DEST/weekly/$(basename "$files")")
    echo "weekly $week saved"
  fi

  prune "$DEST/daily/db-*.dump" "$KEEP_DAILY"
  prune "$DEST/daily/files-*.tar.gz" "$KEEP_DAILY"
  prune "$DEST/weekly/db-*.dump" "$KEEP_WEEKLY"
  prune "$DEST/weekly/files-*.tar.gz" "$KEEP_WEEKLY"
  echo "kept daily=$(ls "$DEST"/daily/db-*.dump | wc -l) weekly=$(ls "$DEST"/weekly/db-*.dump 2>/dev/null | wc -l)"

  if [[ -r $R2_DEST_FILE ]]; then
    offsite daily "$db" "$files"
    (( ${#new_weekly[@]} == 0 )) || offsite weekly "${new_weekly[@]}"
  else
    echo "off-site copy not set up ($R2_DEST_FILE missing): backups are on this machine only"
  fi
}

log=$(mktemp)
trap 'rm -f "$log"' EXIT
hc /start
# Run in a subshell with errexit on and its output captured. Not inside if/||: bash
# ignores set -e in a function called from a condition.
set +e
(set -e; backup) > "$log" 2>&1
rc=$?
set -e
cat "$log"
if (( rc == 0 )); then
  touch "$DEST/.last-success"
  hc "" "$log"
else
  echo "backup FAILED (exit $rc); next try in an hour" | tee -a "$log" >&2
  rm -f "$DEST"/daily/*.tmp
  hc "/$rc" "$log"
  exit "$rc"
fi
