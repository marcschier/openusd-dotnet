# Copyright (c) marcschier. Licensed under the MIT License.

import argparse
import json
import pathlib
import zipfile

from metal_sidecar import validate_sidecar

PACKAGE_LIBRARY_PATH = "runtimes/osx/native/mesh.metallib"
PACKAGE_MANIFEST_PATH = "runtimes/osx/native/mesh.metallib.manifest.json"


def verify_package(
    package_root: pathlib.Path,
    library_path: pathlib.Path,
    manifest_path: pathlib.Path,
    shader_manifest_path: pathlib.Path,
    lock_path: pathlib.Path,
    repository_root: pathlib.Path,
) -> pathlib.Path:
    packages = sorted(
        path
        for path in package_root.glob("OpenUsd.Rendering.Silk.Metal.*.nupkg")
        if not path.name.endswith(".symbols.nupkg")
    )
    if len(packages) != 1:
        raise ValueError(f"Expected one Metal package, found {len(packages)}")
    expected = library_path.read_bytes()
    if not expected:
        raise ValueError("The staged mesh.metallib is empty")
    expected_manifest = manifest_path.read_bytes()
    sidecar = json.loads(expected_manifest.decode("utf-8"))
    shader_manifest = json.loads(
        shader_manifest_path.read_text(encoding="utf-8")
    )
    lock = json.loads(lock_path.read_text(encoding="utf-8"))
    validate_sidecar(
        sidecar,
        shader_manifest,
        lock,
        repository_root.resolve(),
        verify_files=False,
        verify_checked_files=True,
        library_content=expected,
    )
    with zipfile.ZipFile(packages[0]) as package:
        metallibs = [
            name
            for name in package.namelist()
            if name.lower().endswith(".metallib")
        ]
        if metallibs != [PACKAGE_LIBRARY_PATH]:
            raise ValueError(
                f"Package metallib entries do not match: {metallibs}"
            )
        if package.read(PACKAGE_LIBRARY_PATH) != expected:
            raise ValueError("Packaged mesh.metallib differs from the staged file")
        manifests = [
            name
            for name in package.namelist()
            if name.lower().endswith(".metallib.manifest.json")
        ]
        if manifests != [PACKAGE_MANIFEST_PATH]:
            raise ValueError(
                f"Package Metal manifest entries do not match: {manifests}"
            )
        packaged_manifest = package.read(PACKAGE_MANIFEST_PATH)
        if packaged_manifest != expected_manifest:
            raise ValueError("Packaged Metal manifest differs from the staged file")
        validate_sidecar(
            json.loads(packaged_manifest.decode("utf-8")),
            shader_manifest,
            lock,
            repository_root.resolve(),
            verify_files=False,
            verify_checked_files=True,
            library_content=expected,
        )
    return packages[0]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--package-root", required=True, type=pathlib.Path)
    parser.add_argument("--library", required=True, type=pathlib.Path)
    parser.add_argument("--manifest", required=True, type=pathlib.Path)
    parser.add_argument(
        "--shader-manifest",
        type=pathlib.Path,
        default=pathlib.Path("eng/shaders/shader-manifest.json"),
    )
    parser.add_argument(
        "--lock",
        type=pathlib.Path,
        default=pathlib.Path("eng/shaders/toolchain.lock.json"),
    )
    parser.add_argument(
        "--repository-root",
        type=pathlib.Path,
        default=pathlib.Path.cwd(),
    )
    args = parser.parse_args()
    package = verify_package(
        args.package_root,
        args.library,
        args.manifest,
        args.shader_manifest,
        args.lock,
        args.repository_root,
    )
    print(package.as_posix())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
