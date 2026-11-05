#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.

import subprocess
import unittest
from unittest.mock import Mock, call, patch

from eng import select_native_diagnostic as selector


HEAD = "a" * 40
OLDER_HEAD = "b" * 40
LINUX_JOB = {
    "name": "linux-x64 verified native archive",
    "status": "completed",
    "conclusion": "success",
}
ARCHIVE = {"name": "openusd-native-linux-x64", "expired": False}


def run(run_id=10, head=HEAD, status="in_progress", conclusion=""):
    return {
        "databaseId": run_id,
        "headSha": head,
        "status": status,
        "conclusion": conclusion,
    }


class NativeDiagnosticSelectionTests(unittest.TestCase):
    def test_exclusions_are_limited_to_diagnostics_and_promotion_inputs(self):
        self.assertEqual(set(selector.NON_REPRODUCTION_PATHS), {
            ".github/workflows/nuget.yml",
            ".github/workflows/release-validation-checkpoint.yml",
            "docs/packaging.md",
            "docs/rendering.md",
            "eng/select_native_diagnostic.py",
            "eng/tests/test_release_validation_checkpoint.py",
            "eng/tests/test_select_native_diagnostic.py",
            "eng/tests/test_verify_nuget_promotion.py",
            "eng/verify-nuget-promotion.py",
            "tests/OpenUsd.Rendering.ConformanceTests/SilkPointShadowNativeConformanceTests.cs",
            "tests/OpenUsd.Rendering.Tests/SilkShadowNormalSpaceSourceContractTests.cs",
        })

    def test_completed_linux_job_is_usable_while_other_platforms_are_running(self):
        compatible = Mock(return_value=True)
        jobs = Mock(return_value=[LINUX_JOB])
        artifacts = Mock(return_value=[ARCHIVE])

        selected = selector.select_candidate([run()], compatible, jobs, artifacts)

        self.assertEqual(selected, (10, HEAD, "in_progress", ""))
        compatible.assert_called_once_with(HEAD)
        jobs.assert_called_once_with(10)
        artifacts.assert_called_once_with(10)

    def test_incompatible_source_is_not_inspected_or_used(self):
        compatible = Mock(side_effect=[False, True])
        jobs = Mock(return_value=[LINUX_JOB])
        artifacts = Mock(return_value=[ARCHIVE])

        selected = selector.select_candidate(
            [run(), run(11, OLDER_HEAD, "completed", "success")],
            compatible,
            jobs,
            artifacts,
        )

        self.assertEqual(selected, (11, OLDER_HEAD, "completed", "success"))
        self.assertEqual(compatible.call_args_list, [call(HEAD), call(OLDER_HEAD)])
        jobs.assert_called_once_with(11)
        artifacts.assert_called_once_with(11)

    def test_failed_or_unstarted_producer_is_not_selected(self):
        compatible = Mock(return_value=True)
        jobs = Mock(return_value=[LINUX_JOB])
        artifacts = Mock(return_value=[ARCHIVE])

        selected = selector.select_candidate(
            [
                run(status="completed", conclusion="failure"),
                run(12, status="queued"),
            ],
            compatible,
            jobs,
            artifacts,
        )

        self.assertIsNone(selected)
        compatible.assert_not_called()
        jobs.assert_not_called()
        artifacts.assert_not_called()

    def test_completed_linux_job_from_cancelled_multi_platform_run_is_diagnostic_only(self):
        selected = selector.select_candidate(
            [run(status="completed", conclusion="cancelled")],
            lambda _: True,
            lambda _: [LINUX_JOB],
            lambda _: [ARCHIVE],
        )

        self.assertEqual(selected, (10, HEAD, "completed", "cancelled"))

    def test_requires_exactly_one_successful_linux_job(self):
        for job_list in (
            [],
            [{**LINUX_JOB, "status": "in_progress", "conclusion": ""}],
            [{**LINUX_JOB, "conclusion": "failure"}],
            [LINUX_JOB, LINUX_JOB],
        ):
            with self.subTest(job_list=job_list):
                artifacts = Mock(return_value=[ARCHIVE])
                self.assertIsNone(
                    selector.select_candidate(
                        [run()], lambda _: True, lambda _: job_list, artifacts
                    )
                )
                artifacts.assert_not_called()

    def test_requires_one_unexpired_linux_archive(self):
        for artifact_list in (
            [],
            [{**ARCHIVE, "expired": True}],
            [ARCHIVE, ARCHIVE],
            [ARCHIVE, {**ARCHIVE, "expired": True}],
            [{"name": "openusd-native-win-x64", "expired": False}],
        ):
            with self.subTest(artifact_list=artifact_list):
                self.assertIsNone(
                    selector.select_candidate(
                        [run()], lambda _: True, lambda _: [LINUX_JOB],
                        lambda _: artifact_list,
                    )
                )

    def test_invalid_run_identity_is_an_error(self):
        for invalid in (
            {**run(), "databaseId": 0},
            {**run(), "databaseId": True},
            {**run(), "headSha": "not-a-commit"},
        ):
            with self.subTest(invalid=invalid):
                with self.assertRaisesRegex(ValueError, "producer identity"):
                    selector.select_candidate(
                        [invalid], lambda _: True, lambda _: [LINUX_JOB],
                        lambda _: [ARCHIVE],
                    )

    @patch.object(selector.subprocess, "run")
    def test_source_comparison_checks_ancestry_and_only_excludes_diagnostic_files(self, execute):
        execute.side_effect = [
            subprocess.CompletedProcess([], 0),
            subprocess.CompletedProcess([], 0),
        ]

        self.assertTrue(selector.source_is_compatible(OLDER_HEAD, HEAD))

        self.assertEqual(execute.call_count, 2)
        self.assertEqual(execute.call_args_list[0].args[0][1:],
                         ["merge-base", "--is-ancestor", OLDER_HEAD, HEAD])
        diff = execute.call_args_list[1].args[0]
        self.assertEqual(diff[:7],
                         ["git", "diff", "--quiet", OLDER_HEAD, HEAD, "--", "."])
        self.assertEqual(
            diff[7:],
            [f":(exclude){path}" for path in selector.NON_REPRODUCTION_PATHS],
        )

    @patch.object(selector.subprocess, "run")
    def test_source_mismatch_refuses_candidate_and_git_errors_surface(self, execute):
        execute.return_value = subprocess.CompletedProcess([], 1)
        self.assertFalse(selector.source_is_compatible(OLDER_HEAD, HEAD))
        execute.assert_called_once()

        execute.reset_mock()
        execute.side_effect = [
            subprocess.CompletedProcess([], 0),
            subprocess.CompletedProcess([], 1),
        ]
        self.assertFalse(selector.source_is_compatible(OLDER_HEAD, HEAD))

        execute.reset_mock()
        execute.side_effect = [subprocess.CompletedProcess([], 2, stderr="git error")]
        with self.assertRaisesRegex(RuntimeError, "git error"):
            selector.source_is_compatible(OLDER_HEAD, HEAD)


if __name__ == "__main__":
    unittest.main()
