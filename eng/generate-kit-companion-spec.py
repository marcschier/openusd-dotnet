# Copyright (c) marcschier. Licensed under the MIT License.
"""Generates or verifies docs/omniverse-kit-companion-reference.g.md from
eng/kit-companion-spec.json.

eng/kit-companion-spec.json is the machine-readable source behind
docs/omniverse-kit-companion.md, a specification for a separately owned and separately
distributed Omniverse Kit extension. No such extension exists in, or is authorized for, this
repository: this script and the document it generates describe a contract, not an
implementation.

Validation performed (both modes):
  - The spec matches the shape declared by eng/kit-companion-spec.schema.json (enforced here
    by hand, without an external JSON Schema library, exactly like every other generator in
    this repository).
  - protocol.* cannot drift from the real contract: it is cross-checked by regex against
    src/OpenUsd.Bridge.Protocol/BridgeProtocol.cs and
    src/OpenUsd.LiveAuthoring/LiveAuthoringValidation.cs.
  - kitBaseline references a real "kit-baseline" dependency in eng/omniverse-profile.json.
  - acceptanceMatrix carries exactly the fixed set of ids the approved plan requires -- no
    more, no fewer, no renamed id.
  - Every referenced file (protoFiles, exampleArtifacts, docLinks) exists in this repository.

Usage:
    python eng/generate-kit-companion-spec.py            # generate the document
    python eng/generate-kit-companion-spec.py --verify   # fail if stale or invalid
"""

from __future__ import annotations

import argparse
import difflib
import json
import pathlib
import re
import sys


SPEC_PATH = pathlib.Path("eng/kit-companion-spec.json")
SCHEMA_PATH = pathlib.Path("eng/kit-companion-spec.schema.json")
BRIDGE_PROTOCOL_PATH = pathlib.Path("src/OpenUsd.Bridge.Protocol/BridgeProtocol.cs")
LIVE_AUTHORING_VALIDATION_PATH = pathlib.Path(
    "src/OpenUsd.LiveAuthoring/LiveAuthoringValidation.cs"
)
WIRE_PROTO_PATH = pathlib.Path(
    "src/OpenUsd.Bridge.Protocol/protos/openusd/bridge/v1/wire.proto"
)
OMNIVERSE_PROFILE_PATH = pathlib.Path("eng/omniverse-profile.json")
GENERATED_DOC_PATH = "docs/omniverse-kit-companion-reference.g.md"

# The fixed acceptance-matrix ids the approved plan requires. Exactly this set, no more, no
# fewer: adding, removing, or renaming an id here is a change to the accepted plan, not a
# routine spec edit.
REQUIRED_ACCEPTANCE_IDS = frozenset({
    "round-trip",
    "join-leave",
    "ordered-1000-edits",
    "time-sample-authoring",
    "epoch-change-renegotiation",
    "reverse-edits",
    "backpressure",
    "restart-resync",
    "auth-version-mismatch",
    "independent-upgrades",
    "license-isolation",
})

REQUIRED_RPCS = ("Negotiate", "GetSnapshot", "GetStatus", "PublishLocalBatch", "StreamChanges")

INSTALLATION_CHANNELS = ("ngc", "extension-manager", "local-development")


def read_text(path: pathlib.Path) -> str:
    return path.read_text(encoding="utf-8").replace("\r\n", "\n")


def write_text(path: pathlib.Path, value: str) -> None:
    path.write_text(value, encoding="utf-8", newline="\n")


def unified_diff(path: pathlib.Path, current: str, generated: str) -> str:
    return "".join(difflib.unified_diff(
        current.splitlines(keepends=True),
        generated.splitlines(keepends=True),
        fromfile=str(path),
        tofile=f"{path} (generated)",
        lineterm="\n",
    ))


def _require(errors: list[str], condition: bool, message: str) -> None:
    if not condition:
        errors.append(message)


def _require_nonempty_str(errors: list[str], value: object, field: str) -> bool:
    ok = isinstance(value, str) and value.strip() != ""
    if not ok:
        errors.append(f"{field}: requires a non-empty string")
    return ok


def _require_str_list(errors: list[str], value: object, field: str) -> None:
    if not isinstance(value, list) or not value:
        errors.append(f"{field}: requires a non-empty list")
        return
    for index, item in enumerate(value):
        if not isinstance(item, str) or not item.strip():
            errors.append(f"{field}[{index}]: requires a non-empty string")


def validate_spec_shape(spec: dict) -> list[str]:
    """Validates eng/kit-companion-spec.json against the shape declared by
    eng/kit-companion-spec.schema.json. Hand-rolled, mirroring how every other generator in
    this repository validates its own input, so the repository never adds a dependency on an
    external JSON Schema library just for this one file."""
    errors: list[str] = []

    if spec.get("$schemaVersion") != 1:
        errors.append("spec $schemaVersion must be 1")
    if spec.get("generatedDocPath") != GENERATED_DOC_PATH:
        errors.append(f"spec generatedDocPath must be '{GENERATED_DOC_PATH}'")
    if spec.get("specStatus") != "specification-only":
        errors.append("spec specStatus must be 'specification-only'")
    _require_nonempty_str(errors, spec.get("specStatusNote"), "specStatusNote")

    protocol = spec.get("protocol")
    if not isinstance(protocol, dict):
        errors.append("protocol: requires an object")
    else:
        _require_nonempty_str(errors, protocol.get("packageName"), "protocol.packageName")
        _require_nonempty_str(errors, protocol.get("serviceName"), "protocol.serviceName")
        for field in (
            "currentMajorVersion", "currentMinorVersion", "maxUpdatesPerBatch",
            "maxCollectionElementCount", "maxTotalCollectionElementCountPerBatch",
            "maxEstimatedBatchPayloadBytes", "maxFrameOverheadBytes",
            "replayWindowDefaultLength", "replayWindowMaxLength", "replayLedgerEntryBytes",
        ):
            value = protocol.get(field)
            if not isinstance(value, int) or isinstance(value, bool) or value < 0:
                errors.append(f"protocol.{field}: requires a non-negative integer")
        _require_str_list(errors, protocol.get("protoFiles"), "protocol.protoFiles")

    kit_baseline = spec.get("kitBaseline")
    if not isinstance(kit_baseline, dict):
        errors.append("kitBaseline: requires an object")
    else:
        if kit_baseline.get("profileFile") != "eng/omniverse-profile.json":
            errors.append("kitBaseline.profileFile must be 'eng/omniverse-profile.json'")
        if kit_baseline.get("dependencyId") != "kit-baseline":
            errors.append("kitBaseline.dependencyId must be 'kit-baseline'")
        _require_nonempty_str(errors, kit_baseline.get("note"), "kitBaseline.note")

    boundary = spec.get("repositoryBoundary")
    if not isinstance(boundary, dict):
        errors.append("repositoryBoundary: requires an object")
    else:
        this_repo = boundary.get("thisRepository")
        companion = boundary.get("companionExtension")
        if not isinstance(this_repo, dict):
            errors.append("repositoryBoundary.thisRepository: requires an object")
        else:
            _require_str_list(errors, this_repo.get("owns"), "repositoryBoundary.thisRepository.owns")
            _require_nonempty_str(
                errors, this_repo.get("license"), "repositoryBoundary.thisRepository.license"
            )
            _require_nonempty_str(
                errors,
                this_repo.get("distribution"),
                "repositoryBoundary.thisRepository.distribution",
            )
        if not isinstance(companion, dict):
            errors.append("repositoryBoundary.companionExtension: requires an object")
        else:
            _require_str_list(
                errors, companion.get("owns"), "repositoryBoundary.companionExtension.owns"
            )
            _require_nonempty_str(
                errors,
                companion.get("repository"),
                "repositoryBoundary.companionExtension.repository",
            )
            _require_nonempty_str(
                errors, companion.get("license"), "repositoryBoundary.companionExtension.license"
            )
            _require_str_list(
                errors,
                companion.get("distributionChannels"),
                "repositoryBoundary.companionExtension.distributionChannels",
            )
        _require_nonempty_str(
            errors, boundary.get("boundaryStatement"), "repositoryBoundary.boundaryStatement"
        )

    dependencies = spec.get("extensionManifestDependencies")
    if not isinstance(dependencies, list) or not dependencies:
        errors.append("extensionManifestDependencies: requires a non-empty list")
    else:
        for index, dependency in enumerate(dependencies):
            prefix = f"extensionManifestDependencies[{index}]"
            if not isinstance(dependency, dict):
                errors.append(f"{prefix}: requires an object")
                continue
            _require_nonempty_str(errors, dependency.get("id"), f"{prefix}.id")
            _require_nonempty_str(errors, dependency.get("purpose"), f"{prefix}.purpose")
            if dependency.get("versionConstraint") != "pending":
                errors.append(f"{prefix}.versionConstraint must be 'pending'")

    workflow = spec.get("pythonGrpcWorkflow")
    if not isinstance(workflow, dict):
        errors.append("pythonGrpcWorkflow: requires an object")
    else:
        _require_str_list(errors, workflow.get("steps"), "pythonGrpcWorkflow.steps")
        _require_nonempty_str(
            errors, workflow.get("packageNegotiation"), "pythonGrpcWorkflow.packageNegotiation"
        )
        options = workflow.get("maxMessageOptions")
        if not isinstance(options, dict):
            errors.append("pythonGrpcWorkflow.maxMessageOptions: requires an object")
        else:
            _require_nonempty_str(
                errors,
                options.get("grpcMaxSendMessageLength"),
                "pythonGrpcWorkflow.maxMessageOptions.grpcMaxSendMessageLength",
            )
            _require_nonempty_str(
                errors,
                options.get("grpcMaxReceiveMessageLength"),
                "pythonGrpcWorkflow.maxMessageOptions.grpcMaxReceiveMessageLength",
            )

    server_behavior = spec.get("serverBehavior")
    if not isinstance(server_behavior, list):
        errors.append("serverBehavior: requires a list")
    else:
        seen_rpcs = [entry.get("rpc") for entry in server_behavior if isinstance(entry, dict)]
        for rpc in REQUIRED_RPCS:
            if rpc not in seen_rpcs:
                errors.append(f"serverBehavior: missing an entry for rpc '{rpc}'")
        for index, entry in enumerate(server_behavior):
            prefix = f"serverBehavior[{index}]"
            if not isinstance(entry, dict):
                errors.append(f"{prefix}: requires an object")
                continue
            if entry.get("rpc") not in REQUIRED_RPCS:
                errors.append(f"{prefix}.rpc: must be one of {', '.join(REQUIRED_RPCS)}")
            if entry.get("kind") not in ("unary", "bidi-stream"):
                errors.append(f"{prefix}.kind: must be 'unary' or 'bidi-stream'")
            if not isinstance(entry.get("retrySafe"), bool):
                errors.append(f"{prefix}.retrySafe: requires a boolean")
            _require_str_list(errors, entry.get("steps"), f"{prefix}.steps")

    ledger = spec.get("idempotencyLedger")
    if not isinstance(ledger, dict):
        errors.append("idempotencyLedger: requires an object")
    else:
        _require_nonempty_str(errors, ledger.get("keySource"), "idempotencyLedger.keySource")
        _require_str_list(errors, ledger.get("rules"), "idempotencyLedger.rules")

    update_mapping = spec.get("updateMapping")
    if not isinstance(update_mapping, list) or not update_mapping:
        errors.append("updateMapping: requires a non-empty list")
    else:
        for index, entry in enumerate(update_mapping):
            prefix = f"updateMapping[{index}]"
            if not isinstance(entry, dict):
                errors.append(f"{prefix}: requires an object")
                continue
            _require_nonempty_str(errors, entry.get("authoringType"), f"{prefix}.authoringType")
            _require_nonempty_str(errors, entry.get("wireCase"), f"{prefix}.wireCase")
            if "notes" in entry:
                _require_nonempty_str(errors, entry.get("notes"), f"{prefix}.notes")

    unsupported_edits = spec.get("unsupportedEdits")
    if not isinstance(unsupported_edits, list) or not unsupported_edits:
        errors.append("unsupportedEdits: requires a non-empty list")
    else:
        for index, entry in enumerate(unsupported_edits):
            prefix = f"unsupportedEdits[{index}]"
            if not isinstance(entry, dict):
                errors.append(f"{prefix}: requires an object")
                continue
            _require_nonempty_str(errors, entry.get("kitEdit"), f"{prefix}.kitEdit")
            _require_nonempty_str(errors, entry.get("reason"), f"{prefix}.reason")

    scoping = spec.get("scoping")
    if not isinstance(scoping, dict):
        errors.append("scoping: requires an object")
    else:
        for field in ("bridgeRootRule", "layerRule", "mergeRule"):
            _require_nonempty_str(errors, scoping.get(field), f"scoping.{field}")

    modes = spec.get("executionModes")
    if not isinstance(modes, dict):
        errors.append("executionModes: requires an object")
    else:
        for mode_name in ("headlessKitService", "guiExtension"):
            mode = modes.get(mode_name)
            if not isinstance(mode, dict):
                errors.append(f"executionModes.{mode_name}: requires an object")
                continue
            _require_nonempty_str(
                errors, mode.get("description"), f"executionModes.{mode_name}.description"
            )
            _require_str_list(
                errors, mode.get("healthEndpoints"), f"executionModes.{mode_name}.healthEndpoints"
            )

    observability = spec.get("observability")
    if not isinstance(observability, dict):
        errors.append("observability: requires an object")
    else:
        for field in ("logging", "metrics", "credentialRedaction"):
            _require_str_list(errors, observability.get(field), f"observability.{field}")

    installation = spec.get("installation")
    if not isinstance(installation, list) or not installation:
        errors.append("installation: requires a non-empty list")
    else:
        seen_channels = [entry.get("channel") for entry in installation if isinstance(entry, dict)]
        for channel in INSTALLATION_CHANNELS:
            if channel not in seen_channels:
                errors.append(f"installation: missing an entry for channel '{channel}'")
        for index, entry in enumerate(installation):
            prefix = f"installation[{index}]"
            if not isinstance(entry, dict):
                errors.append(f"{prefix}: requires an object")
                continue
            if entry.get("channel") not in INSTALLATION_CHANNELS:
                errors.append(f"{prefix}.channel: must be one of {', '.join(INSTALLATION_CHANNELS)}")
            if entry.get("status") != "not-yet-published":
                errors.append(f"{prefix}.status must be 'not-yet-published'")
            _require_str_list(errors, entry.get("steps"), f"{prefix}.steps")

    acceptance_matrix = spec.get("acceptanceMatrix")
    if not isinstance(acceptance_matrix, list) or not acceptance_matrix:
        errors.append("acceptanceMatrix: requires a non-empty list")
    else:
        ids: list[str] = []
        for index, entry in enumerate(acceptance_matrix):
            prefix = f"acceptanceMatrix[{index}]"
            if not isinstance(entry, dict):
                errors.append(f"{prefix}: requires an object")
                continue
            entry_id = entry.get("id")
            if _require_nonempty_str(errors, entry_id, f"{prefix}.id"):
                ids.append(entry_id)
            _require_nonempty_str(errors, entry.get("title"), f"{prefix}.title")
            _require_nonempty_str(errors, entry.get("method"), f"{prefix}.method")
            _require_nonempty_str(errors, entry.get("passCriteria"), f"{prefix}.passCriteria")
        duplicate_ids = {entry_id for entry_id in ids if ids.count(entry_id) > 1}
        if duplicate_ids:
            errors.append(f"acceptanceMatrix: duplicate id(s): {', '.join(sorted(duplicate_ids))}")
        actual_ids = frozenset(ids)
        missing = sorted(REQUIRED_ACCEPTANCE_IDS - actual_ids)
        extra = sorted(actual_ids - REQUIRED_ACCEPTANCE_IDS)
        if missing:
            errors.append(f"acceptanceMatrix: missing required id(s): {', '.join(missing)}")
        if extra:
            errors.append(
                f"acceptanceMatrix: id(s) not in the approved plan's fixed set: {', '.join(extra)}"
            )

    examples = spec.get("exampleArtifacts")
    if not isinstance(examples, list) or not examples:
        errors.append("exampleArtifacts: requires a non-empty list")
    else:
        for index, entry in enumerate(examples):
            prefix = f"exampleArtifacts[{index}]"
            if not isinstance(entry, dict):
                errors.append(f"{prefix}: requires an object")
                continue
            _require_nonempty_str(errors, entry.get("path"), f"{prefix}.path")
            _require_nonempty_str(errors, entry.get("kind"), f"{prefix}.kind")
            _require_nonempty_str(errors, entry.get("disclaimer"), f"{prefix}.disclaimer")

    doc_links = spec.get("docLinks")
    if not isinstance(doc_links, list) or not doc_links:
        errors.append("docLinks: requires a non-empty list")
    else:
        for index, entry in enumerate(doc_links):
            prefix = f"docLinks[{index}]"
            if not isinstance(entry, dict):
                errors.append(f"{prefix}: requires an object")
                continue
            _require_nonempty_str(errors, entry.get("title"), f"{prefix}.title")
            _require_nonempty_str(errors, entry.get("path"), f"{prefix}.path")

    return errors


def validate_referenced_paths(spec: dict, root: pathlib.Path) -> list[str]:
    """Every path the spec names -- proto sources, example artifacts, and doc links -- must
    exist in this repository. A specification that cites a file which does not exist is worse
    than one that cites nothing."""
    errors: list[str] = []

    def _check(relative_path: object, field: str) -> None:
        if not isinstance(relative_path, str) or not relative_path:
            return
        if "\\" in relative_path or pathlib.PurePosixPath(relative_path).is_absolute():
            errors.append(f"{field}: path must be repository-relative POSIX-style: {relative_path}")
            return
        if not (root / relative_path).exists():
            errors.append(f"{field}: referenced path does not exist: {relative_path}")

    for index, proto_file in enumerate(spec.get("protocol", {}).get("protoFiles", [])):
        _check(proto_file, f"protocol.protoFiles[{index}]")
    for index, example in enumerate(spec.get("exampleArtifacts", [])):
        if isinstance(example, dict):
            _check(example.get("path"), f"exampleArtifacts[{index}].path")
    for index, link in enumerate(spec.get("docLinks", [])):
        if isinstance(link, dict):
            _check(link.get("path"), f"docLinks[{index}].path")

    return errors


def validate_against_bridge_protocol(spec: dict, root: pathlib.Path) -> list[str]:
    """Cross-checks protocol.* against the real C# contract by regex, so the specification
    cannot silently drift from the code that implements the client side of it."""
    errors: list[str] = []
    protocol = spec.get("protocol", {})

    bridge_protocol_path = root / BRIDGE_PROTOCOL_PATH
    if not bridge_protocol_path.exists():
        return [f"cannot cross-check protocol: missing {BRIDGE_PROTOCOL_PATH}"]
    text = read_text(bridge_protocol_path)

    def _extract_int(pattern: str, label: str) -> int | None:
        match = re.search(pattern, text)
        if match is None:
            errors.append(f"could not find {label} in {BRIDGE_PROTOCOL_PATH}")
            return None
        return int(match.group(1))

    def _extract_str(pattern: str, label: str) -> str | None:
        match = re.search(pattern, text)
        if match is None:
            errors.append(f"could not find {label} in {BRIDGE_PROTOCOL_PATH}")
            return None
        return match.group(1)

    package_name = _extract_str(r'PackageName\s*=\s*"([^"]+)"', "PackageName")
    if package_name is not None and package_name != protocol.get("packageName"):
        errors.append(
            f"protocol.packageName '{protocol.get('packageName')}' does not match "
            f"BridgeProtocol.PackageName '{package_name}'"
        )

    service_name = _extract_str(r'ServiceName\s*=\s*"([^"]+)"', "ServiceName")
    if service_name is not None and service_name != protocol.get("serviceName"):
        errors.append(
            f"protocol.serviceName '{protocol.get('serviceName')}' does not match "
            f"BridgeProtocol.ServiceName '{service_name}'"
        )

    major = _extract_int(r"CurrentMajorVersion\s*=\s*(\d+)", "CurrentMajorVersion")
    if major is not None and major != protocol.get("currentMajorVersion"):
        errors.append(
            f"protocol.currentMajorVersion {protocol.get('currentMajorVersion')} does not "
            f"match BridgeProtocol.CurrentMajorVersion {major}"
        )

    minor = _extract_int(r"CurrentMinorVersion\s*=\s*(\d+)", "CurrentMinorVersion")
    if minor is not None and minor != protocol.get("currentMinorVersion"):
        errors.append(
            f"protocol.currentMinorVersion {protocol.get('currentMinorVersion')} does not "
            f"match BridgeProtocol.CurrentMinorVersion {minor}"
        )

    validation_path = root / LIVE_AUTHORING_VALIDATION_PATH
    if not validation_path.exists():
        errors.append(f"cannot cross-check protocol bounds: missing {LIVE_AUTHORING_VALIDATION_PATH}")
        return errors
    validation_text = read_text(validation_path)

    def _extract_validation_int(pattern: str, label: str) -> int | None:
        match = re.search(pattern, validation_text)
        if match is None:
            errors.append(f"could not find {label} in {LIVE_AUTHORING_VALIDATION_PATH}")
            return None
        return int(match.group(1).replace("_", ""))

    checks = (
        (r"MaxUpdatesPerBatch\s*=\s*([\d_]+)", "MaxUpdatesPerBatch", "maxUpdatesPerBatch"),
        (
            r"MaxCollectionElementCount\s*=\s*([\d_]+)",
            "MaxCollectionElementCount",
            "maxCollectionElementCount",
        ),
        (
            r"MaxTotalCollectionElementCountPerBatch\s*=\s*([\d_]+)",
            "MaxTotalCollectionElementCountPerBatch",
            "maxTotalCollectionElementCountPerBatch",
        ),
        (
            r"MaxReplayWindowLength\s*=\s*([\d_]+)",
            "MaxReplayWindowLength",
            "replayWindowMaxLength",
        ),
        (
            r"DefaultReplayWindowLength\s*=\s*([\d_]+)",
            "DefaultReplayWindowLength",
            "replayWindowDefaultLength",
        ),
        (
            r"ReplayLedgerEntryBytes\s*=\s*([\d_]+)",
            "ReplayLedgerEntryBytes",
            "replayLedgerEntryBytes",
        ),
    )
    for pattern, label, spec_field in checks:
        extracted = _extract_validation_int(pattern, label)
        if extracted is not None and extracted != protocol.get(spec_field):
            errors.append(
                f"protocol.{spec_field} {protocol.get(spec_field)} does not match "
                f"LiveAuthoringValidation.{label} {extracted}"
            )

    # MaxEstimatedBatchPayloadBytes is expressed as "16 * 1024 * 1024" rather than a literal.
    payload_match = re.search(
        r"MaxEstimatedBatchPayloadBytes\s*=\s*(\d+)\s*\*\s*(\d+)\s*\*\s*(\d+)", validation_text
    )
    if payload_match is None:
        errors.append(
            "could not find MaxEstimatedBatchPayloadBytes in "
            f"{LIVE_AUTHORING_VALIDATION_PATH}"
        )
    else:
        computed = int(payload_match.group(1)) * int(payload_match.group(2)) * int(
            payload_match.group(3)
        )
        if computed != protocol.get("maxEstimatedBatchPayloadBytes"):
            errors.append(
                "protocol.maxEstimatedBatchPayloadBytes "
                f"{protocol.get('maxEstimatedBatchPayloadBytes')} does not match "
                f"LiveAuthoringValidation.MaxEstimatedBatchPayloadBytes {computed}"
            )

    return errors


def validate_against_wire_proto(spec: dict, root: pathlib.Path) -> list[str]:
    """Cross-checks updateMapping against the real StageUpdate oneof cases in wire.proto,
    instead of trusting a prose number of cases anywhere in the specification. The oneof is
    the only authoritative count of how many wire cases exist; this function parses it
    directly rather than letting a hand-written number silently drift from the contract."""
    errors: list[str] = []
    wire_proto_path = root / WIRE_PROTO_PATH
    if not wire_proto_path.exists():
        return [f"cannot cross-check updateMapping: missing {WIRE_PROTO_PATH}"]

    text = read_text(wire_proto_path)
    stage_update_match = re.search(
        r"message\s+StageUpdate\s*\{\s*oneof\s+update\s*\{(?P<body>.*?)\}\s*\}",
        text,
        re.DOTALL,
    )
    if stage_update_match is None:
        return [
            "could not find 'message StageUpdate { oneof update { ... } }' in "
            f"{WIRE_PROTO_PATH}"
        ]

    field_pattern = re.compile(
        r"^\s*[A-Za-z_][A-Za-z0-9_]*\s+([a-z][a-z0-9_]*)\s*=\s*\d+;\s*$", re.MULTILINE
    )
    proto_field_names = frozenset(field_pattern.findall(stage_update_match.group("body")))
    if not proto_field_names:
        errors.append(f"could not parse any oneof case out of StageUpdate in {WIRE_PROTO_PATH}")
        return errors

    update_mapping = spec.get("updateMapping", [])
    mapped_field_names: list[str] = []
    no_wire_case_entries: list[str] = []
    for entry in update_mapping:
        if not isinstance(entry, dict):
            continue
        wire_case = entry.get("wireCase", "")
        match = re.match(r"^StageUpdate\.([a-z][a-z0-9_]*)\b", wire_case)
        if match:
            mapped_field_names.append(match.group(1))
        else:
            no_wire_case_entries.append(entry.get("authoringType", "<missing>"))

    missing = sorted(proto_field_names - set(mapped_field_names))
    if missing:
        errors.append(
            "updateMapping is missing a wire case present in wire.proto's StageUpdate "
            f"oneof: {', '.join(missing)}"
        )
    extra = sorted(set(mapped_field_names) - proto_field_names)
    if extra:
        errors.append(
            "updateMapping references wire case(s) not in wire.proto's StageUpdate oneof: "
            f"{', '.join(extra)}"
        )
    duplicates = sorted({
        name for name in mapped_field_names if mapped_field_names.count(name) > 1
    })
    if duplicates:
        errors.append(
            f"updateMapping maps more than one entry to the same wire case: "
            f"{', '.join(duplicates)}"
        )
    if len(no_wire_case_entries) != 1:
        errors.append(
            "updateMapping must have exactly one entry with no wire case (the "
            f"overlay-replacement case); found {len(no_wire_case_entries)}: "
            f"{', '.join(no_wire_case_entries)}"
        )

    return errors


def validate_against_omniverse_profile(spec: dict, root: pathlib.Path) -> list[str]:
    """Confirms kitBaseline actually names a real dependency in eng/omniverse-profile.json,
    so the placeholder this specification's example extension.toml uses is tied to a real,
    live-checked source rather than a string that happens to look right."""
    errors: list[str] = []
    profile_path = root / OMNIVERSE_PROFILE_PATH
    if not profile_path.exists():
        return [f"cannot cross-check kitBaseline: missing {OMNIVERSE_PROFILE_PATH}"]

    profile = json.loads(read_text(profile_path))
    dependency_id = spec.get("kitBaseline", {}).get("dependencyId")
    dependencies = profile.get("dependencies", [])
    matches = [dep for dep in dependencies if dep.get("id") == dependency_id]
    if len(matches) != 1:
        errors.append(
            f"kitBaseline.dependencyId '{dependency_id}' must name exactly one dependency in "
            f"{OMNIVERSE_PROFILE_PATH}, found {len(matches)}"
        )
        return errors

    dependency = matches[0]
    if dependency.get("kind") != "external-optional":
        errors.append(
            f"{OMNIVERSE_PROFILE_PATH} dependency '{dependency_id}' must stay "
            "'external-optional' while this specification remains specification-only"
        )

    return errors


def validate_spec(spec: dict, root: pathlib.Path) -> list[str]:
    errors = validate_spec_shape(spec)
    errors.extend(validate_referenced_paths(spec, root))
    errors.extend(validate_against_bridge_protocol(spec, root))
    errors.extend(validate_against_wire_proto(spec, root))
    errors.extend(validate_against_omniverse_profile(spec, root))
    return errors


def _wrap_text(text: str, max_width: int) -> list[str]:
    words = text.split()
    lines: list[str] = []
    current: list[str] = []
    current_len = 0
    for word in words:
        if current and current_len + 1 + len(word) > max_width:
            lines.append(" ".join(current))
            current = [word]
            current_len = len(word)
        else:
            current.append(word)
            current_len = current_len + 1 + len(word) if current_len else len(word)
    if current:
        lines.append(" ".join(current))
    return lines


def _doc_relative_link(repo_relative_path: str) -> str:
    """Converts a repository-root-relative path into a link relative to
    docs/omniverse-kit-companion-reference.g.md, which lives directly under docs/."""
    if repo_relative_path.startswith("docs/"):
        return repo_relative_path[len("docs/"):]
    return f"../{repo_relative_path}"


def generate_document(spec: dict) -> str:
    lines: list[str] = []
    lines.append("<!-- generated by eng/generate-kit-companion-spec.py -- do not edit -->")
    lines.append("")
    lines.append("# Omniverse Kit companion -- generated reference tables")
    lines.append("")
    for wrapped in _wrap_text(
        "Generated from `eng/kit-companion-spec.json`; do not edit by hand. This is the "
        "structured reference appendix for "
        "[`docs/omniverse-kit-companion.md`](omniverse-kit-companion.md), which is the "
        "hand-authored, implementation-ready specification. Nothing in either document is "
        "evidence of an implementation: the companion extension remains a separate, "
        "not-yet-authorized repository.",
        118,
    ):
        lines.append(wrapped)
    lines.append("")

    protocol = spec["protocol"]
    lines.append("## Protocol constants")
    lines.append("")
    for wrapped in _wrap_text(
        "Bound names below are short member names; `LA.` prefixes "
        "`OpenUsd.LiveAuthoring.LiveAuthoringValidation` and `BP.` prefixes "
        "`OpenUsd.Bridge.Protocol.BridgeProtocol`.",
        118,
    ):
        lines.append(wrapped)
    lines.append("")
    lines.append("| Constant | Value | Source |")
    lines.append("| --- | --- | --- |")
    lines.append(f"| Package | `{protocol['packageName']}` | `BP.PackageName` |")
    lines.append(f"| Service | `{protocol['serviceName']}` | `BP.ServiceName` |")
    lines.append(
        f"| Protocol version | `{protocol['currentMajorVersion']}."
        f"{protocol['currentMinorVersion']}` | `BP.Version` |"
    )
    lines.append(
        f"| Max updates per batch | {protocol['maxUpdatesPerBatch']:,} | "
        "`LA.MaxUpdatesPerBatch` |"
    )
    lines.append(
        f"| Max collection elements | {protocol['maxCollectionElementCount']:,} | "
        "`LA.MaxCollectionElementCount` |"
    )
    lines.append(
        f"| Max total collection elements/batch | "
        f"{protocol['maxTotalCollectionElementCountPerBatch']:,} | "
        "`LA.MaxTotalCollectionElementCountPerBatch` |"
    )
    lines.append(
        f"| Max estimated batch payload bytes | "
        f"{protocol['maxEstimatedBatchPayloadBytes']:,} | "
        "`LA.MaxEstimatedBatchPayloadBytes` |"
    )
    lines.append(
        f"| Max frame overhead bytes | {protocol['maxFrameOverheadBytes']:,} | "
        "`BP.MaxFrameBytes` framing allowance |"
    )
    lines.append(
        f"| Replay window (default / max) | {protocol['replayWindowDefaultLength']} / "
        f"{protocol['replayWindowMaxLength']:,} | "
        "`LA.DefaultReplayWindowLength` / `MaxReplayWindowLength` |"
    )
    lines.append(
        f"| Replay ledger entry bytes | {protocol['replayLedgerEntryBytes']} | "
        "`LA.ReplayLedgerEntryBytes` |"
    )
    lines.append("")

    lines.append("## Server RPC behavior summary")
    lines.append("")
    lines.append("| RPC | Kind | Retry-safe |")
    lines.append("| --- | --- | --- |")
    for entry in spec["serverBehavior"]:
        lines.append(
            f"| `{entry['rpc']}` | {entry['kind']} | {'yes' if entry['retrySafe'] else 'no'} |"
        )
    lines.append("")

    lines.append("## Kit USD edit to wire update mapping")
    lines.append("")
    lines.append("| Authoring type | Wire case |")
    lines.append("| --- | --- |")
    noted_entries = []
    for entry in spec["updateMapping"]:
        is_wire_case = entry["wireCase"].startswith("StageUpdate.")
        cell = f"`{entry['wireCase']}`" if is_wire_case else "*(no delta case -- see notes)*"
        lines.append(f"| `{entry['authoringType']}` | {cell} |")
        if "notes" in entry:
            noted_entries.append(entry)
    lines.append("")
    if noted_entries:
        lines.append("Notes:")
        lines.append("")
        for entry in noted_entries:
            lines.append(f"- `{entry['authoringType']}`:")
            for wrapped in _wrap_text(entry["notes"], 114):
                lines.append(f"  {wrapped}")
        lines.append("")

    lines.append("## Kit edits that require a full snapshot")
    lines.append("")
    for entry in spec["unsupportedEdits"]:
        lines.append(f"- **{entry['kitEdit']}**")
        for wrapped in _wrap_text(entry["reason"], 116):
            lines.append(f"  {wrapped}")
    lines.append("")

    lines.append("## Acceptance matrix")
    lines.append("")
    for entry in spec["acceptanceMatrix"]:
        lines.append(f"### `{entry['id']}` -- {entry['title']}")
        lines.append("")
        lines.append("- **Method**:")
        for wrapped in _wrap_text(entry["method"], 112):
            lines.append(f"  {wrapped}")
        lines.append("- **Pass criteria**:")
        for wrapped in _wrap_text(entry["passCriteria"], 112):
            lines.append(f"  {wrapped}")
        lines.append("")

    lines.append("## Installation channel status")
    lines.append("")
    lines.append("| Channel | Status |")
    lines.append("| --- | --- |")
    for entry in spec["installation"]:
        lines.append(f"| `{entry['channel']}` | {entry['status']} |")
    lines.append("")

    lines.append("## Example artifacts")
    lines.append("")
    for entry in spec["exampleArtifacts"]:
        link = _doc_relative_link(entry["path"])
        lines.append(f"- [`{link}`]({link})")
        for wrapped in _wrap_text(entry["disclaimer"], 116):
            lines.append(f"  {wrapped}")
    lines.append("")

    lines.append("## Related documents")
    lines.append("")
    for link in spec["docLinks"]:
        lines.append(f"- [{link['title']}]({_doc_relative_link(link['path'])})")
    lines.append("")

    lines.append("---")
    lines.append("")
    lines.append(
        "_This document is regenerated by `python eng/generate-kit-companion-spec.py`._"
    )
    lines.append(
        "_Run without `--verify` to update it after editing `eng/kit-companion-spec.json`._"
    )
    lines.append("")

    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Generate or verify the Omniverse Kit companion reference document."
    )
    parser.add_argument(
        "--verify",
        action="store_true",
        help="Verify that the committed document is up to date; exit 1 if not.",
    )
    args = parser.parse_args()

    root = pathlib.Path(__file__).resolve().parents[1]

    try:
        spec = json.loads(read_text(root / SPEC_PATH))
    except (json.JSONDecodeError, OSError) as exc:
        print(f"Failed to read companion spec: {exc}", file=sys.stderr)
        return 1

    if not (root / SCHEMA_PATH).exists():
        print(f"Missing companion spec schema: {SCHEMA_PATH}", file=sys.stderr)
        return 1

    errors = validate_spec(spec, root)
    if errors:
        print(f"Kit companion spec validation failed with {len(errors)} error(s):", file=sys.stderr)
        for error in errors:
            print(f"  {error}", file=sys.stderr)
        return 1

    generated = generate_document(spec)
    doc_path = root / spec["generatedDocPath"]

    if args.verify:
        if not doc_path.exists():
            print(
                f"Generated Kit companion reference document is missing: {doc_path}\n"
                "Run 'python eng/generate-kit-companion-spec.py' to create it.",
                file=sys.stderr,
            )
            return 1
        current = read_text(doc_path)
        if current != generated:
            print(
                f"Generated Kit companion reference document is out of date: {doc_path}\n"
                "Run 'python eng/generate-kit-companion-spec.py' to regenerate it.",
                file=sys.stderr,
            )
            print(unified_diff(doc_path, current, generated), file=sys.stderr)
            return 1
        print(f"Kit companion spec verified: {doc_path}")
        return 0

    write_text(doc_path, generated)
    print(f"Kit companion reference document generated: {doc_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
