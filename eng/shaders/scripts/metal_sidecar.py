# Copyright (c) marcschier. Licensed under the MIT License.

import argparse
import hashlib
import json
import pathlib
import re
from typing import Any

from shader_model import (
    build_lock_model,
    metal_library_contract,
    required_checked_inputs,
)


SHA256_PATTERN = re.compile(r"[0-9a-f]{64}")


def sha256_bytes(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def sha256_file(path: pathlib.Path) -> str:
    return sha256_bytes(path.read_bytes())


def require_keys(value: dict[str, Any], expected: set[str], context: str) -> None:
    if set(value) != expected:
        raise ValueError(
            f"{context} keys do not match: "
            f"missing={sorted(expected - set(value))}, "
            f"extra={sorted(set(value) - expected)}"
        )


def require_relative_path(value: Any, context: str) -> str:
    if not isinstance(value, str) or not value or "\\" in value:
        raise ValueError(f"{context} must be a non-empty POSIX relative path")
    if value.startswith("/") or re.match(r"^[A-Za-z]:", value):
        raise ValueError(f"{context} must not be absolute")
    if ".." in pathlib.PurePosixPath(value).parts:
        raise ValueError(f"{context} must not contain parent traversal")
    return value


def require_sha256(value: Any, context: str) -> str:
    if not isinstance(value, str) or SHA256_PATTERN.fullmatch(value) is None:
        raise ValueError(f"{context} must be a lowercase SHA-256 value")
    return value


def require_size(value: Any, context: str) -> int:
    if (
        not isinstance(value, int)
        or isinstance(value, bool)
        or value <= 0
    ):
        raise ValueError(f"{context} must be a positive integer")
    return value


def require_command_path(value: str, context: str) -> None:
    if "/" in value or "\\" in value or re.match(r"^[A-Za-z]:", value):
        require_relative_path(value, context)


def validate_file_record(
    record: dict[str, Any],
    context: str,
    repository_root: pathlib.Path,
    verify_file: bool,
) -> None:
    path = require_relative_path(record["path"], f"{context} path")
    digest = require_sha256(record["sha256"], f"{context} hash")
    size = require_size(record["size"], f"{context} size")
    if verify_file:
        full_path = repository_root / path
        if not full_path.is_file():
            raise ValueError(f"{context} file is missing: {path}")
        content = full_path.read_bytes()
        if len(content) != size:
            raise ValueError(f"{context} size does not match: {path}")
        if sha256_bytes(content) != digest:
            raise ValueError(f"{context} hash does not match: {path}")


def validate_sidecar(
    sidecar: dict[str, Any],
    shader_manifest: dict[str, Any],
    lock: dict[str, Any],
    repository_root: pathlib.Path,
    *,
    verify_files: bool,
    verify_checked_files: bool = False,
    verify_air: bool = False,
    library_content: bytes | None = None,
) -> None:
    verify_checked_files = verify_checked_files or verify_files
    require_keys(
        sidecar,
        {
            "schemaVersion",
            "rid",
            "checkedRoot",
            "payloadRoot",
            "stagedManifestPath",
            "toolchain",
            "provenance",
            "library",
        },
        "sidecar",
    )
    if sidecar["schemaVersion"] != 4 or sidecar["rid"] != "osx-arm64":
        raise ValueError("Sidecar schema or RID is invalid")
    checked_root = require_relative_path(
        sidecar["checkedRoot"],
        "checkedRoot",
    )
    payload_root = require_relative_path(
        sidecar["payloadRoot"],
        "payloadRoot",
    )
    staged_manifest_path = require_relative_path(
        sidecar["stagedManifestPath"],
        "stagedManifestPath",
    )
    if checked_root != "eng/shaders/checked":
        raise ValueError("checkedRoot does not match the runtime staging root")
    if staged_manifest_path != (
        "eng/shaders/checked/mesh.metallib.manifest.json"
    ):
        raise ValueError("stagedManifestPath does not match the package contract")
    model = build_lock_model(lock)
    if sidecar["toolchain"] != model:
        raise ValueError("Sidecar toolchain does not match the lock")

    provenance = sidecar["provenance"]
    if not isinstance(provenance, list):
        raise ValueError("Sidecar provenance must be a list")
    required_provenance = required_checked_inputs(shader_manifest)
    provenance_paths = []
    for index, record in enumerate(provenance):
        if not isinstance(record, dict):
            raise ValueError("Sidecar provenance record is invalid")
        require_keys(record, {"path", "sha256"}, f"provenance[{index}]")
        path = require_relative_path(record["path"], "provenance path")
        digest = require_sha256(record["sha256"], "provenance hash")
        provenance_paths.append(path)
        if verify_checked_files:
            full_path = repository_root / path
            if not full_path.is_file() or sha256_file(full_path) != digest:
                raise ValueError(f"Provenance hash does not match: {path}")
    if len(provenance_paths) != len(set(provenance_paths)):
        raise ValueError("Sidecar provenance contains duplicate paths")
    if set(provenance_paths) != set(required_provenance):
        raise ValueError("Sidecar provenance does not match the required set")

    library = sidecar["library"]
    if not isinstance(library, dict):
        raise ValueError("Sidecar library record is invalid")
    require_keys(
        library,
        {
            "name",
            "path",
            "stagedPath",
            "sha256",
            "size",
            "sources",
            "air",
            "entryPoints",
            "symbolDump",
            "symbolDumpSha256",
            "symbolDumpSize",
            "commands",
        },
        "library",
    )
    if library["name"] != "mesh" or library["path"] != "mesh.metallib":
        raise ValueError("Sidecar library identity is invalid")
    if library["stagedPath"] != "eng/shaders/checked/mesh.metallib":
        raise ValueError("Sidecar staged library path is invalid")
    library_hash = require_sha256(library["sha256"], "library hash")
    library_size = require_size(library["size"], "library size")
    if library_content is not None:
        if len(library_content) != library_size:
            raise ValueError("Combined Metal library size does not match")
        if sha256_bytes(library_content) != library_hash:
            raise ValueError("Combined Metal library hash does not match")
    symbol_dump = require_relative_path(
        library["symbolDump"],
        "symbol dump path",
    )
    if symbol_dump != "mesh.symbols.txt":
        raise ValueError("Sidecar symbol dump path is invalid")
    symbol_path = f"{payload_root}/{symbol_dump}"
    symbol_hash = require_sha256(
        library["symbolDumpSha256"],
        "symbol dump hash",
    )
    symbol_size = require_size(
        library["symbolDumpSize"],
        "symbol dump size",
    )

    contract = metal_library_contract()
    by_program = {entry["programName"]: entry for entry in contract}
    expected_programs = set(by_program)
    sources = library["sources"]
    air_records = library["air"]
    entries = library["entryPoints"]
    records = (sources, air_records, entries)
    if not all(isinstance(value, list) for value in records):
        raise ValueError("Sidecar source, AIR, and entry records must be lists")
    if not all(len(value) == len(contract) for value in records):
        raise ValueError(
            "Sidecar source, AIR, and entry record counts must match the contract"
        )

    source_programs = []
    for index, record in enumerate(sources):
        require_keys(
            record,
            {
                "programName",
                "path",
                "sha256",
                "size",
                "entryPoint",
                "stage",
            },
            f"sources[{index}]",
        )
        program_name = record["programName"]
        source_programs.append(program_name)
        expected = by_program.get(program_name)
        if expected is None:
            raise ValueError(f"Unexpected source program: {program_name}")
        if record["path"] != f"eng/shaders/checked/{program_name}.metal":
            raise ValueError(f"Source path does not match {program_name}")
        if (
            record["entryPoint"] != expected["entryPoint"]
            or record["stage"] != expected["stage"]
        ):
            raise ValueError(f"Source mapping does not match {program_name}")
        validate_file_record(
            record,
            f"source {program_name}",
            repository_root,
            verify_checked_files,
        )
    if len(source_programs) != len(set(source_programs)):
        raise ValueError("Sidecar source records contain duplicates")
    if set(source_programs) != expected_programs:
        raise ValueError("Sidecar source records do not match the contract")

    air_programs = []
    for index, record in enumerate(air_records):
        require_keys(
            record,
            {
                "programName",
                "path",
                "sha256",
                "size",
                "entryPoint",
                "stage",
            },
            f"air[{index}]",
        )
        program_name = record["programName"]
        air_programs.append(program_name)
        expected = by_program.get(program_name)
        if expected is None:
            raise ValueError(f"Unexpected AIR program: {program_name}")
        if record["path"] != f"{payload_root}/{program_name}.air":
            raise ValueError(f"AIR path does not match {program_name}")
        if (
            record["entryPoint"] != expected["entryPoint"]
            or record["stage"] != expected["stage"]
        ):
            raise ValueError(f"AIR mapping does not match {program_name}")
        validate_file_record(
            record,
            f"AIR {program_name}",
            repository_root,
            verify_files and verify_air,
        )
    if len(air_programs) != len(set(air_programs)):
        raise ValueError("Sidecar AIR records contain duplicates")
    if set(air_programs) != expected_programs:
        raise ValueError("Sidecar AIR records do not match the contract")

    entry_programs = []
    for index, record in enumerate(entries):
        require_keys(
            record,
            {"programName", "name", "stage"},
            f"entryPoints[{index}]",
        )
        program_name = record["programName"]
        entry_programs.append(program_name)
        expected = by_program.get(program_name)
        if expected is None or (
            record["name"] != expected["entryPoint"]
            or record["stage"] != expected["stage"]
        ):
            raise ValueError(f"Entry mapping does not match {program_name}")
    if len(entry_programs) != len(set(entry_programs)):
        raise ValueError("Sidecar entry records contain duplicates")
    if set(entry_programs) != expected_programs:
        raise ValueError("Sidecar entry records do not match the contract")

    commands = library["commands"]
    require_keys(
        commands,
        {"compile", "link", "inspect", "validateSymbols"},
        "commands",
    )
    compile_commands = commands["compile"]
    if (
        not isinstance(compile_commands, list)
        or len(compile_commands) != len(contract)
    ):
        raise ValueError("Sidecar compile command count must match the contract")
    compile_programs = []
    for index, command in enumerate(compile_commands):
        require_keys(
            command,
            {"programName", "executable", "arguments"},
            f"compile[{index}]",
        )
        program_name = command["programName"]
        compile_programs.append(program_name)
        expected = by_program.get(program_name)
        if expected is None or command["executable"] != "xcrun":
            raise ValueError(f"Compile command is invalid for {program_name}")
        expected_arguments = [
            "-sdk",
            "macosx",
            "metal",
            f"-std={model['metalStandard']}",
            "-c",
            f"eng/shaders/checked/{program_name}.metal",
            "-o",
            f"{payload_root}/{program_name}.air",
        ]
        if command["arguments"] != expected_arguments:
            raise ValueError(f"Compile command does not match {program_name}")
        for argument in command["arguments"]:
            if isinstance(argument, str):
                require_command_path(argument, "compile argument")
    if len(compile_programs) != len(set(compile_programs)):
        raise ValueError("Sidecar compile commands contain duplicates")
    if set(compile_programs) != expected_programs:
        raise ValueError("Sidecar compile commands do not match the contract")

    for command_name in ("link", "inspect"):
        command = commands[command_name]
        require_keys(
            command,
            {"executable", "arguments"},
            f"commands.{command_name}",
        )
        if command["executable"] != "xcrun":
            raise ValueError(f"{command_name} executable is invalid")
        for argument in command["arguments"]:
            if isinstance(argument, str):
                require_command_path(argument, f"{command_name} argument")
    expected_air_paths = [
        f"{payload_root}/{entry['programName']}.air"
        for entry in contract
    ]
    expected_link = [
        "-sdk",
        "macosx",
        "metallib",
        *expected_air_paths,
        "-o",
        f"{payload_root}/mesh.metallib",
    ]
    if commands["link"]["arguments"] != expected_link:
        raise ValueError("Link command does not match the library contract")
    expected_inspect = [
        "metal-objdump",
        "--syms",
        f"{payload_root}/mesh.metallib",
    ]
    if commands["inspect"]["arguments"] != expected_inspect:
        raise ValueError("Inspect command does not match the library contract")

    symbol_commands = commands["validateSymbols"]
    if (
        not isinstance(symbol_commands, list)
        or len(symbol_commands) != len(contract)
    ):
        raise ValueError(
            "Sidecar symbol validation command count must match the contract"
        )
    expected_symbol_commands = [
        {
            "programName": entry["programName"],
            "executable": "python",
            "arguments": [
                "eng/shaders/scripts/checked_payload.py",
                "--symbols-input",
                symbol_path,
                "--entry-point",
                entry["entryPoint"],
            ],
        }
        for entry in contract
    ]
    if symbol_commands != expected_symbol_commands:
        raise ValueError("Symbol validation commands do not match the contract")

    if verify_files:
        library_path = repository_root / payload_root / library["path"]
        if not library_path.is_file():
            raise ValueError("Combined Metal library is missing")
        library_content = library_path.read_bytes()
        if len(library_content) != library_size:
            raise ValueError("Combined Metal library size does not match")
        if sha256_bytes(library_content) != library_hash:
            raise ValueError("Combined Metal library hash does not match")
        full_symbol_path = repository_root / symbol_path
        if not full_symbol_path.is_file():
            raise ValueError("Metal symbol dump is missing")
        symbol_content = full_symbol_path.read_bytes()
        if len(symbol_content) != symbol_size:
            raise ValueError("Metal symbol dump size does not match")
        if sha256_bytes(symbol_content) != symbol_hash:
            raise ValueError("Metal symbol dump hash does not match")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--sidecar", required=True, type=pathlib.Path)
    parser.add_argument("--manifest", required=True, type=pathlib.Path)
    parser.add_argument("--lock", required=True, type=pathlib.Path)
    parser.add_argument("--library", type=pathlib.Path)
    parser.add_argument(
        "--repository-root",
        type=pathlib.Path,
        default=pathlib.Path.cwd(),
    )
    parser.add_argument("--verify-files", action="store_true")
    parser.add_argument("--verify-checked-files", action="store_true")
    parser.add_argument("--verify-air", action="store_true")
    args = parser.parse_args()
    validate_sidecar(
        json.loads(args.sidecar.read_text(encoding="utf-8")),
        json.loads(args.manifest.read_text(encoding="utf-8")),
        json.loads(args.lock.read_text(encoding="utf-8")),
        args.repository_root.resolve(),
        verify_files=args.verify_files,
        verify_checked_files=args.verify_checked_files,
        verify_air=args.verify_air,
        library_content=(
            args.library.read_bytes()
            if args.library is not None
            else None
        ),
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
