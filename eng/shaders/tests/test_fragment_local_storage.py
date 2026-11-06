# Copyright (c) marcschier. Licensed under the MIT License.

import importlib.util
import pathlib
import struct
import sys
import tempfile
import unittest


SCRIPT_ROOT = pathlib.Path(__file__).resolve().parents[1] / "scripts"
sys.path.insert(0, str(SCRIPT_ROOT))
SPEC = importlib.util.spec_from_file_location("verify_artifacts", SCRIPT_ROOT / "verify-artifacts.py")
VERIFY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFY)


def instruction(opcode, *operands):
    return [((len(operands) + 1) << 16) | opcode, *operands]


def module(count, arrays=1, storage=7, functions=1):
    # A minimal instruction stream for this storage calculation, not a complete executable shader.
    words = [0x07230203, 0x00010500, 0, 100, 0]
    words += instruction(22, 1, 32)
    words += instruction(21, 2, 32, 0)
    words += instruction(43, 2, 3, count)
    words += instruction(23, 4, 1, 4)
    words += instruction(28, 5, 4, 3)
    words += instruction(32, 6, storage, 5)
    for function in range(functions):
        words += instruction(54, 1, 10 + function, 0, 99)
        for array in range(arrays):
            words += instruction(59, 6, 20 + function * arrays + array, storage)
        words += instruction(56)
    return struct.pack(f"<{len(words)}I", *words)


class FragmentLocalStorageTests(unittest.TestCase):
    def test_seven_light_tables_require_fourteen_kib_per_fragment(self):
        self.assertEqual(VERIFY.spirv_numeric_local_bytes(module(128, arrays=7)), 14336)

    def test_storage_buffer_arrays_are_not_function_local_copies(self):
        self.assertEqual(VERIFY.spirv_numeric_local_bytes(module(128, arrays=7, storage=12)), 0)

    def test_peak_function_storage_is_not_sum_of_unrelated_functions(self):
        self.assertEqual(VERIFY.spirv_numeric_local_bytes(module(64, functions=2)), 1024)

    def test_private_invocation_storage_is_counted(self):
        self.assertEqual(VERIFY.spirv_numeric_local_bytes(module(128, storage=6)), 2048)

    def test_exact_mesh_fragment_budget_passes_and_next_vector_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            path = root / "mesh.fragment.spv"
            program = {"name": "mesh.fragment", "stage": "fragment"}
            path.write_bytes(module(64))
            self.assertEqual(VERIFY.verify_program(root, program, 0x00010500, "spirv", False), [path])
            path.write_bytes(module(65))
            with self.assertRaisesRegex(ValueError, "1040 bytes, exceeding 1024"):
                VERIFY.verify_program(root, program, 0x00010500, "spirv", False)

    def test_truncated_and_zero_length_instructions_fail(self):
        for data in (b"", b"\x03\x02\x23", module(64)[:-4],
                     struct.pack("<6I", 0x07230203, 0x00010500, 0, 100, 0, 0)):
            with self.assertRaises(ValueError):
                VERIFY.spirv_numeric_local_bytes(data)


if __name__ == "__main__":
    unittest.main()
