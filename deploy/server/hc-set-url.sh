#!/bin/bash
# Stores the healthchecks.io ping URL that backup.sh reports to (start / success / failure).
# Reads it without echoing and writes ~/.config/growit-backup/healthcheck-url, mode 600.
# Re-run it to change checks. Sends no ping. Installed as ~/docker/growit/hc-set-url.sh.
set -euo pipefail
FILE="$HOME/.config/growit-backup/healthcheck-url"

umask 077
mkdir -p "$(dirname "$FILE")"
read -rsp "healthchecks.io ping URL: " url; echo
url=${url//[[:space:]]/}
url=${url%/}
if [[ ! $url =~ ^https://hc-ping\.com/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]; then
  echo "That isn't a https://hc-ping.com/<uuid> ping URL. Nothing saved." >&2
  exit 1
fi
printf '%s\n' "$url" > "$FILE.tmp"
chmod 600 "$FILE.tmp"
mv "$FILE.tmp" "$FILE"
unset url
echo "Saved $FILE (mode $(stat -c %a "$FILE"))."
