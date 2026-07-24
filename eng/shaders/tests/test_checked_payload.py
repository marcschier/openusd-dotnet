# Copyright (c) marcschier. Licensed under the MIT License.

import pathlib
import struct
import sys
import unittest


SCRIPT_ROOT = pathlib.Path(__file__).resolve().parents[1] / "scripts"
sys.path.insert(0, str(SCRIPT_ROOT))

import checked_payload  # noqa: E402
from shader_model import required_checked_inputs  # noqa: E402


class CheckedPayloadTests(unittest.TestCase):
    def test_spirv_header_uses_locked_version(self) -> None:
        content = struct.pack("<IIIII", 0x07230203, 0x00010500, 0, 1, 0)
        checked_payload.validate_spirv_bytes(content, "1.5")
        with self.assertRaisesRegex(ValueError, "locked version"):
            checked_payload.validate_spirv_bytes(content, "1.6")

    def test_msl_requires_exact_entry_point_stage(self) -> None:
        source = "[[vertex]] float4 vertexMain() { return 0; }"
        checked_payload.validate_msl_text(source, "vertexMain", "vertex")
        with self.assertRaisesRegex(ValueError, "missing fragment"):
            checked_payload.validate_msl_text(
                source,
                "vertexMain",
                "fragment",
            )

    def test_msl_rejects_prior_attribute_and_helper_decoys(self) -> None:
        source = """
[[vertex]] float4 helper() { return 0; }
float4 vertexMain() { return 0; }
[[vertex]] float4 vertexMainSuffix() { return 0; }
"""
        with self.assertRaisesRegex(ValueError, "missing vertex"):
            checked_payload.validate_msl_text(
                source,
                "vertexMain",
                "vertex",
            )

    def test_metallib_symbol_match_is_exact(self) -> None:
        checked_payload.validate_exact_exported_symbol(
            "00000000 g F __TEXT,__text 00000008 vertexMain\n",
            "vertexMain",
        )
        for decoy in (
            "00000000 g F helper_vertexMain\n",
            "00000010 g F vertexMainSuffix\n",
            "00000020 g F helper vertexMain helperSuffix\n",
            "note vertexMain\n",
            "00000030 l F __TEXT,__text 00000008 vertexMain\n",
            "00000040 g O __DATA,__data 00000008 vertexMain\n",
        ):
            with self.subTest(decoy=decoy):
                with self.assertRaisesRegex(ValueError, "exact exported entry"):
                    checked_payload.validate_exact_exported_symbol(
                        decoy,
                        "vertexMain",
                    )

    def test_metallib_requires_every_checked_export(self) -> None:
        symbols = "\n".join(
            [
                "00000000 g F __TEXT,__text 00000008 vertexMain",
                "00000010 g F __TEXT,__text 00000008 fragmentMain",
                "00000020 g F __TEXT,__text 00000008 pickVertexMain",
                "00000030 g F __TEXT,__text 00000008 pickFragmentMain",
                "00000040 g F __TEXT,__text 00000008 fillMain",
                "00000050 g F __TEXT,__text 00000008 scaleMain",
            ]
        )
        required = [
            "vertexMain",
            "fragmentMain",
            "pickVertexMain",
            "pickFragmentMain",
            "fillMain",
            "scaleMain",
        ]
        checked_payload.validate_exact_exported_symbols(symbols, required)
        for missing in required:
            corrupted = "\n".join(
                line
                for line in symbols.splitlines()
                if not line.endswith(missing)
            )
            with self.subTest(missing=missing):
                with self.assertRaisesRegex(ValueError, missing):
                    checked_payload.validate_exact_exported_symbols(
                        corrupted,
                        required,
                    )

    def test_provenance_requires_exact_unique_input_set(self) -> None:
        manifest = {
            "programs": [
                {
                    "source": "eng/shaders/sources/mesh.slang",
                }
            ]
        }
        required = required_checked_inputs(manifest)
        records = [{"path": path, "sha256": "00"} for path in required]
        checked_payload.validate_provenance_records(records, required)

        with self.assertRaisesRegex(ValueError, "duplicate paths"):
            checked_payload.validate_provenance_records(
                [*records, records[0]],
                required,
            )
        with self.assertRaisesRegex(ValueError, "required set"):
            checked_payload.validate_provenance_records(
                records[1:],
                required,
            )
        with self.assertRaisesRegex(ValueError, "required set"):
            checked_payload.validate_provenance_records(
                [*records, {"path": "eng/shaders/unexpected.ps1"}],
                required,
            )


if __name__ == "__main__":
    unittest.main()
