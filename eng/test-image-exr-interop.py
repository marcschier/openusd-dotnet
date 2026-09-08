# Copyright (c) marcschier. Licensed under the MIT License.

import pathlib
import unittest

import image_exr_interop


class ExrInteropGeneratorTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        root = pathlib.Path(__file__).resolve().parents[1]
        cls.header = (root / "native" / "openusd_dotnet" / "include" /
                      "openusd_image_exr.h").read_text(encoding="utf-8")

    def test_generates_from_status_and_structure_declarations(self):
        generated = image_exr_interop.generate(self.header)
        self.assertIn("internal uint StructSize;", generated)
        self.assertIn("internal ulong PixelCeiling;", generated)
        self.assertIn("internal OpenUsdImageEncodeStatus Status;", generated)
        self.assertIn("OutOfMemory = 7", generated)
        self.assertIn("delegate* unmanaged[Cdecl]<nint, uint, uint, ulong, uint>", generated)

    def test_rejects_changed_callback(self):
        changed = self.header.replace("uint32_t phase", "uint64_t phase")
        with self.assertRaisesRegex(ValueError, "callback ABI changed"):
            image_exr_interop.generate(changed)

    def test_rejects_changed_bulk_signature(self):
        changed = self.header.replace("uint64_t rgba16f_bytes", "uint32_t rgba16f_bytes")
        with self.assertRaisesRegex(ValueError, "signature changed"):
            image_exr_interop.generate(changed)

    def test_rejects_changed_packet_extent(self):
        changed = self.header.replace("uint64_t reserved2;", "uint64_t reserved2;\n    uint64_t reserved3;")
        with self.assertRaisesRegex(ValueError, "112/40"):
            image_exr_interop.generate(changed)

    def test_rejects_changed_status_domain(self):
        changed = self.header.replace("OPENUSD_IMAGE_ENCODE_IO_ERROR 5u", "OPENUSD_IMAGE_ENCODE_IO_ERROR 9u")
        with self.assertRaisesRegex(ValueError, "ordered 0..7"):
            image_exr_interop.generate(changed)


if __name__ == "__main__":
    unittest.main()
