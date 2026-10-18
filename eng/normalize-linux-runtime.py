#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.
"""Finalize installed ELF paths before recording SDK binary provenance."""

import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys


def run(*arguments):
    return subprocess.run(arguments, check=True, capture_output=True, text=True).stdout


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sdk-root", required=True, type=Path)
    args = parser.parse_args()
    if sys.platform != "linux":
        parser.error("Linux runtime finalization requires a native Linux host.")
    for tool in ("patchelf", "readelf"):
        if not shutil.which(tool):
            parser.error(f"{tool} is required; install the Linux patchelf/binutils build prerequisites.")
    root = args.sdk_root.resolve(strict=True)
    library_root = root / "lib"
    if not library_root.is_dir():
        parser.error(f"The SDK library directory is missing: {library_root}")
    records = []
    for directory in (library_root, root / "bin", root / "plugin"):
        if not directory.is_dir():
            continue
        for path in sorted(directory.rglob("*")):
            if path.is_symlink() or not path.is_file():
                continue
            with path.open("rb") as stream:
                magic = stream.read(4)
            if magic != b"\x7fELF":
                continue
            dynamic = run("readelf", "--dynamic", "--wide", str(path))
            if "(NEEDED)" not in dynamic:
                continue
            needed = re.findall(r"\(NEEDED\).*\[(.*?)\]", dynamic)
            if any("/" in name for name in needed):
                raise RuntimeError(f"Nonrelocatable DT_NEEDED in {path}: {needed}")
            relative = os.path.relpath(library_root, path.parent)
            desired = "$ORIGIN" if relative == "." else f"$ORIGIN/{relative}"
            # Also finalizes non-CMake dependencies such as Boost's b2 outputs.
            current = re.findall(r"\(RUNPATH\).*\[(.*?)\]", dynamic)
            if current != [desired] or "(RPATH)" in dynamic:
                run("patchelf", "--set-rpath", desired, str(path))
            final = run("readelf", "--dynamic", "--wide", str(path))
            paths = re.findall(r"\(RUNPATH\).*\[(.*?)\]", final)
            if paths != [desired] or "(RPATH)" in final:
                raise RuntimeError(f"Unexpected finalized ELF paths for {path}: {paths}")
            records.append({"path": str(path.relative_to(root)), "runpath": desired})
    if not any(record["path"] == "lib/libusd_ms.so" for record in records):
        raise RuntimeError("The finalized runtime does not contain the OpenUSD monolith.")
    print(json.dumps({"libraries": records}, indent=2))


if __name__ == "__main__":
    main()
