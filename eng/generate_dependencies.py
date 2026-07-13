#!/usr/bin/env python3
import json
import pathlib
import subprocess
import sys

root = pathlib.Path(__file__).resolve().parents[1]
destination = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else root / "artifacts/release/dependencies.json")
result = subprocess.run(
    ["dotnet", "list", str(root / "TradeRelay.sln"), "package", "--include-transitive", "--format", "json"],
    check=True,
    capture_output=True,
    text=True,
)
data = json.loads(result.stdout)
packages: dict[tuple[str, str], dict[str, str]] = {}
for project in data.get("projects", []):
    for framework in project.get("frameworks", []):
        for kind in ("topLevelPackages", "transitivePackages"):
            for package in framework.get(kind, []):
                name = package.get("id", "")
                version = package.get("resolvedVersion", package.get("requestedVersion", ""))
                if name and version:
                    packages[(name, version)] = {"name": name, "version": version}

manifest = {"schemaVersion": "1.0", "applicationVersion": "1.0.0", "dependencies": sorted(packages.values(), key=lambda item: (item["name"].lower(), item["version"]))}
destination.parent.mkdir(parents=True, exist_ok=True)
destination.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
