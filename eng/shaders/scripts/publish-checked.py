# Copyright (c) marcschier. Licensed under the MIT License.

import argparse
import hashlib
import json
import pathlib
import shutil
from typing import Any

from shader_model import (
    build_lock_model,
    required_checked_inputs,
    validate_checked_input_bytes,
    validate_manifest,
)


ARTIFACT_SUFFIXES = ("dxil", "spv", "metal", "reflection.json")


def sha256_bytes(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_checked_inputs(
    repository_root: pathlib.Path,
    relative_paths: tuple[str, ...],
    command_path: pathlib.Path,
) -> list[tuple[str, bytes]]:
    inputs = []
    for relative_path in relative_paths:
        path = (
            command_path
            if relative_path == "eng/shaders/checked/executed-commands.json"
            else repository_root / relative_path
        )
        if not path.is_file():
            raise ValueError(f"Pipeline input is missing: {relative_path}")
        content = path.read_bytes()
        validate_checked_input_bytes(relative_path, content)
        inputs.append((relative_path, content))
    return inputs


def check_path_stripping(path: pathlib.Path, repository_root: pathlib.Path) -> None:
    content = path.read_bytes()
    absolute_path = str(repository_root.resolve())
    forbidden = [
        absolute_path.encode("utf-8"),
        absolute_path.replace("\\", "/").encode("utf-8"),
        absolute_path.encode("utf-16-le"),
    ]
    if any(value in content for value in forbidden):
        raise ValueError(f"Absolute repository path found in {path}")
    if path.suffix == ".metal" and b"#line" in content:
        raise ValueError(f"Line directives were not stripped from {path}")


def publish(
    input_root: pathlib.Path,
    output_root: pathlib.Path,
    manifest_path: pathlib.Path,
    lock_path: pathlib.Path,
) -> dict[str, Any]:
    shader_manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    lock = json.loads(lock_path.read_text(encoding="utf-8"))
    model = build_lock_model(lock)
    source_manifest = shader_manifest
    shader_manifest = validate_manifest(shader_manifest, model)
    repository_root = lock_path.resolve().parents[2]
    command_path = input_root / "executed-commands.json"
    executed = json.loads(command_path.read_text(encoding="utf-8"))
    if executed.get("toolchain") != model:
        raise ValueError("Executed command toolchain does not match the lock")
    command_programs = {
        program["name"]: program
        for program in executed["programs"]
    }
    checked_inputs = read_checked_inputs(
        repository_root,
        required_checked_inputs(source_manifest),
        command_path,
    )

    if output_root.exists():
        shutil.rmtree(output_root)
    output_root.mkdir(parents=True)
    checked_command_path = output_root / "executed-commands.json"
    shutil.copyfile(command_path, checked_command_path)

    programs = []
    for program in shader_manifest["programs"]:
        artifact_records = {}
        for suffix in ARTIFACT_SUFFIXES:
            file_name = f"{program['name']}.{suffix}"
            source = input_root / file_name
            destination = output_root / file_name
            if not source.is_file() or source.stat().st_size == 0:
                raise ValueError(f"Missing generated artifact: {source}")
            check_path_stripping(source, repository_root)
            if suffix == "metal":
                metal_text = source.read_text(encoding="utf-8")
                destination.write_text(
                    metal_text.replace("\r\n", "\n").replace("\r", "\n"),
                    encoding="utf-8",
                    newline="\n",
                )
            else:
                shutil.copyfile(source, destination)
            artifact_records[suffix] = {
                "path": f"eng/shaders/checked/{file_name}",
                "sha256": sha256(destination),
                "size": destination.stat().st_size,
            }

        executed_program = command_programs.get(program["name"])
        if executed_program is None:
            raise ValueError(f"Executed commands missing for {program['name']}")
        programs.append(
            {
                "name": program["name"],
                "source": program["source"],
                "entryPoint": program["entryPoint"],
                "stage": program["stage"],
                "profile": program["profile"],
                "resourceContract": program["resources"],
                "executedCommands": executed_program["commands"],
                "artifacts": artifact_records,
            }
        )

    inputs = [
        {
            "path": relative_path,
            "sha256": sha256_bytes(content),
        }
        for relative_path, content in checked_inputs
    ]
    checked_manifest = {
        "schemaVersion": 2,
        "authority": {
            "deterministicHost": "Windows win-x64",
            "deterministicArtifacts": [
                "DXIL",
                "SPIR-V",
                "MSL source",
                "normalized reflection JSON",
            ],
            "metallibHost": "macOS osx-arm64",
            "metallibXcodeVersion": model["xcodeVersion"],
        },
        "toolchain": model,
        "inputs": inputs,
        "programs": programs,
    }
    output_path = output_root / "manifest.json"
    output_path.write_text(
        json.dumps(checked_manifest, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    return checked_manifest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input-root", required=True, type=pathlib.Path)
    parser.add_argument("--output-root", required=True, type=pathlib.Path)
    parser.add_argument("--manifest", required=True, type=pathlib.Path)
    parser.add_argument("--lock", required=True, type=pathlib.Path)
    args = parser.parse_args()
    publish(
        args.input_root,
        args.output_root,
        args.manifest,
        args.lock,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
