# Copyright (c) marcschier. Licensed under the MIT License.

import importlib.util
import pathlib
import sys
import unittest


SCRIPT_ROOT = pathlib.Path(__file__).resolve().parents[1] / "scripts"
sys.path.insert(0, str(SCRIPT_ROOT))
SPEC = importlib.util.spec_from_file_location(
    "publish_checked",
    SCRIPT_ROOT / "publish-checked.py",
)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError("Could not load publish-checked.py")
publish_checked = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(publish_checked)


class FakePath:
    def __init__(self, files: dict[str, bytes], value: str = "") -> None:
        self.files = files
        self.value = value

    def __truediv__(self, value: str) -> "FakePath":
        path = f"{self.value}/{value}".strip("/")
        return FakePath(self.files, path)

    def is_file(self) -> bool:
        return self.value in self.files

    def read_bytes(self) -> bytes:
        return self.files[self.value]


class PublishCheckedTests(unittest.TestCase):
    def test_reads_lf_inputs_before_hashing(self) -> None:
        files = {
            "eng/shaders/source.slang": b"source\n",
            "command": b'{"schemaVersion": 1}\n',
        }
        inputs = publish_checked.read_checked_inputs(
            FakePath(files),
            (
                "eng/shaders/source.slang",
                "eng/shaders/checked/executed-commands.json",
            ),
            FakePath(files, "command"),
        )

        self.assertEqual(
            [
                ("eng/shaders/source.slang", b"source\n"),
                (
                    "eng/shaders/checked/executed-commands.json",
                    b'{"schemaVersion": 1}\n',
                ),
            ],
            inputs,
        )

    def test_rejects_crlf_input_before_publication(self) -> None:
        files = {"eng/shaders/source.slang": b"source\r\n"}
        with self.assertRaisesRegex(ValueError, "LF-only"):
            publish_checked.read_checked_inputs(
                FakePath(files),
                ("eng/shaders/source.slang",),
                FakePath(files, "command"),
            )


if __name__ == "__main__":
    unittest.main()
