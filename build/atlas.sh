#!/usr/bin/env bash
# Repacks Sokoban's sprite atlas from apps/Sokoban/Art/sprites, with the jentl-assets tool (from the engine
# packages; run build/jentl.sh first). The app tests fail while the atlas is out of date with its sprites.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet tool restore
dotnet jentl-assets atlas apps/Sokoban/Art/sprites apps/Sokoban/Assets/sprites.atlas.json
