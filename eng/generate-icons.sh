#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
source_image="$repo_root/assets/branding/TradeRelay.png"
icon_dir="$repo_root/assets/icons"
desktop_asset="$repo_root/src/TradeRelay.Desktop/Assets/TradeRelay.png"
iconset="$(mktemp -d)/TradeRelay.iconset"
trap 'rm -rf "$(dirname "$iconset")"' EXIT

command -v magick >/dev/null || { printf 'ImageMagick is required.\n' >&2; exit 2; }
command -v sips >/dev/null || { printf 'sips is required.\n' >&2; exit 2; }
command -v iconutil >/dev/null || { printf 'iconutil is required.\n' >&2; exit 2; }

mkdir -p "$icon_dir" "$(dirname "$desktop_asset")" "$iconset"
magick "$source_image" -resize 1024x1024 -gravity center -extent 1024x1024 -strip "$icon_dir/TradeRelay-1024.png"
magick "$source_image" -resize 256x256 -gravity center -extent 256x256 -strip "$desktop_asset"
magick "$source_image" -strip -define icon:auto-resize=256,128,64,48,32,24,16 "$icon_dir/TradeRelay.ico"

sips -z 16 16 "$icon_dir/TradeRelay-1024.png" --out "$iconset/icon_16x16.png" >/dev/null
sips -z 32 32 "$icon_dir/TradeRelay-1024.png" --out "$iconset/icon_16x16@2x.png" >/dev/null
sips -z 32 32 "$icon_dir/TradeRelay-1024.png" --out "$iconset/icon_32x32.png" >/dev/null
sips -z 64 64 "$icon_dir/TradeRelay-1024.png" --out "$iconset/icon_32x32@2x.png" >/dev/null
sips -z 128 128 "$icon_dir/TradeRelay-1024.png" --out "$iconset/icon_128x128.png" >/dev/null
sips -z 256 256 "$icon_dir/TradeRelay-1024.png" --out "$iconset/icon_128x128@2x.png" >/dev/null
sips -z 256 256 "$icon_dir/TradeRelay-1024.png" --out "$iconset/icon_256x256.png" >/dev/null
sips -z 512 512 "$icon_dir/TradeRelay-1024.png" --out "$iconset/icon_256x256@2x.png" >/dev/null
sips -z 512 512 "$icon_dir/TradeRelay-1024.png" --out "$iconset/icon_512x512.png" >/dev/null
cp "$icon_dir/TradeRelay-1024.png" "$iconset/icon_512x512@2x.png"
iconutil -c icns "$iconset" -o "$icon_dir/TradeRelay.icns"

printf 'Regenerated TradeRelay PNG, ICO, and ICNS assets.\n'
