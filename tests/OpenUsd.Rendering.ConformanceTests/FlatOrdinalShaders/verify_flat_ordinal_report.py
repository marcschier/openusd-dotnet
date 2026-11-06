# Copyright (c) marcschier. Licensed under the MIT License.

import argparse
import datetime
import json
import pathlib
import re


CLASS = "SilkFlatOrdinalTransportConformanceTests"
METHOD = "SingleIndexedDrawCarriesFlatCompactOrdinalsWithoutProvingPtex"
ROW = re.compile(
    rf"{METHOD}\((D3D12|Vulkan|Metal),\s*(255|32767|65534),\s*(False|True)\)"
)
BACKENDS = {
    "windows": ("D3D12", "Vulkan"),
    "linux": ("Vulkan",),
    "macos": ("Metal",),
}


def require_summary(summary: dict[str, object], count: int) -> None:
    expected = {
        "total": count,
        "passed": count,
        "failed": 0,
        "skipped": 0,
        "cancelled": 0,
        "timedOut": 0,
    }
    if any(summary.get(name) != value for name, value in expected.items()):
        raise ValueError(f"Expected exactly {count} passes and no skipped or incomplete cases: {summary}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", required=True, type=pathlib.Path)
    parser.add_argument("--assembly", required=True, type=pathlib.Path)
    parser.add_argument("--platform", required=True, choices=tuple(BACKENDS))
    parser.add_argument("--started-utc", required=True)
    arguments = parser.parse_args()
    started = datetime.datetime.fromisoformat(arguments.started_utc.replace("Z", "+00:00"))
    if started.tzinfo is None or started.utcoffset() != datetime.timedelta(0):
        raise ValueError("The test start time must include a UTC offset.")
    project = pathlib.Path(__file__).resolve().parent.parent
    assembly_time = arguments.assembly.stat().st_mtime
    if assembly_time > started.timestamp() or any(
        (project / source).stat().st_mtime > assembly_time
        for source in ("SilkFlatOrdinalTransportConformanceTests.cs", "FlatOrdinalProbeShader.cs")
    ):
        raise ValueError("The selected assembly is stale or changed after the test started.")
    if arguments.report.stat().st_mtime < started.timestamp():
        raise ValueError("The flat-ordinal report predates the selected test execution.")

    report = json.loads(arguments.report.read_text(encoding="utf-8"))
    groups = report.get("groups")
    if (
        report.get("schemaVersion") != 1
        or report.get("assemblyName") != "OpenUsd.Rendering.ConformanceTests"
        or not report.get("publicationGeneration")
        or not isinstance(groups, list)
        or len(groups) != 1
        or groups[0].get("namespace") != "OpenUsd.Rendering.ConformanceTests"
        or groups[0].get("className") != CLASS
    ):
        raise ValueError("The report does not identify the selected flat-ordinal test class.")

    expected = {
        (backend, ordinal, varying)
        for backend in BACKENDS[arguments.platform]
        for ordinal in (255, 32767, 65534)
        for varying in ("False", "True")
    }
    count = len(expected)
    require_summary(report["summary"], count)
    require_summary(groups[0]["summary"], count)
    tests = groups[0].get("tests")
    if not isinstance(tests, list) or len(tests) != count:
        raise ValueError(f"The report must contain exactly {count} flat-ordinal cases.")

    ids = set()
    for test in tests:
        identifier = test.get("id")
        match = ROW.fullmatch(test.get("displayName", ""))
        if (
            test.get("className") != CLASS
            or test.get("methodName") != METHOD
            or test.get("status") != "passed"
            or not isinstance(identifier, str)
            or not identifier
            or identifier in ids
            or match is None
        ):
            raise ValueError(f"Unexpected, duplicated, or non-passing test case: {test}")
        ids.add(identifier)
        row = (match.group(1), int(match.group(2)), match.group(3))
        if row not in expected:
            raise ValueError(f"Unexpected or duplicated flat-ordinal row: {row}")
        expected.remove(row)
    if expected:
        raise ValueError(f"The report is missing flat-ordinal rows: {sorted(expected)}")
    print(f"Flat-ordinal report verified: {arguments.platform}, {count} passed, no skips.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
