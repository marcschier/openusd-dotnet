#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.

import importlib.util
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
import zipfile


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "eng" / "verify-nuget-promotion.py"
SPEC = importlib.util.spec_from_file_location("verify_nuget_promotion", SCRIPT)
PROMOTION = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROMOTION)
VERSION = "0.17.0-alpha"
COMMIT = "a" * 40
REPOSITORY = "marcschier/openusd-dotnet"


class NuGetPromotionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.packages = self.root / "feed"
        self.released = self.root / "release"
        self.packages.mkdir()
        self.released.mkdir()
        self.inventory = {"packages": ["OpenUsd", "OpenUsd.Runtime"],
                          "symbols": ["OpenUsd"]}
        self.evidence = {
            "schemaVersion": 1, "repository": REPOSITORY, "commit": COMMIT,
            "ref": f"refs/tags/v{VERSION}", "runId": "123", "runAttempt": "2",
            "jobs": {name: "success" for name in PROMOTION.REQUIRED_GATES},
        }
        self.run = {
            "repository": {"full_name": REPOSITORY}, "id": 123,
            "path": ".github/workflows/release.yml", "event": "push",
            "status": "completed", "conclusion": "success",
            "head_branch": f"v{VERSION}", "head_sha": COMMIT, "run_attempt": 2,
        }
        for package_id, extension in (
                ("OpenUsd", "nupkg"), ("OpenUsd", "snupkg"),
                ("OpenUsd.Runtime", "nupkg")):
            path = self.released / f"{package_id.lower()}.{VERSION}.{extension}"
            self.write_package(path, package_id)
            shutil.copyfile(path, self.packages / path.name)

    @staticmethod
    def write_package(path, package_id="OpenUsd", version=VERSION, commit=COMMIT,
                      metadata=None, payload=b"released payload"):
        if metadata is None:
            metadata = (
                '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">'
                f"<metadata><id>{package_id}</id><version>{version}</version>"
                f'<repository type="git" commit="{commit}" /></metadata></package>'
            )
        with zipfile.ZipFile(path, "w") as archive:
            if metadata is not False:
                archive.writestr(f"{package_id}.nuspec", metadata)
            archive.writestr("lib/payload.bin", payload)

    def verify(self, **overrides):
        arguments = {
            "packages": self.packages, "release_packages": self.released,
            "evidence": self.evidence, "release_run": self.run,
            "inventory": self.inventory, "version": VERSION, "run_id": "123",
            "repository": REPOSITORY, "tag_commit": COMMIT,
        }
        arguments.update(overrides)
        return PROMOTION.verify_promotion(**arguments)

    def test_accepts_complete_identical_source_bound_package_and_symbol_set(self):
        self.assertEqual(self.verify(), {
            "version": VERSION, "commit": COMMIT, "runId": "123",
            "packages": 2, "symbols": 1,
        })

    def test_missing_package_or_symbol_on_either_side_is_rejected(self):
        for directory in (self.packages, self.released):
            for path in list(directory.iterdir()):
                with self.subTest(directory=directory.name, file=path.name):
                    content = path.read_bytes()
                    path.unlink()
                    with self.assertRaisesRegex(ValueError, "inventory mismatch"):
                        self.verify()
                    path.write_bytes(content)

    def test_unexpected_packages_and_wrong_version_are_rejected(self):
        for directory in (self.packages, self.released):
            for filename in (f"extra.{VERSION}.nupkg", "openusd.0.16.6-alpha.nupkg"):
                with self.subTest(directory=directory.name, file=filename):
                    path = directory / filename
                    self.write_package(path)
                    with self.assertRaisesRegex(ValueError, "inventory mismatch|version"):
                        self.verify()
                    path.unlink()

    def test_payload_and_symbol_byte_changes_are_rejected(self):
        for extension in ("nupkg", "snupkg"):
            path = self.packages / f"openusd.{VERSION}.{extension}"
            with self.subTest(extension=extension):
                self.write_package(path, payload=b"changed payload")
                with self.assertRaisesRegex(ValueError, "bytes differ"):
                    self.verify()
                shutil.copyfile(self.released / path.name, path)

    def test_matching_hashes_do_not_substitute_for_nuspec_identity(self):
        for extension in ("nupkg", "snupkg"):
            for arguments, message in (
                    ({"package_id": "Wrong.Id"}, "ID/version"),
                    ({"version": "0.16.6-alpha"}, "ID/version"),
                    ({"commit": "b" * 40}, "source commit")):
                with self.subTest(extension=extension, arguments=arguments):
                    path = self.released / f"openusd.{VERSION}.{extension}"
                    self.write_package(path, **arguments)
                    shutil.copyfile(path, self.packages / path.name)
                    with self.assertRaisesRegex(ValueError, message):
                        self.verify()
            self.write_package(path)
            shutil.copyfile(path, self.packages / path.name)

    def test_matching_packages_and_symbols_reject_unapproved_compiler_binaries(self):
        names = (
            "spirv-cross.dll", "libspirv-cross.so", "libspirv-cross.so.1",
            "libspirv-cross.dylib", "SPIRV-CROSS.DLL", "dxc.exe",
            "dxcompiler.dll", "dxil.dll", "libdxcompiler.so", "libdxcompiler.so.3.8",
            "libdxil.so.1", "libdxil.dylib",
        )
        for extension in ("nupkg", "snupkg"):
            path = self.released / f"openusd.{VERSION}.{extension}"
            for name in names:
                with self.subTest(extension=extension, name=name):
                    self.write_package(path)
                    with zipfile.ZipFile(path, "a") as archive:
                        archive.writestr(f"runtimes/nested/{name}", b"compiler")
                    shutil.copyfile(path, self.packages / path.name)
                    with self.assertRaisesRegex(ValueError, "unapproved compiler binary"):
                        self.verify()
            self.write_package(path)
            shutil.copyfile(path, self.packages / path.name)

    def test_compiler_notices_are_not_mistaken_for_binaries(self):
        path = self.released / f"openusd.{VERSION}.nupkg"
        with zipfile.ZipFile(path, "a") as archive:
            archive.writestr("notices/spirv-cross-notice.txt", b"notice")
        shutil.copyfile(path, self.packages / path.name)
        self.assertEqual(self.verify()["packages"], 2)

    def test_release_archive_check_rejects_bad_or_missing_archives_before_publication(self):
        arguments = [sys.executable, str(SCRIPT), "--check-compiler-assets",
                     "--packages", str(self.released)]
        result = subprocess.run(arguments, text=True, capture_output=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout), {"archives": 3})

        path = self.released / f"openusd.{VERSION}.snupkg"
        with zipfile.ZipFile(path, "a") as archive:
            archive.writestr(r"nested\SPIRV-CROSS.DLL", b"compiler")
        result = subprocess.run(arguments, text=True, capture_output=True, timeout=30)
        self.assertEqual(result.returncode, 1)
        self.assertIn("unapproved compiler binary", result.stderr)
        self.assertIn("SPIRV-CROSS.DLL", result.stderr)
        self.assertEqual(result.stdout, "")

        path.write_bytes(b"not an archive")
        result = subprocess.run(arguments, text=True, capture_output=True, timeout=30)
        self.assertEqual(result.returncode, 1)
        self.assertIn("Invalid package payload", result.stderr)

        arguments[-1] = str(self.root / "absent")
        result = subprocess.run(arguments, text=True, capture_output=True, timeout=30)
        self.assertEqual(result.returncode, 1)
        self.assertIn("Package directory does not exist", result.stderr)
        arguments[-1] = str(self.root)
        result = subprocess.run(arguments, text=True, capture_output=True, timeout=30)
        self.assertEqual(result.returncode, 1)
        self.assertIn("contains no archives", result.stderr)

    def test_release_workflow_checks_compilers_before_the_first_package_push(self):
        workflow = (ROOT / ".github" / "workflows" / "release.yml").read_text(
            encoding="utf-8")
        publish = workflow.split("\n  publish:", 1)[1]
        guard = publish.index("      - name: Reject unapproved compiler binaries")
        push = publish.index("      - name: Push to the GitHub Packages NuGet feed")
        self.assertLess(guard, push)
        guard_step = publish[guard:publish.index("\n      - name:", guard + 1)]
        self.assertIn("--check-compiler-assets", guard_step)
        self.assertIn("--packages artifacts/nupkg", guard_step)
        self.assertIn("if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }", guard_step)

    def test_missing_malformed_oversized_and_wrong_root_nuspec_are_rejected(self):
        for metadata, message in (
                (False, "bounded root nuspec"), ("<package", "Invalid package metadata"),
                ("x" * (1024 * 1024 + 1), "bounded root nuspec"),
                ("<other><metadata /></other>", "nuspec root"),
                ("<package />", "Missing package metadata")):
            with self.subTest(metadata=str(metadata)[:30]):
                path = self.released / f"openusd.{VERSION}.nupkg"
                self.write_package(path, metadata=metadata)
                shutil.copyfile(path, self.packages / path.name)
                with self.assertRaisesRegex(ValueError, message):
                    self.verify()

    def test_every_gate_must_be_named_and_successful(self):
        for jobs in (
                {}, {"ci": "success"},
                {**self.evidence["jobs"], "extra": "success"},
                {**self.evidence["jobs"], "render": "skipped"},
                {**self.evidence["jobs"], "native": "failure"}, None):
            with self.subTest(jobs=jobs):
                evidence = {**self.evidence, "jobs": jobs}
                with self.assertRaisesRegex(ValueError, "Every required release gate"):
                    self.verify(evidence=evidence)

    def test_evidence_must_match_repository_commit_tag_and_run(self):
        for key, value in (
                ("schemaVersion", 2), ("schemaVersion", True),
                ("repository", "other/repository"),
                ("commit", "b" * 40), ("ref", "refs/heads/main"), ("runId", "124")):
            with self.subTest(key=key):
                evidence = {**self.evidence, key: value}
                with self.assertRaisesRegex(ValueError, "Release evidence does not match"):
                    self.verify(evidence=evidence)

    def test_workflow_run_and_attempt_must_match_successful_tagged_release(self):
        for key, value in (
                ("repository", {"full_name": "other/repository"}), ("id", 124),
                ("path", ".github/workflows/package.yml"), ("event", "workflow_dispatch"),
                ("status", "in_progress"), ("conclusion", "failure"),
                ("head_branch", "main"), ("head_sha", "b" * 40),
                ("run_attempt", 1), ("run_attempt", 0), ("run_attempt", True),
                ("run_attempt", None)):
            with self.subTest(key=key, value=value):
                run = {**self.run, key: value}
                with self.assertRaisesRegex(ValueError, "successful matching tagged attempt"):
                    self.verify(release_run=run)

    def test_invalid_empty_duplicate_and_unpublished_symbol_inventories_are_rejected(self):
        for inventory in (
                {"packages": [], "symbols": ["OpenUsd"]},
                {"packages": ["OpenUsd"], "symbols": []},
                {"packages": ["OpenUsd", "OPENUSD"], "symbols": ["OpenUsd"]},
                {"packages": ["OpenUsd"], "symbols": ["OpenUsd", "OPENUSD"]},
                {"packages": ["OpenUsd"], "symbols": ["Unknown"]},
                {"packages": ["../OpenUsd"], "symbols": ["OpenUsd"]},
                {"packages": [1], "symbols": ["OpenUsd"]},
                {"packages": "OpenUsd", "symbols": ["OpenUsd"]}):
            with self.subTest(inventory=inventory):
                with self.assertRaises(ValueError):
                    self.verify(inventory=inventory)

    def test_invalid_inputs_and_absent_directories_are_rejected(self):
        for arguments in (
                {"version": "0.17.0-alpha;echo bad"}, {"run_id": "0"},
                {"run_id": "123/other"}, {"tag_commit": "a" * 7},
                {"evidence": []}, {"release_run": None}, {"inventory": []},
                {"packages": self.root / "absent"}):
            with self.subTest(arguments=arguments):
                with self.assertRaises(ValueError):
                    self.verify(**arguments)

    def test_cli_reports_verified_identity_and_refuses_changed_bytes(self):
        arguments = [sys.executable, str(SCRIPT)]
        for name, value in (("evidence", self.evidence), ("release-run", self.run),
                            ("inventory", self.inventory)):
            path = self.root / f"{name}.json"
            path.write_text(json.dumps(value), encoding="utf-8-sig")
            arguments.extend((f"--{name}", str(path)))
        for name, value in (
                ("packages", self.packages), ("release-packages", self.released),
                ("version", VERSION), ("run-id", "123"), ("repository", REPOSITORY),
                ("tag-commit", COMMIT)):
            arguments.extend((f"--{name}", str(value)))
        result = subprocess.run(arguments, text=True, capture_output=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)["commit"], COMMIT)
        self.write_package(self.packages / f"openusd.{VERSION}.nupkg", payload=b"changed")
        result = subprocess.run(arguments, text=True, capture_output=True, timeout=30)
        self.assertEqual(result.returncode, 1)
        self.assertIn("NUGET_PROMOTION_INVALID: Promotion bytes differ", result.stderr)
        self.assertEqual(result.stdout, "")

    def public_packages(self):
        directory = self.root / "public"
        directory.mkdir()
        for path in self.released.iterdir():
            shutil.copyfile(path, directory / path.name)
        return directory

    def test_public_payload_identity_ignores_only_root_signature_and_zip_metadata(self):
        public = self.public_packages()
        for path in public.iterdir():
            with zipfile.ZipFile(path, "a") as archive:
                archive.comment = b"repository-signed archive"
                archive.writestr(".signature.p7s", b"repository signature container")
        result = self.verify(public_packages=public)
        self.assertEqual(result["publicPackages"], 2)
        self.assertEqual(result["publicSymbols"], 1)
        self.assertTrue(result["publicComplete"])
        path = public / f"openusd.{VERSION}.nupkg"
        with zipfile.ZipFile(path, "a") as archive:
            archive.writestr("other/.signature.p7s", b"unexpected payload")
        with self.assertRaisesRegex(ValueError, "Public package content differs"):
            self.verify(public_packages=public)

    def test_public_payload_changes_and_corrupt_archives_are_rejected(self):
        public = self.public_packages()
        for extension in ("nupkg", "snupkg"):
            with self.subTest(extension=extension):
                path = public / f"openusd.{VERSION}.{extension}"
                original = path.read_bytes()
                self.write_package(path, payload=b"different public package")
                with self.assertRaisesRegex(ValueError, "Public package content differs"):
                    self.verify(public_packages=public)
                path.write_bytes(b"not a package")
                with self.assertRaisesRegex(ValueError, "Invalid package payload"):
                    self.verify(public_packages=public)
                path.write_bytes(original)

    def test_duplicate_public_entries_cannot_hide_behind_signature_handling(self):
        public = self.public_packages()
        path = public / f"openusd.{VERSION}.nupkg"
        with zipfile.ZipFile(path, "a") as archive:
            archive.writestr(".signature.p7s", b"first")
            with self.assertWarnsRegex(UserWarning, "Duplicate name"):
                archive.writestr(".signature.p7s", b"second")
        with self.assertRaisesRegex(ValueError, "Duplicate package entry"):
            self.verify(public_packages=public)

    def test_partial_public_preflight_does_not_establish_complete_publication(self):
        public = self.root / "public"
        public.mkdir()
        result = self.verify(public_packages=public, allow_partial_public=True)
        self.assertEqual(result["publicPackages"], 0)
        self.assertEqual(result["publicSymbols"], 0)
        self.assertFalse(result["publicComplete"])
        with self.assertRaisesRegex(ValueError, "inventory mismatch"):
            self.verify(public_packages=public)
        path = self.released / f"openusd.{VERSION}.nupkg"
        shutil.copyfile(path, public / path.name)
        result = self.verify(public_packages=public, allow_partial_public=True)
        self.assertEqual(result["publicPackages"], 1)
        self.assertFalse(result["publicComplete"])
        shutil.copyfile(path, public / f"extra.{VERSION}.nupkg")
        with self.assertRaisesRegex(ValueError, "inventory mismatch"):
            self.verify(public_packages=public, allow_partial_public=True)
        with self.assertRaisesRegex(ValueError, "requires a public package directory"):
            self.verify(allow_partial_public=True)

    def test_workflow_verifies_identity_before_requesting_publication_credentials(self):
        workflow = (ROOT / ".github" / "workflows" / "nuget.yml").read_text(encoding="utf-8")
        stage = workflow.index("- name: Stage symbol packages for nuget.org")
        public = workflow.index("- name: Check existing nuget.org packages")
        verify = workflow.index("- name: Verify promotion bytes and tagged source")
        login = workflow.index("- name: NuGet login (OIDC trusted publishing)")
        self.assertLess(stage, public)
        self.assertLess(public, verify)
        self.assertLess(verify, login)
        block = workflow[verify:login]
        self.assertIn("python ./eng/verify-nuget-promotion.py", block)
        self.assertIn("/commits/v$env:PROMOTION_VERSION", block)
        self.assertIn("/actions/runs/$env:RELEASE_RUN_ID", block)
        self.assertIn("--tag-commit $commit", block)
        self.assertIn("--release-packages release-artifacts/nupkg", block)
        self.assertIn("--public-packages public-existing", block)
        self.assertIn("--allow-partial-public", block)

    def test_required_gate_names_match_release_aggregate(self):
        workflow = (ROOT / ".github" / "workflows" / "release.yml").read_text(encoding="utf-8")
        aggregate = workflow.split("\n  aggregate:\n", 1)[1].split("\n  pack:\n", 1)[0]
        needs = re.search(r"needs:\s*\[([^]]+)]", aggregate)[1]
        self.assertEqual({name.strip() for name in needs.split(",")},
                         PROMOTION.REQUIRED_GATES)


if __name__ == "__main__":
    unittest.main()
