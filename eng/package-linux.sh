#!/usr/bin/env bash
set -euo pipefail

rid="${1:?usage: package-linux.sh <linux-arm64|linux-x64> [output-directory]}"
output_root="${2:-artifacts/release}"
version="${TRADERELAY_VERSION:-1.0.0}"

case "$rid" in
  linux-arm64|linux-x64) ;;
  *) printf 'Unsupported Linux RID: %s\n' "$rid" >&2; exit 2 ;;
esac

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
publish_dir="$repo_root/artifacts/publish/$rid"
stage_dir="$repo_root/artifacts/package/$rid/TradeRelay"
archive="$repo_root/$output_root/TradeRelay-$version-$rid.tar.gz"

rm -rf "$publish_dir" "$(dirname "$stage_dir")"
mkdir -p "$stage_dir" "$stage_dir/share/applications" "$stage_dir/share/icons/hicolor/256x256/apps" "$(dirname "$archive")"

dotnet publish "$repo_root/src/TradeRelay.Desktop/TradeRelay.Desktop.csproj" \
  --configuration Release \
  --runtime "$rid" \
  --self-contained true \
  --no-restore \
  --output "$publish_dir" \
  -p:DebugType=None \
  -p:DebugSymbols=false

cp -R "$publish_dir/." "$stage_dir/"
cp "$repo_root/assets/icons/TradeRelay-1024.png" "$stage_dir/share/icons/hicolor/256x256/apps/traderelay.png"
cp "$repo_root/packaging/linux/traderelay.desktop" "$stage_dir/share/applications/io.github.rambod.TradeRelay.desktop"
cp "$repo_root/packaging/linux/README-LINUX.txt" "$stage_dir/README-LINUX.txt"
cp "$repo_root/packaging/linux/launch-traderelay" "$stage_dir/launch-traderelay"
chmod +x "$stage_dir/TradeRelay" "$stage_dir/launch-traderelay"

rm -f "$archive"
python3 "$repo_root/eng/create_reproducible_tar.py" "$stage_dir" "$archive" TradeRelay
printf 'unsigned\n' > "$archive.signing-state"
printf 'Created %s (unsigned)\n' "$archive"
