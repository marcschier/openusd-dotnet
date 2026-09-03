# Copyright (c) marcschier. Licensed under the MIT License.
"""Regression tests for eng/generate-kit-companion-spec.py.

Run with:
    python -m unittest discover eng/tests

These tests exercise the Kit companion spec generator/validator against synthetic in-memory
spec documents and synthetic fixture repositories so they need no OpenUSD install and never
touch the real, committed eng/kit-companion-spec.json (except where explicitly noted as a
real-repository smoke check).
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
GENERATOR = REPO_ROOT / "eng" / "generate-kit-companion-spec.py"


def load_module(name: str, path: pathlib.Path):
    loader = importlib.machinery.SourceFileLoader(name, str(path))
    spec = importlib.util.spec_from_loader(loader.name, loader)
    module = importlib.util.module_from_spec(spec)
    sys.modules[loader.name] = module
    loader.exec_module(module)
    return module


gks = load_module("generate_kit_companion_spec", GENERATOR)

FAKE_BRIDGE_PROTOCOL_CS = """
namespace OpenUsd.Bridge.Protocol;
public static class BridgeProtocol
{
    public const string PackageName = "openusd.bridge.v1";
    public const string ServiceName = "openusd.bridge.v1.LiveBridge";
    public const int CurrentMajorVersion = 1;
    public const int CurrentMinorVersion = 0;
}
"""

FAKE_LIVE_AUTHORING_VALIDATION_CS = """
namespace OpenUsd.LiveAuthoring;
public static class LiveAuthoringValidation
{
    public const int MaxUpdatesPerBatch = 4096;
    public const int MaxCollectionElementCount = 65536;
    public const long MaxTotalCollectionElementCountPerBatch = 4_194_304;
    public const long MaxEstimatedBatchPayloadBytes = 16 * 1024 * 1024;
    public const int MaxReplayWindowLength = 4096;
    public const int DefaultReplayWindowLength = 64;
    public const int ReplayLedgerEntryBytes = 64;
}
"""

FAKE_WIRE_PROTO = """
message StageUpdate {
  oneof update {
    DefinePrim define_prim = 1;
    RemovePrim remove_prim = 2;
    SetAttribute set_attribute = 3;
  }
}
"""


def base_spec() -> dict:
    return {
        "$schemaVersion": 1,
        "generatedDocPath": "docs/omniverse-kit-companion-reference.g.md",
        "specStatus": "specification-only",
        "specStatusNote": "Test note.",
        "protocol": {
            "packageName": "openusd.bridge.v1",
            "serviceName": "openusd.bridge.v1.LiveBridge",
            "currentMajorVersion": 1,
            "currentMinorVersion": 0,
            "protoFiles": ["fake/wire.proto"],
            "maxUpdatesPerBatch": 4096,
            "maxCollectionElementCount": 65536,
            "maxTotalCollectionElementCountPerBatch": 4194304,
            "maxEstimatedBatchPayloadBytes": 16777216,
            "maxFrameOverheadBytes": 65536,
            "replayWindowDefaultLength": 64,
            "replayWindowMaxLength": 4096,
            "replayLedgerEntryBytes": 64,
        },
        "kitBaseline": {
            "profileFile": "eng/omniverse-profile.json",
            "dependencyId": "kit-baseline",
            "note": "Test note.",
        },
        "repositoryBoundary": {
            "thisRepository": {
                "owns": ["the wire contract"],
                "license": "MIT",
                "distribution": "NuGet.org",
            },
            "companionExtension": {
                "owns": ["omni.client"],
                "repository": "separate, not yet authorized",
                "license": "chosen independently",
                "distributionChannels": ["NGC"],
            },
            "boundaryStatement": "Test statement.",
        },
        "extensionManifestDependencies": [
            {"id": "omni.usd", "purpose": "test", "versionConstraint": "pending"},
        ],
        "pythonGrpcWorkflow": {
            "steps": ["Generate stubs."],
            "packageNegotiation": "Test negotiation.",
            "maxMessageOptions": {
                "grpcMaxSendMessageLength": "MaxFrameBytes",
                "grpcMaxReceiveMessageLength": "MaxFrameBytes",
            },
        },
        "serverBehavior": [
            {
                "rpc": rpc,
                "kind": "bidi-stream" if rpc == "StreamChanges" else "unary",
                "retrySafe": rpc in ("GetSnapshot", "GetStatus"),
                "preconditions": ["test"],
                "steps": ["test step"],
                "failureModes": ["TEST_FAILURE"],
            }
            for rpc in gks.REQUIRED_RPCS
        ],
        "idempotencyLedger": {
            "keySource": "idempotency_key",
            "windowDefaultLength": 64,
            "windowMaxLength": 4096,
            "entryBytes": 64,
            "maxLedgerBytes": 262144,
            "rules": ["test rule"],
        },
        "updateMapping": [
            {"authoringType": "DefinePrimUpdate", "wireCase": "StageUpdate.define_prim"},
            {"authoringType": "RemovePrimUpdate", "wireCase": "StageUpdate.remove_prim"},
            {
                "authoringType": "SetAttributeUpdate",
                "wireCase": "StageUpdate.set_attribute",
                "notes": "Default value vs. time sample test note.",
            },
            {"authoringType": "ReplaceBridgeOverlayUpdate", "wireCase": "No delta case"},
        ],
        "unsupportedEdits": [
            {"kitEdit": "rename", "reason": "no rename case"},
        ],
        "scoping": {
            "bridgeRootRule": "test",
            "layerRule": "test",
            "mergeRule": "test",
        },
        "executionModes": {
            "headlessKitService": {"description": "test", "healthEndpoints": ["GetStatus"]},
            "guiExtension": {"description": "test", "healthEndpoints": ["GetStatus"]},
        },
        "observability": {
            "logging": ["test"],
            "metrics": ["test"],
            "credentialRedaction": ["test"],
        },
        "installation": [
            {"channel": channel, "status": "not-yet-published", "steps": ["test"]}
            for channel in gks.INSTALLATION_CHANNELS
        ],
        "acceptanceMatrix": [
            {
                "id": acceptance_id,
                "title": acceptance_id,
                "method": "test method",
                "passCriteria": "test criteria",
            }
            for acceptance_id in sorted(gks.REQUIRED_ACCEPTANCE_IDS)
        ],
        "exampleArtifacts": [
            {"path": "fake/extension.toml", "kind": "sketch", "disclaimer": "not installable"},
        ],
        "docLinks": [
            {"title": "Bridge", "path": "fake/bridge.md"},
        ],
    }


class ShapeValidationTests(unittest.TestCase):
    def test_valid_spec_has_no_shape_errors(self):
        self.assertEqual(gks.validate_spec_shape(base_spec()), [])

    def test_wrong_schema_version_is_rejected(self):
        spec = base_spec()
        spec["$schemaVersion"] = 2
        errors = gks.validate_spec_shape(spec)
        self.assertTrue(any("schemaVersion" in error for error in errors))

    def test_wrong_spec_status_is_rejected(self):
        spec = base_spec()
        spec["specStatus"] = "implemented"
        errors = gks.validate_spec_shape(spec)
        self.assertTrue(any("specStatus" in error for error in errors))

    def test_missing_rpc_is_rejected(self):
        spec = base_spec()
        spec["serverBehavior"] = [
            entry for entry in spec["serverBehavior"] if entry["rpc"] != "Negotiate"
        ]
        errors = gks.validate_spec_shape(spec)
        self.assertTrue(any("Negotiate" in error for error in errors))

    def test_missing_installation_channel_is_rejected(self):
        spec = base_spec()
        spec["installation"] = [
            entry for entry in spec["installation"] if entry["channel"] != "ngc"
        ]
        errors = gks.validate_spec_shape(spec)
        self.assertTrue(any("ngc" in error for error in errors))

    def test_installation_status_must_be_not_yet_published(self):
        spec = base_spec()
        spec["installation"][0]["status"] = "published"
        errors = gks.validate_spec_shape(spec)
        self.assertTrue(any("not-yet-published" in error for error in errors))

    def test_extension_dependency_version_must_be_pending(self):
        spec = base_spec()
        spec["extensionManifestDependencies"][0]["versionConstraint"] = "1.0.0"
        errors = gks.validate_spec_shape(spec)
        self.assertTrue(any("versionConstraint" in error for error in errors))

    def test_acceptance_matrix_missing_required_id_is_rejected(self):
        spec = base_spec()
        spec["acceptanceMatrix"] = spec["acceptanceMatrix"][:-1]
        errors = gks.validate_spec_shape(spec)
        self.assertTrue(any("missing required id" in error for error in errors))

    def test_acceptance_matrix_extra_id_is_rejected(self):
        spec = base_spec()
        spec["acceptanceMatrix"].append(
            {
                "id": "made-up-id",
                "title": "x",
                "method": "x",
                "passCriteria": "x",
            }
        )
        errors = gks.validate_spec_shape(spec)
        self.assertTrue(any("not in the approved plan" in error for error in errors))

    def test_acceptance_matrix_duplicate_id_is_rejected(self):
        spec = base_spec()
        spec["acceptanceMatrix"].append(copy.deepcopy(spec["acceptanceMatrix"][0]))
        errors = gks.validate_spec_shape(spec)
        self.assertTrue(any("duplicate id" in error for error in errors))

    def test_kit_baseline_wrong_dependency_id_is_rejected(self):
        spec = base_spec()
        spec["kitBaseline"]["dependencyId"] = "wrong-id"
        errors = gks.validate_spec_shape(spec)
        self.assertTrue(any("dependencyId" in error for error in errors))


class ReferencedPathsTests(unittest.TestCase):
    def setUp(self):
        self.tempdir = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.tempdir.name)
        (self.root / "fake").mkdir()
        (self.root / "fake" / "wire.proto").write_text("syntax = \"proto3\";", encoding="utf-8")
        (self.root / "fake" / "extension.toml").write_text("[package]", encoding="utf-8")
        (self.root / "fake" / "bridge.md").write_text("# Bridge", encoding="utf-8")

    def tearDown(self):
        self.tempdir.cleanup()

    def test_existing_paths_have_no_errors(self):
        self.assertEqual(gks.validate_referenced_paths(base_spec(), self.root), [])

    def test_missing_proto_file_is_rejected(self):
        spec = base_spec()
        spec["protocol"]["protoFiles"] = ["fake/does-not-exist.proto"]
        errors = gks.validate_referenced_paths(spec, self.root)
        self.assertTrue(any("does not exist" in error for error in errors))

    def test_missing_example_artifact_is_rejected(self):
        spec = base_spec()
        spec["exampleArtifacts"][0]["path"] = "fake/missing.py"
        errors = gks.validate_referenced_paths(spec, self.root)
        self.assertTrue(any("does not exist" in error for error in errors))

    def test_absolute_path_is_rejected(self):
        spec = base_spec()
        spec["docLinks"][0]["path"] = "/etc/passwd"
        errors = gks.validate_referenced_paths(spec, self.root)
        self.assertTrue(any("repository-relative" in error for error in errors))


class BridgeProtocolCrossCheckTests(unittest.TestCase):
    def setUp(self):
        self.tempdir = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.tempdir.name)
        (self.root / "src" / "OpenUsd.Bridge.Protocol").mkdir(parents=True)
        (self.root / "src" / "OpenUsd.LiveAuthoring").mkdir(parents=True)
        (self.root / "src" / "OpenUsd.Bridge.Protocol" / "BridgeProtocol.cs").write_text(
            FAKE_BRIDGE_PROTOCOL_CS, encoding="utf-8"
        )
        (self.root / "src" / "OpenUsd.LiveAuthoring" / "LiveAuthoringValidation.cs").write_text(
            FAKE_LIVE_AUTHORING_VALIDATION_CS, encoding="utf-8"
        )

    def tearDown(self):
        self.tempdir.cleanup()

    def test_matching_protocol_facts_have_no_errors(self):
        self.assertEqual(gks.validate_against_bridge_protocol(base_spec(), self.root), [])

    def test_drifted_major_version_is_rejected(self):
        spec = base_spec()
        spec["protocol"]["currentMajorVersion"] = 2
        errors = gks.validate_against_bridge_protocol(spec, self.root)
        self.assertTrue(any("CurrentMajorVersion" in error for error in errors))

    def test_drifted_package_name_is_rejected(self):
        spec = base_spec()
        spec["protocol"]["packageName"] = "wrong.package"
        errors = gks.validate_against_bridge_protocol(spec, self.root)
        self.assertTrue(any("PackageName" in error for error in errors))

    def test_drifted_max_updates_per_batch_is_rejected(self):
        spec = base_spec()
        spec["protocol"]["maxUpdatesPerBatch"] = 1
        errors = gks.validate_against_bridge_protocol(spec, self.root)
        self.assertTrue(any("MaxUpdatesPerBatch" in error for error in errors))

    def test_drifted_max_estimated_batch_payload_bytes_is_rejected(self):
        spec = base_spec()
        spec["protocol"]["maxEstimatedBatchPayloadBytes"] = 1
        errors = gks.validate_against_bridge_protocol(spec, self.root)
        self.assertTrue(
            any("MaxEstimatedBatchPayloadBytes" in error for error in errors)
        )

    def test_missing_bridge_protocol_file_is_reported(self):
        (self.root / "src" / "OpenUsd.Bridge.Protocol" / "BridgeProtocol.cs").unlink()
        errors = gks.validate_against_bridge_protocol(base_spec(), self.root)
        self.assertTrue(any("missing" in error for error in errors))


class WireProtoCrossCheckTests(unittest.TestCase):
    """Exercises validate_against_wire_proto, which cross-checks updateMapping against the
    real StageUpdate oneof cases in wire.proto instead of trusting a prose case count."""

    def setUp(self):
        self.tempdir = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.tempdir.name)
        proto_dir = self.root / "src" / "OpenUsd.Bridge.Protocol" / "protos" / "openusd" / \
            "bridge" / "v1"
        proto_dir.mkdir(parents=True)
        (proto_dir / "wire.proto").write_text(FAKE_WIRE_PROTO, encoding="utf-8")

    def tearDown(self):
        self.tempdir.cleanup()

    def test_matching_update_mapping_has_no_errors(self):
        self.assertEqual(gks.validate_against_wire_proto(base_spec(), self.root), [])

    def test_missing_wire_case_is_rejected(self):
        spec = base_spec()
        spec["updateMapping"] = [
            entry for entry in spec["updateMapping"]
            if entry["authoringType"] != "RemovePrimUpdate"
        ]
        errors = gks.validate_against_wire_proto(spec, self.root)
        self.assertTrue(any("missing a wire case" in error and "remove_prim" in error
                             for error in errors))

    def test_extra_wire_case_is_rejected(self):
        spec = base_spec()
        spec["updateMapping"].append(
            {"authoringType": "MadeUpUpdate", "wireCase": "StageUpdate.made_up_case"}
        )
        errors = gks.validate_against_wire_proto(spec, self.root)
        self.assertTrue(any("not in wire.proto's StageUpdate oneof" in error for error in errors))

    def test_duplicate_wire_case_is_rejected(self):
        spec = base_spec()
        spec["updateMapping"].append(copy.deepcopy(spec["updateMapping"][0]))
        errors = gks.validate_against_wire_proto(spec, self.root)
        self.assertTrue(any("more than one entry to the same wire case" in error
                             for error in errors))

    def test_missing_no_wire_case_entry_is_rejected(self):
        spec = base_spec()
        spec["updateMapping"] = [
            entry for entry in spec["updateMapping"]
            if entry["authoringType"] != "ReplaceBridgeOverlayUpdate"
        ]
        errors = gks.validate_against_wire_proto(spec, self.root)
        self.assertTrue(any("exactly one entry with no wire case" in error for error in errors))

    def test_extra_no_wire_case_entry_is_rejected(self):
        spec = base_spec()
        spec["updateMapping"].append(
            {"authoringType": "AnotherNoWireCaseUpdate", "wireCase": "Also no delta case"}
        )
        errors = gks.validate_against_wire_proto(spec, self.root)
        self.assertTrue(any("exactly one entry with no wire case" in error for error in errors))

    def test_missing_wire_proto_file_is_reported(self):
        (self.root / "src" / "OpenUsd.Bridge.Protocol" / "protos" / "openusd" / "bridge" /
         "v1" / "wire.proto").unlink()
        errors = gks.validate_against_wire_proto(base_spec(), self.root)
        self.assertTrue(any("missing" in error for error in errors))


class OmniverseProfileCrossCheckTests(unittest.TestCase):
    def setUp(self):
        self.tempdir = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.tempdir.name)
        (self.root / "eng").mkdir()

    def tearDown(self):
        self.tempdir.cleanup()

    def _write_profile(self, dependencies: list[dict]) -> None:
        (self.root / "eng" / "omniverse-profile.json").write_text(
            json.dumps({"dependencies": dependencies}), encoding="utf-8"
        )

    def test_valid_kit_baseline_dependency_has_no_errors(self):
        self._write_profile([{"id": "kit-baseline", "kind": "external-optional"}])
        self.assertEqual(gks.validate_against_omniverse_profile(base_spec(), self.root), [])

    def test_missing_kit_baseline_dependency_is_rejected(self):
        self._write_profile([{"id": "openusd", "kind": "runtime-pinned"}])
        errors = gks.validate_against_omniverse_profile(base_spec(), self.root)
        self.assertTrue(any("must name exactly one dependency" in error for error in errors))

    def test_kit_baseline_wrong_kind_is_rejected(self):
        self._write_profile([{"id": "kit-baseline", "kind": "runtime-pinned"}])
        errors = gks.validate_against_omniverse_profile(base_spec(), self.root)
        self.assertTrue(any("external-optional" in error for error in errors))

    def test_duplicate_kit_baseline_dependency_is_rejected(self):
        self._write_profile([
            {"id": "kit-baseline", "kind": "external-optional"},
            {"id": "kit-baseline", "kind": "external-optional"},
        ])
        errors = gks.validate_against_omniverse_profile(base_spec(), self.root)
        self.assertTrue(any("must name exactly one dependency" in error for error in errors))


class GeneratedDocumentDriftTests(unittest.TestCase):
    def test_document_is_deterministic_for_the_same_input(self):
        spec = base_spec()
        first = gks.generate_document(spec)
        second = gks.generate_document(spec)
        self.assertEqual(first, second)

    def test_document_changes_when_acceptance_matrix_changes(self):
        first = gks.generate_document(base_spec())
        changed = base_spec()
        changed["acceptanceMatrix"][0]["title"] = "Something else entirely"
        second = gks.generate_document(changed)
        self.assertNotEqual(first, second)

    def test_document_links_are_relative_to_docs_directory(self):
        spec = base_spec()
        spec["docLinks"] = [{"title": "Profile", "path": "eng/omniverse-profile.json"}]
        spec["exampleArtifacts"] = [
            {
                "path": "docs/examples/omniverse-kit-companion/extension.toml",
                "kind": "sketch",
                "disclaimer": "not installable",
            }
        ]
        document = gks.generate_document(spec)
        self.assertIn("(../eng/omniverse-profile.json)", document)
        self.assertIn("(examples/omniverse-kit-companion/extension.toml)", document)


class RealRepositorySmokeTests(unittest.TestCase):
    """Sanity checks against the real, committed spec, schema, and generated document."""

    def test_committed_spec_is_valid_and_doc_is_up_to_date(self):
        spec = json.loads(
            (REPO_ROOT / "eng" / "kit-companion-spec.json").read_text(encoding="utf-8")
        )
        self.assertTrue((REPO_ROOT / "eng" / "kit-companion-spec.schema.json").exists())

        errors = gks.validate_spec(spec, REPO_ROOT)
        self.assertEqual(errors, [])

        generated = gks.generate_document(spec)
        doc_path = REPO_ROOT / spec["generatedDocPath"]
        committed = gks.read_text(doc_path)
        self.assertEqual(committed, generated)

    def test_acceptance_matrix_matches_the_approved_plan_exactly(self):
        spec = json.loads(
            (REPO_ROOT / "eng" / "kit-companion-spec.json").read_text(encoding="utf-8")
        )
        actual_ids = frozenset(entry["id"] for entry in spec["acceptanceMatrix"])
        self.assertEqual(actual_ids, gks.REQUIRED_ACCEPTANCE_IDS)

    def test_spec_json_never_claims_atomic_or_durable_kit_transactions(self):
        text = (REPO_ROOT / "eng" / "kit-companion-spec.json").read_text(encoding="utf-8")
        self.assertNotIn("as one atomic transaction", text)
        self.assertNotIn("durably commits", text)

    def test_companion_doc_never_claims_atomic_or_durable_kit_transactions(self):
        text = (REPO_ROOT / "docs" / "omniverse-kit-companion.md").read_text(encoding="utf-8")
        self.assertNotIn("one atomic transaction", text)
        self.assertNotIn("durably commits", text)

    def test_spec_json_does_not_claim_fourteen_stage_update_cases(self):
        text = (REPO_ROOT / "eng" / "kit-companion-spec.json").read_text(encoding="utf-8")
        self.assertNotIn("fourteen", text.lower())

    def test_set_attribute_update_mapping_documents_time_sample_support(self):
        spec = json.loads(
            (REPO_ROOT / "eng" / "kit-companion-spec.json").read_text(encoding="utf-8")
        )
        entry = next(
            item for item in spec["updateMapping"]
            if item["authoringType"] == "SetAttributeUpdate"
        )
        self.assertIn("time_code", entry.get("notes", ""))
        self.assertIn("time sample", entry.get("notes", ""))

    def test_get_snapshot_never_silently_adopts_a_different_epoch(self):
        spec = json.loads(
            (REPO_ROOT / "eng" / "kit-companion-spec.json").read_text(encoding="utf-8")
        )
        get_snapshot = next(
            entry for entry in spec["serverBehavior"] if entry["rpc"] == "GetSnapshot"
        )
        steps_text = " ".join(get_snapshot["steps"])
        self.assertIn("fail the call", steps_text.lower())
        self.assertNotIn("current epoch's own snapshot", steps_text.lower())
        self.assertTrue(
            any("EPOCH_ADVANCED" in mode for mode in get_snapshot["failureModes"])
        )

    def test_publish_local_batch_never_replays_the_original_applied_acknowledgement(self):
        spec = json.loads(
            (REPO_ROOT / "eng" / "kit-companion-spec.json").read_text(encoding="utf-8")
        )
        publish = next(
            entry for entry in spec["serverBehavior"] if entry["rpc"] == "PublishLocalBatch"
        )
        steps_text = " ".join(publish["steps"])
        self.assertIn("form and return a new acknowledgement", steps_text.lower())
        self.assertNotIn("return the ledger's recorded acknowledgement unchanged", steps_text.lower())

        ledger_rules_text = " ".join(spec["idempotencyLedger"]["rules"])
        self.assertNotIn("returns the original acknowledgement", ledger_rules_text.lower())
        self.assertIn("never returns the first", ledger_rules_text.lower())

    def test_get_status_requires_accurate_counters_not_omission(self):
        spec = json.loads(
            (REPO_ROOT / "eng" / "kit-companion-spec.json").read_text(encoding="utf-8")
        )
        get_status = next(
            entry for entry in spec["serverBehavior"] if entry["rpc"] == "GetStatus"
        )
        steps_text = " ".join(get_status["steps"])
        self.assertIn("non-optional scalar", steps_text.lower())
        self.assertIn("track and report", steps_text.lower())
        self.assertNotIn("is omitted rather than", steps_text.lower())

    def test_example_extension_toml_is_not_a_valid_semantic_version_manifest(self):
        extension_toml = (
            REPO_ROOT
            / "docs"
            / "examples"
            / "omniverse-kit-companion"
            / "extension.toml"
        ).read_text(encoding="utf-8")
        self.assertIn('version = "pending"', extension_toml)

    def test_example_companion_service_refuses_to_run(self):
        companion_service = (
            REPO_ROOT
            / "docs"
            / "examples"
            / "omniverse-kit-companion"
            / "companion_service.py"
        ).read_text(encoding="utf-8")
        self.assertIn('if __name__ == "__main__":', companion_service)
        self.assertIn("raise RuntimeError", companion_service)

    def test_docs_readme_links_the_companion_specification(self):
        readme = (REPO_ROOT / "docs" / "README.md").read_text(encoding="utf-8")
        self.assertIn("omniverse-kit-companion.md", readme)

    def test_omniverse_bridge_doc_links_the_companion_specification(self):
        bridge_doc = (REPO_ROOT / "docs" / "omniverse-bridge.md").read_text(encoding="utf-8")
        self.assertIn("omniverse-kit-companion.md", bridge_doc)


if __name__ == "__main__":
    unittest.main()
