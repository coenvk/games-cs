#!/usr/bin/env bash
# Copies the game server's data volume (the poker data and scores.json) to a dated tar.gz.
#   bash deploy/backup.sh [target-folder]      (default: ./backups)
# The volume is mounted read-only. For a consistent copy of the owners and bots, stop the server first
# (docker compose -f deploy/docker-compose.yml stop game-server); a copy of a running server is usually fine too.
set -euo pipefail

volume="${VOLUME:-games_poker-data}"
target="$(mkdir -p "${1:-backups}" && cd "${1:-backups}" && pwd)"
name="games-data-$(date +%Y-%m-%d-%H%M%S).tar.gz"

# docker run would create a missing volume, empty: fail instead
docker volume inspect "$volume" >/dev/null

docker run --rm -v "$volume":/data:ro -v "$target":/backup alpine \
    tar czf "/backup/$name" -C /data .

echo "Wrote $target/$name"
