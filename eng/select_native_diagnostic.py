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
NON_REPRODUCTION_PATHS = (
    ".github/workflows/release-validation-checkpoint.yml",
    "docs/rendering.md",
    "eng/select_native_diagnostic.py",
    "eng/tests/test_release_validation_checkpoint.py",
    "eng/tests/test_select_native_diagnostic.py",
    "tests/OpenUsd.Rendering.Tests/SilkShadowNormalSpaceSourceContractTests.cs",
)
LINUX_JOB = "linux-x64 verified native archive"
LINUX_ARCHIVE = "openusd-native-linux-x64"
COMMIT = re.compile(r"[0-9a-f]{40}\Z")


def select_candidate(runs, compatible, jobs_for, artifacts_for):
    for run in runs:
        status = run["status"]
        if status != "in_progress" and not (
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
            return run_id, source, status, run["conclusion"]
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
        runs, lambda source: source_is_compatible(source, args.head), jobs_for, artifacts_for
    )
    if selected is None:
        write_outputs({"compatible": "false"})
        print("::notice::No source-compatible completed Linux native archive is available for diagnosis.")
        return
    run_id, source, status, conclusion = selected
    write_outputs({
        "compatible": "true",
        "native_run_id": run_id,
        "native_source": source,
        "native_status": status,
        "native_conclusion": conclusion,
    })
    print(
        f"::notice::Using Linux native run {run_id} at {source} "
        f"({status}, {conclusion or 'no conclusion'}) for diagnosis only; "
        "this does not qualify the complete native pipeline."
    )


if __name__ == "__main__":
    try:
        main()
    except (KeyError, OSError, RuntimeError, TypeError, ValueError) as error:
        print(f"Native diagnostic selection failed: {error}", file=sys.stderr)
        sys.exit(1)
