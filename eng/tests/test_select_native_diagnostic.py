#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.

import subprocess
import unittest
from unittest.mock import Mock, call, patch

from eng import select_native_diagnostic as selector


HEAD = "a" * 40
OLDER_HEAD = "b" * 40
CHECKOUT = "c" * 40
TREE = "d" * 40
JOB_ID = 501
LINUX_JOB = {
    "name": "linux-x64 verified native archive",
    "databaseId": JOB_ID,
    "status": "completed",
    "conclusion": "success",
}
ARCHIVE = {"name": "openusd-native-linux-x64", "expired": False}
CHECKOUT_LOG = (
    "2026-01-01T00:00:00Z ##[group]Run actions/checkout@pinned\n"
    "2026-01-01T00:00:01Z ##[endgroup]\n"
    "2026-01-01T00:00:02Z ##[group]Checking out the ref\n"
    "2026-01-01T00:00:03Z ##[endgroup]\n"
    "2026-01-01T00:00:04Z [command]/usr/bin/git log -1 --format=%H\n"
    f"2026-01-01T00:00:05Z {CHECKOUT}\n"
)


def matching_checkout(run_id, job_id, source):
    return CHECKOUT, TREE


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
            "tests/OpenUsd.Rendering.ConformanceTests/VolumeCameraJointPassConformanceTests.cs",
            "tests/OpenUsd.Rendering.Tests/SilkShadowNormalSpaceSourceContractTests.cs",
        })

    def test_completed_linux_job_is_usable_while_other_platforms_are_running(self):
        compatible = Mock(return_value=True)
        jobs = Mock(return_value=[LINUX_JOB])
        artifacts = Mock(return_value=[ARCHIVE])
        checkout = Mock(return_value=(CHECKOUT, TREE))

        selected = selector.select_candidate([run()], compatible, jobs, artifacts, checkout)

        self.assertEqual(selected, (10, HEAD, "in_progress", "", CHECKOUT, TREE))
        compatible.assert_called_once_with(HEAD)
        jobs.assert_called_once_with(10)
        artifacts.assert_called_once_with(10)
        checkout.assert_called_once_with(10, JOB_ID, HEAD)

    def test_completed_linux_job_is_usable_while_the_workflow_is_queued(self):
        selected = selector.select_candidate(
            [run(status="queued")],
            lambda _: True,
            lambda _: [LINUX_JOB],
            lambda _: [ARCHIVE],
            matching_checkout,
        )

        self.assertEqual(selected, (10, HEAD, "queued", "", CHECKOUT, TREE))

    def test_incompatible_source_is_not_inspected_or_used(self):
        compatible = Mock(side_effect=[False, True])
        jobs = Mock(return_value=[LINUX_JOB])
        artifacts = Mock(return_value=[ARCHIVE])

        selected = selector.select_candidate(
            [run(), run(11, OLDER_HEAD, "completed", "success")],
            compatible,
            jobs,
            artifacts,
            matching_checkout,
        )

        self.assertEqual(selected, (11, OLDER_HEAD, "completed", "success", CHECKOUT, TREE))
        self.assertEqual(compatible.call_args_list, [call(HEAD), call(OLDER_HEAD)])
        jobs.assert_called_once_with(11)
        artifacts.assert_called_once_with(11)

    def test_failed_producer_is_not_selected(self):
        compatible = Mock(return_value=True)
        jobs = Mock(return_value=[LINUX_JOB])
        artifacts = Mock(return_value=[ARCHIVE])

        selected = selector.select_candidate(
            [run(status="completed", conclusion="failure")],
            compatible,
            jobs,
            artifacts,
            matching_checkout,
        )

        self.assertIsNone(selected)
        compatible.assert_not_called()
        jobs.assert_not_called()
        artifacts.assert_not_called()

    def test_queued_producer_without_a_completed_linux_job_is_not_selected(self):
        artifacts = Mock(return_value=[ARCHIVE])

        selected = selector.select_candidate(
            [run(status="queued")],
            lambda _: True,
            lambda _: [{**LINUX_JOB, "status": "in_progress", "conclusion": ""}],
            artifacts,
            matching_checkout,
        )

        self.assertIsNone(selected)
        artifacts.assert_not_called()

    def test_completed_linux_job_from_cancelled_multi_platform_run_is_diagnostic_only(self):
        selected = selector.select_candidate(
            [run(status="completed", conclusion="cancelled")],
            lambda _: True,
            lambda _: [LINUX_JOB],
            lambda _: [ARCHIVE],
            matching_checkout,
        )

        self.assertEqual(selected, (10, HEAD, "completed", "cancelled", CHECKOUT, TREE))

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
                        [run()], lambda _: True, lambda _: job_list, artifacts,
                        matching_checkout,
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
                        matching_checkout,
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
                        matching_checkout,
                    )

    def test_candidate_with_different_checkout_tree_is_skipped(self):
        checkout = Mock(side_effect=[None, (CHECKOUT, TREE)])

        selected = selector.select_candidate(
            [run(), run(11, OLDER_HEAD, "completed", "success")],
            lambda _: True, lambda _: [LINUX_JOB], lambda _: [ARCHIVE], checkout,
        )

        self.assertEqual(selected, (11, OLDER_HEAD, "completed", "success", CHECKOUT, TREE))
        self.assertEqual(checkout.call_args_list, [
            call(10, JOB_ID, HEAD),
            call(11, JOB_ID, OLDER_HEAD),
        ])

    def test_candidate_is_not_selected_when_no_checkout_tree_matches(self):
        selected = selector.select_candidate(
            [run()], lambda _: True, lambda _: [LINUX_JOB], lambda _: [ARCHIVE],
            lambda *_: None,
        )

        self.assertIsNone(selected)

    def test_invalid_linux_job_identity_is_an_error(self):
        for invalid_id in (None, 0, True, "501"):
            with self.subTest(job_id=invalid_id):
                job = {**LINUX_JOB, "databaseId": invalid_id}
                with self.assertRaisesRegex(ValueError, "producer job identity"):
                    selector.select_candidate(
                        [run()], lambda _: True, lambda _: [job], lambda _: [ARCHIVE],
                        matching_checkout,
                    )

    def test_checkout_log_extracts_only_the_checkout_step_commit(self):
        before = (
            "2026-01-01T00:00:00Z ##[group]Run unrelated\n"
            "2026-01-01T00:00:01Z [command]/usr/bin/git log -1 --format=%H\n"
            f"2026-01-01T00:00:02Z {HEAD}\n"
        )
        after = before.replace("Run unrelated", "Run build")

        self.assertEqual(selector.checkout_from_log(before + CHECKOUT_LOG + after), CHECKOUT)

    def test_checkout_log_rejects_missing_invalid_or_ambiguous_commits(self):
        for log in (
            "",
            "\n".join(CHECKOUT_LOG.splitlines()[:-1]),
            CHECKOUT_LOG.replace(CHECKOUT, "not-a-commit"),
            CHECKOUT_LOG + CHECKOUT_LOG,
            CHECKOUT_LOG.replace("Run actions/checkout@pinned", "Run build"),
            CHECKOUT_LOG.replace(
                f"2026-01-01T00:00:05Z {CHECKOUT}",
                "2026-01-01T00:00:05Z ##[group]Run another step\n"
                f"2026-01-01T00:00:06Z {CHECKOUT}",
            ),
        ):
            with self.subTest(log=log):
                with self.assertRaisesRegex(ValueError, "native checkout log"):
                    selector.checkout_from_log(log)

    @patch.object(selector, "github_json")
    @patch.object(selector.subprocess, "run")
    def test_verified_checkout_compares_actual_checkout_and_producer_head_trees(
        self, execute, github,
    ):
        execute.side_effect = [
            subprocess.CompletedProcess([], 0, stdout=CHECKOUT_LOG),
            subprocess.CompletedProcess([], 0, stdout=f"{TREE}\n"),
        ]
        github.return_value = {"sha": CHECKOUT, "tree": {"sha": TREE}}

        self.assertEqual(
            selector.verified_checkout("owner/repo", 10, JOB_ID, HEAD),
            (CHECKOUT, TREE),
        )
        self.assertEqual(execute.call_args_list[0].args[0], [
            "gh", "api", "--allow-escape-sequences",
            f"repos/owner/repo/actions/jobs/{JOB_ID}/logs",
        ])
        github.assert_called_once_with([
            "api", f"repos/owner/repo/git/commits/{CHECKOUT}",
        ])
        self.assertEqual(execute.call_args_list[1].args[0], [
            "git", "rev-parse", "--verify", f"{HEAD}^{{tree}}",
        ])

    @patch.object(selector, "github_json")
    @patch.object(selector.subprocess, "run")
    def test_verified_checkout_refuses_tree_mismatch(self, execute, github):
        execute.side_effect = [
            subprocess.CompletedProcess([], 0, stdout=CHECKOUT_LOG),
            subprocess.CompletedProcess([], 0, stdout=f'{HEAD}\n'),
        ]
        github.return_value = {"sha": CHECKOUT, "tree": {"sha": TREE}}

        with patch("builtins.print") as notice:
            self.assertIsNone(selector.verified_checkout("owner/repo", 10, JOB_ID, HEAD))

        self.assertIn("different source tree", notice.call_args.args[0])

    @patch.object(selector, "github_json")
    @patch.object(selector.subprocess, "run")
    def test_verified_checkout_surfaces_invalid_metadata_and_log_errors(self, execute, github):
        for metadata in (None, [], {}, {"sha": HEAD, "tree": {"sha": TREE}},
                         {"sha": CHECKOUT, "tree": {"sha": "invalid"}}):
            with self.subTest(metadata=metadata):
                execute.reset_mock()
                execute.return_value = subprocess.CompletedProcess([], 0, stdout=CHECKOUT_LOG)
                github.return_value = metadata
                with self.assertRaisesRegex(ValueError, "checkout commit metadata"):
                    selector.verified_checkout("owner/repo", 10, JOB_ID, HEAD)
                self.assertEqual(execute.call_count, 1)

        execute.reset_mock()
        execute.return_value = subprocess.CompletedProcess([], 1, stderr="log unavailable")
        with self.assertRaisesRegex(RuntimeError, "log unavailable"):
            selector.verified_checkout("owner/repo", 10, JOB_ID, HEAD)

    @patch.object(selector, "github_json")
    @patch.object(selector.subprocess, "run")
    def test_verified_checkout_surfaces_source_tree_resolution_errors(self, execute, github):
        github.return_value = {"sha": CHECKOUT, "tree": {"sha": TREE}}
        for result in (
            subprocess.CompletedProcess([], 1, stdout="", stderr="missing source"),
            subprocess.CompletedProcess([], 0, stdout="invalid", stderr=""),
        ):
            with self.subTest(result=result):
                execute.side_effect = [
                    subprocess.CompletedProcess([], 0, stdout=CHECKOUT_LOG),
                    result,
                ]
                with self.assertRaisesRegex(RuntimeError, "resolve the native source tree"):
                    selector.verified_checkout("owner/repo", 10, JOB_ID, HEAD)

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
