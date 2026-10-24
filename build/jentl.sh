#!/usr/bin/env bash
# Packs the Jentl engine into .jentl-packages, where restore looks for it (see nuget.config).
#
# Usage: build/jentl.sh [jentl-checkout]
#   With a checkout (default: ../jentl next to this repository, if there is one), packs that, as it is: handy
#   while changing the engine. Without one, clones github.com/coenvk/jentl at the version in dependencies.props.
#
# The packages carry the version in dependencies.props. NuGet caches packages by version, so after packing changed
# engine code under the same version, clear the cache: dotnet nuget locals global-packages --clear
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
version="$(sed -n 's:.*<JentlVersion>\(.*\)</JentlVersion>.*:\1:p' "$root/dependencies.props")"
source="${1:-$root/../jentl}"

if [[ ! -d "$source" ]]; then
    source="$(mktemp -d)/jentl"
    git clone --quiet --depth 1 --branch "v$version" https://github.com/coenvk/jentl.git "$source"
fi

dotnet pack "$source/Jentl.slnx" -c Release -p:Version="$version" -o "$root/.jentl-packages"
echo "Packed Jentl $version from $source into .jentl-packages"
