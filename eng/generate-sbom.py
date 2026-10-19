#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.
"""Generate the release CycloneDX SBOM from pinned repository inputs."""

from __future__ import annotations

import argparse
import ast
import difflib
import hashlib
import json
import re
import sys
import tarfile
import time
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUTPUT = REPO_ROOT / "eng" / "sbom" / "openusd-release.cdx.json"
DEFAULT_VCPKG_COMPONENTS = REPO_ROOT / "eng" / "sbom" / "cesium-vcpkg-components.lock.json"
CYCLONEDX_SPEC_VERSION = "1.6"
VCPKG_CACHE_ROOT = REPO_ROOT / "artifacts" / "sbom" / "vcpkg-metadata"

PINNED_INPUTS = [
    "version.json",
    "global.json",
    "Directory.Packages.props",
    "eng/openusd.install.lock.json",
    "eng/openusd-runtime-patches.lock.json",
    "eng/patches/openusd/unresolved-layer-reader-lookup.patch",
    "eng/patches/openusd/tf-refptr-last-release-acquire.patch",
    "eng/patches/openusd/storm-aov-restoration.patch",
    "eng/patches/openusd/strict-compiler-portability.patch",
    "eng/openusd-storage-admission.lock.json",
    "eng/patches/openusd/storage-admission-v1.patch",
    "eng/patches/openusd/render-product-hash-adl.patch",
    "eng/cesium.lock.json",
    "eng/physx.lock.json",
    "eng/mdl.lock.json",
    "eng/shaders/toolchain.lock.json",
    "eng/pack-packages.ps1",
    "eng/publish-viewer-bundle.ps1",
    "eng/sbom/cesium-vcpkg-components.lock.json",
    "eng/generate-sbom.py",
    "eng/generate-native-notices.py",
    "eng/shaderc-source-patches.lock.json",
    "eng/patches/shaderc/spirv-tools-explicit-case-breaks.patch",
    "eng/patches/shaderc/shaderc-zero-padded-string-copy.patch",
]

SHADERC_SOURCE_MANIFEST_COMMIT = "75137cca3b41a59b8b3e5c42177da8a9490c042f"
SHADERC_SOURCE_MANIFEST_SHA256 = "8ffa915811760a266e2eb5b683eff227bb0fe077ea722fc5b9ac7d681f60e534"
SHADERC_COMPILED_SOURCES = (
    ("shaderc", "google/shaderc", "b64fd42c4a2a9d501c47aca5d3571f7673a5e9cc"),
    ("glslang", "KhronosGroup/glslang", "e1b562a8bed273a02f30b59b66a5d499793cede5"),
    ("SPIRV-Tools", "KhronosGroup/SPIRV-Tools", "ef96ed763b43b59b33b31b362f09a02b729fa1c9"),
    ("SPIRV-Headers", "KhronosGroup/SPIRV-Headers", "04fd3caa1e8267e4d95c806cad901181728e1006"),
)

VCPKG_TRANSITIVE_EXCLUDE = {
    # Build-system helper ports are fetched by vcpkg but are not redistributed in
    # the NuGet runtime packages or Viewer bundle.
    "vcpkg-cmake",
    "vcpkg-cmake-config",
    "vcpkg-tool-meson",
}


class SbomGenerationError(Exception):
    """Raised for generation failures that should be reported without a traceback."""


def read_json(relative: str) -> dict[str, Any]:
    return json.loads((REPO_ROOT / relative).read_text(encoding="utf-8"))


def sha256_file(path: Path) -> str:
    content = path.read_bytes()
    return hashlib.sha256(content.replace(b"\r\n", b"\n")).hexdigest()


def compiler_source_repair(lock: dict[str, Any]) -> dict[str, Any]:
    sdk = lock["vulkanSdk"]
    if sha256_file(REPO_ROOT / sdk["sourcePatchLock"]) != sdk["sourcePatchLockSha256"].lower():
        raise SbomGenerationError("The compiler source-repair profile differs from the native lock.")
    repair = read_json(sdk["sourcePatchLock"])
    components = repair.get("components", [])
    expected = {name.lower(): commit for name, _, commit in SHADERC_COMPILED_SOURCES}
    if (repair.get("$schemaVersion") != 3 or not components or
            len({item["component"] for item in components}) != len(components) or
            repair.get("sourceManifestCommit") != SHADERC_SOURCE_MANIFEST_COMMIT):
        raise SbomGenerationError("The exact reviewed compiler source repair is absent or modified.")
    for item in components:
        files = item.get("files", [])
        if (item["component"] not in {"spirv-tools", "shaderc"} or
                item["sourceCommit"] != expected.get(item["component"]) or
                not files or len({entry["file"] for entry in files}) != len(files) or
                any(not re.fullmatch(r"(?:source|libshaderc_util)/(?:[a-z_]+/)*[a-z_]+\.(?:cpp|cc)", entry["file"])
                    for entry in files) or
                sha256_file(REPO_ROOT / item["patch"]) != item["patchSha256"].lower()):
            raise SbomGenerationError("The exact reviewed compiler source repair is absent or modified.")
    return repair


def normalized_json(data: dict[str, Any]) -> str:
    return json.dumps(data, indent=2, sort_keys=False, ensure_ascii=False) + "\n"


def package_versions() -> list[dict[str, str]]:
    tree = ET.parse(REPO_ROOT / "Directory.Packages.props")
    packages: list[dict[str, str]] = []
    for item in tree.findall(".//PackageVersion"):
        name = item.attrib["Include"]
        version = item.attrib["Version"]
        packages.append({"name": name, "version": version})
    return sorted(packages, key=lambda x: x["name"].lower())


def published_package_ids() -> list[str]:
    text = (REPO_ROOT / "eng" / "pack-packages.ps1").read_text(encoding="utf-8")
    match = re.search(r"\$published\s*=\s*@\((?P<body>.*?)\)\s*# Deferred", text, re.S)
    if not match:
        raise ValueError("Could not find the published package list in eng/pack-packages.ps1.")
    return re.findall(r"'([^']+)'", match.group("body"))


def component(
    bom_ref: str,
    name: str,
    version: str | None,
    ctype: str = "library",
    purl: str | None = None,
    licenses: list[str] | None = None,
    properties: dict[str, str] | None = None,
    external_refs: list[dict[str, str]] | None = None,
    hashes: dict[str, str] | None = None,
) -> dict[str, Any]:
    result: dict[str, Any] = {"type": ctype, "bom-ref": bom_ref, "name": name}
    if version:
        result["version"] = version
    if purl:
        result["purl"] = purl
    if licenses:
        # A CycloneDX license `id` must be an SPDX identifier. A LicenseRef- value is by definition
        # not one, so it is emitted as a `name` instead; writing it as an `id` produces a document
        # that fails schema validation and, worse, claims SPDX standing for proprietary terms.
        result["licenses"] = [
            {"license": {"name": value}}
            if value.startswith("LicenseRef-")
            else {"license": {"id": value}}
            for value in licenses
        ]
    if hashes:
        result["hashes"] = [{"alg": alg, "content": value} for alg, value in hashes.items()]
    if external_refs:
        result["externalReferences"] = external_refs
    if properties:
        result["properties"] = [{"name": key, "value": value} for key, value in properties.items()]
    return result


def source_refs(item: dict[str, Any]) -> list[dict[str, str]]:
    refs: list[dict[str, str]] = []
    for key in ("repository", "url", "archiveUrl"):
        if key in item:
            refs.append({"type": "distribution", "url": str(item[key])})
            break
    return refs


def add_component(components: dict[str, dict[str, Any]], item: dict[str, Any]) -> None:
    components[item["bom-ref"]] = item


def github_purl(url: str, version: str | None, commit: str | None) -> str | None:
    match = re.search(r"github\.com[:/](?P<owner>[^/]+)/(?P<repo>[^/.]+)", url)
    if not match:
        return None
    suffix = commit or version
    return f"pkg:github/{match.group('owner')}/{match.group('repo')}@{suffix}" if suffix else None


def cesium_overlay_ports(lock: dict[str, Any]) -> tuple[dict[str, dict[str, Any]], dict[str, Any]]:
    source = lock["cesiumNative"]
    archive = REPO_ROOT / "native" / "downloads" / source["archiveName"]
    if not archive.is_file():
        raise SbomGenerationError(
            f"The pinned Cesium source archive is required to refresh overlay metadata: {archive}. "
            "Fetch the locked Cesium inputs before --refresh-vcpkg."
        )
    digest = hashlib.sha256()
    with archive.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    if digest.hexdigest() != source["archiveSha256"].lower():
        raise SbomGenerationError("The Cesium overlay source archive does not match the pinned SHA-256.")
    manifests: dict[str, dict[str, Any]] = {}
    ports = []
    with tarfile.open(archive, "r:gz") as contents:
        entries = {entry.name.partition("/")[2]: entry for entry in contents.getmembers()}
        for path, entry in sorted(entries.items()):
            prefix = "extern/vcpkg/ports/"
            if not path.startswith(prefix) or path.count("/") != 4 or not path.endswith("/vcpkg.json"):
                continue
            port_path = path.rsplit("/", 1)[0]
            portfile = entries.get(f"{port_path}/portfile.cmake")
            if not entry.isfile() or portfile is None or not portfile.isfile():
                raise SbomGenerationError(f"The Cesium overlay is missing its regular port files: {port_path}")
            with contents.extractfile(entry) as stream:
                raw_manifest = stream.read()
            with contents.extractfile(portfile) as stream:
                raw_portfile = stream.read()
            manifest = json.loads(raw_manifest)
            name = manifest["name"]
            if name != port_path.rsplit("/", 1)[1] or name in manifests:
                raise SbomGenerationError(f"Invalid or duplicate Cesium overlay port name: {name}")
            manifests[name] = manifest
            ports.append({
                "name": name,
                "path": port_path,
                "manifestSha256": hashlib.sha256(raw_manifest).hexdigest(),
                "portfileSha256": hashlib.sha256(raw_portfile).hexdigest(),
            })
    if not manifests:
        raise SbomGenerationError("No overlay ports were found in the pinned Cesium source archive.")
    return manifests, {
        "commit": source["commit"],
        "archiveSha256": source["archiveSha256"],
        "ports": ports,
    }


def vcpkg_platform_matches(expression: str | None, targets: list[set[str]]) -> bool:
    if expression is None:
        return True
    if not re.fullmatch(r"[A-Za-z0-9_!&|()\s]+", expression):
        raise SbomGenerationError(f"Unsupported vcpkg platform expression: {expression!r}")

    def evaluate(node: ast.AST) -> bool:
        if isinstance(node, ast.Constant) and isinstance(node.value, bool):
            return node.value
        if isinstance(node, ast.UnaryOp) and isinstance(node.op, ast.Not):
            return not evaluate(node.operand)
        if isinstance(node, ast.BoolOp) and isinstance(node.op, (ast.And, ast.Or)):
            values = [evaluate(value) for value in node.values]
            return all(values) if isinstance(node.op, ast.And) else any(values)
        raise SbomGenerationError(f"Unsupported vcpkg platform expression: {expression!r}")

    for target in targets:
        expanded = re.sub(
            r"[A-Za-z0-9_]+",
            lambda match: "True" if match.group(0) in target else "False",
            expression,
        ).replace("!", " not ").replace("&", " and ").replace("|", " or ").strip()
        try:
            parsed = ast.parse(expanded, mode="eval")
        except SyntaxError as exc:
            raise SbomGenerationError(f"Invalid vcpkg platform expression: {expression!r}") from exc
        if evaluate(parsed.body):
            return True
    return False


class VcpkgRegistry:
    def __init__(self, lock: dict[str, Any], cache_root: Path) -> None:
        self.baseline = lock["vcpkg"]["baseline"]
        self.repository = lock["vcpkg"]["repository"]
        self.cache_root = cache_root / self.baseline
        self.baseline_data: dict[str, Any] | None = None
        self.port_cache: dict[str, dict[str, Any]] = {}
        self.overlays, self.overlay_source = cesium_overlay_ports(lock)
        self.targets = []
        systems = {"win": "windows", "linux": "linux", "osx": "osx"}
        for rid in lock["profile"]["platforms"]:
            system, architecture = rid.rsplit("-", 1)
            if system not in systems:
                raise SbomGenerationError(f"Unsupported Cesium SBOM target RID: {rid}")
            flags = {systems[system], architecture, "native", "all"}
            if lock["profile"]["libraryLinkage"] == "static":
                flags.add("static")
            self.targets.append(flags)

    def fetch_text(self, relative: str) -> str:
        local = self.cache_root / relative
        if not local.exists():
            local.parent.mkdir(parents=True, exist_ok=True)
            url = (
                f"https://raw.githubusercontent.com/microsoft/vcpkg/"
                f"{self.baseline}/{relative.replace(chr(92), '/')}"
            )
            request = urllib.request.Request(url, headers={"User-Agent": "openusd-dotnet-sbom"})
            last_error: Exception | None = None
            for attempt in range(1, 4):
                try:
                    with urllib.request.urlopen(request, timeout=30) as response:
                        local.write_bytes(response.read())
                    break
                except Exception as exc:  # pragma: no cover - exercised by transient network faults.
                    last_error = exc
                    if attempt == 3:
                        raise SbomGenerationError(
                            f"Could not refresh vcpkg metadata '{relative}' from pinned baseline "
                            f"{self.baseline}: {exc}. SBOM checks are hermetic; retry only in "
                            "an explicit --refresh-vcpkg update."
                        ) from exc
                    time.sleep(attempt)
            if last_error is not None and not local.exists():
                raise last_error
        return local.read_text(encoding="utf-8")

    def baseline_versions(self) -> dict[str, Any]:
        if self.baseline_data is None:
            data = json.loads(self.fetch_text("versions/baseline.json"))
            self.baseline_data = data["default"]
        return self.baseline_data

    def port_manifest(self, name: str) -> dict[str, Any]:
        key = name.lower()
        if key in self.overlays:
            return self.overlays[key]
        if key not in self.port_cache:
            self.port_cache[key] = json.loads(self.fetch_text(f"ports/{key}/vcpkg.json"))
        return self.port_cache[key]

    def resolve(self, direct_dependencies: list[str]) -> list[dict[str, str]]:
        selected: dict[str, set[str]] = {}
        queue: list[tuple[str, str | None]] = []
        for dependency in direct_dependencies:
            match = re.match(r"^(?P<name>[^\[]+)(\[(?P<features>[^\]]+)\])?$", dependency)
            if not match:
                continue
            name = match.group("name").lower()
            queue.append((name, None))
            for feature in (match.group("features") or "").split(","):
                if feature:
                    queue.append((name, feature))

        while queue:
            name, feature = queue.pop(0)
            if name in VCPKG_TRANSITIVE_EXCLUDE:
                continue
            selected.setdefault(name, set())
            if feature:
                if feature in selected[name]:
                    continue
                selected[name].add(feature)
            elif "" in selected[name]:
                continue
            else:
                selected[name].add("")

            manifest = self.port_manifest(name)
            dependencies = list(manifest.get("dependencies", []))
            if not feature:
                for default_feature in manifest.get("default-features", []):
                    if isinstance(default_feature, str):
                        queue.append((name, default_feature))
                    elif (isinstance(default_feature, dict) and "name" in default_feature and
                            vcpkg_platform_matches(default_feature.get("platform"), self.targets)):
                        queue.append((name, default_feature["name"]))
            if feature:
                dependencies += manifest.get("features", {}).get(feature, {}).get("dependencies", [])
            for dependency in dependencies:
                dep_name: str | None = None
                if isinstance(dependency, str):
                    dep_name = dependency
                elif (isinstance(dependency, dict) and not dependency.get("host", False) and
                        vcpkg_platform_matches(dependency.get("platform"), self.targets)):
                    dep_name = dependency.get("name")
                    for dep_feature in dependency.get("features", []):
                        if dep_name:
                            queue.append((dep_name.lower(), dep_feature))
                if dep_name:
                    queue.append((dep_name.lower(), None))

        baseline = self.baseline_versions()
        components: list[dict[str, str]] = []
        for name in sorted(selected):
            manifest = self.port_manifest(name)
            if name in self.overlays:
                versions = [
                    manifest[key] for key in ("version", "version-semver", "version-date", "version-string")
                    if key in manifest
                ]
                if len(versions) != 1:
                    raise SbomGenerationError(f"Overlay port {name} must declare exactly one version.")
                version = versions[0]
                port_version = int(manifest.get("port-version", 0))
            else:
                entry = baseline.get(name)
                if not entry:
                    raise SbomGenerationError(f"vcpkg baseline {self.baseline} has no version entry for {name}.")
                version = entry["baseline"]
                port_version = int(entry.get("port-version", 0))
            if port_version:
                version = f"{version}#{port_version}"
            license_value = manifest.get("license") or "NOASSERTION"
            if isinstance(license_value, list):
                license_value = " AND ".join(license_value)
            item = {
                "name": name,
                "version": version,
                "license": str(license_value),
                "features": ",".join(sorted(f for f in selected[name] if f)),
            }
            if name in self.overlays:
                item["overlay"] = next(
                    port["path"] for port in self.overlay_source["ports"] if port["name"] == name
                )
            components.append(item)
        return components


def load_vcpkg_components(path: Path, lock: dict[str, Any]) -> list[dict[str, str]]:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise SbomGenerationError(
            f"Resolved vcpkg component data is missing at {path}. Run "
            "'python eng/generate-sbom.py --refresh-vcpkg' after intentionally updating "
            "eng/cesium.lock.json."
        ) from exc
    except json.JSONDecodeError as exc:
        raise SbomGenerationError(f"Resolved vcpkg component data at {path} is not valid JSON: {exc}") from exc

    expected_baseline = lock["vcpkg"]["baseline"]
    actual_baseline = data.get("vcpkg", {}).get("baseline")
    if actual_baseline != expected_baseline:
        raise SbomGenerationError(
            "Resolved vcpkg component data does not match eng/cesium.lock.json. "
            f"Expected baseline {expected_baseline}, found {actual_baseline or '<missing>'}. "
            "Run 'python eng/generate-sbom.py --refresh-vcpkg' after intentionally updating "
            "the Cesium vcpkg pin."
        )

    expected_direct = sorted(lock["directVcpkgDependencies"])
    actual_direct = sorted(data.get("directVcpkgDependencies", []))
    if actual_direct != expected_direct:
        raise SbomGenerationError(
            "Resolved vcpkg component data was generated for a different direct dependency set. "
            "Run 'python eng/generate-sbom.py --refresh-vcpkg' after intentionally updating "
            "eng/cesium.lock.json."
        )

    overlay_source = data.get("overlaySource", {})
    if (overlay_source.get("commit") != lock["cesiumNative"]["commit"] or
            overlay_source.get("archiveSha256") != lock["cesiumNative"]["archiveSha256"] or
            not overlay_source.get("ports")):
        raise SbomGenerationError(
            "Resolved Cesium overlay provenance is missing or stale. "
            "Refresh from the pinned Cesium archive with --refresh-vcpkg."
        )

    components = data.get("components")
    if not isinstance(components, list) or not components:
        raise SbomGenerationError(f"Resolved vcpkg component data at {path} has no components.")
    for item in components:
        if not all(key in item for key in ("name", "version", "license", "features")):
            raise SbomGenerationError(f"Resolved vcpkg component entry is incomplete: {item!r}")
    return components


def refresh_vcpkg_components(cache_root: Path, output: Path) -> None:
    lock = read_json("eng/cesium.lock.json")
    registry = VcpkgRegistry(lock, cache_root)
    components = registry.resolve(lock["directVcpkgDependencies"])
    data = {
        "$schemaVersion": 1,
        "description": (
            "Resolved Cesium vcpkg component data used by the hermetic release SBOM generator. "
            "Refresh deliberately with python eng/generate-sbom.py --refresh-vcpkg."
        ),
        "source": "eng/cesium.lock.json",
        "vcpkg": {
            "repository": lock["vcpkg"]["repository"],
            "baseline": lock["vcpkg"]["baseline"],
        },
        "directVcpkgDependencies": lock["directVcpkgDependencies"],
        "overlaySource": registry.overlay_source,
        "components": components,
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(normalized_json(data), encoding="utf-8", newline="\n")
    print(f"Wrote resolved vcpkg component data: {output} ({len(components)} components)")


def generate(cache_root: Path, vcpkg_components: Path) -> dict[str, Any]:
    version = read_json("version.json")["version"]
    sdk_version = read_json("global.json")["sdk"]["version"]
    openusd = read_json("eng/openusd.install.lock.json")
    cesium = read_json("eng/cesium.lock.json")
    physx = read_json("eng/physx.lock.json")
    shaders = read_json("eng/shaders/toolchain.lock.json")
    components: dict[str, dict[str, Any]] = {}
    root_ref = f"pkg:nuget/OpenUsd@{version}"

    for package_id in published_package_ids():
        add_component(
            components,
            component(
                f"nuget:{package_id}",
                package_id,
                version,
                "library",
                f"pkg:nuget/{package_id}@{version}",
                properties={"openusd:release-artifact": "nupkg"},
            ),
        )

    for package in package_versions():
        add_component(
            components,
            component(
                f"nuget-dependency:{package['name']}",
                package["name"],
                package["version"],
                "library",
                f"pkg:nuget/{package['name']}@{package['version']}",
                properties={"openusd:source": "Directory.Packages.props"},
            ),
        )

    add_component(
        components,
        component(
            "dotnet-runtime:Microsoft.NETCore.App",
            "Microsoft.NETCore.App",
            None,
            "framework",
            properties={
                "openusd:source": "global.json; eng/publish-viewer-bundle.ps1",
                "openusd:release-artifact": "Viewer self-contained bundle",
                "openusd:unresolved": (
                    "Exact runtime pack patch is selected by the pinned .NET SDK during restore; "
                    f"the repository pins SDK {sdk_version} but does not record the restored runtime "
                    "pack version."
                ),
            },
        ),
    )

    native = openusd["openUsd"]
    add_component(
        components,
        component(
            "native:OpenUSD",
            "OpenUSD",
            native["version"],
            purl=github_purl(native["archiveUrl"], native["tag"], native["commit"]),
            licenses=[native["license"]],
            external_refs=source_refs(native),
            hashes={"SHA-256": native["archiveSha256"]},
            properties={"openusd:source": "eng/openusd.install.lock.json"},
        ),
    )
    for dependency in openusd["dependencies"]:
        props = {"openusd:source": "eng/openusd.install.lock.json"}
        if "platforms" in dependency:
            props["openusd:platforms"] = ",".join(dependency["platforms"])
        ref = f"native:{dependency['name']}:{dependency['version']}:{props.get('openusd:platforms', 'all')}"
        add_component(
            components,
            component(
                ref,
                dependency["name"],
                dependency.get("version"),
                purl=github_purl(dependency.get("url", ""), dependency.get("tag"), dependency.get("commit")),
                licenses=[dependency["license"]] if "license" in dependency else None,
                external_refs=source_refs(dependency),
                hashes={"SHA-256": dependency["sha256"]} if "sha256" in dependency else None,
                properties=props,
            ),
        )

    for repo in openusd["vulkanSdk"]["repositories"]:
        add_component(
            components,
            component(
                f"native:{repo['name']}:{repo['commit']}",
                repo["name"],
                repo["commit"],
                purl=github_purl(repo["url"], None, repo["commit"]),
                external_refs=source_refs(repo),
                properties={
                    "openusd:source": "eng/openusd.install.lock.json",
                    "openusd:vulkanSdk": openusd["vulkanSdk"]["version"],
                },
            ),
        )

    shaderc_manifests = [
        repo for repo in openusd["vulkanSdk"]["repositories"]
        if repo["name"] == "shaderc-source-manifest"
    ]
    if (len(shaderc_manifests) != 1 or
            shaderc_manifests[0]["commit"] != SHADERC_SOURCE_MANIFEST_COMMIT):
        raise SbomGenerationError(
            "The shaderc source manifest changed. Refresh the verified compiled-source snapshot "
            "in eng/generate-sbom.py from the newly pinned known_good.json."
        )
    source_repair = compiler_source_repair(openusd)
    for name, repository, commit in SHADERC_COMPILED_SOURCES:
        url = f"https://github.com/{repository}.git"
        properties = {
            "openusd:source": "eng/openusd.install.lock.json; eng/generate-sbom.py",
            "openusd:shaderc-source-manifest": SHADERC_SOURCE_MANIFEST_COMMIT,
            "openusd:shaderc-source-manifest-sha256": SHADERC_SOURCE_MANIFEST_SHA256,
            "openusd:role": "native shader compiler source",
        }
        repairs = [item for item in source_repair["components"] if item["component"] == name.lower()]
        if repairs:
            properties.update({
                "openusd:source-repair": openusd["vulkanSdk"]["sourcePatchLock"],
                "openusd:source-repair-sha256": openusd["vulkanSdk"]["sourcePatchLockSha256"],
                "openusd:repaired-sources": json.dumps(
                    {item["file"]: item["afterSha256"] for item in repairs[0]["files"]}, sort_keys=True
                ),
            })
        add_component(
            components,
            component(
                f"native:{name}:{commit}",
                name,
                commit,
                purl=github_purl(url, None, commit),
                external_refs=[{"type": "vcs", "url": url}],
                properties=properties,
            ),
        )

    cesium_native = cesium["cesiumNative"]
    add_component(
        components,
        component(
            "native:cesium-native",
            "cesium-native",
            cesium_native["version"],
            purl=github_purl(cesium_native["archiveUrl"], cesium_native["tag"], cesium_native["commit"]),
            licenses=[cesium_native["license"]],
            external_refs=source_refs(cesium_native),
            hashes={"SHA-256": cesium_native["archiveSha256"]},
            properties={"openusd:source": "eng/cesium.lock.json"},
        ),
    )
    for dependency in load_vcpkg_components(vcpkg_components, cesium):
        properties = {
            "openusd:source": "eng/cesium.lock.json; eng/sbom/cesium-vcpkg-components.lock.json",
            "openusd:vcpkg-baseline": cesium["vcpkg"]["baseline"],
            "openusd:vcpkg-features": dependency["features"],
            "openusd:license": dependency["license"],
        }
        if "overlay" in dependency:
            properties.update({
                "openusd:vcpkg-overlay": dependency["overlay"],
                "openusd:overlay-source-commit": cesium["cesiumNative"]["commit"],
                "openusd:overlay-source-archive-sha256": cesium["cesiumNative"]["archiveSha256"],
            })
        add_component(
            components,
            component(
                f"vcpkg:{dependency['name']}",
                dependency["name"],
                dependency["version"],
                properties=properties,
            ),
        )

    physx_native = physx["physx"]
    add_component(
        components,
        component(
            "native:PhysX",
            "PhysX",
            physx_native["version"],
            licenses=[physx_native["license"]],
            external_refs=[{"type": "vcs", "url": physx_native["repository"]}],
            properties={
                "openusd:source": "eng/physx.lock.json",
                "openusd:release-scope": physx_native["releaseScope"],
                "openusd:release-rids": ",".join(physx_native["supportedRids"]),
            },
        ),
    )

    # The GPU and device modules are recorded because the vcpkg port really does download them on
    # every physics build, so an SBOM that omitted them would describe a build that never happened.
    # They are recorded as build and local runtime inputs and never as release artifacts: they are
    # packman blobs under NVIDIA proprietary terms rather than outputs of the BSD-3-Clause sources,
    # and no OpenUsd package contains one. The distinction is the whole point of listing them.
    for module in physx_native["gpuModules"]:
        add_component(
            components,
            component(
                f"native-input:{module}",
                module,
                physx_native["version"],
                licenses=[physx_native["gpuModuleLicense"]],
                properties={
                    "openusd:source": "eng/physx.lock.json",
                    "openusd:release-scope": physx_native["gpuModuleScope"],
                    "openusd:redistributed": "false",
                },
            ),
        )

    for name, data in (
        ("Slang", shaders["slang"]),
        ("SPIRV-Tools", shaders["spirvTools"]),
        ("SPIRV-Headers", shaders["spirvHeaders"]),
    ):
        version_value = data.get("version") or data.get("commit")
        add_component(
            components,
            component(
                f"shader-toolchain:{name}",
                name,
                version_value,
                purl=github_purl(data["repository"], data.get("tag"), data.get("commit")),
                external_refs=[{"type": "vcs", "url": data["repository"]}],
                properties={"openusd:source": "eng/shaders/toolchain.lock.json"},
            ),
        )

    input_properties = {
        f"openusd:input:{relative}:sha256": sha256_file(REPO_ROOT / relative)
        for relative in PINNED_INPUTS
    }

    dependencies = [{"ref": root_ref, "dependsOn": [c["bom-ref"] for c in components.values()]}]
    dependencies += [{"ref": c["bom-ref"], "dependsOn": []} for c in components.values()]

    return {
        "bomFormat": "CycloneDX",
        "specVersion": CYCLONEDX_SPEC_VERSION,
        "serialNumber": "urn:uuid:1fd11c24-482a-50a8-9e2c-7a97f5d7d1e6",
        "version": 1,
        "metadata": {
            "tools": {
                "components": [
                    {
                        "type": "application",
                        "name": "eng/generate-sbom.py",
                        "version": "1",
                    }
                ]
            },
            "component": {
                "type": "application",
                "bom-ref": root_ref,
                "name": "OpenUsd release artifacts",
                "version": version,
                "purl": root_ref,
            },
            "properties": [
                {"name": key, "value": value}
                for key, value in sorted(input_properties.items())
            ]
            + [
                {
                    "name": "openusd:scope",
                    "value": (
                            "Published NuGet packages and Viewer bundles; PhysX is statically "
                            "linked into the published OpenUsd.Runtime.Physics.<rid> packages, and "
                            "its proprietary GPU modules are never redistributed."
                        ),
                }
            ],
        },
        "components": sorted(components.values(), key=lambda c: c["bom-ref"]),
        "dependencies": dependencies,
    }


def validate(bom: dict[str, Any]) -> None:
    if bom.get("bomFormat") != "CycloneDX":
        raise ValueError("bomFormat must be CycloneDX.")
    if bom.get("specVersion") != CYCLONEDX_SPEC_VERSION:
        raise ValueError(f"specVersion must be {CYCLONEDX_SPEC_VERSION}.")
    if not isinstance(bom.get("components"), list) or not bom["components"]:
        raise ValueError("components must be a non-empty list.")
    refs = set()
    for item in bom["components"]:
        for field in ("type", "bom-ref", "name"):
            if field not in item:
                raise ValueError(f"component is missing required field {field}.")
        ref = item["bom-ref"]
        if ref in refs:
            raise ValueError(f"duplicate bom-ref: {ref}")
        refs.add(ref)
    for relation in bom.get("dependencies", []):
        if "ref" not in relation or "dependsOn" not in relation:
            raise ValueError("dependency entries require ref and dependsOn.")


def stale_difference(path: Path, expected: str, maximum_lines: int = 200) -> str:
    current_bytes = path.read_bytes() if path.exists() else b""
    expected_bytes = expected.encode("utf-8")
    current_text = current_bytes.decode("utf-8", errors="replace")
    current_lines = current_text.splitlines()
    expected_lines = expected.splitlines()
    diff = list(difflib.unified_diff(
        current_lines,
        expected_lines,
        fromfile=f"{path} (current)",
        tofile=f"{path} (generated)",
        lineterm=""))
    truncated = len(diff) > maximum_lines
    diff = diff[:maximum_lines]
    current_crlf = current_bytes.count(b"\r\n")
    expected_crlf = expected_bytes.count(b"\r\n")
    lines = [
        f"Current SHA-256:  {hashlib.sha256(current_bytes).hexdigest() if current_bytes else '<missing>'}",
        f"Generated SHA-256: {hashlib.sha256(expected_bytes).hexdigest()}",
        f"Current CRLF lines: {current_crlf}; generated CRLF lines: {expected_crlf}",
        "Unified diff (current -> generated):",
        *diff,
    ]
    if truncated:
        lines.append(f"... diff truncated after {maximum_lines} lines ...")
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--cache-root", type=Path, default=VCPKG_CACHE_ROOT)
    parser.add_argument("--vcpkg-components", type=Path, default=DEFAULT_VCPKG_COMPONENTS)
    parser.add_argument("--refresh-vcpkg", action="store_true", help="refresh committed vcpkg component data")
    parser.add_argument("--check", action="store_true", help="fail if the output file is stale")
    parser.add_argument("--validate", action="store_true", help="validate an existing output file")
    args = parser.parse_args()

    if args.refresh_vcpkg:
        refresh_vcpkg_components(args.cache_root, args.vcpkg_components)
        return 0

    if args.validate and not args.check:
        bom = json.loads(args.output.read_text(encoding="utf-8"))
        validate(bom)
        print(f"Validated CycloneDX SBOM: {args.output} ({len(bom['components'])} components)")
        return 0

    bom = generate(args.cache_root, args.vcpkg_components)
    validate(bom)
    text = normalized_json(bom)
    expected_bytes = text.encode("utf-8")

    if args.check:
        current = args.output.read_bytes() if args.output.exists() else b""
        if current != expected_bytes:
            print(f"SBOM is stale: regenerate {args.output}", file=sys.stderr)
            print(stale_difference(args.output, text), file=sys.stderr)
            return 1
        print(f"SBOM is current: {args.output} ({len(bom['components'])} components)")
        return 0

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(text, encoding="utf-8", newline="\n")
    print(f"Wrote CycloneDX SBOM: {args.output} ({len(bom['components'])} components)")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except SbomGenerationError as exc:
        print(f"SBOM generation failed: {exc}", file=sys.stderr)
        raise SystemExit(1)
