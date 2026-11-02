# Copyright (c) marcschier. Licensed under the MIT License.

import copy
from datetime import datetime, timezone
import importlib.util
from pathlib import Path
import unittest


path = Path(__file__).resolve().parents[1] / "verify-storm-normal-reference-report.py"
spec = importlib.util.spec_from_file_location("normal_reference_report", path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
STARTED = datetime(2026, 1, 1, tzinfo=timezone.utc)
FINISHED = datetime(2026, 1, 1, 0, 0, 10, tzinfo=timezone.utc)
EXPECTED_ROWS = {
    "StormCoarseNormalsFollowSchemeAndPointIdentity(none, True, False)",
    "StormCoarseNormalsFollowSchemeAndPointIdentity(none, True, True)",
    "StormCoarseNormalsFollowSchemeAndPointIdentity(none, False, True)",
    "StormCoarseNormalsFollowSchemeAndPointIdentity(bilinear, True, False)",
    "StormCoarseNormalsFollowSchemeAndPointIdentity(bilinear, True, True)",
    "StormCoarseNormalsFollowSchemeAndPointIdentity(catmullClark, True, False)",
    "StormCoarseNormalsFollowSchemeAndPointIdentity(catmullClark, True, True)",
    "StormCoarseNormalsFollowSchemeAndPointIdentity(catmullClark, False, False)",
    "StormCoarseNormalsFollowSchemeAndPointIdentity(catmullClark, False, True)",
    "StormCoarseNormalsFollowSchemeAndPointIdentity(loop, True, False)",
    "StormCoarseNormalsFollowSchemeAndPointIdentity(loop, True, True)",
    "StormCoarseNormalsFollowSchemeAndPointIdentity(loop, False, True)",
    "StormCoarseNgonNormalsUseFaceAdjacency(False)",
    "StormCoarseNgonNormalsUseFaceAdjacency(True)",
    "StormNonplanarNgonSmoothNormalsUseEachOriginalFaceCorner(False)",
    "StormNonplanarNgonSmoothNormalsUseEachOriginalFaceCorner(True)",
    "StormCoarseSmoothNormalsIncludeHoleFaces(catmullClark, False)",
    "StormCoarseSmoothNormalsIncludeHoleFaces(catmullClark, True)",
    "StormCoarseSmoothNormalsIncludeHoleFaces(loop, False)",
    "StormCoarseSmoothNormalsIncludeHoleFaces(loop, True)",
    "StormNonplanarPolygonFlatNormalsUseEachEmittedTriangle(none, False)",
    "StormNonplanarPolygonFlatNormalsUseEachEmittedTriangle(none, True)",
    "StormNonplanarPolygonFlatNormalsUseEachEmittedTriangle(bilinear, False)",
    "StormNonplanarPolygonFlatNormalsUseEachEmittedTriangle(bilinear, True)",
}


def valid_report():
    tests = []
    for index, row in enumerate(sorted(EXPECTED_ROWS)):
        tests.append({
            "id": f"case-{index}",
            "className": module.CLASS,
            "methodName": row.split("(", 1)[0],
            "displayName": row,
            "status": "passed",
            "startTime": "2026-01-01T00:00:02Z",
            "endTime": "2026-01-01T00:00:03Z",
        })
    tests.append({
        "methodName": "AnUnrelatedParityTest",
        "status": "passed",
    })
    return {
        "schemaVersion": 1,
        "assemblyName": "OpenUsd.Rendering.ConformanceTests",
        "publicationGeneration": "0123456789abcdef0123456789abcdef",
        "groups": [{
            "namespace": "OpenUsd.Rendering.ConformanceTests",
            "className": module.CLASS,
            "tests": tests,
        }],
    }


class StormNormalReferenceReportTests(unittest.TestCase):
    def test_exact_fresh_rows_are_accepted(self):
        self.assertEqual(module.expected_rows(), EXPECTED_ROWS)
        module.verify(valid_report(), STARTED, FINISHED)

    def test_incomplete_or_stale_evidence_is_rejected(self):
        def changed(mutator):
            report = valid_report()
            mutator(report, report["groups"][0]["tests"])
            return report

        mutations = (
            lambda report, rows: rows.pop(0),
            lambda report, rows: rows.append(copy.deepcopy(rows[0])),
            lambda report, rows: rows[0].__setitem__("status", "skipped"),
            lambda report, rows: rows[1].__setitem__("id", rows[0]["id"]),
            lambda report, rows: rows[0].__setitem__("displayName", "unexpected"),
            lambda report, rows: rows[0].__setitem__("methodName", "AnUnrelatedParityTest"),
            lambda report, rows: rows[0].__setitem__("startTime", "2025-12-31T23:59:59Z"),
            lambda report, rows: rows[0].__setitem__("endTime", "2026-01-01T00:00:11Z"),
            lambda report, rows: rows[0].__setitem__("startTime", "2026-01-01T00:00:02"),
            lambda report, rows: report.__setitem__("publicationGeneration", "0" * 32),
            lambda report, rows: report["groups"].append(copy.deepcopy(report["groups"][0])),
        )
        for index, mutator in enumerate(mutations):
            with self.subTest(mutation=index):
                with self.assertRaises((ValueError, KeyError)):
                    module.verify(changed(mutator), STARTED, FINISHED)


if __name__ == "__main__":
    unittest.main()
