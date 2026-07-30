# Copyright (c) marcschier. Licensed under the MIT License.

import argparse
import hashlib
import json
import pathlib
import struct
from typing import Any

from shader_model import (
    ARTIFACT_SCOPES,
    artifact_suffixes_for_scope,
    build_lock_model,
    command_names_for_artifact_scope,
    validate_manifest,
)


SPIRV_MAGIC = 0x07230203


def spirv_version_word(version: str) -> int:
    major, minor = (int(value) for value in version.split("."))
    return (major << 16) | (minor << 8)


def sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require_file(path: pathlib.Path) -> None:
    if not path.is_file() or path.stat().st_size == 0:
        raise ValueError(f"Missing or empty artifact: {path}")


def verify_program(
    output_root: pathlib.Path,
    program: dict[str, Any],
    expected_spirv_version: int,
    artifact_scope: str,
    require_metallib: bool,
) -> list[pathlib.Path]:
    base = output_root / program["name"]
    artifacts = [
        pathlib.Path(f"{base}.{suffix}")
        for suffix in artifact_suffixes_for_scope(
            artifact_scope,
            require_metallib,
        )
    ]
    for artifact in artifacts:
        require_file(artifact)

    if artifact_scope != "metal":
        spirv = pathlib.Path(f"{base}.spv")
        spirv_header = spirv.read_bytes()[:8]
        if len(spirv_header) != 8:
            raise ValueError(f"Invalid SPIR-V header: {spirv}")
        magic, version = struct.unpack("<II", spirv_header)
        if magic != SPIRV_MAGIC or version != expected_spirv_version:
            raise ValueError(
                f"Unexpected SPIR-V header in {spirv}: magic={magic:#x}, "
                f"version={version:#x}"
            )

    if artifact_scope == "spirv":
        return artifacts

    if artifact_scope != "metal":
        dxil = pathlib.Path(f"{base}.dxil")
        if dxil.read_bytes()[:4] != b"DXBC":
            raise ValueError(f"Invalid DXIL container header: {dxil}")

    metal = pathlib.Path(f"{base}.metal")
    metal_text = metal.read_text(encoding="utf-8")
    if "#include <metal_stdlib>" not in metal_text:
        raise ValueError(f"Invalid MSL source: {metal}")

    # DXIL cannot be produced outside Windows, so the macOS scope stops here.
    if artifact_scope == "metal":
        return artifacts

    reflection = pathlib.Path(f"{base}.reflection.json")
    reflection_data = json.loads(reflection.read_text(encoding="utf-8"))
    if reflection_data.get("schemaVersion") != 2:
        raise ValueError(f"Unexpected reflection schema: {reflection}")
    entry_point = reflection_data.get("entryPoint", {})
    if entry_point.get("name") != program["entryPoint"]:
        raise ValueError(f"Unexpected reflection entry point: {reflection}")
    if entry_point.get("stage") != program["stage"]:
        raise ValueError(f"Unexpected reflection stage: {reflection}")
    if entry_point.get("profile") != program["profile"]:
        raise ValueError(f"Unexpected reflection profile: {reflection}")
    for resource in reflection_data.get("resources", []):
        bindings = resource.get("bindings", {})
        if set(bindings) != {"d3d", "vulkan"}:
            raise ValueError(f"Incomplete resource bindings: {reflection}")

    return artifacts


def validate_command_plan(
    plan: dict[str, Any],
    manifest: dict[str, Any],
    model: dict[str, Any],
    artifact_scope: str,
) -> None:
    if plan.get("toolchain") != model:
        raise ValueError("Executed command toolchain does not match the lock")
    if plan.get("artifactScope", "full") != artifact_scope:
        raise ValueError("Executed command artifact scope does not match")
    expected_commands = set(command_names_for_artifact_scope(artifact_scope))
    plan_programs = plan.get("programs")
    if not isinstance(plan_programs, list):
        raise ValueError("Executed command programs must be a list")
    by_name = {
        program.get("name"): program
        for program in plan_programs
        if isinstance(program, dict)
    }
    if len(by_name) != len(plan_programs):
        raise ValueError("Executed command programs are invalid or duplicated")
    if set(by_name) != {
        program["name"]
        for program in manifest["programs"]
    }:
        raise ValueError("Executed command program set does not match the manifest")
    for program in manifest["programs"]:
        planned = by_name[program["name"]]
        for key in ("source", "entryPoint", "stage", "profile"):
            if planned.get(key) != program[key]:
                raise ValueError(f"Executed command {key} does not match")
        commands = planned.get("commands")
        if not isinstance(commands, dict) or set(commands) != expected_commands:
            raise ValueError(
                f"Executed commands do not match {artifact_scope} scope: "
                f"{program['name']}"
            )


def reject_unexpected_shader_artifacts(
    output_root: pathlib.Path,
    manifest: dict[str, Any],
    expected_artifacts: list[pathlib.Path],
) -> None:
    expected = set(expected_artifacts)
    known_suffixes = artifact_suffixes_for_scope(
        "full",
        require_metallib=True,
    )
    expected_by_name = {
        path.name
        for path in expected
    }
    unexpected = []
    for path in output_root.iterdir():
        if not path.is_file() or path.name in expected_by_name:
            continue
        if any(path.name.endswith(f".{suffix}") for suffix in known_suffixes):
            unexpected.append(path.name)
    for program in manifest["programs"]:
        base = output_root / program["name"]
        for suffix in known_suffixes:
            path = pathlib.Path(f"{base}.{suffix}")
            if path.is_file() and path not in expected:
                unexpected.append(path.name)
    if unexpected:
        raise ValueError(
            f"Unexpected shader artifacts for this scope: {sorted(unexpected)}"
        )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-root", required=True, type=pathlib.Path)
    parser.add_argument("--manifest", required=True, type=pathlib.Path)
    parser.add_argument("--lock", required=True, type=pathlib.Path)
    parser.add_argument(
        "--artifact-scope",
        choices=ARTIFACT_SCOPES,
        default="full",
    )
    parser.add_argument("--require-metallib", action="store_true")
    args = parser.parse_args()

    lock = json.loads(args.lock.read_text(encoding="utf-8"))
    model = build_lock_model(lock)
    manifest = validate_manifest(
        json.loads(args.manifest.read_text(encoding="utf-8")),
        model,
    )
    expected_spirv_version = spirv_version_word(model["spirvVersion"])
    command_path = args.output_root / "executed-commands.json"
    require_file(command_path)
    command_plan = json.loads(command_path.read_text(encoding="utf-8"))
    validate_command_plan(
        command_plan,
        manifest,
        model,
        args.artifact_scope,
    )
    artifacts = []
    for program in manifest["programs"]:
        artifacts.extend(
            verify_program(
                args.output_root,
                program,
                expected_spirv_version,
                args.artifact_scope,
                args.require_metallib,
            )
        )
    reject_unexpected_shader_artifacts(
        args.output_root,
        manifest,
        artifacts,
    )

    hashes = {
        path.relative_to(args.output_root).as_posix(): sha256(path)
        for path in sorted(artifacts)
    }
    artifact_manifest = {
        "schemaVersion": 1,
        "toolchain": model,
        "executedCommands": {
            "path": "executed-commands.json",
            "sha256": sha256(command_path),
        },
        "artifacts": hashes,
    }
    if args.artifact_scope != "full":
        artifact_manifest["artifactScope"] = args.artifact_scope
    output_path = args.output_root / "artifact-manifest.json"
    output_path.write_text(
        json.dumps(artifact_manifest, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
