# Copyright (c) marcschier. Licensed under the MIT License.

import argparse
import hashlib
import json
import pathlib
import re
import struct
from typing import Any

from shader_model import (
    build_lock_model,
    metal_library_contract,
    metal_library_programs,
    required_checked_inputs,
    validate_checked_input_bytes,
    validate_manifest,
)


SPIRV_MAGIC = 0x07230203
STAGE_ATTRIBUTES = {
    "vertex": "vertex",
    "fragment": "fragment",
    "compute": "kernel",
}


def sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def spirv_version_word(version: str) -> int:
    major, minor = (int(value) for value in version.split("."))
    return (major << 16) | (minor << 8)


def validate_spirv_bytes(content: bytes, version: str) -> None:
    if len(content) < 20 or len(content) % 4 != 0:
        raise ValueError("SPIR-V payload has an invalid size")
    magic, actual_version = struct.unpack("<II", content[:8])
    if magic != SPIRV_MAGIC:
        raise ValueError("SPIR-V payload has an invalid magic number")
    if actual_version != spirv_version_word(version):
        raise ValueError("SPIR-V payload does not match the locked version")


def validate_msl_text(text: str, entry_point: str, stage: str) -> None:
    if "#line" in text:
        raise ValueError("MSL payload contains line directives")
    declarations = []
    pattern = re.compile(
        r"\[\[\s*(vertex|fragment|kernel)\s*\]\]\s+"
        r"([^;{}\[\]]+?)\(",
        re.MULTILINE,
    )
    for match in pattern.finditer(text):
        name_match = re.search(r"([A-Za-z_][A-Za-z0-9_]*)\s*$", match.group(2))
        if name_match is not None:
            declarations.append((match.group(1), name_match.group(1)))
    expected = (STAGE_ATTRIBUTES[stage], entry_point)
    if expected not in declarations:
        raise ValueError(f"MSL payload is missing {stage} entry point {entry_point}")


def exported_symbol_names(text: str) -> set[str]:
    names = set()
    for line in text.splitlines():
        match = re.fullmatch(
            r"\s*(?:0x)?[0-9A-Fa-f]+\s+(.+?)\s+"
            r"([A-Za-z_][A-Za-z0-9_]*)\s*",
            line,
        )
        if match is None:
            continue
        columns = match.group(1).split()
        is_nm_export = columns == ["T"]
        is_objdump_export = "g" in columns and "F" in columns
        if is_nm_export or is_objdump_export:
            names.add(match.group(2))
    return names


def validate_exact_exported_symbol(text: str, entry_point: str) -> None:
    if entry_point not in exported_symbol_names(text):
        raise ValueError(f"Metallib is missing exact exported entry {entry_point}")


def validate_exact_exported_symbols(
    text: str,
    entry_points: list[str],
) -> None:
    for entry_point in entry_points:
        validate_exact_exported_symbol(text, entry_point)


def validate_provenance_records(
    records: list[dict[str, Any]],
    required_paths: tuple[str, ...],
) -> None:
    paths = []
    for record in records:
        if not isinstance(record, dict):
            raise ValueError("Checked manifest input record is invalid")
        path = record.get("path")
        if not isinstance(path, str) or not path:
            raise ValueError("Checked manifest input path is invalid")
        paths.append(path)
    if len(paths) != len(set(paths)):
        raise ValueError("Checked manifest inputs contain duplicate paths")
    actual = set(paths)
    required = set(required_paths)
    if actual != required:
        missing = sorted(required - actual)
        unexpected = sorted(actual - required)
        raise ValueError(
            "Checked manifest inputs do not match the required set: "
            f"missing={missing}, unexpected={unexpected}"
        )


def validate_reflection(
    reflection: dict[str, Any],
    program: dict[str, Any],
) -> None:
    if reflection.get("schemaVersion") != 2:
        raise ValueError("Checked reflection schema is not version 2")
    entry = reflection.get("entryPoint", {})
    for key in ("name", "stage", "profile"):
        expected = program["entryPoint" if key == "name" else key]
        if entry.get(key) != expected:
            raise ValueError(f"Checked reflection {key} mismatch")
    reflected_resources = {
        resource["name"]: resource
        for resource in reflection.get("resources", [])
    }
    if set(reflected_resources) != {
        resource["name"]
        for resource in program["resources"]
    }:
        raise ValueError("Checked reflection resource contract mismatch")
    for resource in program["resources"]:
        reflected = reflected_resources[resource["name"]]
        if reflected.get("bindings", {}).get("d3d") != resource["d3d"]:
            raise ValueError("Checked reflection D3D binding mismatch")
        if reflected.get("bindings", {}).get("vulkan") != resource["vulkan"]:
            raise ValueError("Checked reflection Vulkan binding mismatch")
        if reflected.get("shape", {}).get("access") != resource["access"]:
            raise ValueError("Checked reflection resource access mismatch")
    thread_group_size = entry.get("threadGroupSize")
    if program["stage"] == "compute":
        if (
            not isinstance(thread_group_size, list)
            or len(thread_group_size) != 3
            or any(
                not isinstance(value, int) or isinstance(value, bool) or value <= 0
                for value in thread_group_size
            )
        ):
            raise ValueError("Checked compute threadGroupSize is invalid")
    elif thread_group_size is not None:
        raise ValueError("Non-compute reflection has a threadGroupSize")
    for interface_name in ("stageInputs", "stageOutputs"):
        for value in reflection.get(interface_name, []):
            semantic = value.get("semantic", {})
            if semantic.get("systemValue"):
                if value.get("location") is not None:
                    raise ValueError("System value must have a null location")
            elif not isinstance(value.get("location"), int):
                raise ValueError("User varying must have an integer location")


def validate_checked(
    checked_root: pathlib.Path,
    lock_path: pathlib.Path,
    shader_manifest_path: pathlib.Path,
    skip_hashes: bool = False,
) -> dict[str, Any]:
    checked_manifest_path = checked_root / "manifest.json"
    checked_manifest = json.loads(
        checked_manifest_path.read_text(encoding="utf-8")
    )
    lock = json.loads(lock_path.read_text(encoding="utf-8"))
    shader_manifest = json.loads(
        shader_manifest_path.read_text(encoding="utf-8")
    )
    model = build_lock_model(lock)
    source_manifest = shader_manifest
    shader_manifest = validate_manifest(shader_manifest, model)
    if checked_manifest.get("schemaVersion") != 2:
        raise ValueError("Checked manifest schema is not version 2")
    if checked_manifest.get("toolchain") != model:
        raise ValueError("Checked manifest toolchain does not match the lock")
    executed_path = checked_root / "executed-commands.json"
    executed = json.loads(executed_path.read_text(encoding="utf-8"))
    if executed.get("toolchain") != model:
        raise ValueError("Checked executed commands do not match the lock")
    executed_programs = {
        program["name"]: program
        for program in executed.get("programs", [])
    }
    repository_root = lock_path.resolve().parents[2]
    input_values = checked_manifest.get("inputs")
    if not isinstance(input_values, list):
        raise ValueError("Checked manifest inputs must be a list")
    validate_provenance_records(
        input_values,
        required_checked_inputs(source_manifest),
    )
    for input_value in input_values:
        relative_path = input_value.get("path")
        input_path = repository_root / relative_path
        if not input_path.is_file():
            raise ValueError(f"Checked manifest input is missing: {relative_path}")
        input_content = input_path.read_bytes()
        validate_checked_input_bytes(relative_path, input_content)
        if hashlib.sha256(input_content).hexdigest() != input_value.get("sha256"):
            raise ValueError(f"Checked manifest input hash mismatch: {relative_path}")

    checked_programs = {
        program["name"]: program
        for program in checked_manifest.get("programs", [])
    }
    expected_program_names = {
        program["name"]
        for program in shader_manifest["programs"]
    }
    if set(checked_programs) != expected_program_names:
        raise ValueError("Checked manifest program set does not match the manifest")
    expected_files = {"manifest.json", "executed-commands.json"}
    for program in shader_manifest["programs"]:
        checked_program = checked_programs.get(program["name"])
        if checked_program is None:
            raise ValueError(f"Checked program is missing: {program['name']}")
        for key in ("source", "entryPoint", "stage", "profile"):
            if checked_program.get(key) != program[key]:
                raise ValueError(f"Checked program {key} mismatch")
        if checked_program.get("resourceContract") != program["resources"]:
            raise ValueError("Checked resource contract mismatch")
        executed_program = executed_programs.get(program["name"])
        if executed_program is None:
            raise ValueError("Checked executed commands are incomplete")
        if checked_program.get("executedCommands") != executed_program.get("commands"):
            raise ValueError("Checked executed command arguments mismatch")

        for suffix in ("dxil", "spv", "metal", "reflection.json"):
            expected_files.add(f"{program['name']}.{suffix}")
            artifact = checked_program["artifacts"][suffix]
            path = checked_root / f"{program['name']}.{suffix}"
            if not path.is_file() or path.stat().st_size == 0:
                raise ValueError(f"Checked artifact is missing: {path}")
            if not skip_hashes and sha256(path) != artifact["sha256"]:
                raise ValueError(f"Checked artifact hash mismatch: {path.name}")
            if path.stat().st_size != artifact["size"]:
                raise ValueError(f"Checked artifact size mismatch: {path.name}")

        dxil = checked_root / f"{program['name']}.dxil"
        if dxil.read_bytes()[:4] != b"DXBC":
            raise ValueError(f"Invalid checked DXIL: {dxil.name}")
        spirv = checked_root / f"{program['name']}.spv"
        validate_spirv_bytes(spirv.read_bytes(), model["spirvVersion"])
        metal = checked_root / f"{program['name']}.metal"
        validate_msl_text(
            metal.read_text(encoding="utf-8"),
            program["entryPoint"],
            program["stage"],
        )
        reflection = checked_root / f"{program['name']}.reflection.json"
        validate_reflection(
            json.loads(reflection.read_text(encoding="utf-8")),
            program,
        )
    actual_files = {
        path.name
        for path in checked_root.iterdir()
        if path.is_file()
    }
    if actual_files != expected_files:
        raise ValueError("Checked payload file set does not match the manifest")
    return {
        "model": model,
        "manifest": shader_manifest,
        "checkedManifest": checked_manifest,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--checked-root", type=pathlib.Path)
    parser.add_argument("--lock", type=pathlib.Path)
    parser.add_argument("--manifest", type=pathlib.Path)
    parser.add_argument("--skip-hashes", action="store_true")
    parser.add_argument("--symbols-input", type=pathlib.Path)
    parser.add_argument("--entry-point")
    parser.add_argument("--print-required-inputs", action="store_true")
    parser.add_argument("--print-metal-library-contract", action="store_true")
    args = parser.parse_args()
    if args.print_required_inputs:
        if args.manifest is None:
            parser.error("--manifest is required with --print-required-inputs")
        shader_manifest = json.loads(
            args.manifest.read_text(encoding="utf-8")
        )
        print(json.dumps(required_checked_inputs(shader_manifest)))
        return 0
    if args.print_metal_library_contract:
        if args.manifest is None:
            parser.error(
                "--manifest is required with --print-metal-library-contract"
            )
        shader_manifest = json.loads(
            args.manifest.read_text(encoding="utf-8")
        )
        metal_library_programs(shader_manifest)
        print(json.dumps(metal_library_contract(shader_manifest)))
        return 0
    if args.symbols_input is not None:
        if args.entry_point is None:
            parser.error("--entry-point is required with --symbols-input")
        validate_exact_exported_symbol(
            args.symbols_input.read_text(encoding="utf-8"),
            args.entry_point,
        )
        return 0
    if args.checked_root is None or args.lock is None or args.manifest is None:
        parser.error("--checked-root, --lock, and --manifest are required")
    validate_checked(
        args.checked_root,
        args.lock,
        args.manifest,
        args.skip_hashes,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
