# Copyright (c) marcschier. Licensed under the MIT License.
#
# Generates or verifies docs/omniverse-profile.md from eng/omniverse-profile.json.
#
# This profile is the version-pinned Omniverse interoperability lock: named dependency
# baselines (OpenUSD, optional Kit/usd-exchange/MDL baselines, MaterialX), platforms, and
# cross-references into eng/support-manifest.json for feature status and permanent
# exclusions. Feature/exclusion status is never duplicated here -- it is always resolved
# live from eng/support-manifest.json so the two files cannot drift apart silently.
#
# Usage:
#   python eng/generate-omniverse-profile.py            # generate the document
#   python eng/generate-omniverse-profile.py --verify   # fail if stale or invalid
#
# Validation performed in both modes:
#   - $schemaVersion, runtimeVersion (must match version.json), and generatedDocPath.
#   - platforms must be a non-empty subset of the fixed platform vocabulary.
#   - Every dependency has a unique id, a valid kind, and a version.
#     - "runtime-pinned" dependencies must never be "pending" and must declare a
#       versionSource that resolves, inside an existing repository-relative file, to the
#       exact declared version (drift detection).
#     - "external-optional" and "external-test-only" dependencies support two valid states:
#       (1) "pending" with no versionSource, when no baseline is authorized yet, or
#       (2) a pinned version with a versionSource that resolves exactly like a
#       runtime-pinned dependency, once a named Kit/usd-exchange/MDL baseline is authorized.
#       This repository never fabricates or infers an external baseline version out of thin
#       air -- every non-"pending" version must be provable against its own
#       repository-relative provenance/lock source. A pinned external version is allowed to
#       coincidentally equal another dependency's version string: dependency separation is
#       proven by each dependency having its own independent id and versionSource, not by
#       version-string inequality.
#   - Every feature/permanentExclusion cross-reference must resolve to an existing area and
#     entry in eng/support-manifest.json with a status consistent with its claim kind.
#   - kitExecutionEvidence.status must be "pending" (no evidenceWorkflow claimed) unless it is
#     "executed" and evidenceWorkflow/evidenceWorkflowJobs name a real, existing workflow job:
#     execution against Kit is never claimed without an actual external job.

import argparse
import difflib
import json
import pathlib
import re
import sys


VALID_DEPENDENCY_KINDS = frozenset({
    "runtime-pinned",
    "external-optional",
    "external-test-only",
})

# Must match generate-support-manifest.py's VALID_PLATFORMS. Cross-checked by
# eng/tests/test_generate_omniverse_profile.py so the two vocabularies cannot drift apart.
VALID_PLATFORMS = frozenset({"win-x64", "linux-x64", "osx-arm64"})

GENERATED_DOC_PATH = "docs/omniverse-profile.md"

POINTER_SEGMENT_PATTERN = re.compile(
    r"^([a-zA-Z0-9_]+)(?:\[([a-zA-Z0-9_]+)=([^\]]+)\])?$"
)


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


def resolve_repo_relative_path(root: pathlib.Path, relative_path: str) -> pathlib.Path:
    if (
        not isinstance(relative_path, str)
        or not relative_path
        or "\\" in relative_path
        or pathlib.PurePosixPath(relative_path).is_absolute()
        or ".." in pathlib.PurePosixPath(relative_path).parts
    ):
        raise ValueError(f"path must be repository-relative: {relative_path}")
    full_path = (root / relative_path).resolve()
    if not full_path.is_relative_to(root.resolve()):
        raise ValueError(f"path escapes repository: {relative_path}")
    return full_path


def resolve_pointer(data: object, pointer: str) -> object:
    """Resolves a dotted pointer with an optional single '[key=value]' list filter per segment.

    Example: "dependencies[name=MaterialX].version" selects the "version" field of the
    object in the "dependencies" list whose "name" field equals "MaterialX".
    """
    current = data
    for segment in pointer.split("."):
        match = POINTER_SEGMENT_PATTERN.match(segment)
        if not match:
            raise ValueError(f"invalid pointer segment: {segment}")
        key, filter_key, filter_value = match.groups()
        if not isinstance(current, dict) or key not in current:
            raise ValueError(f"pointer segment '{key}' not found")
        current = current[key]
        if filter_key is not None:
            if not isinstance(current, list):
                raise ValueError(f"pointer segment '{key}' is not a list")
            matches = [
                item for item in current
                if isinstance(item, dict) and str(item.get(filter_key)) == filter_value
            ]
            if len(matches) != 1:
                raise ValueError(
                    f"pointer filter '{filter_key}={filter_value}' matched "
                    f"{len(matches)} item(s) under '{key}'"
                )
            current = matches[0]
    return current


def load_support_manifest(root: pathlib.Path) -> dict:
    manifest_path = root / "eng" / "support-manifest.json"
    return json.loads(read_text(manifest_path))


def find_manifest_entry(manifest: dict, area_id: str, entry_id: str) -> dict | None:
    for area in manifest.get("areas", []):
        if area.get("id") != area_id:
            continue
        for entry in area.get("entries", []):
            if entry.get("id") == entry_id:
                return entry
    return None


def validate_dependencies(
    profile: dict,
    root: pathlib.Path,
) -> tuple[list[str], dict[str, str]]:
    errors: list[str] = []
    resolved_versions: dict[str, str] = {}
    seen_ids: set[str] = set()

    dependencies = profile.get("dependencies", [])
    if not isinstance(dependencies, list) or not dependencies:
        return ["profile requires at least one dependency"], resolved_versions

    for dependency in dependencies:
        dep_id = dependency.get("id", "<missing>")
        if not isinstance(dep_id, str) or not dep_id.strip():
            errors.append("every dependency requires a non-empty string id")
            continue
        if dep_id in seen_ids:
            errors.append(f"{dep_id}: duplicate dependency id")
        seen_ids.add(dep_id)

        kind = dependency.get("kind")
        if kind not in VALID_DEPENDENCY_KINDS:
            errors.append(
                f"{dep_id}: invalid kind '{kind}'; must be one of: "
                f"{', '.join(sorted(VALID_DEPENDENCY_KINDS))}"
            )
            continue

        if not isinstance(dependency.get("description"), str) or not dependency[
            "description"
        ].strip():
            errors.append(f"{dep_id}: requires a non-empty description")

        version = dependency.get("version")
        if not isinstance(version, str) or not version.strip():
            errors.append(f"{dep_id}: requires a non-empty string version")
            continue

        version_source = dependency.get("versionSource")
        resolved_versions[dep_id] = version

        if kind == "runtime-pinned":
            # Runtime-pinned dependencies are always pinned: never "pending", and always
            # provable against a repository-relative provenance/lock source.
            if version == "pending":
                errors.append(f"{dep_id}: runtime-pinned dependency must not be 'pending'")
                continue
            errors.extend(
                _validate_pinned_version_source(dep_id, version, version_source, root)
            )
        else:
            # external-optional / external-test-only: two valid states.
            #   1. pending -- no baseline authorized yet, so no versionSource is allowed.
            #   2. pinned -- an authorized baseline, provable against a repository-relative
            #      provenance/lock source exactly like a runtime-pinned dependency. A pinned
            #      external version is permitted to coincidentally equal any other
            #      dependency's version; separation is proven by each dependency having its
            #      own independent id and versionSource, not by inequality of version
            #      strings.
            if version == "pending":
                if version_source is not None:
                    errors.append(
                        f"{dep_id}: external dependency must not declare a versionSource "
                        "while its version is 'pending'"
                    )
            else:
                errors.extend(
                    _validate_pinned_version_source(dep_id, version, version_source, root)
                )

    return errors, resolved_versions


def _validate_pinned_version_source(
    dep_id: str,
    version: str,
    version_source: object,
    root: pathlib.Path,
) -> list[str]:
    """Validates that a non-"pending" dependency version resolves to a repository-relative
    provenance/lock source. Shared by runtime-pinned dependencies and pinned external
    dependencies so both are held to the same provability standard."""
    errors: list[str] = []
    if not isinstance(version_source, dict):
        errors.append(
            f"{dep_id}: a pinned dependency (version other than 'pending') requires a "
            "versionSource"
        )
        return errors
    try:
        source_path = resolve_repo_relative_path(root, version_source.get("file", ""))
        if not source_path.exists():
            errors.append(
                f"{dep_id}: versionSource file does not exist: "
                f"{version_source.get('file')}"
            )
        else:
            source_data = json.loads(read_text(source_path))
            resolved = resolve_pointer(source_data, version_source.get("pointer", ""))
            if resolved != version:
                errors.append(
                    f"{dep_id}: profile version '{version}' has drifted from "
                    f"{version_source.get('file')} ({version_source.get('pointer')}"
                    f" = '{resolved}')"
                )
    except ValueError as exc:
        errors.append(f"{dep_id}: {exc}")
    return errors


def validate_cross_references(
    profile: dict,
    manifest: dict,
    list_key: str,
    required_area: str | None,
    allowed_statuses: frozenset[str],
) -> list[str]:
    errors: list[str] = []
    seen_ids: set[str] = set()
    for item in profile.get(list_key, []):
        item_id = item.get("id", "<missing>")
        if not isinstance(item_id, str) or not item_id.strip():
            errors.append(f"{list_key}: every entry requires a non-empty string id")
            continue
        if item_id in seen_ids:
            errors.append(f"{list_key}/{item_id}: duplicate id")
        seen_ids.add(item_id)

        area_id = item.get("manifestArea")
        entry_id = item.get("manifestEntry")
        if required_area is not None and area_id != required_area:
            errors.append(
                f"{list_key}/{item_id}: manifestArea must be '{required_area}'"
            )
            continue
        entry = find_manifest_entry(manifest, area_id, entry_id)
        if entry is None:
            errors.append(
                f"{list_key}/{item_id}: no support-manifest entry "
                f"'{area_id}/{entry_id}' found"
            )
            continue
        status = entry.get("status")
        if status not in allowed_statuses:
            errors.append(
                f"{list_key}/{item_id}: support-manifest entry '{area_id}/{entry_id}' "
                f"has status '{status}', expected one of: {', '.join(sorted(allowed_statuses))}"
            )
    return errors


def validate_kit_execution_evidence(profile: dict, root: pathlib.Path) -> list[str]:
    errors: list[str] = []
    evidence = profile.get("kitExecutionEvidence")
    if not isinstance(evidence, dict):
        return ["kitExecutionEvidence is required"]

    status = evidence.get("status")
    if status not in ("pending", "executed"):
        errors.append(
            f"kitExecutionEvidence.status must be 'pending' or 'executed', got '{status}'"
        )
        return errors

    if not isinstance(evidence.get("description"), str) or not evidence[
        "description"
    ].strip():
        errors.append("kitExecutionEvidence.description must be a non-empty string")

    if status == "pending":
        if not isinstance(evidence.get("blockedOn"), str) or not evidence[
            "blockedOn"
        ].strip():
            errors.append(
                "kitExecutionEvidence.blockedOn must be a non-empty string while pending"
            )
        if evidence.get("evidenceWorkflow") is not None:
            errors.append(
                "kitExecutionEvidence.evidenceWorkflow must be null while status is "
                "'pending': execution against Kit is never claimed without an actual "
                "external job"
            )
        if evidence.get("evidenceWorkflowJobs"):
            errors.append(
                "kitExecutionEvidence.evidenceWorkflowJobs must be empty while status is "
                "'pending'"
            )
    else:
        workflow_file = evidence.get("evidenceWorkflow")
        jobs = evidence.get("evidenceWorkflowJobs") or []
        if (
            not isinstance(workflow_file, str)
            or "\\" in workflow_file
            or len(pathlib.PurePosixPath(workflow_file).parts) != 1
        ):
            errors.append(
                "kitExecutionEvidence.evidenceWorkflow must name a single workflow file "
                "when status is 'executed'"
            )
        else:
            workflow_path = root / ".github" / "workflows" / workflow_file
            if not workflow_path.exists():
                errors.append(
                    f"kitExecutionEvidence.evidenceWorkflow file does not exist: "
                    f"{workflow_file}"
                )
            elif not jobs:
                errors.append(
                    "kitExecutionEvidence.evidenceWorkflowJobs requires at least one job "
                    "id when status is 'executed'"
                )
            else:
                workflow_text = read_text(workflow_path)
                for job in jobs:
                    job_pattern = re.compile(rf"^  {re.escape(job)}:\s*$", re.MULTILINE)
                    if not job_pattern.search(workflow_text):
                        errors.append(
                            f"kitExecutionEvidence: job '{job}' does not exist in "
                            f"workflow '{workflow_file}'"
                        )
    return errors


def validate_profile(
    profile: dict,
    manifest: dict,
    root: pathlib.Path,
    expected_version: str,
) -> list[str]:
    errors: list[str] = []

    if profile.get("$schemaVersion") != 1:
        errors.append("profile $schemaVersion must be 1")
    if profile.get("runtimeVersion") != expected_version:
        errors.append(
            f"profile runtimeVersion '{profile.get('runtimeVersion')}' does not match "
            f"version.json '{expected_version}'"
        )
    if profile.get("generatedDocPath") != GENERATED_DOC_PATH:
        errors.append(f"profile generatedDocPath must be '{GENERATED_DOC_PATH}'")

    platforms = profile.get("platforms")
    if not isinstance(platforms, list) or not platforms:
        errors.append("profile requires a non-empty platforms list")
    else:
        unknown_platforms = sorted(set(platforms) - VALID_PLATFORMS)
        if unknown_platforms:
            errors.append(f"unknown platforms: {', '.join(unknown_platforms)}")

    dependency_errors, _ = validate_dependencies(profile, root)
    errors.extend(dependency_errors)

    errors.extend(validate_cross_references(
        profile,
        manifest,
        "features",
        required_area=None,
        allowed_statuses=frozenset({"implemented", "workflow-gated", "implemented-not-gated"}),
    ))
    errors.extend(validate_cross_references(
        profile,
        manifest,
        "permanentExclusions",
        required_area="excluded-unreachable",
        allowed_statuses=frozenset({"excluded", "unreachable"}),
    ))
    errors.extend(validate_kit_execution_evidence(profile, root))

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


def _status_badge(status: str) -> str:
    mapping = {
        "implemented": "Implemented",
        "workflow-gated": "Workflow-gated",
        "compile-only": "Compile-only",
        "pending-hosted-proof": "Pending hosted proof",
        "implemented-not-gated": "Implemented, not gated",
        "excluded": "Excluded",
        "unreachable": "Unreachable",
        "not-supported": "Not supported",
    }
    return mapping.get(status, status)


def generate_document(profile: dict, manifest: dict) -> str:
    lines: list[str] = []
    lines.append("<!-- generated by eng/generate-omniverse-profile.py -- do not edit -->")
    lines.append("")
    lines.append("# Omniverse interoperability profile")
    lines.append("")
    for wrapped in _wrap_text(
        f"Version-pinned Omniverse interoperability profile for `{profile['runtimeVersion']}`. "
        "Generated from `eng/omniverse-profile.json`; do not edit by hand. Feature and "
        "exclusion status is always resolved live from `eng/support-manifest.json` -- this "
        "document never fabricates a status independently of that manifest. See "
        "[`docs/support-matrix.md`](support-matrix.md) for narrative detail and "
        "[`docs/support-manifest.md`](support-manifest.md) for the full executable summary.",
        120,
    ):
        lines.append(wrapped)
    lines.append("")

    lines.append("## Dependency baselines")
    lines.append("")
    for dependency in profile["dependencies"]:
        lines.append(
            f"- **`{dependency['id']}`** ({dependency['kind']}, version "
            f"`{dependency['version']}`)"
        )
        for wrapped in _wrap_text(dependency["description"], 116):
            lines.append(f"  {wrapped}")
    lines.append("")

    lines.append("## Interchange features")
    lines.append("")
    lines.append("| Id | Status | Support manifest entry |")
    lines.append("| --- | --- | --- |")
    for feature in profile["features"]:
        entry = find_manifest_entry(
            manifest, feature["manifestArea"], feature["manifestEntry"]
        )
        status = _status_badge(entry["status"]) if entry else "unknown"
        lines.append(
            f"| `{feature['id']}` | {status} | "
            f"`{feature['manifestArea']}/{feature['manifestEntry']}` |"
        )
    lines.append("")

    lines.append("## Permanent proprietary exclusions")
    lines.append("")
    for exclusion in profile["permanentExclusions"]:
        entry = find_manifest_entry(
            manifest, exclusion["manifestArea"], exclusion["manifestEntry"]
        )
        status = _status_badge(entry["status"]) if entry else "unknown"
        reason = entry.get("exclusionReason", "") if entry else ""
        lines.append(f"- **`{exclusion['id']}`** ({status})")
        for wrapped in _wrap_text(reason, 116):
            lines.append(f"  {wrapped}")
    lines.append("")

    lines.append("## Kit execution evidence")
    lines.append("")
    evidence = profile["kitExecutionEvidence"]
    lines.append(f"Status: **{evidence['status']}**")
    lines.append("")
    for wrapped in _wrap_text(evidence["description"], 120):
        lines.append(wrapped)
    lines.append("")
    if evidence["status"] == "pending":
        for wrapped in _wrap_text(evidence["blockedOn"], 120):
            lines.append(wrapped)
        lines.append("")

    lines.append("---")
    lines.append("")
    lines.append(
        "_This document is regenerated by `python eng/generate-omniverse-profile.py`._"
    )
    lines.append(
        "_Run without `--verify` to update it after editing"
        " `eng/omniverse-profile.json`._"
    )
    lines.append("")

    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Generate or verify the Omniverse interoperability profile document."
    )
    parser.add_argument(
        "--verify",
        action="store_true",
        help="Verify that the committed document is up to date; exit 1 if not.",
    )
    args = parser.parse_args()

    root = pathlib.Path(__file__).resolve().parents[1]
    profile_path = root / "eng" / "omniverse-profile.json"

    try:
        profile = json.loads(read_text(profile_path))
    except (json.JSONDecodeError, OSError) as exc:
        print(f"Failed to read Omniverse profile: {exc}", file=sys.stderr)
        return 1

    try:
        manifest = load_support_manifest(root)
    except (json.JSONDecodeError, OSError) as exc:
        print(f"Failed to read support manifest: {exc}", file=sys.stderr)
        return 1

    try:
        expected_version = json.loads(read_text(root / "version.json"))["version"]
    except (KeyError, json.JSONDecodeError, OSError) as exc:
        print(f"Failed to read version.json: {exc}", file=sys.stderr)
        return 1

    errors = validate_profile(profile, manifest, root, expected_version)
    if errors:
        print(
            f"Omniverse profile validation failed with {len(errors)} error(s):",
            file=sys.stderr,
        )
        for error in errors:
            print(f"  {error}", file=sys.stderr)
        return 1

    generated = generate_document(profile, manifest)
    doc_path = root / profile["generatedDocPath"]

    if args.verify:
        if not doc_path.exists():
            print(
                f"Generated Omniverse profile document is missing: {doc_path}\n"
                "Run 'python eng/generate-omniverse-profile.py' to create it.",
                file=sys.stderr,
            )
            return 1
        current = read_text(doc_path)
        if current != generated:
            print(
                f"Generated Omniverse profile document is out of date: {doc_path}\n"
                "Run 'python eng/generate-omniverse-profile.py' to regenerate it.",
                file=sys.stderr,
            )
            print(unified_diff(doc_path, current, generated), file=sys.stderr)
            return 1
        print(f"Omniverse profile verified: {doc_path}")
        return 0

    write_text(doc_path, generated)
    print(f"Omniverse profile generated: {doc_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
