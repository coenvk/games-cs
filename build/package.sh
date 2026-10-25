#!/usr/bin/env bash
# Packages one of the games for distribution.
#
# Usage: build/package.sh <app> [rid|web]   (app: Sokoban, Solitaire, CardTable, Poker, Mossbrook, CodeLock, Bricks or Paddock;
#                                            default: this machine's runtime, e.g. osx-arm64)
#
# For this machine's own runtime the game is compiled ahead of time (NativeAOT): one native
# executable, fast start-up, no .NET install needed. Other runtimes cannot be AOT-compiled
# from here, so they get a self-contained, trimmed single-file build instead.
#
# Output in dist/:
#   macOS:          <Title>-<version>-<rid>.zip containing <Title>.app (ad-hoc signed)
#   Windows/Linux:  <Title>-<version>-<rid>.zip containing a folder with the game
#   web:            <Title>-<version>-web.zip, a static site (index.html at the root) for any web host
#
# The engine comes from .jentl-packages: run build/jentl.sh first.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
app="${1:?Which app? Sokoban, Solitaire, CardTable, Poker, Mossbrook, CodeLock, Bricks or Paddock}"
host_rid="$(dotnet --info | awk '/RID:/ { print $2; exit }')"
rid="${2:-$host_rid}"
project="$root/apps/$app"
[[ -d "$project" ]] || { echo "There is no app $app in apps/." >&2; exit 1; }

# The executable's name (AppName) and the title players see, from the app's projects.
executable="$(sed -n 's:.*<AppName>\(.*\)</AppName>.*:\1:p' "$project/Desktop/$app.Desktop.csproj")"
title="$(sed -n 's:.*<AppTitle>\(.*\)</AppTitle>.*:\1:p' "$project/Web/$app.Web.csproj")"
name="${title// /}"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/shell/Desktop.props")"

if [[ "$rid" == web ]]; then
    site="$root/dist/$app-web"
    rm -rf "$site"
    dotnet publish "$project/Web" -c Release -o "$site" -p:DebugType=None
    archive="$root/dist/$name-$version-web.zip"
    rm -f "$archive"
    (cd "$site/wwwroot" && zip -qr "$archive" .)
    echo "Created $archive"
    exit 0
fi

work="$root/dist/$app-$rid"
out="$work/$name"

publish_args=(-c Release -r "$rid" -o "$out" -p:DebugType=None -p:DebugSymbols=false)
if [[ "$rid" == "$host_rid" ]]; then
    echo "Publishing $title for $rid with NativeAOT"
    publish_args+=(-p:PublishAot=true -p:StripSymbols=true)
else
    echo "Publishing $title for $rid as a self-contained single file (NativeAOT needs a $rid machine)"
    # Native libraries (SDL2, OpenAL) stay next to the executable: MonoGame loads them by path,
    # so they cannot be packed into the single file.
    publish_args+=(--self-contained -p:PublishSingleFile=true -p:PublishTrimmed=true)
fi

rm -rf "$work"
dotnet publish "$project/Desktop" "${publish_args[@]}"

# Stripped debug symbols land next to the executable; they are not part of the game.
rm -rf "$out"/*.dSYM

mkdir -p "$root/dist"
archive="$root/dist/$name-$version-$rid.zip"
rm -f "$archive"

if [[ "$rid" == osx-* ]]; then
    bundle="$work/$name.app"
    mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
    cp -R "$out/." "$bundle/Contents/MacOS/"
    cp "$root/build/macos/Sokoban.icns" "$bundle/Contents/Resources/"
    sed -e "s/@VERSION@/$version/g" -e "s/@NAME@/$title/g" -e "s/@EXECUTABLE@/$executable/g" \
        -e "s/@IDENTIFIER@/com.github.coenvk.$(echo "$name" | tr '[:upper:]' '[:lower:]')/g" \
        "$root/build/macos/Info.plist" > "$bundle/Contents/Info.plist"
    rm -rf "$out"

    # Apple Silicon refuses to run unsigned code; an ad-hoc signature is enough for local use.
    # Distribution to other Macs needs a Developer ID signature and notarisation instead.
    codesign --force --deep --sign - "$bundle"
    (cd "$work" && ditto -c -k --keepParent "$name.app" "$archive")
else
    (cd "$work" && zip -qr "$archive" "$name")
fi

echo "Created $archive"
