#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.

"""Verify promotion inputs against the successful tagged release."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET
import zipfile


REQUIRED_GATES = {"ci", "shaders", "native", "performance", "packages", "render"}
VERSION_PATTERN = r"[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?"


def _inventory(values, label):
    if not isinstance(values, list) or not values:
        raise ValueError(f"{label} inventory must be a nonempty array.")
    if any(not isinstance(value, str) or not re.fullmatch(r"[A-Za-z0-9_.-]+", value)
           for value in values):
        raise ValueError(f"{label} inventory contains an invalid package ID.")
    result = {value.casefold() for value in values}
    if len(result) != len(values):
        raise ValueError(f"{label} inventory contains duplicate IDs.")
    return result


def _packages(directory, version, expected, symbols):
    if not directory.is_dir():
        raise ValueError(f"Package directory does not exist: {directory}")
    found = {}
    for path in directory.iterdir():
        extension = path.suffix.casefold()
        if extension not in (".nupkg", ".snupkg"):
            continue
        if not path.is_file() or path.is_symlink():
            raise ValueError(f"Package must be a regular file: {path}")
        match = re.fullmatch(
            rf"(.+)\.{re.escape(version)}\.(nupkg|snupkg)", path.name, re.I)
        if match is None:
            raise ValueError(f"Package does not name version {version}: {path.name}")
        key = (match[1].casefold(), extension)
        if key in found:
            raise ValueError(f"Duplicate package in {directory}: {path.name}")
        found[key] = path
    wanted = {(name, ".nupkg") for name in expected}
    wanted.update((name, ".snupkg") for name in symbols)
    if set(found) != wanted:
        missing = sorted(wanted - set(found))
        unexpected = sorted(set(found) - wanted)
        raise ValueError(
            f"Package inventory mismatch in {directory}. "
            f"Missing: {missing}. Unexpected: {unexpected}.")
    return found


def _sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def _metadata(path, package_id, version, commit):
    try:
        with zipfile.ZipFile(path) as archive:
            entries = [
                entry for entry in archive.infolist()
                if "/" not in entry.filename and "\\" not in entry.filename
                and entry.filename.casefold().endswith(".nuspec")
            ]
            if len(entries) != 1 or entries[0].file_size > 1024 * 1024:
                raise ValueError(f"Expected one bounded root nuspec in {path.name}.")
            root = ET.fromstring(archive.read(entries[0]))
    except (zipfile.BadZipFile, ET.ParseError, RuntimeError) as error:
        raise ValueError(f"Invalid package metadata in {path.name}: {error}") from error
    if root.tag.rsplit("}", 1)[-1] != "package":
        raise ValueError(f"Invalid nuspec root in {path.name}.")
    metadata = root.find("{*}metadata")
    if metadata is None:
        raise ValueError(f"Missing package metadata in {path.name}.")
    actual_id = metadata.findtext("{*}id", default="")
    actual_version = metadata.findtext("{*}version", default="")
    repository = metadata.find("{*}repository")
    if actual_id.casefold() != package_id or actual_version.casefold() != version.casefold():
        raise ValueError(f"Package ID/version does not match its filename: {path.name}")
    if repository is None or repository.get("commit", "").casefold() != commit:
        raise ValueError(f"Package source commit does not match the release: {path.name}")


def verify_promotion(*, packages, release_packages, evidence, release_run,
                     inventory, version, run_id, repository, tag_commit):
    if any(not isinstance(value, dict) for value in (evidence, release_run, inventory)):
        raise ValueError("Release evidence, run metadata and inventory must be objects.")
    if re.fullmatch(VERSION_PATTERN, version) is None:
        raise ValueError("An exact supported NuGet version is required.")
    if re.fullmatch(r"[1-9][0-9]*", run_id) is None:
        raise ValueError("A positive release run ID is required.")
    if re.fullmatch(r"[0-9a-f]{40}", tag_commit) is None:
        raise ValueError("The tag must resolve to a full commit SHA.")
    expected_ref = f"refs/tags/v{version}"
    if (type(evidence.get("schemaVersion")) is not int
            or evidence["schemaVersion"] != 1
            or evidence.get("repository") != repository
            or evidence.get("commit") != tag_commit
            or evidence.get("ref") != expected_ref
            or str(evidence.get("runId")) != run_id):
        raise ValueError("Release evidence does not match the repository, tag and run.")
    jobs = evidence.get("jobs")
    if (not isinstance(jobs, dict) or set(jobs) != REQUIRED_GATES
            or any(value != "success" for value in jobs.values())):
        raise ValueError("Every required release gate must be present and successful.")
    run_repository = release_run.get("repository")
    if (not isinstance(run_repository, dict)
            or run_repository.get("full_name") != repository
            or str(release_run.get("id")) != run_id
            or release_run.get("path") != ".github/workflows/release.yml"
            or release_run.get("event") != "push"
            or release_run.get("status") != "completed"
            or release_run.get("conclusion") != "success"
            or release_run.get("head_branch") != f"v{version}"
            or release_run.get("head_sha") != tag_commit
            or type(release_run.get("run_attempt")) is not int
            or release_run["run_attempt"] < 1
            or str(evidence.get("runAttempt")) != str(release_run["run_attempt"])):
        raise ValueError("The release run must be the successful matching tagged attempt.")
    expected = _inventory(inventory.get("packages"), "Published package")
    symbols = _inventory(inventory.get("symbols"), "Symbol package")
    if not symbols <= expected:
        raise ValueError("Symbol package IDs must belong to the published package set.")
    downloaded = _packages(packages, version, expected, symbols)
    released = _packages(release_packages, version, expected, symbols)
    for key, path in sorted(downloaded.items()):
        if _sha256(path) != _sha256(released[key]):
            raise ValueError(f"Promotion bytes differ from the release artifact: {path.name}")
        _metadata(path, key[0], version, tag_commit)
    return {"version": version, "commit": tag_commit, "runId": run_id,
            "packages": len(expected), "symbols": len(symbols)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("packages", "release-packages", "evidence", "release-run", "inventory"):
        parser.add_argument(f"--{name}", type=Path, required=True)
    for name in ("version", "run-id", "repository", "tag-commit"):
        parser.add_argument(f"--{name}", required=True)
    args = parser.parse_args()
    try:
        result = verify_promotion(
            packages=args.packages, release_packages=args.release_packages,
            evidence=json.loads(args.evidence.read_text(encoding="utf-8-sig")),
            release_run=json.loads(args.release_run.read_text(encoding="utf-8-sig")),
            inventory=json.loads(args.inventory.read_text(encoding="utf-8-sig")),
            version=args.version, run_id=args.run_id, repository=args.repository,
            tag_commit=args.tag_commit)
    except (OSError, ValueError) as error:
        print(f"NUGET_PROMOTION_INVALID: {error}", file=sys.stderr)
        return 1
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
