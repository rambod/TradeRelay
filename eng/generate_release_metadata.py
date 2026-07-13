#!/usr/bin/env python3
import hashlib
import json
import pathlib
import sys

release_dir = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts/release").resolve()
version = sys.argv[2] if len(sys.argv) > 2 else "1.0.0"
archives = sorted(list(release_dir.glob("TradeRelay-*.zip")) + list(release_dir.glob("TradeRelay-*.tar.gz")))
items = []
for archive in archives:
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    state_file = pathlib.Path(str(archive) + ".signing-state")
    signing = state_file.read_text(encoding="utf-8").strip() if state_file.exists() else "unsigned"
    items.append({"file": archive.name, "sha256": digest, "bytes": archive.stat().st_size, "signing": signing})

metadata = {
    "schemaVersion": "1.0",
    "product": "TradeRelay",
    "version": version,
    "architectures": ["arm64", "x64"],
    "artifacts": items,
}
(release_dir / "release-metadata.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
with (release_dir / "SHA256SUMS").open("w", encoding="utf-8", newline="\n") as checksums:
    for item in items:
        checksums.write(f'{item["sha256"]}  {item["file"]}\n')

notes = [
    f"# TradeRelay {version}",
    "",
    "TradeRelay is a safety-first local MCP control panel for Bybit Demo and explicitly enabled Live workflows.",
    "",
    "## Package signing",
    "",
]
for item in items:
    notes.append(f'- `{item["file"]}` — **{item["signing"]}**')
notes.extend([
    "",
    "Unsigned packages are expected when maintainer signing credentials are not configured. Verify every download with `SHA256SUMS` and review `release-metadata.json` before opening it.",
    "",
    "## Safety reminder",
    "",
    "Every launch starts with trading disabled. Use Bybit Demo first. TradeRelay rejects withdrawal-enabled keys and never blindly retries ambiguous order submissions.",
])
(release_dir / "RELEASE_NOTES.md").write_text("\n".join(notes) + "\n", encoding="utf-8")
