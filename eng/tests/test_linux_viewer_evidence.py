#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.

import base64
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest


class LinuxViewerEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        runner = Path(__file__).resolve().parents[1] / "run-storm-native-child-linux.sh"
        script = runner.read_text(encoding="utf-8")
        start = 'if [[ -n "$viewer_evidence_path" ]]; then\npython3 -c \'\n'
        cls.validator = script.split(start, 1)[1].split('\n\' "$viewer_evidence_path"', 1)[0]
        cls.summary = script.rsplit("python3 -c '\n", 1)[1].split('\n\' "$platform"', 1)[0]

    def setUp(self):
        payload = struct.pack("<I", 1) + bytes(520)
        signature = "0123456789ABCDEF"
        self.evidence = {
            "schemaVersion": 8,
            "stormChildAbiVersion": 9,
            "states": [{
                "backend": "Storm", "phase": "explicit", "revision": 7,
                "cameraMode": "Matrices",
                "cameraPayload": base64.b64encode(payload).decode("ascii"),
                "cameraSignature": hashlib.sha256(payload).hexdigest().upper(),
                "nativeCameraSignature": signature,
            }],
            "cameraTransitions": [{
                "backend": "Storm", "explicitPhase": "explicit",
                "asyncCoalescingValidated": True, "latestRequestedRevision": 7,
                "latestRequestedCameraSignature": signature,
                "latestRenderedCameraSignature": signature,
            }],
            "pixels": [{
                "backend": "Storm", "phase": "explicit",
                "captureApi": "openusd_storm_child_capture_framebuffer(ABI9,preserved-texture)",
            }, {
                "backend": "Storm", "phase": "explicit-shell",
                "captureApi": "XGetImage(ZPixmap,viewer-shell)",
            }],
        }

    def validate(self, expected):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "evidence.json"
            path.write_text(json.dumps(self.evidence), encoding="utf-8")
            result = subprocess.run(
                [sys.executable, "-c", self.validator, str(path)],
                capture_output=True, text=True, timeout=10, check=False,
            )
        self.assertEqual(result.returncode, expected, result.stdout + result.stderr)
        if expected:
            self.assertIn("ABI 9 capture label", result.stderr)

    def test_accepts_native_capture_without_shell(self):
        self.evidence["pixels"].pop()
        self.validate(0)

    def test_accepts_independent_shell_paired_with_native_capture(self):
        self.validate(0)

    def test_rejects_non_abi9_primary_capture(self):
        self.evidence["pixels"][0]["captureApi"] = "unsupported"
        self.validate(1)

    def test_rejects_unpaired_shell_capture(self):
        self.evidence["pixels"].pop(0)
        self.validate(1)

    def test_rejects_shell_with_wrong_phase(self):
        self.evidence["pixels"][1]["phase"] = "unrelated-shell"
        self.validate(1)

    def test_rejects_shell_with_unsupported_api(self):
        self.evidence["pixels"][1]["captureApi"] = "unsupported"
        self.validate(1)

    def test_summary_reports_actual_presentation_path_and_readback_usage(self):
        for path, outcome, readback in [
            ("HostReadback", "passed", "presentation and diagnostics"),
            ("DirectVulkan", "passed", "diagnostics only"),
            (None, "vulkan-unavailable", "diagnostics only"),
        ]:
            with self.subTest(path=path), tempfile.TemporaryDirectory() as directory:
                capability = Path(directory) / "capability.json"
                capability.write_text(json.dumps({"presentationPath": path}), encoding="utf-8")
                result = subprocess.run(
                    [sys.executable, "-c", self.summary,
                     "xwayland", "100", outcome, "2", "", str(capability)],
                    capture_output=True, text=True, timeout=10, check=False,
                )
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                summary = json.loads(result.stdout)
                self.assertEqual(summary["vulkanPresentationPath"], path)
                self.assertEqual(summary["cpuReadback"], readback)
                self.assertEqual(summary["switches"], 100 if outcome == "passed" else 0)
                self.assertEqual(
                    summary["vulkanCapability"],
                    "Vulkan composition available" if outcome == "passed" else "typed unavailable",
                )


if __name__ == "__main__":
    unittest.main()
