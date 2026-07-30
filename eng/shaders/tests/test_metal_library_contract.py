# Copyright (c) marcschier. Licensed under the MIT License.

"""Guards the Metal library contract against the checked manifest.

The metallib entry-point contract is only consumed by validate-checked-payload.ps1
on the macOS leg, because packing the library needs Xcode. That meant a rename in
that script went unnoticed on Windows and Linux and only failed on the macOS CI
job, after the whole permutation expansion had already merged. Resolving the
contract is pure data, so it can and should be checked everywhere.
"""

import json
import pathlib
import sys
import unittest


REPO_ROOT = pathlib.Path(__file__).resolve().parents[3]
SCRIPT_ROOT = REPO_ROOT / "eng" / "shaders" / "scripts"
sys.path.insert(0, str(SCRIPT_ROOT))

import shader_model  # noqa: E402


def load(relative: str) -> dict:
    return json.loads(
        (REPO_ROOT / relative).read_text(encoding="utf-8")
    )


class MetalLibraryContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self.manifest = load("eng/shaders/shader-manifest.json")
        self.checked = load("eng/shaders/checked/manifest.json")
        # metal_library_contract() defaults to the pre-permutation base entries,
        # so it must be given the manifest to get the expanded set. Calling it
        # bare silently yields ten entries and would make this whole file vacuous.
        self.contracts = shader_model.metal_library_contract(self.manifest)

    def test_every_contract_entry_resolves_to_one_checked_program(self) -> None:
        by_name = {}
        for program in self.checked["programs"]:
            self.assertNotIn(
                program["name"],
                by_name,
                "checked manifest declares a duplicate program name",
            )
            by_name[program["name"]] = program

        for contract in self.contracts:
            name = contract["programName"]
            self.assertIn(
                name,
                by_name,
                f"metal library contract names {name}, which the checked "
                "manifest does not declare",
            )
            program = by_name[name]
            self.assertEqual(program["entryPoint"], contract["entryPoint"])
            self.assertEqual(program["stage"], contract["stage"])

    def test_contract_covers_every_expanded_graphics_program(self) -> None:
        # An entry point missing from the packed library is a shader that cannot
        # be loaded on Metal at all, so the contract must be exhaustive rather
        # than merely consistent. Compute programs are packed separately.
        expected = {
            program["name"]
            for program in self.checked["programs"]
            if program["stage"] in ("vertex", "fragment")
        }
        actual = {
            contract["programName"]
            for contract in self.contracts
            if contract["stage"] in ("vertex", "fragment")
        }
        self.assertEqual(expected, actual)

    def test_entry_points_are_unique(self) -> None:
        # Two permutations sharing an entry point would silently collapse into
        # one symbol in the packed library.
        entries = [contract["entryPoint"] for contract in self.contracts]
        self.assertEqual(len(entries), len(set(entries)))

    def test_expanded_programs_are_not_raw_manifest_programs(self) -> None:
        # Pins the distinction the macOS failure turned on: the checked manifest
        # holds expanded permutations while the authored manifest holds only the
        # base programs, so resolving a contract against the authored manifest
        # is wrong even though it happens to match for the base names.
        authored = {program["name"] for program in self.manifest["programs"]}
        expanded = {program["name"] for program in self.checked["programs"]}
        self.assertTrue(authored < expanded)


if __name__ == "__main__":
    unittest.main()
