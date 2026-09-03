# Copyright (c) marcschier. Licensed under the MIT License.
"""Regression tests for eng/generate-omniverse-profile.py.

Run with:
    python -m unittest discover eng/tests

These tests exercise the Omniverse interoperability profile generator/validator against
synthetic in-memory profile and support-manifest documents so they need no OpenUSD install
and never touch the real, committed eng/omniverse-profile.json or
eng/support-manifest.json (except where explicitly noted as a real-repository smoke check).
"""

from __future__ import annotations

import copy
import importlib.machinery
import importlib.util
import json
import pathlib
import sys
import tempfile
import unittest

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
GENERATOR = REPO_ROOT / "eng" / "generate-omniverse-profile.py"
SUPPORT_MANIFEST_GENERATOR = REPO_ROOT / "eng" / "generate-support-manifest.py"


def load_module(name: str, path: pathlib.Path):
    loader = importlib.machinery.SourceFileLoader(name, str(path))
    spec = importlib.util.spec_from_loader(loader.name, loader)
    module = importlib.util.module_from_spec(spec)
    sys.modules[loader.name] = module
    loader.exec_module(module)
    return module


gop = load_module("generate_omniverse_profile", GENERATOR)


def base_manifest() -> dict:
    return {
        "$schemaVersion": 1,
        "version": "1.2.3",
        "generatedDocPath": "docs/support-manifest.md",
        "areas": [
            {
                "id": "omniverse-interchange",
                "title": "Omniverse interchange profile",
                "description": "Test area.",
                "entries": [
                    {
                        "id": "unknown-metadata-roundtrip",
                        "description": "Test entry.",
                        "status": "implemented",
                        "evidenceTests": ["tests/OpenUsd.Tests/Fake.cs"],
                        "evidencePlatforms": ["win-x64"],
                    },
                ],
            },
            {
                "id": "excluded-unreachable",
                "title": "Excluded and unreachable",
                "description": "Test area.",
                "entries": [
                    {
                        "id": "omniverse-rtx",
                        "description": "Test entry.",
                        "status": "unreachable",
                        "exclusionReason": "Closed platform.",
                    },
                ],
            },
        ],
    }


def base_profile() -> dict:
    return {
        "$schemaVersion": 1,
        "runtimeVersion": "1.2.3",
        "generatedDocPath": "docs/omniverse-profile.md",
        "platforms": ["win-x64"],
        "dependencies": [
            {
                "id": "openusd",
                "kind": "runtime-pinned",
                "description": "Pinned OpenUSD version.",
                "version": "26.05",
                "versionSource": {
                    "file": "lock.json",
                    "pointer": "openUsd.version",
                },
            },
            {
                "id": "kit-baseline",
                "kind": "external-optional",
                "description": "Optional Kit baseline.",
                "version": "pending",
                "versionSource": None,
            },
        ],
        "features": [
            {
                "id": "unknown-metadata-roundtrip",
                "manifestArea": "omniverse-interchange",
                "manifestEntry": "unknown-metadata-roundtrip",
            },
        ],
        "permanentExclusions": [
            {
                "id": "omniverse-rtx",
                "manifestArea": "excluded-unreachable",
                "manifestEntry": "omniverse-rtx",
            },
        ],
        "kitExecutionEvidence": {
            "status": "pending",
            "description": "Kit round trip.",
            "blockedOn": "No authorized job exists.",
            "evidenceWorkflow": None,
            "evidenceWorkflowJobs": [],
        },
    }


class PointerResolutionTests(unittest.TestCase):
    def test_resolves_simple_dotted_path(self):
        data = {"openUsd": {"version": "26.05"}}
        self.assertEqual(gop.resolve_pointer(data, "openUsd.version"), "26.05")

    def test_resolves_list_filter_segment(self):
        data = {"dependencies": [
            {"name": "oneTBB", "version": "2021.12.0"},
            {"name": "MaterialX", "version": "1.39.4"},
        ]}
        self.assertEqual(
            gop.resolve_pointer(data, "dependencies[name=MaterialX].version"),
            "1.39.4",
        )

    def test_raises_when_filter_matches_zero_items(self):
        data = {"dependencies": [{"name": "oneTBB", "version": "1.0"}]}
        with self.assertRaises(ValueError):
            gop.resolve_pointer(data, "dependencies[name=Missing].version")

    def test_raises_when_filter_matches_multiple_items(self):
        data = {"dependencies": [
            {"name": "Dup", "version": "1.0"},
            {"name": "Dup", "version": "2.0"},
        ]}
        with self.assertRaises(ValueError):
            gop.resolve_pointer(data, "dependencies[name=Dup].version")

    def test_raises_for_missing_key(self):
        with self.assertRaises(ValueError):
            gop.resolve_pointer({}, "missing.key")


class SchemaValidationTests(unittest.TestCase):
    def setUp(self):
        self.tempdir = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.tempdir.name)
        (self.root / "lock.json").write_text(
            json.dumps({"openUsd": {"version": "26.05"}}), encoding="utf-8"
        )

    def tearDown(self):
        self.tempdir.cleanup()

    def test_valid_profile_has_no_errors(self):
        errors = gop.validate_profile(base_profile(), base_manifest(), self.root, "1.2.3")
        self.assertEqual(errors, [])

    def test_wrong_schema_version_is_rejected(self):
        profile = base_profile()
        profile["$schemaVersion"] = 2
        errors = gop.validate_profile(profile, base_manifest(), self.root, "1.2.3")
        self.assertTrue(any("schemaVersion" in error for error in errors))

    def test_runtime_version_mismatch_is_rejected(self):
        profile = base_profile()
        profile["runtimeVersion"] = "9.9.9"
        errors = gop.validate_profile(profile, base_manifest(), self.root, "1.2.3")
        self.assertTrue(any("runtimeVersion" in error for error in errors))

    def test_unknown_platform_is_rejected(self):
        profile = base_profile()
        profile["platforms"] = ["ios-arm64"]
        errors = gop.validate_profile(profile, base_manifest(), self.root, "1.2.3")
        self.assertTrue(any("unknown platforms" in error for error in errors))


class DependencyVersionSeparationTests(unittest.TestCase):
    def setUp(self):
        self.tempdir = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.tempdir.name)
        (self.root / "lock.json").write_text(
            json.dumps({
                "openUsd": {"version": "26.05"},
                "kitBaseline": {"version": "108.0"},
            }),
            encoding="utf-8",
        )

    def tearDown(self):
        self.tempdir.cleanup()

    def test_drifted_runtime_pinned_version_is_rejected(self):
        profile = base_profile()
        profile["dependencies"][0]["version"] = "99.99"
        errors, _ = gop.validate_dependencies(profile, self.root)
        self.assertTrue(any("drifted" in error for error in errors))

    def test_runtime_pinned_dependency_requires_version_source(self):
        profile = base_profile()
        profile["dependencies"][0]["versionSource"] = None
        errors, _ = gop.validate_dependencies(profile, self.root)
        self.assertTrue(any("requires a versionSource" in error for error in errors))

    def test_external_dependency_with_fabricated_version_and_no_source_is_rejected(self):
        profile = base_profile()
        profile["dependencies"][1]["version"] = "104.0"
        errors, _ = gop.validate_dependencies(profile, self.root)
        self.assertTrue(any("requires a versionSource" in error for error in errors))

    def test_external_dependency_pending_with_a_version_source_is_rejected(self):
        profile = base_profile()
        profile["dependencies"][1]["versionSource"] = {
            "file": "lock.json",
            "pointer": "kitBaseline.version",
        }
        errors, _ = gop.validate_dependencies(profile, self.root)
        self.assertTrue(any("must not declare a versionSource" in error for error in errors))

    def test_external_dependency_can_be_pinned_with_its_own_provenance_source(self):
        profile = base_profile()
        profile["dependencies"][1]["version"] = "108.0"
        profile["dependencies"][1]["versionSource"] = {
            "file": "lock.json",
            "pointer": "kitBaseline.version",
        }
        errors, _ = gop.validate_dependencies(profile, self.root)
        self.assertEqual(errors, [])

    def test_pinned_external_dependency_with_drifted_source_is_rejected(self):
        profile = base_profile()
        profile["dependencies"][1]["version"] = "999.0"
        profile["dependencies"][1]["versionSource"] = {
            "file": "lock.json",
            "pointer": "kitBaseline.version",
        }
        errors, _ = gop.validate_dependencies(profile, self.root)
        self.assertTrue(any("drifted" in error for error in errors))

    def test_pinned_external_dependency_may_coincidentally_equal_a_runtime_version(self):
        # Version-string equality alone must never be treated as an error: separation is
        # proven by independent ids/sources, not by inequality of version strings.
        profile = base_profile()
        profile["dependencies"][1]["version"] = "26.05"
        profile["dependencies"][1]["versionSource"] = {
            "file": "lock.json",
            "pointer": "openUsd.version",
        }
        errors, _ = gop.validate_dependencies(profile, self.root)
        self.assertEqual(errors, [])

    def test_duplicate_dependency_ids_are_rejected(self):
        profile = base_profile()
        profile["dependencies"].append(copy.deepcopy(profile["dependencies"][0]))
        errors, _ = gop.validate_dependencies(profile, self.root)
        self.assertTrue(any("duplicate dependency id" in error for error in errors))

    def test_invalid_dependency_kind_is_rejected(self):
        profile = base_profile()
        profile["dependencies"][0]["kind"] = "made-up-kind"
        errors, _ = gop.validate_dependencies(profile, self.root)
        self.assertTrue(any("invalid kind" in error for error in errors))


class ManifestCrossReferenceTests(unittest.TestCase):
    def setUp(self):
        self.tempdir = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.tempdir.name)
        (self.root / "lock.json").write_text(
            json.dumps({"openUsd": {"version": "26.05"}}), encoding="utf-8"
        )

    def tearDown(self):
        self.tempdir.cleanup()

    def test_missing_feature_entry_is_rejected(self):
        profile = base_profile()
        profile["features"][0]["manifestEntry"] = "does-not-exist"
        errors = gop.validate_profile(profile, base_manifest(), self.root, "1.2.3")
        self.assertTrue(any("no support-manifest entry" in error for error in errors))

    def test_feature_with_unreachable_status_is_rejected(self):
        manifest = base_manifest()
        manifest["areas"][0]["entries"][0]["status"] = "not-supported"
        profile = base_profile()
        errors = gop.validate_profile(profile, manifest, self.root, "1.2.3")
        self.assertTrue(any("expected one of" in error for error in errors))

    def test_exclusion_must_reference_excluded_unreachable_area(self):
        profile = base_profile()
        profile["permanentExclusions"][0]["manifestArea"] = "omniverse-interchange"
        errors = gop.validate_profile(profile, base_manifest(), self.root, "1.2.3")
        self.assertTrue(any("manifestArea must be" in error for error in errors))

    def test_exclusion_with_implemented_status_is_rejected(self):
        manifest = base_manifest()
        manifest["areas"][1]["entries"][0]["status"] = "excluded"
        del manifest["areas"][1]["entries"][0]["exclusionReason"]
        manifest["areas"][1]["entries"][0]["exclusionReason"] = "Now excluded, not unreachable."
        profile = base_profile()
        errors = gop.validate_profile(profile, manifest, self.root, "1.2.3")
        # "excluded" is still an allowed status for permanentExclusions, so this must pass.
        self.assertEqual(errors, [])


class KitExecutionEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.tempdir = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.tempdir.name)
        (self.root / ".github" / "workflows").mkdir(parents=True)

    def tearDown(self):
        self.tempdir.cleanup()

    def test_pending_with_declared_workflow_is_rejected(self):
        profile = base_profile()
        profile["kitExecutionEvidence"]["evidenceWorkflow"] = "kit.yml"
        errors = gop.validate_kit_execution_evidence(profile, self.root)
        self.assertTrue(any("must be null" in error for error in errors))

    def test_executed_without_existing_workflow_is_rejected(self):
        profile = base_profile()
        profile["kitExecutionEvidence"] = {
            "status": "executed",
            "description": "Kit round trip.",
            "evidenceWorkflow": "missing.yml",
            "evidenceWorkflowJobs": ["job"],
        }
        errors = gop.validate_kit_execution_evidence(profile, self.root)
        self.assertTrue(any("does not exist" in error for error in errors))

    def test_executed_with_real_workflow_and_job_passes(self):
        workflow_path = self.root / ".github" / "workflows" / "kit.yml"
        workflow_path.write_text("jobs:\n  kit-execution:\n    runs-on: ubuntu-latest\n",
                                  encoding="utf-8")
        profile = base_profile()
        profile["kitExecutionEvidence"] = {
            "status": "executed",
            "description": "Kit round trip.",
            "evidenceWorkflow": "kit.yml",
            "evidenceWorkflowJobs": ["kit-execution"],
        }
        errors = gop.validate_kit_execution_evidence(profile, self.root)
        self.assertEqual(errors, [])

    def test_executed_with_unknown_job_is_rejected(self):
        workflow_path = self.root / ".github" / "workflows" / "kit.yml"
        workflow_path.write_text("jobs:\n  kit-execution:\n    runs-on: ubuntu-latest\n",
                                  encoding="utf-8")
        profile = base_profile()
        profile["kitExecutionEvidence"] = {
            "status": "executed",
            "description": "Kit round trip.",
            "evidenceWorkflow": "kit.yml",
            "evidenceWorkflowJobs": ["does-not-exist"],
        }
        errors = gop.validate_kit_execution_evidence(profile, self.root)
        self.assertTrue(any("does not exist in" in error for error in errors))


class GeneratedDocumentDriftTests(unittest.TestCase):
    def test_document_changes_when_dependency_version_changes(self):
        manifest = base_manifest()
        first = gop.generate_document(base_profile(), manifest)
        changed_profile = base_profile()
        changed_profile["dependencies"][0]["version"] = "27.00"
        second = gop.generate_document(changed_profile, manifest)
        self.assertNotEqual(first, second)

    def test_document_is_deterministic_for_the_same_input(self):
        profile = base_profile()
        manifest = base_manifest()
        first = gop.generate_document(profile, manifest)
        second = gop.generate_document(profile, manifest)
        self.assertEqual(first, second)

    def test_document_reflects_live_manifest_status_not_a_cached_copy(self):
        profile = base_profile()
        manifest = base_manifest()
        manifest["areas"][0]["entries"][0]["status"] = "implemented"
        implemented_doc = gop.generate_document(profile, manifest)
        manifest["areas"][0]["entries"][0]["status"] = "workflow-gated"
        gated_doc = gop.generate_document(profile, manifest)
        self.assertIn("Implemented", implemented_doc)
        self.assertIn("Workflow-gated", gated_doc)


class RealRepositorySmokeTests(unittest.TestCase):
    """Sanity checks against the real, committed profile and manifest."""

    def test_committed_profile_is_valid_and_doc_is_up_to_date(self):
        profile = json.loads(GENERATOR.parent.joinpath(
            "omniverse-profile.json").read_text(encoding="utf-8"))
        manifest = gop.load_support_manifest(REPO_ROOT)
        expected_version = json.loads(
            (REPO_ROOT / "version.json").read_text(encoding="utf-8")
        )["version"]

        errors = gop.validate_profile(profile, manifest, REPO_ROOT, expected_version)
        self.assertEqual(errors, [])

        generated = gop.generate_document(profile, manifest)
        doc_path = REPO_ROOT / profile["generatedDocPath"]
        committed = doc_path.read_text(encoding="utf-8").replace("\r\n", "\n")
        self.assertEqual(committed, generated)

    def test_platform_vocabulary_matches_support_manifest_generator(self):
        support_manifest_module = load_module(
            "generate_support_manifest", SUPPORT_MANIFEST_GENERATOR
        )
        self.assertEqual(gop.VALID_PLATFORMS, support_manifest_module.VALID_PLATFORMS)


if __name__ == "__main__":
    unittest.main()
