#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.
"""Compare a completed source-identity inventory with an effective placement census."""

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import sys

MAX_FILE_BYTES = 64 * 1024 * 1024
MAX_LINE_BYTES = 1024 * 1024
MAX_RECORDS = 100_000
SAMPLE_LIMIT = 64
TRANSFORM_ABSOLUTE_TOLERANCE = 1e-6
TRANSFORM_RELATIVE_TOLERANCE = 1e-9


def bounded_json(path):
    with path.open("rb") as stream:
        payload = stream.read(MAX_FILE_BYTES + 1)
    if len(payload) > MAX_FILE_BYTES:
        raise ValueError(f"Input exceeds the 64-MiB limit: {path}")
    value = json.loads(payload)
    if not isinstance(value, dict):
        raise ValueError(f"A JSON object is required: {path}")
    return value, {"path": str(path.resolve()), "sha256": hashlib.sha256(payload).hexdigest()}


def prim_path(value):
    if not isinstance(value, str) or not value.startswith("/") or any(ord(c) < 32 for c in value):
        raise ValueError("An identity must contain an absolute USD prim path without control characters.")
    return value


def integer(value):
    if type(value) is not int or value < 0:
        raise ValueError("Identity counts and indices must be non-negative integers.")
    return value


def examples(values):
    ordered = sorted(values)
    return {"count": len(ordered), "sample": ordered[:SAMPLE_LIMIT]}


def matrix(value):
    if not isinstance(value, list) or len(value) != 16 or any(
            type(component) not in (int, float) or not math.isfinite(component) for component in value):
        raise ValueError("A world transform must contain exactly 16 finite numeric components.")
    return value


def reconcile(census_path, manifest_path, receipt_path):
    census, census_hash = bounded_json(census_path)
    receipt, receipt_hash = bounded_json(receipt_path)
    if type(census.get("schemaVersion")) is not int or census["schemaVersion"] not in (1, 2) or \
            census.get("complete") is not True or census.get("errors") != []:
        raise ValueError("A complete, error-free schema-1/2 placement census is required.")
    compare_transforms = census["schemaVersion"] == 2
    if compare_transforms and (type(census.get("timeCode")) not in (int, float) or census["timeCode"] != 0):
        raise ValueError("The placement-transform oracle must describe numeric time zero.")
    root = census.get("root")
    stage = receipt.get("stage")
    if not isinstance(root, str) or not isinstance(stage, str) or \
            os.path.normcase(os.path.normpath(root)) != os.path.normcase(os.path.normpath(stage)):
        raise ValueError("The census and capture refer to different stage roots.")
    if receipt.get("sourceUnchanged") is not True or receipt.get("nativeUnchanged") is not True:
        raise ValueError("The capture did not preserve its source/runtime identities.")
    placements = census.get("placements")
    if not isinstance(placements, list) or len(placements) > MAX_RECORDS:
        raise ValueError("The census placement collection is absent or exceeds its record limit.")
    expected = set()
    expected_transforms = {}
    for placement in placements:
        if not isinstance(placement, dict) or type(placement.get("isSubset")) is not bool or \
                type(placement.get("renderEligible")) is not bool:
            raise ValueError("Census placement eligibility/subset fields must be explicit booleans.")
        if placement["isSubset"] or not placement["renderEligible"]:
            continue
        path = prim_path(placement.get("path"))
        if path in expected:
            raise ValueError(f"The eligible census contains a duplicate source path: {path}")
        expected.add(path)
        if compare_transforms:
            expected_transforms[path] = matrix(placement.get("worldTransform"))
    counts = census.get("counts")
    if not isinstance(counts, dict) or integer(counts.get("eligibleGeometryPlacements")) != len(expected):
        raise ValueError("The eligible census count does not match its actual identity set.")
    del census

    if manifest_path.stat().st_size > MAX_FILE_BYTES:
        raise ValueError("The identity inventory exceeds the 64-MiB limit.")
    phases = receipt.get("phases")
    if not isinstance(phases, list) or not all(isinstance(phase, dict) for phase in phases):
        raise ValueError("The capture phase collection is invalid.")
    phases = [phase for phase in phases if phase.get("phase") == "placement-identities"]
    if len(phases) != 1:
        raise ValueError("The capture must record exactly one completed identity-inventory phase.")
    phase, = phases
    actual = set()
    wire_keys = set()
    duplicate_sources = set()
    duplicate_wires = set()
    unresolved_points = set()
    transform_mismatches = set()
    maximum_transform_difference = 0.0
    count = 0
    header = None
    footer = None
    manifest_digest = hashlib.sha256()
    manifest_bytes = 0
    with manifest_path.open("rb") as stream:
        while True:
            line = stream.readline(MAX_LINE_BYTES + 1)
            if not line:
                break
            if len(line) > MAX_LINE_BYTES:
                raise ValueError("An identity inventory record exceeds the 1-MiB line limit.")
            manifest_bytes += len(line)
            if manifest_bytes > MAX_FILE_BYTES:
                raise ValueError("The identity inventory exceeds the 64-MiB limit.")
            manifest_digest.update(line)
            value = json.loads(line)
            if not isinstance(value, dict):
                raise ValueError("Identity inventory records must be JSON objects.")
            if header is None:
                if value.get("kind") != "header" or value.get("schema") != 2 or \
                        value.get("pageAbi") not in (26, 27) or integer(value.get("pageRevision")) == 0:
                    raise ValueError("A schema-2 source-resolved inventory header is required.")
                header = value
                if compare_transforms and (
                        type(header.get("timeCode")) not in (int, float) or header["timeCode"] != 0):
                    raise ValueError("The inventory must explicitly match the oracle's numeric time zero.")
                continue
            if footer is not None:
                raise ValueError("The inventory contains records after its completion marker.")
            if value.get("kind") == "complete":
                footer = value
                continue
            if value.get("kind") != "mesh" or count >= MAX_RECORDS:
                raise ValueError("The inventory contains an unknown or excess record.")
            wire = value.get("wire", {})
            if not isinstance(wire, dict):
                raise ValueError("The inventory wire identity must be a JSON object.")
            key = (prim_path(wire.get("Path")), integer(wire.get("InstanceIndex")))
            if key in wire_keys:
                duplicate_wires.add(f"{key[0]} [instance {key[1]}]")
            wire_keys.add(key)
            source = prim_path(value.get("sourcePath"))
            context = value.get("sourceContext")
            if not isinstance(context, list):
                raise ValueError("The inventory must explicitly record its source instancer context.")
            if context:
                unresolved_points.add(source)
            if compare_transforms:
                actual_transform = matrix(wire.get("transform"))
                if not context and source in expected_transforms:
                    expected_transform = expected_transforms[source]
                    differences = [abs(a - b) for a, b in zip(actual_transform, expected_transform)]
                    if not all(math.isfinite(value) for value in differences):
                        raise ValueError("A transform difference exceeds finite numeric range.")
                    maximum_transform_difference = max(maximum_transform_difference, max(differences))
                    if not all(math.isclose(a, b, rel_tol=TRANSFORM_RELATIVE_TOLERANCE,
                            abs_tol=TRANSFORM_ABSOLUTE_TOLERANCE)
                            for a, b in zip(actual_transform, expected_transform)):
                        transform_mismatches.add(source)
            if source in actual:
                duplicate_sources.add(source)
            actual.add(source)
            count += 1
    if header is None or footer is None or integer(footer.get("records")) != count or \
            integer(phase.get("records")) != count:
        raise ValueError("The inventory is incomplete or its actual record count is inconsistent.")
    manifest_hash = {"path": str(manifest_path.resolve()), "sha256": manifest_digest.hexdigest()}
    recorded_hash = phase.get("sha256")
    if not isinstance(recorded_hash, str) or recorded_hash.lower() != manifest_hash["sha256"]:
        raise ValueError("The identity inventory does not match the capture's recorded hash.")
    if integer(phase.get("bytes")) != manifest_bytes:
        raise ValueError("The identity inventory does not match the capture's recorded extent.")
    missing = expected - actual
    unexpected = actual - expected
    matches = not (missing or unexpected or duplicate_sources or duplicate_wires or unresolved_points)
    placement_matches = matches and not transform_mismatches
    return {
        "schemaVersion": 1,
        "scope": "Source identity sets and optional USD world transforms; not topology, material or pixel acceptance.",
        "identitySetMatches": matches,
        "placementMatches": placement_matches,
        "transformsCompared": compare_transforms,
        "worldTransformsMatch": placement_matches if compare_transforms else None,
        "maximumAbsoluteTransformDifference": maximum_transform_difference if compare_transforms else None,
        "transformTolerance": {
            "absolute": TRANSFORM_ABSOLUTE_TOLERANCE, "relative": TRANSFORM_RELATIVE_TOLERANCE,
            "units": "authored stage units",
        } if compare_transforms else None,
        "frameAccepted": False,
        "captureStatus": receipt.get("status"),
        "stage": stage,
        "stageSha256": receipt.get("stageSha256"),
        "census": census_hash,
        "inventory": manifest_hash,
        "captureReceipt": receipt_hash,
        "expectedIdentities": len(expected),
        "actualRecords": count,
        "actualUniqueSources": len(actual),
        "sampleLimitPerCategory": SAMPLE_LIMIT,
        "missing": examples(missing),
        "unexpected": examples(unexpected),
        "duplicateSources": examples(duplicate_sources),
        "duplicateWireKeys": examples(duplicate_wires),
        "unexpandedPointInstanceSources": examples(unresolved_points),
        "transformMismatches": examples(transform_mismatches),
    }


def publish(path, result):
    pending = path.with_name(path.name + ".pending")
    created = False
    try:
        with pending.open("x", encoding="utf-8", newline="\n") as stream:
            created = True
            json.dump(result, stream, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.link(pending, path)
    finally:
        if created:
            pending.unlink()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--census", required=True, type=Path)
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--capture-receipt", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        result = reconcile(args.census, args.manifest, args.capture_receipt)
        publish(args.output, result)
    except (OSError, ValueError) as error:
        print(f"Placement reconciliation refused: {error}", file=sys.stderr)
        return 2
    print(json.dumps({"placementMatches": result["placementMatches"], "report": str(args.output)}))
    return 0 if result["placementMatches"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
