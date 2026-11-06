# Copyright (c) marcschier. Licensed under the MIT License.

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import uuid


CLASS = "StormSilkParityCaptureDriverTests"
SCHEME_CASES = (
    ("none", True, False),
    ("none", True, True),
    ("none", False, True),
    ("bilinear", True, False),
    ("bilinear", True, True),
    ("catmullClark", True, False),
    ("catmullClark", True, True),
    ("catmullClark", False, False),
    ("catmullClark", False, True),
    ("loop", True, False),
    ("loop", True, True),
    ("loop", False, True),
)
METHODS = {
    "StormCoarseNormalsFollowSchemeAndPointIdentity",
    "StormCoarseNgonNormalsUseFaceAdjacency",
    "StormNonplanarNgonSmoothNormalsUseEachOriginalFaceCorner",
    "StormCoarseSmoothNormalsIncludeHoleFaces",
    "StormNonplanarPolygonFlatNormalsUseEachEmittedTriangle",
}


def expected_rows() -> set[str]:
    result = {
        f"StormCoarseNormalsFollowSchemeAndPointIdentity({scheme}, {shared}, {seamed})"
        for scheme, shared, seamed in SCHEME_CASES
    }
    for method in (
        "StormCoarseNgonNormalsUseFaceAdjacency",
        "StormNonplanarNgonSmoothNormalsUseEachOriginalFaceCorner",
    ):
        result.update(f"{method}({seamed})" for seamed in (False, True))
    for method, schemes in (
        ("StormCoarseSmoothNormalsIncludeHoleFaces", ("catmullClark", "loop")),
        ("StormNonplanarPolygonFlatNormalsUseEachEmittedTriangle", ("none", "bilinear")),
    ):
        result.update(
            f"{method}({scheme}, {seamed})"
            for scheme in schemes
            for seamed in (False, True)
        )
    return result


def utc(value: str) -> datetime:
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.tzinfo is None or parsed.utcoffset() is None:
        raise ValueError(f"Timestamp has no time zone: {value}")
    return parsed.astimezone(timezone.utc)


def verify(report: dict, started: datetime, finished: datetime) -> None:
    if started.tzinfo is None or finished.tzinfo is None or finished < started:
        raise ValueError("The capture interval must be time-zone-aware and ordered.")
    generation = report.get("publicationGeneration")
    try:
        valid_generation = (
            isinstance(generation, str)
            and len(generation) == 32
            and uuid.UUID(hex=generation).int != 0
        )
    except ValueError:
        valid_generation = False
    groups = report.get("groups")
    if (
        report.get("schemaVersion") != 1
        or report.get("assemblyName") != "OpenUsd.Rendering.ConformanceTests"
        or not valid_generation
        or not isinstance(groups, list)
    ):
        raise ValueError("The report does not identify a fresh conformance test run.")
    matching = [
        group for group in groups
        if group.get("namespace") == "OpenUsd.Rendering.ConformanceTests"
        and group.get("className") == CLASS
    ]
    if len(matching) != 1 or not isinstance(matching[0].get("tests"), list):
        raise ValueError("The report must contain one Storm parity test group.")
    remaining = expected_rows()
    ids = set()
    for test in matching[0]["tests"]:
        if test.get("methodName") not in METHODS:
            continue
        identifier = test.get("id")
        row = test.get("displayName")
        if (
            test.get("className") != CLASS
            or test.get("status") != "passed"
            or not isinstance(identifier, str)
            or not identifier
            or identifier in ids
            or row not in remaining
            or not row.startswith(test["methodName"] + "(")
        ):
            raise ValueError(f"Unexpected, duplicate, or non-passing normal-reference row: {row}")
        case_start = utc(test["startTime"])
        case_end = utc(test["endTime"])
        if case_start < started or case_end > finished or case_end < case_start:
            raise ValueError(f"Normal-reference row lies outside this capture: {row}")
        ids.add(identifier)
        remaining.remove(row)
    if remaining:
        raise ValueError(f"Normal-reference rows are missing: {sorted(remaining)}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--started-utc", required=True)
    parser.add_argument("--finished-utc", required=True)
    args = parser.parse_args()
    started = utc(args.started_utc)
    finished = utc(args.finished_utc)
    if args.report.stat().st_mtime < started.timestamp():
        raise ValueError("The normal-reference report predates this capture.")
    verify(json.loads(args.report.read_text(encoding="utf-8-sig")), started, finished)
    print("Storm normal references verified: 24 exact Windows rows passed without skips.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
