#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.

from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github" / "workflows" / "release-validation-checkpoint.yml"


class ReleaseValidationCheckpointTests(unittest.TestCase):
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
