# Copyright (c) marcschier. Licensed under the MIT License.
"""Behavioral tests for the generated support profile."""

from __future__ import annotations

import importlib.machinery
import importlib.util
import pathlib
import sys
import unittest

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
GENERATOR = REPO_ROOT / "eng" / "generate-support-manifest.py"
loader = importlib.machinery.SourceFileLoader("generate_support_manifest", str(GENERATOR))
spec = importlib.util.spec_from_loader(loader.name, loader)
gsm = importlib.util.module_from_spec(spec)
sys.modules[loader.name] = gsm
loader.exec_module(gsm)


def manifest() -> dict:
    return {
        "version": "1.2.3-alpha",
        "statusTerms": {
            "workflow-gated": "A workflow requires execution.",
            "not-supported": "Not implemented.",
        },
        "areas": [{
            "id": "rendering",
            "title": "Rendering",
            "description": "Backend-specific rendering behavior.",
            "entries": [{
                "id": "sampled-density",
                "description": "A single density field; not general volume shading.",
                "status": "workflow-gated",
                "evidencePlatforms": ["win-x64", "osx-arm64"],
                "exclusionReason": "Multiple fields remain unsupported.",
            }, {
                "id": "path-tracing",
                "description": "Offline light transport.",
                "status": "not-supported",
                "exclusionReason": "No path-tracing adapter is shipped.",
            }],
        }],
    }


class GeneratedSupportScopeTests(unittest.TestCase):
    def test_generated_profile_preserves_platform_scope_and_limits(self):
        document = gsm.generate_document(manifest())

        self.assertIn("| Feature | Status | Evidence platforms |", document)
        self.assertIn(
            "| `sampled-density` | Workflow-gated | `win-x64`, `osx-arm64` |",
            document,
        )
        self.assertIn("| `path-tracing` | Not supported | None |", document)
        self.assertIn("A single density field; not general volume shading.", document)
        self.assertIn("Multiple fields remain unsupported.", document)
        self.assertIn("No path-tracing adapter is shipped.", document)

    def test_overview_uses_the_same_status_and_platforms_as_the_profile(self):
        source = manifest()
        entries = [("sampled-density", "Sampled volumes"), ("path-tracing", "Path tracing")]
        overview = gsm.generate_summary(source, entries)

        self.assertIn(
            "| [Sampled volumes][support-1] "
            "| Workflow-gated | `win-x64`, `osx-arm64` |",
            overview,
        )
        self.assertIn(
            "| [Path tracing][support-2] "
            "| Not supported | None |",
            overview,
        )
        self.assertIn("[support-1]: docs/support-manifest.md#sampled-density", overview)
        self.assertIn("[support-2]: docs/support-manifest.md#path-tracing", overview)

        source["areas"][0]["entries"][0]["evidencePlatforms"] = ["win-x64"]
        source["areas"][0]["entries"][0]["status"] = "implemented"
        changed = gsm.generate_summary(source, entries)
        self.assertIn("| Implemented | `win-x64` |", changed)
        self.assertNotIn("osx-arm64", changed)

    def test_overview_update_changes_only_one_unambiguous_region(self):
        begin = "<!-- BEGIN GENERATED RENDER SUPPORT -->"
        end = "<!-- END GENERATED RENDER SUPPORT -->"
        source = f"Before\n{begin}\nOld table\n{end}\nAfter\n"

        self.assertEqual(
            gsm.replace_summary(source, "New table"),
            f"Before\n{begin}\nNew table\n{end}\nAfter\n",
        )
        for malformed in (
            "No markers",
            begin,
            end,
            f"{end}\n{begin}",
            f"{begin}\n{begin}\n{end}",
            f"{begin}\n{end}\n{end}",
        ):
            with self.subTest(document=malformed):
                with self.assertRaisesRegex(ValueError, "summary markers"):
                    gsm.replace_summary(malformed, "New table")

    def test_overview_obeys_the_documentation_line_budget(self):
        source = manifest()
        entry = source["areas"][0]["entries"][0]
        entry["id"] = "catmull-clark-subdivision"
        entry["evidencePlatforms"] = ["win-x64", "linux-x64", "osx-arm64"]

        overview = gsm.generate_summary(
            source,
            [("catmull-clark-subdivision", "Catmull-Clark, Loop and bilinear subdivision")],
        )

        for line in overview.splitlines():
            self.assertLessEqual(len(line), 120, line)

    def test_invalid_platform_claims_are_rejected_even_for_excluded_features(self):
        source = manifest()
        source["$schemaVersion"] = 1
        source["generatedDocPath"] = "docs/support-manifest.md"
        source["statusTerms"] = {status: status for status in (
            "implemented", "workflow-gated", "compile-only", "pending-hosted-proof",
            "implemented-not-gated", "excluded", "unreachable", "not-supported",
        )}
        source["areas"][0]["entries"] = [source["areas"][0]["entries"][1]]
        entry = source["areas"][0]["entries"][0]
        self.assertEqual(gsm.validate_manifest(source, REPO_ROOT, set(), "1.2.3-alpha"), [])

        for platforms in (None, "win-x64", ["win-x64", "win-x64"], ["future-rid"], [1]):
            with self.subTest(platforms=platforms):
                entry["evidencePlatforms"] = platforms
                errors = gsm.validate_manifest(source, REPO_ROOT, set(), "1.2.3-alpha")
                self.assertTrue(any("evidencePlatforms" in error for error in errors), errors)


if __name__ == "__main__":
    unittest.main()
