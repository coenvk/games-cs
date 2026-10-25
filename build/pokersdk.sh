#!/usr/bin/env bash
# Packs the poker bot SDK (src/Poker.Bots) into .poker-packages, where the starter kit in bots/csharp restores it from
# (see nuget.config).
#
# Usage: build/pokersdk.sh
#
# The package carries the version in src/Poker.Bots/Poker.Bots.csproj. NuGet caches packages by version, so after
# packing changed SDK code under the same version, clear the cache: dotnet nuget locals global-packages --clear
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
dotnet pack "$root/src/Poker.Bots/Poker.Bots.csproj" -c Release -o "$root/.poker-packages"
echo "Packed the poker bot SDK into .poker-packages"
