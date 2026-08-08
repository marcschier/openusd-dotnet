#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.
"""Generate the release CycloneDX SBOM from pinned repository inputs."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
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
    "eng/cesium.lock.json",
    "eng/physx.lock.json",
    "eng/shaders/toolchain.lock.json",
    "eng/pack-packages.ps1",
    "eng/publish-viewer-bundle.ps1",
    "eng/sbom/cesium-vcpkg-components.lock.json",
]

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
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


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
        result["licenses"] = [{"license": {"id": value}} for value in licenses]
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


class VcpkgRegistry:
    def __init__(self, lock: dict[str, Any], cache_root: Path) -> None:
        self.baseline = lock["vcpkg"]["baseline"]
        self.repository = lock["vcpkg"]["repository"]
        self.cache_root = cache_root / self.baseline
        self.baseline_data: dict[str, Any] | None = None
        self.port_cache: dict[str, dict[str, Any]] = {}

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
                    elif isinstance(default_feature, dict) and "name" in default_feature:
                        queue.append((name, default_feature["name"]))
            if feature:
                dependencies += manifest.get("features", {}).get(feature, {}).get("dependencies", [])
            for dependency in dependencies:
                dep_name: str | None = None
                if isinstance(dependency, str):
                    dep_name = dependency
                elif isinstance(dependency, dict) and not dependency.get("host", False):
                    dep_name = dependency.get("name")
                    for dep_feature in dependency.get("features", []):
                        if dep_name:
                            queue.append((dep_name.lower(), dep_feature))
                if dep_name:
                    queue.append((dep_name.lower(), None))

        baseline = self.baseline_versions()
        components: list[dict[str, str]] = []
        for name in sorted(selected):
            entry = baseline.get(name)
            if not entry:
                raise ValueError(f"vcpkg baseline {self.baseline} has no version entry for {name}.")
            version = entry["baseline"]
            port_version = int(entry.get("port-version", 0))
            if port_version:
                version = f"{version}#{port_version}"
            manifest = self.port_manifest(name)
            license_value = manifest.get("license", "NOASSERTION")
            if isinstance(license_value, list):
                license_value = " AND ".join(license_value)
            components.append(
                {
                    "name": name,
                    "version": version,
                    "license": str(license_value),
                    "features": ",".join(sorted(f for f in selected[name] if f)),
                }
            )
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
        "components": components,
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(normalized_json(data), encoding="utf-8")
    print(f"Wrote resolved vcpkg component data: {output} ({len(components)} components)")


def generate(cache_root: Path, vcpkg_components: Path) -> dict[str, Any]:
    version = read_json("version.json")["version"]
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
                    "the repository pins SDK 10.0.301 but does not record the restored runtime "
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
        add_component(
            components,
            component(
                f"vcpkg:{dependency['name']}",
                dependency["name"],
                dependency["version"],
                properties={
                    "openusd:source": "eng/cesium.lock.json",
                    "openusd:vcpkg-baseline": cesium["vcpkg"]["baseline"],
                    "openusd:vcpkg-features": dependency["features"],
                    "openusd:license": dependency["license"],
                },
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
                "openusd:release-scope": "optional shim is built in native CI but not published as a release artifact",
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
            "timestamp": "1970-01-01T00:00:00Z",
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
                    "value": "Published NuGet packages and Viewer bundles; PhysX is recorded as optional native CI input.",
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

    if args.check:
        current = args.output.read_text(encoding="utf-8") if args.output.exists() else ""
        if current != text:
            print(f"SBOM is stale: regenerate {args.output}", file=sys.stderr)
            return 1
        print(f"SBOM is current: {args.output} ({len(bom['components'])} components)")
        return 0

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(text, encoding="utf-8")
    print(f"Wrote CycloneDX SBOM: {args.output} ({len(bom['components'])} components)")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except SbomGenerationError as exc:
        print(f"SBOM generation failed: {exc}", file=sys.stderr)
        raise SystemExit(1)
