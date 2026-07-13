#!/usr/bin/env bash
set -euo pipefail

rid="${1:?usage: package-macos.sh <osx-arm64|osx-x64> [output-directory]}"
output_root="${2:-artifacts/release}"
version="${TRADERELAY_VERSION:-1.0.0}"

case "$rid" in
  osx-arm64|osx-x64) ;;
  *) printf 'Unsupported macOS RID: %s\n' "$rid" >&2; exit 2 ;;
esac

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
publish_dir="$repo_root/artifacts/publish/$rid"
stage_dir="$repo_root/artifacts/package/$rid"
bundle="$stage_dir/TradeRelay.app"
archive="$repo_root/$output_root/TradeRelay-$version-$rid.zip"

rm -rf "$publish_dir" "$stage_dir"
mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources" "$(dirname "$archive")"

dotnet publish "$repo_root/src/TradeRelay.Desktop/TradeRelay.Desktop.csproj" \
  --configuration Release \
  --runtime "$rid" \
  --self-contained true \
  --no-restore \
  --output "$publish_dir" \
  -p:DebugType=None \
  -p:DebugSymbols=false

cp -R "$publish_dir/." "$bundle/Contents/MacOS/"
cp "$repo_root/assets/icons/TradeRelay.icns" "$bundle/Contents/Resources/TradeRelay.icns"
chmod +x "$bundle/Contents/MacOS/TradeRelay"

cat > "$bundle/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleDisplayName</key><string>TradeRelay</string>
  <key>CFBundleExecutable</key><string>TradeRelay</string>
  <key>CFBundleIconFile</key><string>TradeRelay</string>
  <key>CFBundleIdentifier</key><string>io.github.rambod.TradeRelay</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>TradeRelay</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

signing_state="unsigned"
if [[ -n "${MACOS_SIGN_IDENTITY:-}" ]]; then
  codesign --force --deep --options runtime --timestamp --sign "$MACOS_SIGN_IDENTITY" "$bundle"
  signing_state="signed"
else
  codesign --force --deep --sign - "$bundle"
fi

notary_values=0
[[ -n "${APPLE_ID:-}" ]] && ((notary_values+=1)) || true
[[ -n "${APPLE_TEAM_ID:-}" ]] && ((notary_values+=1)) || true
[[ -n "${APPLE_APP_PASSWORD:-}" ]] && ((notary_values+=1)) || true
if [[ "$notary_values" -ne 0 && "$notary_values" -ne 3 ]]; then
  printf 'Notarization requires APPLE_ID, APPLE_TEAM_ID, and APPLE_APP_PASSWORD together.\n' >&2
  exit 3
fi

rm -f "$archive"
(cd "$stage_dir" && ditto -c -k --keepParent TradeRelay.app "$archive")

if [[ "$notary_values" -eq 3 ]]; then
  if [[ "$signing_state" != "signed" ]]; then
    printf 'Notarization requires MACOS_SIGN_IDENTITY.\n' >&2
    exit 3
  fi
  xcrun notarytool submit "$archive" --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" --password "$APPLE_APP_PASSWORD" --wait
  xcrun stapler staple "$bundle"
  rm -f "$archive"
  (cd "$stage_dir" && ditto -c -k --keepParent TradeRelay.app "$archive")
  signing_state="signed-and-notarized"
fi

printf '%s\n' "$signing_state" > "$archive.signing-state"
printf 'Created %s (%s)\n' "$archive" "$signing_state"
