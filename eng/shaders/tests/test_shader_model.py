# Copyright (c) marcschier. Licensed under the MIT License.

import copy
import pathlib
import re
import sys
import unittest


SCRIPT_ROOT = pathlib.Path(__file__).resolve().parents[1] / "scripts"
SHADER_ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPT_ROOT))

import shader_model  # noqa: E402


def lock() -> dict:
    return {
        "targets": {
            "direct3D": {"shaderModel": "6.0", "format": "DXIL"},
            "vulkan": {"apiVersion": "1.2", "spirvVersion": "1.5"},
            "metal": {"languageVersion": "2.4", "xcodeVersion": "16.4"},
        },
        "slang": {"version": "1", "commit": "slang-commit"},
        "spirvTools": {"version": "2", "commit": "spirv-commit"},
    }


def manifest(profile: str = "cs_6_0") -> dict:
    return {
        "matrixLayout": "row-major",
        "programs": [
            {
                "name": "compute",
                "source": "eng/shaders/sources/compute.slang",
                "entryPoint": "main",
                "stage": "compute",
                "profile": profile,
                "resources": [
                    {
                        "name": "values",
                        "access": "readWrite",
                        "d3d": {
                            "registerClass": "u",
                            "register": 0,
                            "space": 0,
                        },
                        "vulkan": {"binding": 0, "set": 0},
                    }
                ],
            }
        ],
    }


def metal_manifest() -> dict:
    programs = []
    for program_name, entry_point, stage in shader_model.METAL_LIBRARY_ENTRIES:
        programs.append(
            {
                "name": program_name,
                "source": "unused.slang",
                "entryPoint": entry_point,
                "stage": stage,
                "profile": "unused",
                "resources": [],
            }
        )
    return {"programs": programs}


class ShaderModelTests(unittest.TestCase):
    def test_dry_run_uses_all_locked_target_versions(self) -> None:
        mutated_lock = copy.deepcopy(lock())
        mutated_lock["targets"]["direct3D"]["shaderModel"] = "6.1"
        mutated_lock["targets"]["vulkan"]["apiVersion"] = "1.3"
        mutated_lock["targets"]["vulkan"]["spirvVersion"] = "1.6"
        mutated_lock["targets"]["metal"]["languageVersion"] = "3.0"
        mutated_lock["targets"]["metal"]["xcodeVersion"] = "17.0"

        plan = shader_model.generate_plan(
            mutated_lock,
            manifest("cs_6_1"),
            "eng/shaders/out/dry-run",
        )
        commands = plan["programs"][0]["commands"]

        self.assertNotIn("artifactScope", plan)
        self.assertEqual(
            {
                "dxil",
                "spirv",
                "metal",
                "reflection",
                "spirvValidation",
            },
            set(commands),
        )
        self.assertIn("cs_6_1+spirv_1_6", commands["spirv"]["arguments"])
        self.assertIn("cs_6_1+METAL_3_0", commands["metal"]["arguments"])
        self.assertEqual(
            ["--target-env", "vulkan1.3", "eng/shaders/out/dry-run/compute.spv"],
            commands["spirvValidation"]["arguments"],
        )
        self.assertEqual("macos-metal3.0", plan["toolchain"]["metalStandard"])
        self.assertEqual("17.0", plan["toolchain"]["xcodeVersion"])

    def test_spirv_scope_contains_only_spirv_commands_and_artifacts(self) -> None:
        plan = shader_model.generate_plan(
            lock(),
            manifest(),
            "eng/shaders/out/dry-run",
            "spirv",
        )

        self.assertEqual("spirv", plan["artifactScope"])
        self.assertEqual(
            {"spirv", "spirvValidation"},
            set(plan["programs"][0]["commands"]),
        )
        self.assertNotIn(
            "-reflection-json",
            plan["programs"][0]["commands"]["spirv"]["arguments"],
        )
        self.assertEqual(
            ("spv",),
            shader_model.artifact_suffixes_for_scope("spirv"),
        )

    def test_rejects_invalid_artifact_scope(self) -> None:
        with self.assertRaisesRegex(ValueError, "artifact scope"):
            shader_model.generate_plan(
                lock(),
                manifest(),
                "eng/shaders/out/dry-run",
                "portable",
            )
        with self.assertRaisesRegex(ValueError, "full or metal artifact scope"):
            shader_model.artifact_suffixes_for_scope(
                "spirv",
                require_metallib=True,
            )

    def test_metal_scope_emits_only_metal_artifacts(self) -> None:
        self.assertEqual(
            ("metal",),
            shader_model.command_names_for_artifact_scope("metal"),
        )
        self.assertEqual(
            ("metal",),
            shader_model.artifact_suffixes_for_scope("metal"),
        )
        self.assertEqual(
            ("metal", "metallib"),
            shader_model.artifact_suffixes_for_scope(
                "metal",
                require_metallib=True,
            ),
        )

    def test_powershell_artifact_scopes_match_the_model(self) -> None:
        pattern = re.compile(
            r"\[ValidateSet\(([^)]*)\)\]\s*\[string\]\$ArtifactScope",
            re.MULTILINE,
        )
        for name in ("build-shaders.ps1", "verify-shaders.ps1"):
            with self.subTest(script=name):
                text = (SHADER_ROOT / name).read_text(encoding="utf-8")
                match = pattern.search(text)
                self.assertIsNotNone(match, f"{name} must validate ArtifactScope")
                declared = {
                    value.strip().strip("'").lower()
                    for value in match.group(1).split(",")
                }
                self.assertEqual(set(shader_model.ARTIFACT_SCOPES), declared)

    def test_required_checked_inputs_reject_carriage_returns(self) -> None:
        shader_model.validate_checked_input_bytes(
            "eng/shaders/example.ps1",
            b"first\nsecond\n",
        )
        for content in (b"first\r\nsecond\r\n", b"first\rsecond"):
            with self.subTest(content=content):
                with self.assertRaisesRegex(ValueError, "LF-only"):
                    shader_model.validate_checked_input_bytes(
                        "eng/shaders/example.ps1",
                        content,
                    )

    def test_rejects_profile_inconsistent_with_locked_shader_model(self) -> None:
        mutated_lock = copy.deepcopy(lock())
        mutated_lock["targets"]["direct3D"]["shaderModel"] = "6.1"
        with self.assertRaisesRegex(ValueError, "locked profile"):
            shader_model.generate_plan(
                mutated_lock,
                manifest("cs_6_0"),
                "eng/shaders/out/dry-run",
            )

    def test_rejects_incomplete_resource_binding_contract(self) -> None:
        invalid = manifest()
        del invalid["programs"][0]["resources"][0]["vulkan"]["set"]
        with self.assertRaisesRegex(ValueError, "incomplete Vulkan"):
            shader_model.generate_plan(
                lock(),
                invalid,
                "eng/shaders/out/dry-run",
            )

    def test_rejects_missing_resource_access_contract(self) -> None:
        invalid = manifest()
        del invalid["programs"][0]["resources"][0]["access"]
        with self.assertRaisesRegex(ValueError, "missing access"):
            shader_model.generate_plan(
                lock(),
                invalid,
                "eng/shaders/out/dry-run",
            )

    def test_rejects_invalid_resource_access_contract(self) -> None:
        invalid = manifest()
        invalid["programs"][0]["resources"][0]["access"] = "sideways"
        with self.assertRaisesRegex(ValueError, "invalid or missing access"):
            shader_model.generate_plan(
                lock(),
                invalid,
                "eng/shaders/out/dry-run",
            )

    def test_metal_library_contract_requires_all_entries(self) -> None:
        selected = shader_model.metal_library_programs(metal_manifest())
        self.assertEqual(len(shader_model.METAL_LIBRARY_ENTRIES), len(selected))
        for missing, _, _ in shader_model.METAL_LIBRARY_ENTRIES:
            invalid = metal_manifest()
            invalid["programs"] = [
                program
                for program in invalid["programs"]
                if program["name"] != missing
            ]
            with self.subTest(missing=missing):
                with self.assertRaisesRegex(ValueError, missing):
                    shader_model.metal_library_programs(invalid)


if __name__ == "__main__":
    unittest.main()
