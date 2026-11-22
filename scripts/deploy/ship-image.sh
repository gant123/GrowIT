#!/usr/bin/env bash
# Build the grow.IT web image from this checkout and load it on a server over SSH.
# Run it from a clean checkout of the commit you want to ship: uncommitted changes under
# src/ or docker/ are built in, and the tag gets a "-dirty" suffix to say so.
#
# Usage: scripts/deploy/ship-image.sh <ssh-host> [tag]
#
# On the server the image is tagged growit-client:<tag> and growit-client:latest.
# To roll back, set GROWIT_IMAGE=growit-client:<older-tag> in deploy/server/.env.
set -euo pipefail

host="${1:?usage: $0 <ssh-host> [tag]}"
root="$(git rev-parse --show-toplevel)"
sha="$(git -C "$root" rev-parse --short HEAD)"
dirty=""
git -C "$root" diff --quiet HEAD -- src docker || dirty="-dirty"
tag="${2:-$sha$dirty}"
image="growit-client:$tag"

# Not tagged :latest locally, so it never replaces the image the local compose stack uses.
docker build \
  -f "$root/docker/client/Dockerfile" \
  -t "$image" \
  --label "org.opencontainers.image.revision=$sha$dirty" \
  "$root"

echo "Sending $image to $host..."
docker save "$image" | gzip -1 | ssh "$host" 'gunzip | docker load'
ssh "$host" "docker tag '$image' growit-client:latest"
echo "Loaded $image on $host (also tagged growit-client:latest)."
