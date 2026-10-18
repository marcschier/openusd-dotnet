#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.
"""Preserve pinned native source licenses in the runtime redistribution notices."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import pathlib
import re
import subprocess
import sys
import tarfile
import zipfile
from typing import Any


ROOT = pathlib.Path(__file__).resolve().parent.parent
OUTPUTS = {
    "openusd": "THIRD-PARTY.md",
    "cesium": "THIRD-PARTY-CESIUM.md",
    "physx": "THIRD-PARTY-PHYSX.md",
}
METADATA = ".openusd-native-notices.json"
VCPKG_BUILD_HELPERS = {
    "vcpkg-cmake", "vcpkg-cmake-config", "vcpkg-cmake-get-vars", "vcpkg-tool-meson",
}
DOCUMENT = re.compile(
    r"(?i)^(licen[cs]es?|copying|copyright|notices?|third.party|readme\.ijg)([_. -].*)?$"
)
EXCLUDED = {
    ".github", "docs", "documentation", "website", "test", "tests",
    "testenv", "testimages", "examples", "tutorials", "usdviewq",
}


class NoticeError(Exception):
    """A native distribution is missing verifiable licensing inputs."""


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def read_json(path: pathlib.Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def snapshot() -> Any:
    spec = importlib.util.spec_from_file_location(
        "openusd_notice_source_snapshot", ROOT / "eng" / "generate-sbom.py"
    )
    if spec is None or spec.loader is None:
        raise NoticeError("Cannot load the verified compiled-source snapshot.")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def profile(family: str, rid: str) -> dict[str, Any]:
    if family == "physx" and rid == "osx-arm64":
        raise NoticeError("There is no redistributed macOS PhysX runtime.")
    lock_file = {"openusd": "openusd.install.lock.json", "cesium": "cesium.lock.json",
                 "physx": "physx.lock.json"}[family]
    inputs = [
        "LICENSE", "NOTICE", "eng/generate-native-notices.py", f"eng/{lock_file}",
    ]
    value: dict[str, Any] = {"family": family, "rid": rid}
    if family == "openusd":
        inputs.append("eng/generate-sbom.py")
        data = snapshot()
        lock = read_json(ROOT / "eng" / lock_file)
        runtime_path = "eng/openusd-runtime-patches.lock.json"
        runtime = read_json(ROOT / runtime_path)
        if (digest((ROOT / runtime_path).read_bytes()) != lock["openUsd"]["runtimePatchLockSha256"].lower()
                or runtime.get("schemaVersion") != 1
                or runtime.get("sourceCommit") != lock["openUsd"]["commit"]):
            raise NoticeError("The SDK runtime source profile differs from its exact native lock.")
        inputs.append(runtime_path)
        for patch in runtime["patches"]:
            path = patch["path"].replace("\\", "/")
            if digest((ROOT / path).read_bytes()) != patch["sha256"].lower():
                raise NoticeError("An SDK runtime source patch differs from its reviewed profile.")
            inputs.append(path)
        value["sdkRuntimeSourceRepairs"] = {
            entry["path"].replace("\\", "/"): entry["afterSha256"]
            for patch in runtime["patches"] for entry in patch["files"]
        }
        manifests = [x for x in lock["vulkanSdk"]["repositories"]
                     if x["name"] == "shaderc-source-manifest"]
        if len(manifests) != 1 or manifests[0]["commit"] != data.SHADERC_SOURCE_MANIFEST_COMMIT:
            raise NoticeError("Refresh the verified shaderc source snapshot before generating notices.")
        value["compiledShaderSources"] = list(data.SHADERC_COMPILED_SOURCES)
        value["shadercManifestSha256"] = data.SHADERC_SOURCE_MANIFEST_SHA256
        try:
            repair = data.compiler_source_repair(lock)
        except data.SbomGenerationError as error:
            raise NoticeError(str(error)) from error
        inputs += [lock["vulkanSdk"]["sourcePatchLock"]] + [item["patch"] for item in repair["components"]]
        value["compilerSourceRepairs"] = [
            {"component": item["component"], "sourceCommit": item["sourceCommit"],
             "files": {entry["file"]: entry["afterSha256"] for entry in item["files"]}}
            for item in repair["components"]
        ]
    elif family == "cesium":
        inputs.append("eng/sbom/cesium-vcpkg-components.lock.json")
    else:
        inputs.append("eng/build-physx-native.ps1")
    value["inputs"] = {name: digest((ROOT / name).read_bytes()) for name in inputs}
    return value


def document_path(name: str) -> bool:
    path = pathlib.PurePosixPath(name)
    if path.is_absolute() or ".." in path.parts or "\n" in name or "\r" in name:
        raise NoticeError(f"Unsafe license member: {name!r}")
    parts = path.parts[1:]
    if not parts or any(part.lower() in EXCLUDED for part in parts[:-1]):
        return False
    return bool(
        (len(parts) == 1 and DOCUMENT.fullmatch(parts[-1]))
        or any(part.lower() in {"licenses", "licences"} for part in parts[:-1])
        or (DOCUMENT.fullmatch(parts[-1]) and
            any(part.lower() in {"third_party", "thirdparty", "3rdparty", "ext", "vendor"}
                for part in parts[:-1]))
    )


def leading_license(raw: bytes) -> bytes | None:
    match = re.match(
        rb"\A(?:\xef\xbb\xbf)?(?:(?:/\*.*?\*/)|(?://[^\r\n]*(?:\r?\n|$))|\s)+",
        raw, re.DOTALL,
    )
    if match is None:
        return None
    comment = match.group()
    lower = comment.lower()
    if (b"copyright" not in lower or
            not any(word in lower for word in (b"redistribution", b"permission is hereby", b"license"))):
        return None
    if b"pixar" in lower and b"redistribution" not in lower and b"permission is hereby" not in lower:
        return None
    return comment


def archive_documents(
    cache: pathlib.Path, item: dict[str, Any], *, openusd: bool = False
) -> list[tuple[str, bytes]]:
    path = cache / item["archiveName"]
    expected = item.get("archiveSha256", item.get("sha256", "")).lower()
    if not path.is_file() or digest(path.read_bytes()) != expected:
        raise NoticeError(f"Missing or mismatched locked source archive: {path}")
    records: list[tuple[str, bytes]] = []
    prefixes: set[str] = set()
    seen_comments: set[str] = set()

    def add(name: str, data: bytes) -> None:
        if document_path(name):
            records.append((name, data))
        elif openusd and name.endswith((".h", ".hpp", ".cpp", ".cc", ".c")):
            parts = pathlib.PurePosixPath(name).parts
            if any(part.lower() in EXCLUDED for part in parts):
                return
            comment = leading_license(data)
            if comment is not None:
                for vendor in ("pxrDoubleConversion", "pxrLZ4", "pxrTslRobinMap", "pxrCLI11"):
                    if vendor in parts:
                        prefixes.add(vendor)
                identity = digest(comment)
                if identity not in seen_comments:
                    seen_comments.add(identity)
                    records.append((name + " (leading license comment)", comment))

    if zipfile.is_zipfile(path):
        with zipfile.ZipFile(path) as archive:
            for member in archive.infolist():
                if not member.is_dir() and document_path(member.filename):
                    add(member.filename, archive.read(member))
    else:
        with tarfile.open(path, "r:*") as archive:
            for member in archive:
                if not member.isfile():
                    continue
                selected = document_path(member.name)
                header = openusd and member.name.endswith((".h", ".hpp", ".cpp", ".cc", ".c"))
                if selected or header:
                    stream = archive.extractfile(member)
                    if stream is None:
                        raise NoticeError(f"Cannot read pinned license member: {member.name}")
                    add(member.name, stream.read())
    if openusd and prefixes != {"pxrDoubleConversion", "pxrLZ4", "pxrTslRobinMap", "pxrCLI11"}:
        raise NoticeError("The pinned OpenUSD vendored license-comment inventory changed; review it.")
    if not records:
        raise NoticeError(f"No complete license documentation found in {path.name}.")
    return sorted(records)


def git(*arguments: str, cwd: pathlib.Path | None = None) -> bytes:
    result = subprocess.run(["git", *arguments], cwd=cwd, capture_output=True, check=False)
    if result.returncode:
        raise NoticeError(result.stderr.decode("utf-8", errors="replace").strip())
    return result.stdout


def git_documents(
    native: pathlib.Path, cache: pathlib.Path, name: str, url: str, commit: str,
    existing: pathlib.Path | None = None,
) -> list[tuple[str, bytes]]:
    repository = existing
    if repository is None or not (repository / ".git").exists():
        repository = cache / "notice-sources" / f"{name}-{commit}"
        if not (repository / ".git").exists():
            repository.parent.mkdir(parents=True, exist_ok=True)
            git("clone", "--filter=blob:none", "--no-checkout", "--depth", "1",
                url, str(repository), cwd=native)
    probe = subprocess.run(
        ["git", "cat-file", "-e", commit + "^{commit}"], cwd=repository,
        capture_output=True, check=False,
    )
    if probe.returncode:
        git("fetch", "--depth", "1", "origin", commit, cwd=repository)
    names = git("ls-tree", "-r", "--name-only", commit, cwd=repository).decode("utf-8").splitlines()
    records = []
    for relative in names:
        if document_path("source/" + relative):
            records.append((relative, git("show", f"{commit}:{relative}", cwd=repository)))
    if not records:
        raise NoticeError(f"No pinned license texts for {name}@{commit}.")
    return records


def component(name: str, source: str, documents: list[tuple[str, bytes]]) -> dict[str, Any]:
    if not documents:
        raise NoticeError(f"Empty native license set: {name}")
    return {"name": name, "source": source, "documents": documents}


def installed_documents(path: pathlib.Path) -> tuple[str, list[tuple[str, bytes]]]:
    port = path.parent
    spdx = read_json(port / "vcpkg.spdx.json")
    identities = [item for item in spdx.get("packages", []) if item.get("SPDXID") == "SPDXRef-port"]
    if len(identities) != 1 or identities[0].get("name") != port.name:
        raise NoticeError(f"Missing installed port identity for {port.name}.")
    version = identities[0].get("versionInfo")
    if not isinstance(version, str) or not version:
        raise NoticeError(f"Missing installed port version for {port.name}.")
    paths = {path}
    for candidate in port.rglob("*"):
        relative = candidate.relative_to(port)
        if candidate.is_file() and (
            DOCUMENT.fullmatch(candidate.name) or
            any(part.lower() in {"licenses", "licences"} for part in relative.parts[:-1])
        ):
            if not candidate.resolve().is_relative_to(port.resolve()):
                raise NoticeError(f"Installed license escapes its dependency directory: {candidate}")
            paths.add(candidate)
    documents = []
    for candidate in sorted(paths):
        relative = "share/" + port.name + "/" + candidate.relative_to(port).as_posix()
        entries = [item for item in spdx.get("files", [])
                   if item.get("fileName", "").removeprefix("./") == relative]
        hashes = [item["checksumValue"].lower() for entry in entries
                  for item in entry.get("checksums", []) if item.get("algorithm") == "SHA256"]
        raw = candidate.read_bytes()
        if len(hashes) != 1 or digest(raw) != hashes[0]:
            raise NoticeError(f"Installed license differs from its vcpkg file checksum: {relative}")
        documents.append((relative, raw))
    return version, documents


def collect(args: argparse.Namespace) -> list[dict[str, Any]]:
    components = [component("OpenUsd project", "project LICENSE and NOTICE", [
        (name, (ROOT / name).read_bytes()) for name in ("LICENSE", "NOTICE")
    ])]
    lock_name = {"openusd": "openusd", "cesium": "cesium", "physx": "physx"}[args.family]
    lock = read_json(ROOT / "eng" / f"{lock_name}.lock.json")
    if args.family == "openusd":
        upstream = lock["openUsd"]
        components.append(component("OpenUSD", upstream["archiveUrl"] +
                                    "; project SDK source profile " + upstream["runtimePatchLockSha256"],
                                    archive_documents(args.cache_root, upstream, openusd=True)))
        for item in lock["dependencies"]:
            if args.rid in item["platforms"]:
                components.append(component(item["name"], item["url"],
                                            archive_documents(args.cache_root, item)))
        vulkan_root = args.native_root / "src" / f"vulkan-sdk-{lock['vulkanSdk']['version']}"
        if args.rid != "osx-arm64":
            for item in lock["vulkanSdk"]["repositories"]:
                if item["name"] != "shaderc-source-manifest":
                    components.append(component(
                        item["name"], item["url"] + "@" + item["commit"],
                        git_documents(args.native_root, args.cache_root, item["name"],
                                      item["url"], item["commit"], vulkan_root / item["name"]),
                    ))
        data = snapshot()
        source = vulkan_root / "shaderc" / "src"
        directories = {
            "shaderc": source,
            "glslang": source / "third_party" / "glslang",
            "SPIRV-Tools": source / "third_party" / "spirv-tools",
            "SPIRV-Headers": source / "third_party" / "spirv-tools" / "external" / "spirv-headers",
        }
        for name, repository, commit in data.SHADERC_COMPILED_SOURCES:
            url = f"https://github.com/{repository}.git"
            source_reference = url + "@" + commit
            if name in ("SPIRV-Tools", "shaderc"):
                source_reference += "; project source repair " + lock["vulkanSdk"]["sourcePatchLockSha256"]
            components.append(component(
                name, source_reference,
                git_documents(args.native_root, args.cache_root, name, url, commit, directories[name]),
            ))
    else:
        if args.share_root is None or not args.share_root.is_dir():
            raise NoticeError("The actual locked vcpkg share directory is required.")
        if args.family == "cesium":
            upstream = lock["cesiumNative"]
            components.append(component("cesium-native", upstream["archiveUrl"],
                                        archive_documents(args.cache_root, upstream)))
            copyright_files = sorted(args.share_root.glob("*/copyright"))
        else:
            if lock["physx"]["license"] != "BSD-3-Clause":
                raise NoticeError("Review the PhysX source/GPU redistribution distinction for this license.")
            copyright_files = [args.share_root / "physx" / "copyright"]
        if not copyright_files:
            raise NoticeError("No installed dependency copyright files found.")
        versions = {}
        if args.family == "cesium":
            resolved = read_json(ROOT / "eng" / "sbom" / "cesium-vcpkg-components.lock.json")
            versions = {item["name"]: item["version"] for item in resolved["components"]}
            if set(versions) - VCPKG_BUILD_HELPERS - {path.parent.name for path in copyright_files}:
                raise NoticeError("The installed Cesium dependency copyright inventory is incomplete.")
        for path in copyright_files:
            if not path.is_file() or not path.stat().st_size:
                raise NoticeError(f"Missing full installed dependency notice: {path}")
            version, documents = installed_documents(path)
            name = path.parent.name
            if args.family == "cesium" and name not in VCPKG_BUILD_HELPERS and versions.get(name) != version:
                raise NoticeError(f"Installed {name}@{version} does not match the resolved source snapshot.")
            if args.family == "physx":
                vcpkg = args.native_root / ".cache" / ("vcpkg-" + lock["vcpkg"]["baseline"][:12])
                port = json.loads(git(
                    "show", lock["vcpkg"]["baseline"] + ":ports/physx/vcpkg.json", cwd=vcpkg
                ))
                expected = next((port[key] for key in ("version", "version-semver", "version-string")
                                 if key in port), None)
                if port.get("port-version"):
                    expected = f"{expected}#{port['port-version']}"
                if version != expected:
                    raise NoticeError("Installed PhysX copyright belongs to another port version.")
            source = (f"vcpkg baseline {lock['vcpkg']['baseline']}; installed port {version}"
                      + ("; build-tool documentation, not an embedded runtime"
                         if name in VCPKG_BUILD_HELPERS else ""))
            if args.family == "cesium":
                source += "; overlay/source graph " + lock["cesiumNative"]["archiveSha256"]
            components.append(component(name, source, documents))
    return components


def write(args: argparse.Namespace) -> None:
    records = collect(args)
    output = bytearray(
        b"# OpenUsd native third-party notices\n\n"
        b"Project-owned aggregate from exact locked source licenses and notices.\n"
        b"Upstream documentation may also describe platform alternatives; inclusion is not "
        b"a claim that every optional source is embedded in each runtime package.\n\n"
    )
    if args.family == "physx":
        output.extend(
            b"PhysX source-built CPU libraries are redistributed under BSD-3-Clause.\n"
            b"Proprietary NVIDIA GPU/device modules are NOT redistributed.\n\n"
        )
    if args.preamble_file is not None:
        preamble = args.preamble_file.read_bytes()
        preamble.decode("utf-8-sig")
        if not preamble.strip():
            raise NoticeError("The native redistribution overview is empty.")
        output.extend(preamble)
        output.extend(b"\n\n")
    prefix_length = len(output)
    prefix_hash = digest(output)
    metadata_components = []
    for record in records:
        output.extend(f"## {record['name']}\n\nSource: {record['source']}\n\n".encode("utf-8"))
        documents = []
        for name, raw in record["documents"]:
            if not raw.strip() or len(raw) > 4 * 1024 * 1024:
                raise NoticeError(f"Empty or oversized license text: {record['name']}/{name}")
            raw.decode("utf-8-sig")
            output.extend(f"### {name}\n\n".encode("utf-8"))
            output.extend(raw)
            output.extend(b"\n\n")
            documents.append({"path": name, "sha256": digest(raw), "bytes": len(raw)})
        metadata_components.append({
            "name": record["name"], "source": record["source"], "documents": documents,
        })
    args.install_root.mkdir(parents=True, exist_ok=True)
    (args.install_root / OUTPUTS[args.family]).write_bytes(output)
    metadata = {
        "schemaVersion": 1, "profile": profile(args.family, args.rid),
        "output": OUTPUTS[args.family], "sha256": digest(output),
        "prefixBytes": prefix_length, "prefixSha256": prefix_hash,
        "components": metadata_components,
    }
    (args.install_root / METADATA).write_text(
        json.dumps(metadata, indent=2) + "\n", encoding="utf-8"
    )
    verify(args)


def verify(args: argparse.Namespace) -> dict[str, Any]:
    metadata = read_json(args.install_root / METADATA)
    if (metadata.get("schemaVersion") != 1 or
            metadata.get("profile") != json.loads(json.dumps(profile(args.family, args.rid))) or
            metadata.get("output") != OUTPUTS[args.family]):
        raise NoticeError("Native notice provenance does not match current locked inputs.")
    raw = (args.install_root / OUTPUTS[args.family]).read_bytes()
    if not raw.startswith(b"# OpenUsd native third-party notices\n") or digest(raw) != metadata.get("sha256"):
        raise NoticeError("Native notice aggregate is absent, stale or modified.")
    components = metadata.get("components", [])
    if not components or any(not item.get("documents") for item in components):
        raise NoticeError("Native notice aggregate is missing complete source license documents.")
    if len({item["name"] for item in components}) != len(components):
        raise NoticeError("Duplicate native notice component identity.")
    cursor = metadata.get("prefixBytes")
    if (not isinstance(cursor, int) or not 0 < cursor < len(raw) or
            digest(raw[:cursor]) != metadata.get("prefixSha256")):
        raise NoticeError("Native redistribution overview is missing or modified.")
    for item in components:
        heading = f"## {item['name']}\n\nSource: {item['source']}\n\n".encode("utf-8")
        if raw[cursor:cursor + len(heading)] != heading:
            raise NoticeError("Native notice source sections differ from their manifest.")
        cursor += len(heading)
        for document in item["documents"]:
            heading = f"### {document['path']}\n\n".encode("utf-8")
            if raw[cursor:cursor + len(heading)] != heading:
                raise NoticeError("Native notice document is absent or out of order.")
            cursor += len(heading)
            length = document["bytes"]
            if not isinstance(length, int) or not 0 < length <= 4 * 1024 * 1024:
                raise NoticeError("Invalid native notice document length.")
            body = raw[cursor:cursor + length]
            if digest(body) != document["sha256"] or raw[cursor + length:cursor + length + 2] != b"\n\n":
                raise NoticeError("A complete native source license was truncated or modified.")
            cursor += length + 2
    if cursor != len(raw):
        raise NoticeError("Unexpected native notice payload after the source documents.")
    if args.family == "openusd":
        expected = {"OpenUsd project", "OpenUSD"}
        lock = read_json(ROOT / "eng" / "openusd.lock.json")
        expected.update(item["name"] for item in lock["dependencies"] if args.rid in item["platforms"])
        expected.update(item[0] for item in snapshot().SHADERC_COMPILED_SOURCES)
        if args.rid != "osx-arm64":
            expected.update(item["name"] for item in lock["vulkanSdk"]["repositories"]
                            if item["name"] != "shaderc-source-manifest")
        if {item["name"] for item in components} != expected:
            raise NoticeError("Native notice component inventory is incomplete or unexpected.")
    elif args.family == "cesium":
        names = {item["name"] for item in components}
        resolved = read_json(ROOT / "eng" / "sbom" / "cesium-vcpkg-components.lock.json")
        expected = {"OpenUsd project", "cesium-native"} | {
            item["name"] for item in resolved["components"]
        }
        if expected - names or names - expected - VCPKG_BUILD_HELPERS:
            raise NoticeError("Cesium notices lack the upstream license or installed dependency notices.")
    elif {item["name"] for item in components} != {"OpenUsd project", "physx"}:
        raise NoticeError("PhysX notices lack the source-built SDK license.")
    if args.package is not None:
        name = "THIRD-PARTY-PHYSX.md" if args.family == "physx" else "THIRD-PARTY-NOTICES.md"
        with zipfile.ZipFile(args.package) as package:
            if package.namelist().count(name) != 1 or package.read(name) != raw:
                raise NoticeError(f"Packed native notice differs from verified source: {args.package}")
    print(f"Verified {args.family} {args.rid} complete native notices: "
          f"{len(components)} source/dependency groups, SHA256 {digest(raw)}.")
    return metadata


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("write", "verify"))
    parser.add_argument("--family", choices=OUTPUTS, default="openusd")
    parser.add_argument("--rid", required=True, choices=("win-x64", "linux-x64", "osx-arm64"))
    parser.add_argument("--install-root", type=pathlib.Path, required=True)
    parser.add_argument("--native-root", type=pathlib.Path, default=ROOT / "native")
    parser.add_argument("--cache-root", type=pathlib.Path, default=ROOT / "native" / "downloads")
    parser.add_argument("--share-root", type=pathlib.Path)
    parser.add_argument("--package", type=pathlib.Path)
    parser.add_argument("--preamble-file", type=pathlib.Path)
    args = parser.parse_args()
    try:
        if args.operation == "write":
            write(args)
        else:
            verify(args)
    except (NoticeError, OSError, ValueError, zipfile.BadZipFile, tarfile.TarError) as error:
        print(f"Native notice validation failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
