# Copyright (c) marcschier. Licensed under the MIT License.

"""Acquire only pinned standalone PBR/glass MDL sources, without installing Kit."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
from urllib.parse import quote

import simready_warehouse as acquisition


class ModuleLayout(acquisition.Layout):
    def __init__(self, root, identity):
        if not root.is_absolute():
            raise acquisition.WarehouseError("The module root must be an absolute physical directory.")
        acquisition.reject_reparse(root)
        self.base = root.joinpath("warehouse-01", "dependencies", f"mdl-{identity[:16]}")
        self.source = self.base.joinpath("modules")
        self.parts = self.base.joinpath("parts")
        self.metadata = self.base.joinpath("metadata")


def read_files(path):
    data = path.read_bytes()
    if len(data) > 1024 * 1024:
        raise acquisition.WarehouseError("The module profile exceeds its byte bound.")
    profile = json.loads(data)
    if profile.get("schemaVersion") != 1 or profile.get("id") != "simready-mdl-core":
        raise acquisition.WarehouseError("The module acquisition profile is not supported.")
    files = profile["files"]
    if not isinstance(files, list) or not 1 <= len(files) <= 32:
        raise acquisition.WarehouseError("The module file count is outside its bound.")
    entries = []
    urls = {}
    for item in files:
        if (item["repository"] not in ("NVIDIA/simready-blender-add-on", "NVIDIA/MDL-SDK")
                or not re.fullmatch(r"[0-9a-f]{40}", item["revision"])):
            raise acquisition.WarehouseError("Only pinned first-party module sources are admitted.")
        acquisition.validate_relative_path(item["sourcePath"])
        entry = acquisition.FileEntry(item["path"], item["size"], item["gitBlobSha1"], None)
        if (not entry.path.endswith(".mdl") or not item["sourcePath"].endswith(".mdl")
                or not 1 <= entry.size <= 1024 * 1024):
            raise acquisition.WarehouseError("Only bounded MDL source files are acquired.")
        entries.append(entry)
        urls[entry.path] = (
            f"https://raw.githubusercontent.com/{item['repository']}/{item['revision']}/"
            f"{quote(item['sourcePath'], safe='/')}"
        )
    acquisition.ordered_entries(entries)
    return profile, entries, urls, hashlib.sha256(data).hexdigest()


def run(profile_path, data_root, operation, opener=acquisition.open_http):
    if operation not in ("Download", "Verify"):
        raise acquisition.WarehouseError("The module operation must be Download or Verify.")
    profile, entries, urls, identity = read_files(profile_path)
    layout = ModuleLayout(data_root, identity)
    with acquisition.snapshot_lock(layout):
        if operation == "Download":
            missing = sum(item.size for item in entries if not layout.source_path(item).exists())
            if acquisition.available_space(layout) < missing + 16 * 1024 * 1024:
                raise acquisition.WarehouseError("The optional module snapshot lacks storage headroom.")
            for item in entries:
                acquisition.download_file(item, profile, layout, opener, source_url=urls[item.path])
        records = []
        for item in entries:
            path = layout.source_path(item)
            acquisition.verify_file(path, item)
            text = path.read_text(encoding="utf-8")
            if ("Redistribution and use in source and binary forms" not in text[:6000]
                    or "Neither the name of NVIDIA" not in text[:6000]):
                raise acquisition.WarehouseError(f"The pinned source lacks its expected licence notice: {item.path}")
            records.append({
                "path": item.path, "sourceUrl": urls[item.path], "bytes": item.size,
                "gitBlobSha1": item.oid, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            })
        report = {
            "schemaVersion": 1, "scope": profile["scope"], "profileSha256": identity,
            "moduleRoot": str(layout.source), "filesVerified": len(entries),
            "licenceNoticesPreserved": True, "kitInstalled": False,
            "mdlCompilationVerified": False, "files": records,
        }
        acquisition.write_json(layout.metadata.joinpath("verified.json"), report)
        return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--operation", choices=("Download", "Verify"), default="Download")
    parser.add_argument("--data-root", type=Path, default=Path(r"D:\simready"))
    parser.add_argument("--profile", type=Path,
                        default=Path(__file__).with_name("datasets").joinpath("simready-mdl-core.json"))
    args = parser.parse_args()
    try:
        print(json.dumps(run(args.profile, args.data_root, args.operation), indent=2))
        return 0
    except (acquisition.WarehouseError, OSError, ValueError, KeyError, TypeError) as error:
        print(f"WAREHOUSE_MDL_ACQUISITION_FAILED: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
