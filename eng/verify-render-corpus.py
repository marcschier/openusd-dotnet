# Copyright (c) marcschier. Licensed under the MIT License.
"""Validate the executed parity corpus and its independent perturbation report."""

from __future__ import annotations

import math
import argparse
import json
import os
import pathlib
import re
import sys
import tempfile


def _object(value: object, context: str) -> dict:
    if not isinstance(value, dict):
        raise ValueError(f"{context} requires an object")
    return value


def _number(value: object, low: float, high: float, context: str) -> float:
    if type(value) not in (int, float) or not low <= value <= high or not math.isfinite(value):
        raise ValueError(f"{context} requires a finite value in [{low}, {high}]")
    return value


def _hash(value: object, context: str) -> str:
    if not isinstance(value, str) or not re.fullmatch(r"[0-9A-Fa-f]{64}", value):
        raise ValueError(f"{context} requires a SHA-256 identity")
    return value


def _rows(value: object, key: str, context: str) -> dict[str, dict]:
    if not isinstance(value, list) or not value:
        raise ValueError(f"{context} requires a nonempty case/scene list")
    result = {}
    for row in value:
        if not isinstance(row, dict) or not isinstance(row.get(key), str) or not row[key]:
            raise ValueError(f"{context} contains an unnamed case/scene")
        if row[key] in result:
            raise ValueError(f"{context} repeats case/scene {row[key]}")
        result[row[key]] = row
    return result


def _deterministic(row: dict, first: str, second: str, context: str) -> None:
    one = _hash(row.get(first), context)
    two = _hash(row.get(second), context)
    if row.get("deterministic") is not True or one != two:
        raise ValueError(f"{context} did not render deterministically")


def _storm_repeat(scene: dict, case: dict, context: str) -> None:
    policy = case.get("stormRepeatPolicy")
    if scene.get("stormRepeatPolicy") != policy:
        raise ValueError(f"{context} repeatability policy differs from the plan")
    if policy == "byte-identical":
        _deterministic(scene, "stormFirstHash", "stormSecondHash", context)
        return
    if policy != "identical-coverage" or case.get("ColorComparisonReady") is not False:
        raise ValueError(f"{context} has an invalid repeatability policy")
    gl = _object(scene.get("stormOpenGl"), context)
    if gl.get("Vendor") != "Mesa" or scene.get("deterministic") is not True:
        raise ValueError(f"{context} coverage repeatability is confined to Mesa geometry cases")
    first = _hash(scene.get("stormFirstHash"), context)
    second = _hash(scene.get("stormSecondHash"), context)
    if first == second:
        return
    comparison = _object(scene.get("stormRepeatComparison"), f"{context}.repeat")
    if comparison.get("Passed") is not True:
        raise ValueError(f"{context} repeat comparison failed")
    for key, expected in (
        ("CoverageIntersectionOverUnion", 1),
        ("CoverageDifferenceFraction", 0),
        ("UnforgivenCoverageDifferencePixels", 0),
    ):
        if _number(comparison.get(key), 0, 1 << 30, context) != expected:
            raise ValueError(f"{context} repeat coverage changed")
    reference = _number(comparison.get("ReferenceCoveragePixels"), 1, 1 << 30, context)
    if _number(comparison.get("CandidateCoveragePixels"), 1, 1 << 30, context) != reference:
        raise ValueError(f"{context} repeat coverage count changed")


def _input_identity(value: dict, context: str) -> dict[str, dict]:
    _hash(value.get("sha256"), context)
    files = _rows(value.get("files"), "path", context)
    if type(value.get("fileCount")) is not int or value["fileCount"] != len(files):
        raise ValueError(f"{context} file inventory is incomplete")
    if value.get("hashPolicy") not in ("sha256-crlf-to-lf", "sha256-per-input"):
        raise ValueError(f"{context} has an unknown hash policy")
    for path, entry in files.items():
        if "\\" in path or ":" in path or any(part in ("", ".", "..") for part in path.split("/")):
            raise ValueError(f"{context} has a noncanonical input path")
        _hash(entry.get("sha256"), f"{context}.{path}")
        _number(entry.get("length"), 0, (1 << 63) - 1, f"{context}.{path}.length")
        if entry.get("hashPolicy") not in ("sha256-crlf-to-lf", "sha256-raw"):
            raise ValueError(f"{context}.{path} has an unknown hash policy")
    return files


def _execution(value: dict) -> None:
    if value.get("kind") not in ("local", "github-actions"):
        raise ValueError("Unknown execution kind")
    commit = value.get("checkoutCommit")
    dirty = value.get("checkoutDirty")
    status = "unavailable"
    if commit is not None or dirty is not None:
        if not isinstance(commit, str) or not re.fullmatch(r"(?:[0-9a-f]{40}|[0-9a-f]{64})", commit):
            raise ValueError("Invalid execution checkout identity")
        if type(dirty) is not bool:
            raise ValueError("Execution checkout dirty state is missing")
        status = "modified" if dirty else "identified"
    if value.get("checkoutStatus") != status:
        raise ValueError("Execution checkout status contradicts its identity")
    if value.get("runtimeIdentifier") not in ("win-x64", "linux-x64", "osx-arm64") or not value.get("framework"):
        raise ValueError("Execution runtime identity is missing or unsupported")
    if value["kind"] == "github-actions":
        run_id = value.get("runId")
        attempt = value.get("runAttempt")
        if not isinstance(run_id, str) or not run_id.isascii() or not run_id.isdigit() or int(run_id) <= 0:
            raise ValueError("Execution run identity is invalid")
        if type(attempt) is not int or attempt <= 0 or not value.get("repository") or not value.get("job"):
            raise ValueError("Execution attempt/repository/job identity is incomplete")
        if status == "unavailable":
            raise ValueError("Hosted execution requires observed checkout identity")
    elif value.get("runId") is not None or value.get("runAttempt") is not None:
        raise ValueError("Local execution cannot claim a hosted run")


def _comparison(value: object, case: dict, context: str) -> float:
    comparison = _object(value, context)
    for key in ("ReferenceCoveragePixels", "CandidateCoveragePixels"):
        _number(comparison.get(key), 1, 1 << 30, f"{context}.{key}")
    score = _number(comparison.get("AdjustedCoverageIntersectionOverUnion"), 0, 1, context)
    maximum = _number(comparison.get("MaximumChannelDifference"), 0, 255, context)
    mean = _number(comparison.get("MeanChannelDifference"), 0, 255, context)
    if case["GateEnabled"]:
        required = _number(case.get("RequiredAdjustedIou"), 0, 1, context)
        if comparison.get("Passed") is not True or score < required:
            raise ValueError(f"{context} did not meet its required image gate")
        if case.get("ColorComparisonReady") is True:
            tolerance = _object(case.get("tolerance"), f"{context}.tolerance")
            max_limit = _number(tolerance.get("MaximumChannelDifference"), 0, 255, context)
            mean_limit = _number(tolerance.get("MaximumMeanChannelDifference"), 0, 255, context)
            if maximum > max_limit or mean > mean_limit:
                raise ValueError(f"{context} did not meet its required colour gate")
    return score


def validate_evidence(plan: dict, primary: dict, perturbations: dict) -> dict:
    for report in (plan, primary, perturbations):
        if not isinstance(report, dict) or report.get("schemaVersion") != 2:
            raise ValueError("Corpus evidence requires schema 2")
    if plan.get("status") != "planned-not-execution":
        raise ValueError("Corpus plan is not an execution result")
    for key in ("execution", "sourceIdentity", "fixtureIdentity"):
        identity = plan.get(key)
        if not isinstance(identity, dict) or not identity:
            raise ValueError(f"Corpus {key} identity is missing")
        if identity != primary.get(key) or identity != perturbations.get(key):
            raise ValueError(f"Corpus {key} identity/execution differs between reports")
    _execution(plan["execution"])
    _input_identity(plan["sourceIdentity"], "Source identity")
    fixtures = _input_identity(plan["fixtureIdentity"], "Fixture identity")

    cases = _rows(plan.get("cases"), "id", "Corpus plan")
    scenes = _rows(primary.get("scenes"), "scene", "Primary capture")
    probes = _rows(perturbations.get("scenes"), "scene", "Perturbation capture")
    for case in cases.values():
        if type(case.get("selected")) is not bool or type(case.get("GateEnabled")) is not bool:
            raise ValueError("Corpus case selection/gate policy must be explicit")
    selected = {name: case for name, case in cases.items() if case["selected"]}
    if not selected or selected.keys() != scenes.keys() or selected.keys() != probes.keys():
        raise ValueError("Executed scenes do not match the complete selected corpus case set")

    backend_names = None
    minimum_margin = _number(perturbations.get("minimumRequiredMargin"), 0.18, 1, "Perturbation margin")
    for name, case in selected.items():
        scene = scenes[name]
        fixture = fixtures.get(case.get("stagePath"))
        stage = _object(case.get("stage"), f"{name}.stage")
        if fixture is None or any(stage.get(key) != fixture.get(key) for key in ("sha256", "length", "hashPolicy")):
            raise ValueError(f"{name} stage is not bound to the fingerprinted fixture inventory")
        if scene.get("featureIds") != case.get("featureIds") or not case.get("featureIds"):
            raise ValueError(f"{name} feature identity differs from its case")
        for key in ("GateEnabled", "RequiredAdjustedIou", "ColorComparisonReady"):
            if scene.get(key) != case.get(key):
                raise ValueError(f"{name} changed its declared gate policy")
        expected_status = "executed-gate" if case["GateEnabled"] else "measured-reference-limit"
        if scene.get("status") != expected_status:
            raise ValueError(f"{name} did not execute its declared comparison")
        if scene.get("stageIdentity") != case.get("stage"):
            raise ValueError(f"{name} stage identity differs from its case")
        _storm_repeat(scene, case, f"{name}.Storm")
        backends = _rows(scene.get("backends"), "backend", f"{name} backends")
        if backend_names is not None and backend_names != backends.keys():
            raise ValueError(f"{name} did not execute the same backend set")
        backend_names = backends.keys()
        for backend_name, backend in backends.items():
            context = f"{name}.{backend_name}"
            _deterministic(backend, "firstHash", "secondHash", context)
            _comparison(backend.get("comparison"), case, context)
            device = _object(backend.get("device"), f"{context}.device")
            if not device.get("DeviceName") or not device.get("ApiVersion") or type(device.get("IsSoftware")) is not bool:
                raise ValueError(f"{context} has incomplete device evidence")

        probe = probes[name]
        if probe.get("GateEnabled") != case["GateEnabled"]:
            raise ValueError(f"{name} perturbation gate policy differs")
        correct = _comparison(probe.get("correct"), case, f"{name}.correct")
        controls = ["verticalFlip", "horizontalMirror", "transposedAxes", "shiftedCamera"]
        if case.get("TimeCode") != 1:
            controls.append("wrongTimeCode")
        margins = []
        for control in controls:
            result = _object(probe.get(control), f"{name}.{control}")
            score = _number(result.get("AdjustedCoverageIntersectionOverUnion"), 0, 1, f"{name}.{control}")
            margins.append(correct - score)
            if case["GateEnabled"] and result.get("Passed") is not False:
                raise ValueError(f"{name}.{control} did not detect the perturbation")
        declared = _object(probe.get("margins"), f"{name}.margins")
        weakest = _number(declared.get("weakest"), -1, 1, f"{name}.weakestMargin")
        if not math.isclose(weakest, min(margins), rel_tol=0, abs_tol=1e-12):
            raise ValueError(f"{name} perturbation margin differs from its measured images")
        if case["GateEnabled"] and weakest < minimum_margin:
            raise ValueError(f"{name} perturbations are not discriminating")

    return {
        "schemaVersion": 1,
        "status": "executed",
        "scenes": len(scenes),
        "requiredGates": sum(case["GateEnabled"] for case in selected.values()),
        "backends": sorted(backend_names),
        "execution": primary["execution"],
    }


def _read_report(path: pathlib.Path) -> dict:
    if path.stat().st_size > 16 * 1024 * 1024:
        raise ValueError(f"Corpus evidence exceeds the 16 MiB report bound: {path.name}")
    with path.open(encoding="utf-8-sig") as stream:
        return json.load(stream)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=pathlib.Path, required=True, help="Directory containing parity evidence.")
    args = parser.parse_args()
    root = args.root.resolve()
    output = root / "parity-capture-status.json"
    temporary = None
    try:
        output.unlink(missing_ok=True)
        result = validate_evidence(
            _read_report(root / "parity-capture-corpus.json"),
            _read_report(root / "parity-capture-evidence.json"),
            _read_report(root / "parity-capture-perturbations.json"),
        )
        with tempfile.NamedTemporaryFile(
            mode="w", encoding="utf-8", newline="\n", dir=root, suffix=".tmp", delete=False
        ) as stream:
            temporary = pathlib.Path(stream.name)
            stream.write(json.dumps(result, indent=2, allow_nan=False) + "\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, output)
        temporary = None
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"Render corpus evidence failed: {error}", file=sys.stderr)
        return 1
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)
    print(f"Executed {result['requiredGates']} required gates across {result['scenes']} corpus scenes.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
