#!/usr/bin/env python3
import pathlib
import plistlib
import sys
import tarfile
import zipfile

release_dir = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts/release").resolve()
version = sys.argv[2] if len(sys.argv) > 2 else "1.0.0"
expected = {
    f"TradeRelay-{version}-osx-arm64.zip",
    f"TradeRelay-{version}-osx-x64.zip",
    f"TradeRelay-{version}-win-arm64.zip",
    f"TradeRelay-{version}-win-x64.zip",
    f"TradeRelay-{version}-linux-arm64.tar.gz",
    f"TradeRelay-{version}-linux-x64.tar.gz",
}
present = {path.name for path in list(release_dir.glob("*.zip")) + list(release_dir.glob("*.tar.gz"))}
missing = expected - present
if missing:
    raise SystemExit(f"Missing release archives: {', '.join(sorted(missing))}")

for archive_name in sorted(expected):
    archive = release_dir / archive_name
    if "osx-" in archive_name:
        with zipfile.ZipFile(archive) as package:
            names = package.namelist()
            roots = {name.split("/", 1)[0] for name in names if name}
            if roots != {"TradeRelay.app"}:
                raise SystemExit(f"{archive_name} does not contain exactly TradeRelay.app at its root.")
            plist_name = "TradeRelay.app/Contents/Info.plist"
            metadata = plistlib.loads(package.read(plist_name))
            if metadata.get("CFBundleExecutable") != "TradeRelay" or metadata.get("CFBundleShortVersionString") != version:
                raise SystemExit(f"{archive_name} has invalid bundle metadata.")
            required = {"TradeRelay.app/Contents/MacOS/TradeRelay", "TradeRelay.app/Contents/Resources/TradeRelay.icns"}
            if not required.issubset(names):
                raise SystemExit(f"{archive_name} is missing its executable or icon.")
    elif "win-" in archive_name:
        with zipfile.ZipFile(archive) as package:
            names = {pathlib.PurePosixPath(name).name for name in package.namelist()}
            if "TradeRelay.exe" not in names:
                raise SystemExit(f"{archive_name} is missing TradeRelay.exe.")
    else:
        with tarfile.open(archive, "r:gz") as package:
            names = set(package.getnames())
            required = {
                "TradeRelay/TradeRelay",
                "TradeRelay/launch-traderelay",
                "TradeRelay/share/applications/io.github.rambod.TradeRelay.desktop",
                "TradeRelay/share/icons/hicolor/256x256/apps/traderelay.png",
                "TradeRelay/README-LINUX.txt",
            }
            if not required.issubset(names):
                raise SystemExit(f"{archive_name} is missing Linux portable metadata.")

print(f"Validated {len(expected)} TradeRelay {version} release archives.")
