#!/usr/bin/env python3
import gzip
import pathlib
import sys
import tarfile

source = pathlib.Path(sys.argv[1]).resolve()
destination = pathlib.Path(sys.argv[2]).resolve()
arc_root = sys.argv[3] if len(sys.argv) > 3 else source.name
fixed_time = 1767225600  # 2026-01-01T00:00:00Z

destination.parent.mkdir(parents=True, exist_ok=True)
with destination.open("wb") as output:
    with gzip.GzipFile(filename="", mode="wb", fileobj=output, mtime=fixed_time) as compressed:
        with tarfile.open(fileobj=compressed, mode="w", format=tarfile.PAX_FORMAT) as archive:
            for path in [source, *sorted(source.rglob("*"), key=lambda item: item.as_posix())]:
                relative = path.relative_to(source)
                archive_name = pathlib.PurePosixPath(arc_root) / pathlib.PurePosixPath(relative.as_posix())
                info = archive.gettarinfo(str(path), str(archive_name))
                info.uid = 0
                info.gid = 0
                info.uname = ""
                info.gname = ""
                info.mtime = fixed_time
                if path.is_file():
                    with path.open("rb") as content:
                        archive.addfile(info, content)
                else:
                    archive.addfile(info)
