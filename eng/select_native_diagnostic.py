#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.

import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]
# These paths do not change the native archive. The diagnostic rebuilds its
# managed test at the requested head rather than using a test from the archive.
NON_REPRODUCTION_PATHS = (
    ".github/workflows/nuget.yml",
    ".github/workflows/release-validation-checkpoint.yml",
    "docs/packaging.md",
    "docs/rendering.md",
    "eng/assert-volume-camera-evidence.ps1",
    "eng/diagnostics/metal-volume-math.metal",
    "eng/diagnostics/metal-volume-math.swift",
    "eng/select_native_diagnostic.py",
    "eng/test-silk-cubic-shadow-report.ps1",
    "eng/tests/test_release_validation_checkpoint.py",
    "eng/tests/test_select_native_diagnostic.py",
    "eng/tests/test_verify_nuget_promotion.py",
    "eng/verify-nuget-promotion.py",
    "tests/OpenUsd.Native.Tests/WorkflowStructureContractTests.cs",
    "tests/OpenUsd.Rendering.ConformanceTests/SilkPointShadowNativeConformanceTests.cs",
    "tests/OpenUsd.Rendering.ConformanceTests/VolumeCameraConformanceTests.cs",
    "tests/OpenUsd.Rendering.ConformanceTests/VolumeCameraJointPassConformanceTests.cs",
    "tests/OpenUsd.Rendering.Tests/SilkShadowNormalSpaceSourceContractTests.cs",
)
LINUX_JOB = "linux-x64 verified native archive"
LINUX_ARCHIVE = "openusd-native-linux-x64"
COMMIT = re.compile(r"[0-9a-f]{40}\Z")


def select_candidate(runs, compatible, jobs_for, artifacts_for, checkout_for):
    for run in runs:
        status = run["status"]
        if status not in ("queued", "in_progress") and not (
            status == "completed" and run["conclusion"] in ("success", "cancelled")
        ):
            continue
        run_id = run["databaseId"]
        source = run["headSha"]
        if type(run_id) is not int or run_id <= 0 or not COMMIT.fullmatch(source):
            raise ValueError("Invalid native producer identity.")
        if not compatible(source):
            continue
        jobs = [job for job in jobs_for(run_id) if job["name"] == LINUX_JOB]
        if len(jobs) != 1 or jobs[0]["status"] != "completed" or jobs[0]["conclusion"] != "success":
            continue
        archives = [
            artifact for artifact in artifacts_for(run_id)
            if artifact["name"] == LINUX_ARCHIVE
        ]
        if len(archives) == 1 and archives[0]["expired"] is False:
            job_id = jobs[0].get("databaseId")
            if type(job_id) is not int or job_id <= 0:
                raise ValueError("Invalid native producer job identity.")
            checkout = checkout_for(run_id, job_id, source)
            if checkout is not None:
                checkout_commit, checkout_tree = checkout
                return run_id, source, status, run["conclusion"], checkout_commit, checkout_tree
    return None


def git_status(arguments):
    result = subprocess.run(
        ["git", *arguments], cwd=ROOT, capture_output=True, text=True, check=False
    )
    if result.returncode not in (0, 1):
        raise RuntimeError(f"git {' '.join(arguments[:2])} failed: {result.stderr.strip()}")
    return result.returncode


def source_is_compatible(source, head):
    if git_status(["merge-base", "--is-ancestor", source, head]) != 0:
        return False
    exclusions = [f":(exclude){path}" for path in NON_REPRODUCTION_PATHS]
    return git_status(["diff", "--quiet", source, head, "--", ".", *exclusions]) == 0


def github_json(arguments):
    result = subprocess.run(
        ["gh", *arguments], cwd=ROOT, capture_output=True, text=True, check=False
    )
    if result.returncode:
        raise RuntimeError(f"GitHub CLI failed: {result.stderr.strip()}")
    return json.loads(result.stdout)


def checkout_from_log(log):
    checkout_lines = []
    in_checkout = False
    for line in log.splitlines():
        step = re.match(r"^\S+\s+##\[group\]Run (.*)$", line)
        if step is not None:
            in_checkout = step[1].startswith("actions/checkout@")
        elif in_checkout:
            checkout_lines.append(line)

    commits = []
    for index, line in enumerate(checkout_lines):
        if re.search(r"\[command\].*\bgit(?:\.exe)? log -1 --format=%H\s*$", line):
            if index + 1 >= len(checkout_lines):
                raise ValueError("The native checkout log is missing its commit.")
            match = re.search(r"(?:^|\s)([0-9a-f]{40})\s*$", checkout_lines[index + 1])
            if match is None:
                raise ValueError("The native checkout log has an invalid commit.")
            commits.append(match[1])
    if len(commits) != 1:
        raise ValueError("The native checkout log must contain exactly one checkout commit.")
    return commits[0]


def verified_checkout(repository, run_id, job_id, source):
    # A pull-request run's head SHA can differ from its checked-out merge tree.
    result = subprocess.run(
        ["gh", "api", "--allow-escape-sequences",
         f"repos/{repository}/actions/jobs/{job_id}/logs"],
        cwd=ROOT, capture_output=True, encoding="utf-8", check=False,
    )
    if result.returncode:
        raise RuntimeError(f"Native producer log retrieval failed: {result.stderr.strip()}")
    checkout = checkout_from_log(result.stdout)
    metadata = github_json(["api", f"repos/{repository}/git/commits/{checkout}"])
    tree = metadata.get("tree") if isinstance(metadata, dict) else None
    actual_tree = tree.get("sha") if isinstance(tree, dict) else None
    if (not isinstance(metadata, dict) or metadata.get("sha") != checkout
            or not isinstance(actual_tree, str)
            or not COMMIT.fullmatch(actual_tree)):
        raise ValueError("Invalid native checkout commit metadata.")
    result = subprocess.run(
        ["git", "rev-parse", "--verify", f"{source}^{{tree}}"],
        cwd=ROOT, capture_output=True, text=True, check=False,
    )
    expected_tree = result.stdout.strip()
    if result.returncode or not COMMIT.fullmatch(expected_tree):
        raise RuntimeError(f"Could not resolve the native source tree: {result.stderr.strip()}")
    if actual_tree != expected_tree:
        print(
            f"::notice::Skipping native run {run_id}: checkout {checkout} has a "
            f"different source tree than producer head {source}."
        )
        return None
    return checkout, actual_tree


def write_outputs(values):
    output = os.environ.get("GITHUB_OUTPUT")
    if output:
        with Path(output).open("a", encoding="utf-8") as stream:
            stream.writelines(f"{key}={value}\n" for key, value in values.items())
    else:
        for key, value in values.items():
            print(f"{key}={value}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository", required=True)
    parser.add_argument("--branch", required=True)
    parser.add_argument("--head", required=True)
    args = parser.parse_args()
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", args.repository):
        raise ValueError("Invalid GitHub repository.")
    if not args.branch or not COMMIT.fullmatch(args.head):
        raise ValueError("Invalid pull request branch or source commit.")
    checkout = subprocess.run(
        ["git", "rev-parse", "HEAD"], cwd=ROOT, capture_output=True, text=True, check=False
    )
    if checkout.returncode or checkout.stdout.strip() != args.head:
        raise RuntimeError("The diagnostic checkout does not match the pull request head.")

    runs = github_json([
        "run", "list", "--repo", args.repository, "--workflow", "native.yml",
        "--branch", args.branch, "--limit", "100",
        "--json", "databaseId,headSha,status,conclusion",
    ])

    def jobs_for(run_id):
        return github_json([
            "run", "view", str(run_id), "--repo", args.repository, "--json", "jobs",
        ])["jobs"]

    def artifacts_for(run_id):
        pages = github_json([
            "api", "--paginate", "--slurp",
            f"repos/{args.repository}/actions/runs/{run_id}/artifacts?per_page=100",
        ])
        return [artifact for page in pages for artifact in page["artifacts"]]

    selected = select_candidate(
        runs, lambda source: source_is_compatible(source, args.head), jobs_for, artifacts_for,
        lambda run_id, job_id, source: verified_checkout(
            args.repository, run_id, job_id, source),
    )
    if selected is None:
        write_outputs({"compatible": "false"})
        print("::notice::No source-compatible completed Linux native archive is available for diagnosis.")
        return
    run_id, source, status, conclusion, checkout, checkout_tree = selected
    write_outputs({
        "compatible": "true",
        "native_run_id": run_id,
        "native_source": source,
        "native_checkout": checkout,
        "native_tree": checkout_tree,
        "native_status": status,
        "native_conclusion": conclusion,
    })
    print(
        f"::notice::Using Linux native run {run_id} at {source} "
        f"(checkout {checkout}, tree {checkout_tree}; {status}, "
        f"{conclusion or 'no conclusion'}) for diagnosis only; "
        "this does not qualify the complete native pipeline."
    )


if __name__ == "__main__":
    try:
        main()
    except (KeyError, OSError, RuntimeError, TypeError, ValueError) as error:
        print(f"Native diagnostic selection failed: {error}", file=sys.stderr)
        sys.exit(1)
