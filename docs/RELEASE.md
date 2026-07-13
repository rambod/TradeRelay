# Release Procedure

## Artifacts

Version `1.0.0` publishes six self-contained portable archives:

- `TradeRelay-1.0.0-osx-arm64.zip`
- `TradeRelay-1.0.0-osx-x64.zip`
- `TradeRelay-1.0.0-win-arm64.zip`
- `TradeRelay-1.0.0-win-x64.zip`
- `TradeRelay-1.0.0-linux-arm64.tar.gz`
- `TradeRelay-1.0.0-linux-x64.tar.gz`

macOS archives contain exactly `TradeRelay.app` at their root. Linux archives include the launcher, desktop metadata, icon, and dependency notes without installing anything.

## Signing configuration

Unsigned artifacts are valid and are identified as `unsigned` in `release-metadata.json`. Optional macOS signing uses `MACOS_SIGN_IDENTITY`. Notarization requires all of `APPLE_ID`, `APPLE_TEAM_ID`, and `APPLE_APP_PASSWORD`; a partial configuration fails. Optional Windows signing requires both `WINDOWS_CERTIFICATE_BASE64` and `WINDOWS_CERTIFICATE_PASSWORD`; a partial configuration fails. Scripts never print secret values.

## Verification

Locally validate the current host package and structure:

```bash
dotnet restore --locked-mode
eng/package-macos.sh osx-arm64
python3 eng/generate_dependencies.py artifacts/release/dependencies.json
python3 eng/generate_release_metadata.py artifacts/release 1.0.0
```

The GitHub Release workflow builds natively on six hosted runners, launches every clean-profile package for a bounded smoke window, validates names/icons/metadata/bundle structure, generates `SHA256SUMS`, `dependencies.json`, `release-metadata.json`, and signing-aware release notes, and attests archive provenance.

## Maintainer sequence

1. Verify the working tree, version metadata, changelog, private vulnerability reporting, locked Release build/test/format/scans, and local host package.
2. Create an annotated tag only: `git tag -a v1.0.0 -m "TradeRelay 1.0.0 — Production-ready open-source release"`.
3. Push `master` and wait for normal CI and security checks.
4. Run the Release workflow manually on `master`; download and inspect the complete dry-run artifact set for all six targets.
5. Push the existing annotated tag: `git push origin v1.0.0`.
6. Confirm the automatic GitHub Release appears only after all package, smoke, scan, checksum, manifest, and attestation jobs succeed.

The workflow rejects lightweight tags and tags that do not match the version in `Directory.Build.props`. Never add or run an automated real Bybit Live write test.
