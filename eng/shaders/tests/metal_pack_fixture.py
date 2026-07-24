# Copyright (c) marcschier. Licensed under the MIT License.

import argparse
import copy
import hashlib
import json
import pathlib
import sys


SCRIPT_ROOT = pathlib.Path(__file__).resolve().parents[1] / "scripts"
sys.path.insert(0, str(SCRIPT_ROOT))

import shader_model  # noqa: E402


LIBRARY_BYTES = b"validated synthetic ten-entry metallib fixture"
SYMBOL_DUMP = (
    b"vertexMain\nfragmentMain\npickVertexMain\npickFragmentMain\n"
    b"fillMain\nscaleMain\n"
)


def sha256(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def file_record(
    repository_root: pathlib.Path,
    program_name: str,
    entry_point: str,
    stage: str,
) -> dict:
    relative_path = f"eng/shaders/checked/{program_name}.metal"
    content = (repository_root / relative_path).read_bytes()
    return {
        "programName": program_name,
        "path": relative_path,
        "sha256": sha256(content),
        "size": len(content),
        "entryPoint": entry_point,
        "stage": stage,
    }


def create_sidecar(
    repository_root: pathlib.Path,
    output_root: pathlib.Path,
) -> dict:
    manifest_path = repository_root / "eng/shaders/shader-manifest.json"
    lock_path = repository_root / "eng/shaders/toolchain.lock.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    lock = json.loads(lock_path.read_text(encoding="utf-8"))
    model = shader_model.build_lock_model(lock)
    payload_root = output_root.relative_to(repository_root).as_posix()
    sources = []
    air = []
    entries = []
    compile_commands = []
    symbol_commands = []
    for program_name, entry_point, stage in shader_model.METAL_LIBRARY_ENTRIES:
        source = file_record(
            repository_root,
            program_name,
            entry_point,
            stage,
        )
        air_path = f"{payload_root}/{program_name}.air"
        sources.append(source)
        air.append(
            {
                "programName": program_name,
                "path": air_path,
                "sha256": sha256(program_name.encode("utf-8")),
                "size": len(program_name),
                "entryPoint": entry_point,
                "stage": stage,
            }
        )
        entries.append(
            {
                "programName": program_name,
                "name": entry_point,
                "stage": stage,
            }
        )
        compile_commands.append(
            {
                "programName": program_name,
                "executable": "xcrun",
                "arguments": [
                    "-sdk",
                    "macosx",
                    "metal",
                    f"-std={model['metalStandard']}",
                    "-c",
                    source["path"],
                    "-o",
                    air_path,
                ],
            }
        )
        symbol_commands.append(
            {
                "programName": program_name,
                "executable": "python",
                "arguments": [
                    "eng/shaders/scripts/checked_payload.py",
                    "--symbols-input",
                    f"{payload_root}/mesh.symbols.txt",
                    "--entry-point",
                    entry_point,
                ],
            }
        )

    provenance = []
    for relative_path in shader_model.required_checked_inputs(manifest):
        content = (repository_root / relative_path).read_bytes()
        provenance.append(
            {
                "path": relative_path,
                "sha256": sha256(content),
            }
        )

    return {
        "schemaVersion": 4,
        "rid": "osx-arm64",
        "checkedRoot": "eng/shaders/checked",
        "payloadRoot": payload_root,
        "stagedManifestPath": (
            "eng/shaders/checked/mesh.metallib.manifest.json"
        ),
        "toolchain": model,
        "provenance": provenance,
        "library": {
            "name": "mesh",
            "path": "mesh.metallib",
            "stagedPath": "eng/shaders/checked/mesh.metallib",
            "sha256": sha256(LIBRARY_BYTES),
            "size": len(LIBRARY_BYTES),
            "sources": sources,
            "air": air,
            "entryPoints": entries,
            "symbolDump": "mesh.symbols.txt",
            "symbolDumpSha256": sha256(SYMBOL_DUMP),
            "symbolDumpSize": len(SYMBOL_DUMP),
            "commands": {
                "compile": compile_commands,
                "link": {
                    "executable": "xcrun",
                    "arguments": [
                        "-sdk",
                        "macosx",
                        "metallib",
                        *[record["path"] for record in air],
                        "-o",
                        f"{payload_root}/mesh.metallib",
                    ],
                },
                "inspect": {
                    "executable": "xcrun",
                    "arguments": [
                        "metal-objdump",
                        "--syms",
                        f"{payload_root}/mesh.metallib",
                    ],
                },
                "validateSymbols": symbol_commands,
            },
        },
    }


def write_json(path: pathlib.Path, value: dict) -> None:
    path.write_text(
        json.dumps(value, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def write_fixtures(
    repository_root: pathlib.Path,
    output_root: pathlib.Path,
) -> None:
    output_root.mkdir(parents=True, exist_ok=True)
    sidecar = create_sidecar(repository_root, output_root)
    (output_root / "mesh.metallib").write_bytes(LIBRARY_BYTES)
    (output_root / "corrupt.metallib").write_bytes(LIBRARY_BYTES + b"corrupt")
    write_json(output_root / "valid.json", sidecar)

    wrong_hash = copy.deepcopy(sidecar)
    wrong_hash["library"]["sha256"] = "0" * 64
    write_json(output_root / "wrong-hash.json", wrong_hash)

    wrong_size = copy.deepcopy(sidecar)
    wrong_size["library"]["size"] += 1
    write_json(output_root / "wrong-size.json", wrong_size)

    missing_compute = copy.deepcopy(sidecar)
    missing_compute["library"]["entryPoints"].pop()
    write_json(output_root / "missing-compute.json", missing_compute)

    stale_source = copy.deepcopy(sidecar)
    stale_source["library"]["sources"][0]["sha256"] = "0" * 64
    write_json(output_root / "stale-source.json", stale_source)

    malformed_record = copy.deepcopy(sidecar)
    malformed_record["library"]["sources"][0].pop("stage")
    write_json(output_root / "malformed-record.json", malformed_record)

    extra_record = copy.deepcopy(sidecar)
    extra_record["library"]["entryPoints"].append(
        {
            "programName": "fake",
            "name": "helper",
            "stage": "compute",
        }
    )
    write_json(output_root / "extra-record.json", extra_record)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository-root", required=True, type=pathlib.Path)
    parser.add_argument("--output-root", required=True, type=pathlib.Path)
    args = parser.parse_args()
    repository_root = args.repository_root.resolve()
    output_root = args.output_root.resolve()
    output_root.relative_to(repository_root)
    write_fixtures(repository_root, output_root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
