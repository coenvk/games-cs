#!/usr/bin/env bash
# Redraws Paddock's car art (apps/Paddock/Assets/cars.png and its atlas) from the roster, with tools/CarArt.
# Pass --preview <path> to also write every car enlarged. Run build/jentl.sh first.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project tools/CarArt -- apps/Paddock/Assets/paddock/cars.json apps/Paddock/Assets "$@"
