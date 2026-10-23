#!/usr/bin/env bash
# Packages the game for distribution.
#
# Usage: build/package.sh [rid|web]   (default: this machine's runtime, e.g. osx-arm64)
#
# For this machine's own runtime the game is compiled ahead of time (NativeAOT): one native
# executable, fast start-up, no .NET install needed. Other runtimes cannot be AOT-compiled
# from here, so they get a self-contained, trimmed single-file build instead.
#
# Output in dist/:
#   macOS:          Sokoban-<rid>.zip containing Sokoban.app (ad-hoc signed)
#   Windows/Linux:  Sokoban-<rid>.zip containing a Sokoban/ folder
#   web:            Sokoban-<version>-web.zip, a static site (index.html at the root) for any web host
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
host_rid="$(dotnet --info | awk '/RID:/ { print $2; exit }')"
rid="${1:-$host_rid}"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/src/Sokoban.Desktop/Sokoban.Desktop.csproj")"

if [[ "$rid" == web ]]; then
    site="$root/dist/web"
    rm -rf "$site"
    dotnet publish "$root/src/Sokoban.Web" -c Release -o "$site" -p:DebugType=None
    archive="$root/dist/Sokoban-$version-web.zip"
    rm -f "$archive"
    (cd "$site/wwwroot" && zip -qr "$archive" .)
    echo "Created $archive"
    exit 0
fi
work="$root/dist/$rid"
app="$work/Sokoban"

publish_args=(-c Release -r "$rid" -o "$app" -p:DebugType=None -p:DebugSymbols=false)
if [[ "$rid" == "$host_rid" ]]; then
    echo "Publishing $rid with NativeAOT"
    publish_args+=(-p:PublishAot=true -p:StripSymbols=true)
else
    echo "Publishing $rid as a self-contained single file (NativeAOT needs a $rid machine)"
    # Native libraries (SDL2, OpenAL) stay next to the executable: MonoGame loads them by path,
    # so they cannot be packed into the single file.
    publish_args+=(--self-contained -p:PublishSingleFile=true -p:PublishTrimmed=true)
fi

rm -rf "$work"
dotnet publish "$root/src/Sokoban.Desktop" "${publish_args[@]}"

# Stripped debug symbols land next to the executable; they are not part of the game.
rm -rf "$app"/*.dSYM

mkdir -p "$root/dist"
archive="$root/dist/Sokoban-$version-$rid.zip"
rm -f "$archive"

if [[ "$rid" == osx-* ]]; then
    bundle="$work/Sokoban.app"
    mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
    cp -R "$app/." "$bundle/Contents/MacOS/"
    cp "$root/build/macos/Sokoban.icns" "$bundle/Contents/Resources/"
    sed "s/@VERSION@/$version/g" "$root/build/macos/Info.plist" > "$bundle/Contents/Info.plist"
    rm -rf "$app"

    # Apple Silicon refuses to run unsigned code; an ad-hoc signature is enough for local use.
    # Distribution to other Macs needs a Developer ID signature and notarisation instead.
    codesign --force --deep --sign - "$bundle"
    (cd "$work" && ditto -c -k --keepParent Sokoban.app "$archive")
else
    (cd "$work" && zip -qr "$archive" Sokoban)
fi

echo "Created $archive"
