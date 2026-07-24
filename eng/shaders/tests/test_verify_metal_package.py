# Copyright (c) marcschier. Licensed under the MIT License.

import copy
import hashlib
import json
import pathlib
import sys
import unittest


SCRIPT_ROOT = pathlib.Path(__file__).resolve().parents[1] / "scripts"
sys.path.insert(0, str(SCRIPT_ROOT))

import metal_sidecar  # noqa: E402
import metal_pack_fixture  # noqa: E402
import shader_model  # noqa: E402


CONTRACTS = shader_model.METAL_LIBRARY_ENTRIES
PAYLOAD_ROOT = "eng/shaders/artifacts/metal-osx-arm64"
LIBRARY_BYTES = b"validated ten-entry metallib"
SYMBOL_DUMP = (
    b"vertexMain\nfragmentMain\npickVertexMain\npickFragmentMain\n"
    b"fillMain\nscaleMain\n"
)
HASH = "1" * 64


def lock() -> dict:
    return {
        "targets": {
            "direct3D": {"shaderModel": "6.0", "format": "DXIL"},
            "vulkan": {"apiVersion": "1.2", "spirvVersion": "1.5"},
            "metal": {"languageVersion": "2.4", "xcodeVersion": "16.4"},
        },
        "slang": {"version": "2026.13.1", "commit": "slang-commit"},
        "spirvTools": {
            "version": "vulkan-sdk-1.4.350.1",
            "commit": "spirv-commit",
        },
    }


def shader_manifest() -> dict:
    programs = []
    for program_name, entry_point, stage in CONTRACTS:
        programs.append(
            {
                "name": program_name,
                "source": (
                    "eng/shaders/sources/mesh.slang"
                    if program_name.startswith("mesh.")
                    else (
                        (
                            "eng/shaders/sources/pick.vertex.slang"
                            if program_name == "pick.vertex"
                            else "eng/shaders/sources/pick.fragment.slang"
                        )
                        if program_name.startswith("pick.")
                        else "eng/shaders/sources/compute.slang"
                    )
                ),
                "entryPoint": entry_point,
                "stage": stage,
            }
        )
    return {"programs": programs}


def valid_sidecar() -> dict:
    manifest = shader_manifest()
    sources = []
    air = []
    entries = []
    compile_commands = []
    symbol_commands = []
    for program_name, entry_point, stage in CONTRACTS:
        source_path = f"eng/shaders/checked/{program_name}.metal"
        air_path = f"{PAYLOAD_ROOT}/{program_name}.air"
        sources.append(
            {
                "programName": program_name,
                "path": source_path,
                "sha256": HASH,
                "size": 101,
                "entryPoint": entry_point,
                "stage": stage,
            }
        )
        air.append(
            {
                "programName": program_name,
                "path": air_path,
                "sha256": HASH,
                "size": 202,
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
                    "-std=metal2.4",
                    "-c",
                    source_path,
                    "-o",
                    air_path,
                ],
            }
        )
        symbol_commands.append(
            {
                "programName": program_name,
                "executable": "python3",
                "arguments": [
                    "eng/shaders/scripts/checked_payload.py",
                    "--symbols-input",
                    f"{PAYLOAD_ROOT}/mesh.symbols.txt",
                    "--entry-point",
                    entry_point,
                ],
            }
        )

    return {
        "schemaVersion": 4,
        "rid": "osx-arm64",
        "checkedRoot": "eng/shaders/checked",
        "payloadRoot": PAYLOAD_ROOT,
        "stagedManifestPath": (
            "eng/shaders/checked/mesh.metallib.manifest.json"
        ),
        "toolchain": shader_model.build_lock_model(lock()),
        "provenance": [
            {"path": path, "sha256": HASH}
            for path in shader_model.required_checked_inputs(manifest)
        ],
        "library": {
            "name": "mesh",
            "path": "mesh.metallib",
            "stagedPath": "eng/shaders/checked/mesh.metallib",
            "sha256": hashlib.sha256(LIBRARY_BYTES).hexdigest(),
            "size": len(LIBRARY_BYTES),
            "sources": sources,
            "air": air,
            "entryPoints": entries,
            "symbolDump": "mesh.symbols.txt",
            "symbolDumpSha256": hashlib.sha256(SYMBOL_DUMP).hexdigest(),
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
                        f"{PAYLOAD_ROOT}/mesh.metallib",
                    ],
                },
                "inspect": {
                    "executable": "xcrun",
                    "arguments": [
                        "metal-objdump",
                        "--syms",
                        f"{PAYLOAD_ROOT}/mesh.metallib",
                    ],
                },
                "validateSymbols": [
                    {**command, "executable": "python"}
                    for command in symbol_commands
                ],
            },
        },
    }


def validate(sidecar: dict) -> None:
    metal_sidecar.validate_sidecar(
        sidecar,
        shader_manifest(),
        lock(),
        pathlib.Path.cwd(),
        verify_files=False,
        library_content=LIBRARY_BYTES,
    )


class VerifyMetalPackageTests(unittest.TestCase):
    def test_complete_schema_v4_is_accepted(self) -> None:
        validate(valid_sidecar())

    def test_missing_fields_and_records_are_rejected(self) -> None:
        mutations = [
            lambda value: value.pop("rid"),
            lambda value: value["library"]["sources"][0].pop("size"),
            lambda value: value["library"]["sources"].pop(),
            lambda value: value["library"]["air"].pop(),
            lambda value: value["library"]["entryPoints"].pop(),
            lambda value: value["library"]["commands"]["compile"].pop(),
            lambda value: value["provenance"].pop(),
        ]
        for mutation in mutations:
            invalid = valid_sidecar()
            mutation(invalid)
            with self.subTest(mutation=mutation):
                with self.assertRaises(ValueError):
                    validate(invalid)

    def test_duplicate_records_are_rejected(self) -> None:
        accessors = [
            lambda value: value["library"]["sources"],
            lambda value: value["library"]["air"],
            lambda value: value["library"]["entryPoints"],
            lambda value: value["library"]["commands"]["compile"],
            lambda value: value["library"]["commands"]["validateSymbols"],
            lambda value: value["provenance"],
        ]
        for accessor in accessors:
            invalid = valid_sidecar()
            records = accessor(invalid)
            records[-1] = copy.deepcopy(records[0])
            with self.subTest(accessor=accessor):
                with self.assertRaises(ValueError):
                    validate(invalid)

    def test_extra_fake_entry_and_top_level_key_are_rejected(self) -> None:
        invalid_entry = valid_sidecar()
        invalid_entry["library"]["entryPoints"].append(
            {"programName": "fake", "name": "helper", "stage": "compute"}
        )
        with self.assertRaises(ValueError):
            validate(invalid_entry)

        invalid_key = valid_sidecar()
        invalid_key["unreviewed"] = True
        with self.assertRaises(ValueError):
            validate(invalid_key)

    def test_absolute_and_traversing_paths_are_rejected(self) -> None:
        mutations = [
            lambda value: value["library"]["sources"][0].__setitem__(
                "path", "C:/secret/mesh.vertex.metal"
            ),
            lambda value: value["library"]["air"][0].__setitem__(
                "path", "../mesh.vertex.air"
            ),
            lambda value: value["library"].__setitem__(
                "symbolDump", "/private/mesh.symbols.txt"
            ),
            lambda value: value["library"]["commands"]["compile"][0][
                "arguments"
            ].__setitem__(5, "/private/mesh.vertex.air"),
        ]
        for mutation in mutations:
            invalid = valid_sidecar()
            mutation(invalid)
            with self.subTest(mutation=mutation):
                with self.assertRaises(ValueError):
                    validate(invalid)

    def test_bad_hashes_and_sizes_are_rejected(self) -> None:
        mutations = [
            lambda value: value["library"].__setitem__("sha256", HASH),
            lambda value: value["library"].__setitem__(
                "size", len(LIBRARY_BYTES) + 1
            ),
            lambda value: value["library"]["sources"][0].__setitem__(
                "sha256", "bad"
            ),
            lambda value: value["library"]["air"][0].__setitem__("size", 0),
        ]
        for mutation in mutations:
            invalid = valid_sidecar()
            mutation(invalid)
            with self.subTest(mutation=mutation):
                with self.assertRaises(ValueError):
                    validate(invalid)

    def test_source_air_entry_and_command_mapping_must_match(self) -> None:
        mutations = [
            lambda value: value["library"]["sources"][0].__setitem__(
                "entryPoint", "helper"
            ),
            lambda value: value["library"]["air"][0].__setitem__(
                "stage", "compute"
            ),
            lambda value: value["library"]["entryPoints"][0].__setitem__(
                "programName", "mesh.fragment"
            ),
            lambda value: value["library"]["commands"]["link"][
                "arguments"
            ].reverse(),
        ]
        for mutation in mutations:
            invalid = valid_sidecar()
            mutation(invalid)
            with self.subTest(mutation=mutation):
                with self.assertRaises(ValueError):
                    validate(invalid)

    def test_pack_mode_verifies_library_checked_sources_and_provenance(
        self,
    ) -> None:
        repository_root = pathlib.Path.cwd()
        output_root = repository_root / "eng/shaders/out/test-pack-sidecar"
        sidecar = metal_pack_fixture.create_sidecar(
            repository_root,
            output_root,
        )
        actual_manifest = json.loads(
            (
                repository_root / "eng/shaders/shader-manifest.json"
            ).read_text(encoding="utf-8")
        )
        actual_lock = json.loads(
            (
                repository_root / "eng/shaders/toolchain.lock.json"
            ).read_text(encoding="utf-8")
        )
        metal_sidecar.validate_sidecar(
            sidecar,
            actual_manifest,
            actual_lock,
            repository_root,
            verify_files=False,
            verify_checked_files=True,
            library_content=metal_pack_fixture.LIBRARY_BYTES,
        )

        mutations = [
            lambda value: value["library"]["sources"][0].__setitem__(
                "sha256", "0" * 64
            ),
            lambda value: value["provenance"][0].__setitem__(
                "sha256", "0" * 64
            ),
        ]
        for mutation in mutations:
            invalid = copy.deepcopy(sidecar)
            mutation(invalid)
            with self.subTest(mutation=mutation):
                with self.assertRaises(ValueError):
                    metal_sidecar.validate_sidecar(
                        invalid,
                        actual_manifest,
                        actual_lock,
                        repository_root,
                        verify_files=False,
                        verify_checked_files=True,
                        library_content=metal_pack_fixture.LIBRARY_BYTES,
                    )


if __name__ == "__main__":
    unittest.main()
