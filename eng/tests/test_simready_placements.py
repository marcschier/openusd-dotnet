# Copyright (c) marcschier. Licensed under the MIT License.

import copy
import hashlib
import json
import math
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import reconcile_simready_placements as reconciliation


class PlacementReconciliationTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory(prefix="openusd-placement-reconciliation-")
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.census_path = self.root / "census.json"
        self.manifest_path = self.root / "identities.jsonl"
        self.receipt_path = self.root / "receipt.json"
        self.output = self.root / "report.json"
        self.header_time = 0
        self.header_abi = 26
        self.census = {
            "schemaVersion": 1, "complete": True, "errors": [],
            "root": str(self.root / "stage.usda"),
            "counts": {"eligibleGeometryPlacements": 2},
            "placements": [
                {"path": "/World/A", "isSubset": False, "renderEligible": True},
                {"path": "/World/B", "isSubset": False, "renderEligible": True},
                {"path": "/World/Hidden", "isSubset": False, "renderEligible": False},
                {"path": "/World/A/Subset", "isSubset": True, "renderEligible": True},
            ],
        }
        self.rows = [
            {"kind": "mesh", "wire": {"Path": "/Prototype", "InstanceIndex": 4},
             "sourcePath": "/World/B", "sourceContext": []},
            {"kind": "mesh", "wire": {"Path": "/Prototype", "InstanceIndex": 9},
             "sourcePath": "/World/A", "sourceContext": []},
        ]
        self.receipt = {
            "stage": self.census["root"], "sourceUnchanged": True, "nativeUnchanged": True,
            "status": "incomplete",
        }
        self.write()

    def write(self, footer=True, trailing=None):
        self.census_path.write_text(json.dumps(self.census), encoding="utf-8")
        values = [{"kind": "header", "schema": 2, "pageAbi": self.header_abi, "pageRevision": 1,
                   "timeCode": self.header_time}, *self.rows]
        if footer:
            values.append({"kind": "complete", "records": len(self.rows)})
        if trailing is not None:
            values.append(trailing)
        payload = "".join(json.dumps(value) + "\n" for value in values).encode()
        self.manifest_path.write_bytes(payload)
        self.receipt["phases"] = [{
            "phase": "placement-identities", "records": len(self.rows), "bytes": len(payload),
            "sha256": hashlib.sha256(payload).hexdigest().upper(),
        }]
        self.receipt_path.write_text(json.dumps(self.receipt), encoding="utf-8")

    def compare(self):
        return reconciliation.reconcile(self.census_path, self.manifest_path, self.receipt_path)

    def test_historical_and_detail_normal_page_inventories_keep_the_same_identity_contract(self):
        for abi in (26, 27):
            with self.subTest(abi=abi):
                self.header_abi = abi
                self.write()
                self.assertTrue(self.compare()["identitySetMatches"])
        self.header_abi = 28
        self.write()
        with self.assertRaisesRegex(ValueError, "schema-2"):
            self.compare()

    def enable_transforms(self):
        self.census["schemaVersion"] = 2
        self.census["timeCode"] = 0
        for placement, x in zip(self.census["placements"][:2], (0, 10)):
            placement["worldTransform"] = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, 0, 0, 1]
        expected = {value["path"]: value["worldTransform"] for value in self.census["placements"][:2]}
        for row in self.rows:
            row["wire"]["transform"] = expected[row["sourcePath"]].copy()
        self.write()

    def test_world_transform_mismatch_is_not_hidden_by_matching_identities(self):
        self.enable_transforms()
        result = self.compare()
        self.assertTrue(result["placementMatches"])
        self.assertTrue(result["worldTransformsMatch"])
        self.rows[0]["wire"]["transform"][12] += 0.01
        self.write()
        result = self.compare()
        self.assertTrue(result["identitySetMatches"])
        self.assertFalse(result["placementMatches"])
        self.assertFalse(result["worldTransformsMatch"])
        self.assertEqual(result["transformMismatches"], {"count": 1, "sample": ["/World/B"]})
        self.assertAlmostEqual(result["maximumAbsoluteTransformDifference"], 0.01)

    def test_transform_absolute_threshold_and_next_float_are_distinguished(self):
        self.enable_transforms()
        self.rows[1]["wire"]["transform"][12] = reconciliation.TRANSFORM_ABSOLUTE_TOLERANCE
        self.write()
        self.assertTrue(self.compare()["placementMatches"])
        self.rows[1]["wire"]["transform"][12] = math.nextafter(
            reconciliation.TRANSFORM_ABSOLUTE_TOLERANCE, math.inf)
        self.write()
        self.assertFalse(self.compare()["placementMatches"])

    def test_transform_relative_tolerance_is_reported_and_enforced(self):
        self.enable_transforms()
        self.census["placements"][0]["worldTransform"][12] = 100_000_000
        self.rows[1]["wire"]["transform"][12] = 100_000_000.05
        self.write()
        result = self.compare()
        self.assertTrue(result["placementMatches"])
        self.assertEqual(result["transformTolerance"]["relative"], 1e-9)
        self.rows[1]["wire"]["transform"][12] = 100_000_000.2
        self.write()
        self.assertFalse(self.compare()["placementMatches"])

    def test_transform_shape_nonfinite_values_and_time_mismatch_are_invalid(self):
        self.enable_transforms()
        original = self.rows[0]["wire"]["transform"].copy()
        for value in (original[:15], [math.nan] + original[1:], [True] + original[1:]):
            with self.subTest(value=value):
                self.rows[0]["wire"]["transform"] = value
                self.write()
                with self.assertRaisesRegex(ValueError, "16 finite numeric"):
                    self.compare()
        self.rows[0]["wire"]["transform"] = original
        self.header_time = 1
        self.write()
        with self.assertRaisesRegex(ValueError, "numeric time zero"):
            self.compare()

    def test_exact_source_set_is_order_independent_and_does_not_accept_frame(self):
        result = self.compare()
        self.assertTrue(result["identitySetMatches"])
        self.assertFalse(result["frameAccepted"])
        self.assertEqual(result["captureStatus"], "incomplete")
        self.assertEqual((result["expectedIdentities"], result["actualRecords"]), (2, 2))
        for name in ("missing", "unexpected", "duplicateSources", "duplicateWireKeys"):
            self.assertEqual(result[name], {"count": 0, "sample": []})

    def test_equal_counts_cannot_hide_missing_and_unexpected_identities(self):
        self.rows[0]["sourcePath"] = "/World/Wrong"
        self.write()
        result = self.compare()
        self.assertFalse(result["identitySetMatches"])
        self.assertEqual(result["missing"], {"count": 1, "sample": ["/World/B"]})
        self.assertEqual(result["unexpected"], {"count": 1, "sample": ["/World/Wrong"]})

    def test_duplicate_source_does_not_count_as_two_placements(self):
        self.rows[0]["sourcePath"] = "/World/A"
        self.write()
        result = self.compare()
        self.assertFalse(result["identitySetMatches"])
        self.assertEqual(result["actualUniqueSources"], 1)
        self.assertEqual(result["duplicateSources"], {"count": 1, "sample": ["/World/A"]})
        self.assertEqual(result["missing"]["sample"], ["/World/B"])

    def test_duplicate_wire_key_is_refused_even_with_distinct_sources(self):
        self.rows[1]["wire"] = copy.deepcopy(self.rows[0]["wire"])
        self.write()
        result = self.compare()
        self.assertFalse(result["identitySetMatches"])
        self.assertEqual(result["duplicateWireKeys"], {"count": 1, "sample": ["/Prototype [instance 4]"]})

    def test_point_instances_need_an_expanded_oracle(self):
        self.rows[0]["sourceContext"] = [{"InstancerPath": "/Instancer", "InstanceIndex": 3}]
        self.write()
        result = self.compare()
        self.assertFalse(result["identitySetMatches"])
        self.assertEqual(result["unexpandedPointInstanceSources"], {"count": 1, "sample": ["/World/B"]})

    def test_empty_eligible_scene_is_an_empty_identity_match(self):
        self.rows = []
        self.census["placements"] = []
        self.census["counts"]["eligibleGeometryPlacements"] = 0
        self.write()
        result = self.compare()
        self.assertTrue(result["identitySetMatches"])
        self.assertEqual((result["expectedIdentities"], result["actualRecords"]), (0, 0))
        self.assertFalse(result["frameAccepted"])

    def test_exact_line_limit_and_excess_record_are_checked(self):
        limit = max(map(len, self.manifest_path.read_bytes().splitlines(keepends=True)))
        with patch.object(reconciliation, "MAX_LINE_BYTES", limit):
            self.assertTrue(self.compare()["identitySetMatches"])
        with patch.object(reconciliation, "MAX_LINE_BYTES", limit - 1):
            with self.assertRaisesRegex(ValueError, "line limit"):
                self.compare()
        self.census["placements"] = self.census["placements"][:2]
        self.rows.append(copy.deepcopy(self.rows[0]))
        self.write()
        with patch.object(reconciliation, "MAX_RECORDS", 2):
            with self.assertRaisesRegex(ValueError, "excess record"):
                self.compare()

    def test_missing_or_nonterminal_completion_marker_is_invalid(self):
        for footer, trailing in ((False, None), (True, self.rows[0])):
            with self.subTest(footer=footer):
                self.write(footer=footer, trailing=trailing)
                with self.assertRaises(ValueError):
                    self.compare()

    def test_changed_manifest_hash_or_recorded_extent_is_invalid(self):
        self.manifest_path.write_bytes(self.manifest_path.read_bytes().replace(b"/World/B", b"/World/C"))
        with self.assertRaisesRegex(ValueError, "recorded hash"):
            self.compare()
        self.write()
        self.receipt["phases"][0]["bytes"] += 1
        self.receipt_path.write_text(json.dumps(self.receipt), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "recorded extent"):
            self.compare()

    def test_inconsistent_census_count_and_duplicate_expected_path_are_invalid(self):
        self.census["counts"]["eligibleGeometryPlacements"] = 3
        self.write()
        with self.assertRaisesRegex(ValueError, "eligible census count"):
            self.compare()
        self.census["counts"]["eligibleGeometryPlacements"] = 2
        self.census["placements"][1]["path"] = "/World/A"
        self.write()
        with self.assertRaisesRegex(ValueError, "duplicate source path"):
            self.compare()

    def test_stage_mismatch_and_source_change_are_invalid(self):
        self.receipt["stage"] = str(self.root / "different.usda")
        self.write()
        with self.assertRaisesRegex(ValueError, "different stage roots"):
            self.compare()
        self.receipt["stage"] = self.census["root"]
        self.receipt["sourceUnchanged"] = False
        self.write()
        with self.assertRaisesRegex(ValueError, "source/runtime identities"):
            self.compare()

    def test_bounded_json_accepts_exact_extent_and_rejects_one_byte_less(self):
        payload = b'{"value":1}'
        self.census_path.write_bytes(payload)
        with patch.object(reconciliation, "MAX_FILE_BYTES", len(payload)):
            value, evidence = reconciliation.bounded_json(self.census_path)
            self.assertEqual(value, {"value": 1})
            self.assertEqual(evidence["sha256"], hashlib.sha256(payload).hexdigest())
        with patch.object(reconciliation, "MAX_FILE_BYTES", len(payload) - 1):
            with self.assertRaisesRegex(ValueError, "64-MiB limit"):
                reconciliation.bounded_json(self.census_path)

    def test_publication_preserves_existing_output_and_pending_evidence(self):
        self.output.write_bytes(b"prior")
        with self.assertRaises(FileExistsError):
            reconciliation.publish(self.output, {"identitySetMatches": True})
        self.assertEqual(self.output.read_bytes(), b"prior")
        self.assertFalse(self.output.with_name("report.json.pending").exists())
        pending = self.output.with_name("report.json.pending")
        pending.write_bytes(b"owned elsewhere")
        with self.assertRaises(FileExistsError):
            reconciliation.publish(self.output, {})
        self.assertEqual(pending.read_bytes(), b"owned elsewhere")

    def test_cli_reports_mismatch_as_failure_without_frame_acceptance(self):
        self.rows.pop()
        self.write()
        result = subprocess.run(
            [sys.executable, reconciliation.__file__, "--census", str(self.census_path),
             "--manifest", str(self.manifest_path), "--capture-receipt", str(self.receipt_path),
             "--output", str(self.output)], capture_output=True, text=True, check=False)
        self.assertEqual(result.returncode, 1, result.stderr)
        report = json.loads(self.output.read_text())
        self.assertFalse(report["identitySetMatches"])
        self.assertFalse(report["frameAccepted"])
        self.assertEqual(report["missing"]["sample"], ["/World/A"])

    def test_cli_match_and_invalid_input_have_distinct_exit_codes(self):
        arguments = [
            sys.executable, reconciliation.__file__, "--census", str(self.census_path),
            "--manifest", str(self.manifest_path), "--capture-receipt", str(self.receipt_path),
            "--output", str(self.output)]
        result = subprocess.run(arguments, capture_output=True, text=True, check=False)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue(json.loads(self.output.read_text())["identitySetMatches"])
        self.output.unlink()
        self.census["complete"] = False
        self.write()
        result = subprocess.run(arguments, capture_output=True, text=True, check=False)
        self.assertEqual(result.returncode, 2)
        self.assertIn("reconciliation refused", result.stderr)
        self.assertFalse(self.output.exists())


if __name__ == "__main__":
    unittest.main()
