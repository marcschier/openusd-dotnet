#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import tarfile
import xml.etree.ElementTree as ET
import zipfile


PACKAGE_ID = "Silk.NET.SPIRV.Cross.Native"
PACKAGE_VERSION = "2.23.0"
PACKAGE_SHA256 = "5c8ea3fcf588584950be018cd47e728ac2d7d4cbbe07b205c0cc6a47c53a06a9"
PACKAGE_SOURCE_COMMIT = "94605142f7b7bd6e69c9201e8e721d245c69eb7e"
PACKAGE_SOURCE_GITLINK = "998146d76fc5cbb2726f44c55e25fa28a573a782"
PRODUCER_COMMIT = "d01d143952ce161c151f771d02d5457d13235f0a"
PRODUCER_SOURCE_COMMIT = "d8e3e2b141b8c8a167b2e3984736a6baacff316c"
PRODUCER_RUN_ID = "21116710278"
PRODUCER_BINARY_COMMIT = "6305f138f92df6132e566072b62a3d8f41fba9aa"
RECIPE_SHA256 = "49743561b27b6001d7ab977631ea0f72565a96cd42100817317610a6ebccf071"
ZIG_VERSION = "0.15.2"
ZIG_ARCHIVE_SHA256 = "02aa270f183da276e5b5920b1dac44a63f1a49e55050ebde3aecc9eb82f93239"
ZIG_ARCHIVE_BINARY = "zig-x86_64-linux-0.15.2/zig"
GITLINK_PATH = "build/submodules/SPIRV-Cross"
RECIPE_PATH = "build/nuke/Native/SPIRVCross.cs"
TARGETS = {
    "win-x64": {
        "target": "x86_64-windows",
        "package": "runtimes/win-x64/native/spirv-cross.dll",
        "built": "zig-out/bin/spirv-cross.dll",
        "sha256": "1b00f8c042d4eb4060b1f4b2c1ee1ef2f0935c7857efe60c62d9cea236ea834e",
        "gitBlob": "b360a178da44d16337d61cbac4840c78632a4f34",
    },
    "linux-x64": {
        "target": "x86_64-linux-gnu.2.17",
        "package": "runtimes/linux-x64/native/libspirv-cross.so",
        "built": "zig-out/lib/libspirv-cross.so",
        "sha256": "513de25783914c2193577e36c5623543eaf970b4e8c5abb1ed97e002aa5f874c",
        "gitBlob": "eca59b8dfa87e8220a633821219d04d6f02701b3",
    },
    "osx-arm64": {
        "target": "aarch64-macos",
        "package": "runtimes/osx-arm64/native/libspirv-cross.dylib",
        "built": "zig-out/lib/libspirv-cross.dylib",
        "sha256": "071c4ef2a38c5fd78ccdda419cb79ec4fded6f85ed3bdeeae5bb09c3401eb093",
        "gitBlob": "29bfc9fc32bb9ff2b19b3358d17a6b3b5f729b72",
    },
}


def file_sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def git(root, *arguments):
    result = subprocess.run(
        ["git", "-C", str(root), *arguments],
        check=True, capture_output=True, text=True,
    )
    return result.stdout.strip()


def verify_checkout(root, expected_commit, label):
    if git(root, "rev-parse", "HEAD") != expected_commit:
        raise ValueError(f"{label} checkout does not match the pinned commit.")
    if git(root, "status", "--porcelain=v1", "--untracked-files=all"):
        raise ValueError(f"{label} checkout is not clean.")


def verify_source_checkouts(producer, source, rid):
    verify_checkout(producer, PRODUCER_COMMIT, "Silk.NET producer")
    verify_checkout(source, PRODUCER_SOURCE_COMMIT, "SPIRV-Cross source")
    entry = git(producer, "ls-tree", "HEAD", "--", GITLINK_PATH)
    if entry != f"160000 commit {PRODUCER_SOURCE_COMMIT}\t{GITLINK_PATH}":
        raise ValueError("The producer's SPIRV-Cross gitlink does not match its source checkout.")
    package_entry = git(producer, "ls-tree", PACKAGE_SOURCE_COMMIT, "--", GITLINK_PATH)
    if package_entry != f"160000 commit {PACKAGE_SOURCE_GITLINK}\t{GITLINK_PATH}":
        raise ValueError("The package commit's SPIRV-Cross gitlink does not match the recorded pin.")
    binary_header = git(producer, "cat-file", "-p", PRODUCER_BINARY_COMMIT).split("\n\n", 1)[0]
    parents = [line.removeprefix("parent ") for line in binary_header.splitlines()
               if line.startswith("parent ")]
    if parents != [PRODUCER_COMMIT]:
        raise ValueError("The binary commit was not produced directly from the pinned producer.")
    asset_path = f"src/Native/{PACKAGE_ID}/{TARGETS[rid]['package']}"
    asset_entry = git(producer, "ls-tree", PRODUCER_BINARY_COMMIT, "--", asset_path)
    if asset_entry != f"100755 blob {TARGETS[rid]['gitBlob']}\t{asset_path}":
        raise ValueError(f"The {rid} package asset does not match the producer binary commit.")


def verify_project(root, expected_commit):
    if not re.fullmatch(r"[0-9a-f]{40}", expected_commit):
        raise ValueError("A full lowercase project commit is required.")
    if git(root, "rev-parse", "HEAD") != expected_commit:
        raise ValueError("The project checkout does not match the expected source commit.")
    if git(root, "status", "--porcelain=v1", "--untracked-files=no"):
        raise ValueError("The project checkout has modified tracked files.")
    packages = ET.parse(root / "Directory.Packages.props").getroot()
    versions = [
        item.get("Version")
        for item in packages.iter()
        if item.tag.rsplit("}", 1)[-1] == "PackageVersion"
        and item.get("Include") == PACKAGE_ID
    ]
    if versions != [PACKAGE_VERSION]:
        raise ValueError("The selected SPIRV-Cross package version does not match the pinned evidence.")


def verify_package(path, rid):
    if file_sha256(path) != PACKAGE_SHA256:
        raise ValueError("The SPIRV-Cross NuGet archive hash does not match the pin.")
    target = TARGETS[rid]
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        nuspecs = [
            item for item in entries
            if "/" not in item.filename and "\\" not in item.filename
            and item.filename.lower().endswith(".nuspec")
        ]
        assets = [item for item in entries if item.filename == target["package"]]
        if len(nuspecs) != 1 or nuspecs[0].file_size > 1024 * 1024 or len(assets) != 1:
            raise ValueError("The package must contain exactly one bounded nuspec and selected native asset.")
        metadata = ET.fromstring(archive.read(nuspecs[0])).find("{*}metadata")
        repository = None if metadata is None else metadata.find("{*}repository")
        if (
            metadata is None
            or metadata.findtext("{*}id") != PACKAGE_ID
            or metadata.findtext("{*}version") != PACKAGE_VERSION
            or repository is None
            or repository.get("commit") != PACKAGE_SOURCE_COMMIT
            or repository.get("url") != "https://github.com/KhronosGroup/SPIRV-Cross"
        ):
            raise ValueError("The package identity or recorded source metadata is unexpected.")
        asset = archive.read(assets[0])
    digest = hashlib.sha256(asset).hexdigest()
    blob = hashlib.sha1(f"blob {len(asset)}\0".encode("ascii") + asset).hexdigest()
    if digest != target["sha256"] or blob != target["gitBlob"]:
        raise ValueError(f"The {rid} package asset does not match the published binary pins.")
    return digest


def get_recipe(producer):
    content = (producer / RECIPE_PATH).read_text(encoding="utf-8")
    matches = re.findall(r'const string SPIRVCrossBuildScript = @"((?:[^"]|"")*)";', content, re.S)
    if len(matches) != 1:
        raise ValueError("The producer must contain exactly one SPIRV-Cross build recipe.")
    recipe = matches[0].replace('""', '"')
    if hashlib.sha256(recipe.encode("utf-8")).hexdigest() != RECIPE_SHA256:
        raise ValueError("The producer's SPIRV-Cross build recipe does not match the pin.")
    return recipe


def verify_zig(archive, executable):
    if file_sha256(archive) != ZIG_ARCHIVE_SHA256:
        raise ValueError("The Zig archive hash does not match the pin.")
    if executable.name != "zig" or executable.is_symlink() or not executable.is_file():
        raise ValueError("The Zig executable is missing or is not a regular file.")
    root = executable.parent
    if root.is_symlink():
        raise ValueError("The Zig toolchain directory must not be a symlink.")
    actual_paths = set()
    for directory, directories, files in os.walk(root, followlinks=False):
        for name in directories + files:
            path = Path(directory) / name
            if path.is_symlink() or not (path.is_dir() or path.is_file()):
                raise ValueError("The Zig toolchain contains a symlink or special file.")
            actual_paths.add(path.relative_to(root))
    archive_root = PurePosixPath(ZIG_ARCHIVE_BINARY).parts[0]
    archive_binary = Path(*PurePosixPath(ZIG_ARCHIVE_BINARY).parts[1:])
    expected_paths = set()
    seen_paths = set()
    with tarfile.open(archive, "r:xz") as bundle:
        for member in bundle:
            parts = PurePosixPath(member.name).parts
            if not parts or parts[0] != archive_root or ".." in parts:
                raise ValueError("The Zig archive contains an unexpected path.")
            if len(parts) == 1:
                if not member.isdir():
                    raise ValueError("The Zig archive root is not a directory.")
                continue
            relative = Path(*parts[1:])
            if relative in seen_paths:
                raise ValueError("The Zig archive contains duplicate paths.")
            seen_paths.add(relative)
            expected_paths.add(relative)
            expected_paths.update(parent for parent in relative.parents if parent != Path("."))
            path = root / relative
            if member.isdir():
                if not path.is_dir():
                    raise ValueError(f"The Zig toolchain directory '{relative}' is missing.")
            elif member.isfile() and member.size <= 512 * 1024 * 1024:
                stream = bundle.extractfile(member)
                if stream is None or not path.is_file() or path.stat().st_size != member.size:
                    raise ValueError(f"The Zig toolchain file '{relative}' does not match the archive.")
                with stream:
                    archive_hash = hashlib.file_digest(stream, "sha256").hexdigest()
                if file_sha256(path) != archive_hash:
                    raise ValueError(f"The Zig toolchain file '{relative}' does not match the archive.")
            else:
                raise ValueError("The Zig archive contains a special or oversized file.")
    if archive_binary not in seen_paths or actual_paths != expected_paths:
        raise ValueError("The extracted Zig toolchain does not match the pinned archive.")
    result = subprocess.run(
        [str(executable), "version"], check=True, capture_output=True, text=True,
    )
    if result.stdout.strip() != ZIG_VERSION:
        raise ValueError("The Zig executable reports an unexpected version.")


def rebuild(source, executable, rid, recipe):
    recipe_path = source / "build.zig"
    if recipe_path.exists() or recipe_path.is_symlink() or (source / "zig-out").exists():
        raise ValueError("The SPIRV-Cross source already contains generated build files.")
    recipe_path.write_text(recipe, encoding="utf-8", newline="\n")
    target = TARGETS[rid]
    subprocess.run(
        [
            str(executable), "build", "-Doptimize=ReleaseFast",
            f"-Dtarget={target['target']}", "-j2",
        ],
        cwd=source, check=True,
    )
    output = source / target["built"]
    if not output.is_file() or output.is_symlink():
        raise ValueError(f"The {rid} rebuild did not produce the expected native asset.")
    return file_sha256(output)


def verify(*, project, project_commit, producer, source, package, zig_archive,
           zig, rid, receipt, run_id, run_attempt):
    if rid not in TARGETS:
        raise ValueError("A supported release RID is required.")
    if not re.fullmatch(r"[1-9][0-9]*", run_id) or not re.fullmatch(r"[1-9][0-9]*", run_attempt):
        raise ValueError("Positive workflow run and attempt IDs are required.")
    if receipt.exists() or receipt.is_symlink():
        raise ValueError("The provenance receipt already exists.")
    verify_project(project, project_commit)
    verify_source_checkouts(producer, source, rid)
    package_hash = verify_package(package, rid)
    recipe = get_recipe(producer)
    verify_zig(zig_archive, zig)
    actual_hash = rebuild(source, zig, rid, recipe)
    evidence = {
        "schemaVersion": 1,
        "projectCommit": project_commit,
        "workflowRunId": run_id,
        "workflowRunAttempt": run_attempt,
        "rid": rid,
        "packageId": PACKAGE_ID,
        "packageVersion": PACKAGE_VERSION,
        "packageArchiveSha256": PACKAGE_SHA256,
        "packageRecordedRepository": "https://github.com/KhronosGroup/SPIRV-Cross",
        "packageRecordedCommit": PACKAGE_SOURCE_COMMIT,
        "packageCommitGitlink": PACKAGE_SOURCE_GITLINK,
        "producerCommit": PRODUCER_COMMIT,
        "producerRunId": PRODUCER_RUN_ID,
        "producerBinaryCommit": PRODUCER_BINARY_COMMIT,
        "producerSourceCommit": PRODUCER_SOURCE_COMMIT,
        "recipeSha256": RECIPE_SHA256,
        "zigVersion": ZIG_VERSION,
        "zigArchiveSha256": ZIG_ARCHIVE_SHA256,
        "target": TARGETS[rid]["target"],
        "packageAssetSha256": package_hash,
        "packageAssetGitBlob": TARGETS[rid]["gitBlob"],
        "rebuiltAssetSha256": actual_hash,
        "byteIdentical": actual_hash == package_hash,
        "licenseReview": "not-evaluated",
    }
    receipt.parent.mkdir(parents=True, exist_ok=True)
    receipt.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    if not evidence["byteIdentical"]:
        raise ValueError(f"The rebuilt {rid} binary differs from the NuGet asset.")
    return evidence


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--project-commit", required=True)
    parser.add_argument("--producer", type=Path, required=True)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--zig-archive", type=Path, required=True)
    parser.add_argument("--zig", type=Path, required=True)
    parser.add_argument("--rid", choices=TARGETS, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--run-id", default=os.environ.get("GITHUB_RUN_ID", ""))
    parser.add_argument("--run-attempt", default=os.environ.get("GITHUB_RUN_ATTEMPT", ""))
    args = parser.parse_args()
    evidence = verify(
        project=args.project, project_commit=args.project_commit,
        producer=args.producer, source=args.source, package=args.package,
        zig_archive=args.zig_archive, zig=args.zig, rid=args.rid,
        receipt=args.receipt, run_id=args.run_id, run_attempt=args.run_attempt,
    )
    print(f"SPIRV_CROSS_SOURCE_REPRODUCED rid={args.rid} sha256={evidence['rebuiltAssetSha256']}")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, KeyError, ET.ParseError, tarfile.TarError,
            zipfile.BadZipFile, subprocess.CalledProcessError) as error:
        print(f"SPIRV_CROSS_PROVENANCE_ERROR: {error}", file=sys.stderr)
        sys.exit(1)
