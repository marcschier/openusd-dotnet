# Copyright (c) marcschier. Licensed under the MIT License.
"""The rendered corpus, rather than a successful test exit, establishes execution."""

from __future__ import annotations

import copy
import importlib.machinery
import importlib.util
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
loader = importlib.machinery.SourceFileLoader(
    "verify_render_corpus", str(ROOT / "eng" / "verify-render-corpus.py")
)
spec = importlib.util.spec_from_loader(loader.name, loader)
corpus = importlib.util.module_from_spec(spec)
sys.modules[loader.name] = corpus
loader.exec_module(corpus)


def documents() -> tuple[dict, dict, dict]:
    execution = {
        "kind": "local", "checkoutCommit": "a" * 40, "checkoutDirty": False,
        "checkoutStatus": "identified", "runId": None, "runAttempt": None,
        "runtimeIdentifier": "win-x64", "framework": ".NET 10.0",
    }
    stage = {
        "path": "panel.usda", "sha256": "d" * 64, "length": 42,
        "hashPolicy": "sha256-crlf-to-lf",
    }
    source = {
        "sha256": "b" * 64, "fileCount": 1, "hashPolicy": "sha256-crlf-to-lf",
        "files": [{**stage, "path": "source.cs"}],
    }
    fixtures = {
        "sha256": "c" * 64, "fileCount": 1, "hashPolicy": "sha256-per-input",
        "files": [{**stage, "path": "fixtures/panel.usda"}],
    }
    common = {
        "schemaVersion": 2, "execution": execution,
        "sourceIdentity": source, "fixtureIdentity": fixtures,
    }
    case = {
        "id": "panel", "featureIds": ["preview-surface-textures"], "selected": True,
        "GateEnabled": True, "RequiredAdjustedIou": 1, "ColorComparisonReady": True,
        "TimeCode": 1, "stage": stage, "stagePath": "fixtures/panel.usda",
        "stormRepeatPolicy": "byte-identical",
        "tolerance": {"MaximumChannelDifference": 16, "MaximumMeanChannelDifference": 8},
    }
    comparison = {
        "Passed": True, "ReferenceCoveragePixels": 20, "CandidateCoveragePixels": 20,
        "AdjustedCoverageIntersectionOverUnion": 1, "MaximumChannelDifference": 3,
        "MeanChannelDifference": 0.5,
    }
    backend = {
        "backend": "D3D12 WARP", "firstHash": "e" * 64, "secondHash": "e" * 64,
        "deterministic": True, "comparison": comparison,
        "device": {"DeviceName": "Test adapter", "ApiVersion": "D3D12", "IsSoftware": True},
    }
    scene = {
        "scene": "panel", "featureIds": case["featureIds"],
        "status": "executed-gate", "GateEnabled": True, "RequiredAdjustedIou": 1,
        "ColorComparisonReady": True, "stageIdentity": stage,
        "stormRepeatPolicy": "byte-identical",
        "deterministic": True, "stormFirstHash": "f" * 64, "stormSecondHash": "f" * 64,
        "backends": [backend],
    }
    perturbation = {
        "scene": "panel", "GateEnabled": True, "correct": comparison,
        "margins": {"weakest": 0.3},
        "wrongTimeCode": None,
    }
    for name in ("verticalFlip", "horizontalMirror", "transposedAxes", "shiftedCamera"):
        perturbation[name] = {"Passed": False, "AdjustedCoverageIntersectionOverUnion": 0.7}
    plan = {**copy.deepcopy(common), "status": "planned-not-execution", "cases": [case]}
    primary = {**copy.deepcopy(common), "scenes": [scene]}
    perturbed = {
        **copy.deepcopy(common), "minimumRequiredMargin": 0.18, "scenes": [perturbation]
    }
    return tuple(copy.deepcopy(report) for report in (plan, primary, perturbed))


class CorpusEvidenceTests(unittest.TestCase):
    def test_a_complete_rendered_case_is_classified_separately_from_its_plan(self):
        result = corpus.validate_evidence(*documents())

        self.assertEqual(result["status"], "executed")
        self.assertEqual(result["scenes"], 1)
        self.assertEqual(result["requiredGates"], 1)
        self.assertEqual(result["backends"], ["D3D12 WARP"])
        self.assertEqual(result["execution"]["checkoutCommit"], "a" * 40)

    def test_missing_or_duplicate_selected_cases_cannot_pass(self):
        for report_index, key in ((0, "cases"), (1, "scenes"), (2, "scenes")):
            for replacement in ("missing", "duplicate"):
                with self.subTest(report=report_index, replacement=replacement):
                    reports = list(documents())
                    entries = reports[report_index][key]
                    reports[report_index][key] = [] if replacement == "missing" else entries * 2
                    with self.assertRaisesRegex(ValueError, "case|scene"):
                        corpus.validate_evidence(*reports)

    def test_reports_from_different_sources_or_attempts_cannot_be_joined(self):
        for report_index in range(3):
            for key, field, replacement in (
                ("execution", "runAttempt", 3),
                ("sourceIdentity", "sha256", "a" * 64),
                ("fixtureIdentity", "sha256", "b" * 64),
            ):
                with self.subTest(report=report_index, key=key):
                    reports = list(documents())
                    reports[report_index][key][field] = replacement
                    with self.assertRaisesRegex(ValueError, "identity|execution"):
                        corpus.validate_evidence(*reports)

    def test_rendered_gates_require_visible_deterministic_colour_sensitive_results(self):
        mutations = (
            ("comparison", "Passed", False),
            ("comparison", "ReferenceCoveragePixels", 0),
            ("comparison", "CandidateCoveragePixels", 0),
            ("comparison", "AdjustedCoverageIntersectionOverUnion", 0.99),
            ("comparison", "MaximumChannelDifference", 17),
            ("comparison", "MeanChannelDifference", float("nan")),
            ("comparison", "MeanChannelDifference", 9),
            ("backend", "deterministic", False),
            ("backend", "secondHash", "a" * 64),
            ("scene", "deterministic", False),
            ("scene", "stormSecondHash", "a" * 64),
            ("scene", "status", "capability-skip"),
            ("scene", "featureIds", ["a-different-feature"]),
            ("scene", "backends", []),
        )
        for target, field, value in mutations:
            with self.subTest(target=target, field=field, value=value):
                plan, primary, perturbed = documents()
                scene = primary["scenes"][0]
                container = scene if target == "scene" else scene["backends"][0]
                if target == "comparison":
                    container = container["comparison"]
                container[field] = value
                with self.assertRaises(ValueError):
                    corpus.validate_evidence(plan, primary, perturbed)

    def test_insensitive_or_missing_negative_controls_cannot_pass(self):
        for field, value in (
            ("verticalFlip", {"Passed": True, "AdjustedCoverageIntersectionOverUnion": 0.7}),
            ("shiftedCamera", None),
            ("horizontalMirror", {"Passed": False, "AdjustedCoverageIntersectionOverUnion": 0.95}),
            ("margins", {"weakest": 0.1}),
            ("margins", {"weakest": float("nan")}),
        ):
            with self.subTest(field=field):
                plan, primary, perturbed = documents()
                perturbed["scenes"][0][field] = value
                with self.assertRaises(ValueError):
                    corpus.validate_evidence(plan, primary, perturbed)

    def test_consistently_malformed_identity_cannot_claim_execution(self):
        for key, field, value in (
            ("sourceIdentity", "sha256", "not-a-hash"),
            ("fixtureIdentity", "fileCount", 2),
            ("sourceIdentity", "files", []),
            ("execution", "kind", "success"),
            ("execution", "checkoutStatus", "identified-but-not-observed"),
        ):
            with self.subTest(key=key, field=field):
                reports = list(documents())
                for report in reports:
                    report[key][field] = value
                with self.assertRaises(ValueError):
                    corpus.validate_evidence(*reports)

    def test_a_planned_stage_must_belong_to_the_fingerprinted_fixture_set(self):
        plan, primary, perturbed = documents()
        plan["cases"][0]["stagePath"] = "fixtures/a-different-stage.usda"
        with self.assertRaisesRegex(ValueError, "fixture"):
            corpus.validate_evidence(plan, primary, perturbed)

    def test_a_measured_reference_limit_is_not_promoted_to_a_required_gate(self):
        plan, primary, perturbed = documents()
        measured_case = copy.deepcopy(plan["cases"][0])
        measured_case.update(id="limited-reference", GateEnabled=False, RequiredAdjustedIou=None)
        measured_scene = copy.deepcopy(primary["scenes"][0])
        measured_scene.update(
            scene="limited-reference", GateEnabled=False, RequiredAdjustedIou=None,
            status="measured-reference-limit",
        )
        measured_scene["backends"][0]["comparison"]["Passed"] = False
        measured_probe = copy.deepcopy(perturbed["scenes"][0])
        measured_probe.update(scene="limited-reference", GateEnabled=False)
        measured_probe["correct"]["Passed"] = False
        plan["cases"].append(measured_case)
        primary["scenes"].append(measured_scene)
        perturbed["scenes"].append(measured_probe)
        excluded = copy.deepcopy(measured_case)
        excluded.update(id="excluded-for-this-host", selected=False)
        plan["cases"].append(excluded)

        result = corpus.validate_evidence(plan, primary, perturbed)

        self.assertEqual(result["scenes"], 2)
        self.assertEqual(result["requiredGates"], 1)

    def test_the_command_never_leaves_a_stale_success_when_evidence_is_missing(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            for name, report in zip((
                "parity-capture-corpus.json", "parity-capture-evidence.json",
                "parity-capture-perturbations.json",
            ), documents(), strict=True):
                (root / name).write_text(json.dumps(report), encoding="utf-8")
            command = [sys.executable, str(ROOT / "eng" / "verify-render-corpus.py"), "--root", str(root)]

            first = subprocess.run(command, capture_output=True, text=True, check=False)

            self.assertEqual(first.returncode, 0, first.stderr)
            status = root / "parity-capture-status.json"
            self.assertTrue(status.is_file(), first.stdout)
            self.assertEqual(json.loads(status.read_text(encoding="utf-8"))["requiredGates"], 1)
            (root / "parity-capture-evidence.json").unlink()

            second = subprocess.run(command, capture_output=True, text=True, check=False)

            self.assertNotEqual(second.returncode, 0)
            self.assertIn("evidence", second.stderr)
            self.assertFalse(status.exists())
            self.assertEqual(list(root.glob("*.tmp")), [])

    def test_mesa_geometry_repeatability_requires_the_existing_exact_coverage_contract(self):
        plan, primary, perturbed = documents()
        case = plan["cases"][0]
        scene = primary["scenes"][0]
        case["ColorComparisonReady"] = scene["ColorComparisonReady"] = False
        case["stormRepeatPolicy"] = scene["stormRepeatPolicy"] = "identical-coverage"
        scene["stormSecondHash"] = "a" * 64
        scene["stormOpenGl"] = {"Vendor": "Mesa"}
        scene["stormRepeatComparison"] = {
            "Passed": True, "CoverageIntersectionOverUnion": 1,
            "CoverageDifferenceFraction": 0, "UnforgivenCoverageDifferencePixels": 0,
            "ReferenceCoveragePixels": 20, "CandidateCoveragePixels": 20,
        }

        self.assertEqual(corpus.validate_evidence(plan, primary, perturbed)["requiredGates"], 1)

        for field, value in (
            ("CoverageIntersectionOverUnion", 0.999),
            ("CoverageDifferenceFraction", 0.001),
            ("UnforgivenCoverageDifferencePixels", 1),
            ("CandidateCoveragePixels", 19),
        ):
            with self.subTest(field=field):
                changed = copy.deepcopy(primary)
                changed["scenes"][0]["stormRepeatComparison"][field] = value
                with self.assertRaises(ValueError):
                    corpus.validate_evidence(plan, changed, perturbed)
        case["ColorComparisonReady"] = scene["ColorComparisonReady"] = True
        with self.assertRaises(ValueError):
            corpus.validate_evidence(plan, primary, perturbed)



if __name__ == "__main__":
    unittest.main()
