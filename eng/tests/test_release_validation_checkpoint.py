#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.

from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github" / "workflows" / "release-validation-checkpoint.yml"


class ReleaseValidationCheckpointTests(unittest.TestCase):
    def test_native_session_probe_syntax_job_compiles_the_checked_out_source(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        triggers, jobs = workflow.split("\njobs:\n", 1)
        self.assertTrue(
            "  native-session-probe-syntax:\n" in jobs, "Native syntax job is missing."
        )
        job = jobs.split("  native-session-probe-syntax:\n", 1)[1]
        job = re.split(r"\n  [a-z0-9-]+:\n", job, maxsplit=1)[0]

        for path in (
            "native/hdSilk/tests/hdsilk_direct_lights_session_probe.cpp",
            "native/hdSilk/tests/hdsilk_direct_lights_test_decode.h",
            "native/hdSilk/include/openusd_hdsilk.h",
            "native/openusd_dotnet/include/openusd_dotnet.h",
            "native/include/openusd_render_camera.h",
        ):
            self.assertIn(f"      - '{path}'", triggers)
        self.assertIn("ref: ${{ github.event.pull_request.head.sha }}", job)
        self.assertIn(
            "g++ -std=c++17 -Wall -Wextra -Wpedantic -Werror -fsyntax-only",
            job,
        )
        for include in (
            "native/hdSilk/tests",
            "native/hdSilk/include",
            "native/openusd_dotnet/include",
            "native/include",
        ):
            self.assertIn(f"-I{include}", job)
        self.assertIn(
            "native/hdSilk/tests/hdsilk_direct_lights_session_probe.cpp", job
        )
        self.assertNotIn("|| true", job)

    def test_metal_reproducer_resolves_runner_temp_at_step_scope(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        job = workflow.split("\n  metal-volume-repro:", 1)[1]
        header, steps = job.split("\n    steps:", 1)
        run = steps.split("\n      - name: Run targeted Metal volume reproduction", 1)[1]
        run = run.split("\n      - name:", 1)[0]

        self.assertNotIn("${{ runner.", header)
        self.assertIn(
            "OPENUSD_SILK_SHADER_CACHE_DIR: ${{ runner.temp }}/openusd-volume-repro-shaders",
            run,
        )
        self.assertIn(
            "OPENUSD_TEST_WORK_ROOT: ${{ runner.temp }}/openusd-volume-repro-tests",
            run,
        )
        self.assertIn("eng/run-managed-tests.ps1", run)


if __name__ == "__main__":
    unittest.main()
