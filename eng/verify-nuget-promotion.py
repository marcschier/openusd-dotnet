#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.

"""Verify promotion inputs against the successful tagged release."""

import argparse
from datetime import datetime, timezone
import hashlib
import http.client
import json
import math
from pathlib import Path
import re
import sys
import tempfile
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
import zipfile
import zlib


REQUIRED_GATES = {"ci", "shaders", "native", "performance", "packages", "render"}
VERSION_PATTERN = r"[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?"
PUBLIC_ATTEMPTS = 41
PUBLIC_RETRY_DELAY_SECONDS = 30
PUBLIC_MAX_WAIT_SECONDS = 20 * 60
PUBLIC_REQUEST_TIMEOUT_SECONDS = 30
UNAPPROVED_COMPILER_PATTERN = re.compile(
    r"(?:(?:lib)?spirv[-_]?cross(?:\.dll|\.dylib|\.so(?:\.[0-9]+)*)|"
    r"dxc\.exe|(?:lib)?(?:dxcompiler|dxil)(?:\.dll|\.dylib|\.so(?:\.[0-9]+)*))",
    re.IGNORECASE,
)


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


def _packages(directory, version, expected, symbols, *, allow_partial=False):
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
    if set(found) - wanted or (not allow_partial and set(found) != wanted):
        missing = sorted(wanted - set(found))
        unexpected = sorted(set(found) - wanted)
        raise ValueError(
            f"Package inventory mismatch in {directory}. "
            f"Missing: {missing}. Unexpected: {unexpected}.")
    return found


def _sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def _payloads(path):
    result = {}
    seen = set()
    try:
        with zipfile.ZipFile(path) as archive:
            for entry in archive.infolist():
                if entry.filename in seen:
                    raise ValueError(f"Duplicate package entry in {path.name}: {entry.filename}")
                seen.add(entry.filename)
                if entry.filename == ".signature.p7s":
                    continue
                with archive.open(entry) as stream:
                    result[entry.filename] = (
                        hashlib.file_digest(stream, "sha256").hexdigest(),
                        entry.create_system,
                        entry.external_attr,
                    )
    except (zipfile.BadZipFile, RuntimeError, EOFError, zlib.error) as error:
        raise ValueError(f"Invalid package payload in {path.name}: {error}") from error
    return result


def wait_for_public_packages(release_packages, public_packages, inventory, version, *,
                             attempts=PUBLIC_ATTEMPTS,
                             retry_delay=PUBLIC_RETRY_DELAY_SECONDS,
                             max_wait=PUBLIC_MAX_WAIT_SECONDS, opener=None,
                             sleep=None, clock=None):
    if type(attempts) is not int or attempts < 1:
        raise ValueError("Public verification attempts must be a positive integer.")
    if (isinstance(retry_delay, bool) or not isinstance(retry_delay, (int, float))
            or not math.isfinite(retry_delay) or retry_delay < 0):
        raise ValueError("Public verification retry delay must be finite and nonnegative.")
    if (isinstance(max_wait, bool) or not isinstance(max_wait, (int, float))
            or not math.isfinite(max_wait) or max_wait <= 0):
        raise ValueError("Public verification time limit must be finite and positive.")
    if not isinstance(inventory, dict) or not isinstance(version, str):
        raise ValueError("A package inventory and exact version are required.")
    if re.fullmatch(VERSION_PATTERN, version) is None:
        raise ValueError("An exact supported NuGet version is required.")

    expected = _inventory(inventory.get("packages"), "Published package")
    symbols = _inventory(inventory.get("symbols"), "Symbol package")
    if not symbols <= expected:
        raise ValueError("Symbol package IDs must belong to the published package set.")
    released = _packages(release_packages, version, expected, symbols)
    if (public_packages.is_symlink()
            or public_packages.resolve() == release_packages.resolve()):
        raise ValueError("Public verification requires a separate directory.")
    public_packages.mkdir(parents=True, exist_ok=True)
    if any(public_packages.iterdir()):
        raise ValueError("Public verification directory must not contain existing files.")

    opener = urllib.request.urlopen if opener is None else opener
    sleep = time.sleep if sleep is None else sleep
    clock = time.monotonic if clock is None else clock
    deadline = clock() + max_wait
    version = version.casefold()
    pending = set(released)
    reasons = {key: "not yet checked" for key in pending}

    def filename(key):
        return f"{key[0]}.{version}{key[1]}"

    attempts_done = 0
    for attempt in range(1, attempts + 1):
        if clock() >= deadline:
            break
        attempts_done = attempt
        for key in sorted(pending):
            remaining = deadline - clock()
            if remaining <= 0:
                break
            name = filename(key)
            if key[1] == ".snupkg":
                url = f"https://globalcdn.nuget.org/symbol-packages/{name}"
            else:
                url = f"https://api.nuget.org/v3-flatcontainer/{key[0]}/{version}/{name}"
            temporary = None
            try:
                with opener(url, timeout=min(PUBLIC_REQUEST_TIMEOUT_SECONDS, remaining)) as response:
                    headers = getattr(response, "headers", None)
                    length = headers.get("Content-Length") if headers is not None else None
                    if length is not None and not str(length).isdigit():
                        raise ValueError(f"Invalid Content-Length for public package {name}.")
                    with tempfile.NamedTemporaryFile(
                            dir=public_packages, prefix=f".{name}.", suffix=".partial",
                            delete=False) as output:
                        temporary = Path(output.name)
                        copied = 0
                        while True:
                            if clock() >= deadline:
                                raise TimeoutError("public verification time limit elapsed")
                            block = response.read1(1024 * 1024)
                            if not block:
                                break
                            output.write(block)
                            copied += len(block)
                    if length is not None and copied != int(length):
                        raise ConnectionError(
                            f"incomplete download: received {copied} of {length} bytes")
                if clock() >= deadline:
                    raise TimeoutError("public verification time limit elapsed")
                try:
                    public_payload = _payloads(temporary)
                except ValueError as error:
                    raise ValueError(f"Public package {name}: {error}") from error
                if public_payload != _payloads(released[key]):
                    raise ValueError(f"Public package content differs from the release: {name}")
                if clock() >= deadline:
                    raise TimeoutError("public verification time limit elapsed")
                temporary.replace(public_packages / name)
                temporary = None
                pending.remove(key)
            except urllib.error.HTTPError as error:
                if error.code not in (404, 408, 429) and not 500 <= error.code <= 599:
                    raise ValueError(f"Public package {name} returned HTTP {error.code}.") from error
                reasons[key] = f"HTTP {error.code}"
            except (urllib.error.URLError, http.client.IncompleteRead,
                    TimeoutError, ConnectionError) as error:
                reasons[key] = str(error)
            finally:
                if temporary is not None:
                    temporary.unlink(missing_ok=True)
        if not pending:
            return
        remaining = deadline - clock()
        if attempt == attempts or remaining <= 0:
            break
        print(f"Waiting for {len(pending)} public package(s); "
              f"attempt {attempt} of {attempts}.", file=sys.stderr)
        sleep(min(retry_delay, remaining))

    missing = ", ".join(f"{filename(key)} ({reasons[key]})" for key in sorted(pending))
    raise ValueError(
        f"Public package verification incomplete after {attempts_done} attempts: {missing}.")


def write_public_progress(path, release_packages, public_packages, inventory, version,
                          tag_commit, run_id, error):
    expected = _inventory(inventory.get("packages"), "Published package")
    symbols = _inventory(inventory.get("symbols"), "Symbol package")
    if not symbols <= expected:
        raise ValueError("Symbol package IDs must belong to the published package set.")
    released = _packages(release_packages, version, expected, symbols)
    public = _packages(public_packages, version, expected, symbols, allow_partial=True)
    verified = []
    not_verified = set(released) - set(public)
    for key, package in sorted(public.items()):
        if _payloads(package) != _payloads(released[key]):
            not_verified.add(key)
            continue
        verified.append({
            "file": package.name,
            "publicSha256": _sha256(package),
            "releaseSha256": _sha256(released[key]),
        })
    if error is None and not_verified:
        raise ValueError("The public package set changed while recording verification progress.")
    progress = {
        "schemaVersion": 1,
        "version": version,
        "commit": tag_commit,
        "runId": run_id,
        "observedAtUtc": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "complete": error is None and not not_verified,
        "verified": verified,
        "notVerified": [
            f"{key[0]}.{version.casefold()}{key[1]}" for key in sorted(not_verified)
        ],
        "error": error,
    }
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(
                mode="w", encoding="utf-8", dir=path.parent, prefix=f".{path.name}.",
                suffix=".partial", delete=False) as output:
            temporary = Path(output.name)
            json.dump(progress, output, indent=2, sort_keys=True)
            output.write("\n")
        temporary.replace(path)
        temporary = None
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def _check_compiler_entries(archive, path):
    for entry in archive.infolist():
        name = entry.filename.replace("\\", "/").rsplit("/", 1)[-1]
        if not entry.is_dir() and UNAPPROVED_COMPILER_PATTERN.fullmatch(name):
            raise ValueError(
                f"Package {path.name} contains an unapproved compiler binary: {entry.filename}")


def verify_compiler_assets(directory):
    if not directory.is_dir():
        raise ValueError(f"Package directory does not exist: {directory}")
    paths = sorted(path for path in directory.iterdir()
                   if path.suffix.casefold() in (".nupkg", ".snupkg"))
    if not paths:
        raise ValueError(f"Package directory contains no archives: {directory}")
    for path in paths:
        if not path.is_file() or path.is_symlink():
            raise ValueError(f"Package must be a regular file: {path}")
        try:
            with zipfile.ZipFile(path) as archive:
                _check_compiler_entries(archive, path)
        except (zipfile.BadZipFile, RuntimeError) as error:
            raise ValueError(f"Invalid package payload in {path.name}: {error}") from error
    return {"archives": len(paths)}


def _metadata(path, package_id, version, commit):
    try:
        with zipfile.ZipFile(path) as archive:
            _check_compiler_entries(archive, path)
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
                     inventory, version, run_id, repository, tag_commit,
                     public_packages=None, allow_partial_public=False):
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
    result = {"version": version, "commit": tag_commit, "runId": run_id,
              "packages": len(expected), "symbols": len(symbols)}
    if allow_partial_public and public_packages is None:
        raise ValueError("Partial public verification requires a public package directory.")
    if public_packages is not None:
        public = _packages(public_packages, version, expected, symbols,
                           allow_partial=allow_partial_public)
        for key, path in sorted(public.items()):
            if _payloads(path) != _payloads(released[key]):
                raise ValueError(f"Public package content differs from the release: {path.name}")
        wanted = {(name, ".nupkg") for name in expected}
        wanted.update((name, ".snupkg") for name in symbols)
        result.update(
            publicPackages=sum(key[1] == ".nupkg" for key in public),
            publicSymbols=sum(key[1] == ".snupkg" for key in public),
            publicComplete=set(public) == wanted)
    return result


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "--check-compiler-assets":
        parser = argparse.ArgumentParser(description="Check release archives for unapproved compiler binaries.")
        parser.add_argument("--packages", type=Path, required=True)
        args = parser.parse_args(sys.argv[2:])
        try:
            result = verify_compiler_assets(args.packages)
        except (OSError, ValueError) as error:
            print(f"COMPILER_ASSET_CHECK_INVALID: {error}", file=sys.stderr)
            return 1
        print(json.dumps(result, sort_keys=True))
        return 0

    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("packages", "release-packages", "evidence", "release-run", "inventory"):
        parser.add_argument(f"--{name}", type=Path, required=True)
    for name in ("version", "run-id", "repository", "tag-commit"):
        parser.add_argument(f"--{name}", required=True)
    parser.add_argument("--public-packages", type=Path)
    parser.add_argument("--allow-partial-public", action="store_true")
    parser.add_argument("--wait-for-public", action="store_true")
    parser.add_argument("--public-attempts", type=int, default=PUBLIC_ATTEMPTS)
    parser.add_argument("--public-progress", type=Path)
    args = parser.parse_args()
    error = None
    result = None
    wait_started = False
    try:
        if args.public_progress is not None and not args.wait_for_public:
            raise ValueError("Public progress requires waiting for publication.")
        if args.wait_for_public and (
                args.public_packages is None or args.allow_partial_public):
            raise ValueError(
                "Waiting for publication requires a public package directory "
                "and complete public verification.")
        evidence = json.loads(args.evidence.read_text(encoding="utf-8-sig"))
        release_run = json.loads(args.release_run.read_text(encoding="utf-8-sig"))
        inventory = json.loads(args.inventory.read_text(encoding="utf-8-sig"))
        arguments = dict(
            packages=args.packages, release_packages=args.release_packages,
            evidence=evidence, release_run=release_run, inventory=inventory,
            version=args.version, run_id=args.run_id, repository=args.repository,
            tag_commit=args.tag_commit)
        if args.wait_for_public:
            verify_promotion(**arguments)
            wait_started = True
            wait_for_public_packages(args.release_packages, args.public_packages,
                                     inventory, args.version, attempts=args.public_attempts)
        result = verify_promotion(
            **arguments, public_packages=args.public_packages,
            allow_partial_public=args.allow_partial_public)
    except (OSError, ValueError) as caught:
        error = caught
    if args.public_progress is not None and wait_started:
        try:
            write_public_progress(
                args.public_progress, args.release_packages, args.public_packages,
                inventory, args.version, args.tag_commit, args.run_id,
                None if error is None else str(error))
        except (OSError, ValueError) as progress_error:
            message = f"Could not record public package progress: {progress_error}"
            error = ValueError(f"{error}; {message}" if error is not None else message)
    if error is not None:
        print(f"NUGET_PROMOTION_INVALID: {error}", file=sys.stderr)
        return 1
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
