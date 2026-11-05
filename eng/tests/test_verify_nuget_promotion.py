#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.

from contextlib import redirect_stderr, redirect_stdout
import http.client
import importlib.util
import io
import json
from pathlib import Path
import re
import shutil
import stat
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import urllib.error
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

    def cli_arguments(self):
        arguments = [str(SCRIPT)]
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
        return arguments

    def test_cli_reports_verified_identity_and_refuses_changed_bytes(self):
        arguments = [sys.executable, *self.cli_arguments()]
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

    def test_public_payload_identity_preserves_zip_extraction_attributes(self):
        release = self.released / f"openusd.{VERSION}.nupkg"
        link_name = "runtimes/linux-x64/native/libopenusd_storm_child.so"
        info = zipfile.ZipInfo(link_name)
        info.create_system = 3
        info.external_attr = (stat.S_IFLNK | 0o777) << 16
        with zipfile.ZipFile(release, "a") as archive:
            archive.writestr(info, "libopenusd_storm_child.so.9")
        shutil.copyfile(release, self.packages / release.name)
        public = self.public_packages()
        path = public / release.name
        self.assertTrue(self.verify(public_packages=public)["publicComplete"])

        with zipfile.ZipFile(path) as archive:
            entries = [(entry, archive.read(entry)) for entry in archive.infolist()]
        for change in ("file-type", "permissions", "host-system"):
            with self.subTest(change=change):
                with zipfile.ZipFile(path, "w") as archive:
                    for entry, content in entries:
                        if entry.filename == link_name:
                            entry.create_system = 0 if change == "host-system" else 3
                            mode = stat.S_IFREG | 0o777 if change == "file-type" else (
                                stat.S_IFLNK | 0o700 if change == "permissions"
                                else stat.S_IFLNK | 0o777)
                            entry.external_attr = mode << 16
                        archive.writestr(entry, content)
                with self.assertRaisesRegex(ValueError, "Public package content differs"):
                    self.verify(public_packages=public)

    def test_public_payload_identity_ignores_timestamps_and_compression(self):
        public = self.public_packages()
        path = public / f"openusd.{VERSION}.nupkg"
        with zipfile.ZipFile(path) as archive:
            entries = [(entry, archive.read(entry)) for entry in archive.infolist()]
        with zipfile.ZipFile(path, "w") as archive:
            for entry, content in entries:
                entry.date_time = (2000, 1, 1, 0, 0, 0)
                entry.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(entry, content)
        self.assertTrue(self.verify(public_packages=public)["publicComplete"])

    def test_public_payload_identity_preserves_directory_attributes(self):
        release = self.released / f"openusd.{VERSION}.nupkg"
        directory = zipfile.ZipInfo("runtimes/linux-x64/native/")
        directory.create_system = 3
        directory.external_attr = (stat.S_IFDIR | 0o755) << 16 | 0x10
        with zipfile.ZipFile(release, "a") as archive:
            archive.writestr(directory, b"")
        shutil.copyfile(release, self.packages / release.name)
        public = self.public_packages()
        path = public / release.name
        self.assertTrue(self.verify(public_packages=public)["publicComplete"])
        with zipfile.ZipFile(path) as archive:
            entries = [(entry, archive.read(entry)) for entry in archive.infolist()]
        with zipfile.ZipFile(path, "w") as archive:
            for entry, content in entries:
                if entry.filename == directory.filename:
                    entry.external_attr = (stat.S_IFDIR | 0o700) << 16 | 0x10
                archive.writestr(entry, content)
        with self.assertRaisesRegex(ValueError, "Public package content differs"):
            self.verify(public_packages=public)

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

    def public_urls(self):
        prefix = "https://api.nuget.org/v3-flatcontainer"
        symbol_prefix = "https://globalcdn.nuget.org/symbol-packages"
        return {
            f"{prefix}/openusd/{VERSION}/openusd.{VERSION}.nupkg":
                self.released / f"openusd.{VERSION}.nupkg",
            f"{prefix}/openusd.runtime/{VERSION}/openusd.runtime.{VERSION}.nupkg":
                self.released / f"openusd.runtime.{VERSION}.nupkg",
            f"{symbol_prefix}/openusd.{VERSION}.snupkg":
                self.released / f"openusd.{VERSION}.snupkg",
        }

    @staticmethod
    def repository_signed(path):
        stream = io.BytesIO(path.read_bytes())
        with zipfile.ZipFile(stream, "a") as archive:
            archive.writestr(".signature.p7s", b"repository signature container")
        return stream.getvalue()

    def test_public_wait_downloads_and_verifies_each_package_and_symbol(self):
        urls = self.public_urls()
        calls = []

        def open_public(url, *, timeout):
            calls.append((url, timeout))
            return io.BytesIO(self.repository_signed(urls[url]))

        public = self.root / "published"
        PROMOTION.wait_for_public_packages(
            self.released, public, self.inventory, VERSION,
            attempts=1, retry_delay=0, opener=open_public,
            sleep=lambda _: self.fail("A complete feed must not be retried."))
        self.assertEqual({url for url, _ in calls}, set(urls))
        self.assertEqual(len(calls), len(urls))
        self.assertTrue(self.verify(public_packages=public)["publicComplete"])

    def test_public_wait_retries_only_missing_or_transient_outputs(self):
        urls = self.public_urls()
        calls = {url: 0 for url in urls}
        package = next(url for url in urls if "/openusd/" in url)
        symbol = next(url for url in urls if url.endswith(".snupkg"))
        sleeps = []

        def open_public(url, *, timeout):
            calls[url] += 1
            if url == package and calls[url] == 1:
                raise urllib.error.HTTPError(url, 404, "not indexed", {}, None)
            if url == package and calls[url] == 2:
                raise urllib.error.HTTPError(url, 503, "unavailable", {}, None)
            if url == symbol and calls[url] == 1:
                raise urllib.error.URLError("temporary network failure")
            return io.BytesIO(self.repository_signed(urls[url]))

        public = self.root / "published"
        PROMOTION.wait_for_public_packages(
            self.released, public, self.inventory, VERSION,
            attempts=3, retry_delay=2, opener=open_public, sleep=sleeps.append)
        self.assertEqual(calls[package], 3)
        self.assertEqual(calls[symbol], 2)
        self.assertEqual(sum(calls.values()), 6)
        self.assertEqual(sleeps, [2, 2])
        self.assertTrue(self.verify(public_packages=public)["publicComplete"])

    def test_public_wait_retries_indexing_and_transient_http_statuses(self):
        urls = self.public_urls()
        first_url = next(url for url in urls if "/openusd/" in url)
        for status in (404, 408, 429, 500, 502, 503, 599):
            with self.subTest(status=status):
                calls = []

                def open_public(url, *, timeout):
                    calls.append(url)
                    if url == first_url and calls.count(first_url) == 1:
                        raise urllib.error.HTTPError(url, status, "temporary", {}, None)
                    return io.BytesIO(urls[url].read_bytes())

                public = self.root / f"published-{status}"
                PROMOTION.wait_for_public_packages(
                    self.released, public, self.inventory, VERSION,
                    attempts=2, retry_delay=0, opener=open_public, sleep=lambda _: None)
                self.assertEqual(calls.count(first_url), 2)
                self.assertTrue(self.verify(public_packages=public)["publicComplete"])

    def test_public_wait_reports_every_missing_output_after_bounded_attempts(self):
        calls = []
        sleeps = []

        def missing(url, *, timeout):
            calls.append(url)
            raise urllib.error.HTTPError(url, 404, "not indexed", {}, None)

        public = self.root / "published"
        with self.assertRaisesRegex(ValueError, "2 attempts") as raised:
            PROMOTION.wait_for_public_packages(
                self.released, public, self.inventory, VERSION,
                attempts=2, retry_delay=3, opener=missing, sleep=sleeps.append)
        for path in self.public_urls().values():
            self.assertIn(path.name, str(raised.exception))
        self.assertEqual(len(calls), 6)
        self.assertEqual(sleeps, [3])
        self.assertEqual(list(public.iterdir()), [])

    def test_public_wait_stops_at_the_global_deadline(self):
        calls = []
        now = [0.0]
        sleeps = []

        def missing(url, *, timeout):
            calls.append((url, timeout))
            raise urllib.error.HTTPError(url, 404, "not indexed", {}, None)

        def advance(seconds):
            sleeps.append(seconds)
            now[0] += seconds

        with self.assertRaisesRegex(ValueError, "after 2 attempts"):
            PROMOTION.wait_for_public_packages(
                self.released, self.root / "published", self.inventory, VERSION,
                attempts=20, retry_delay=2, max_wait=3, opener=missing,
                sleep=advance, clock=lambda: now[0])
        self.assertEqual(len(calls), 6)
        self.assertEqual(sleeps, [2, 1])
        self.assertEqual(now[0], 3)
        self.assertEqual([timeout for _, timeout in calls], [3, 3, 3, 1, 1, 1])

    def test_public_wait_checks_deadline_during_a_slow_download(self):
        now = [0.0]
        calls = []

        class SlowResponse(io.BytesIO):
            def read(self, size=-1):
                raise AssertionError("A buffered read can wait for the entire requested block.")

            def read1(self, size=-1):
                now[0] += 1
                return b"x"

        def open_public(url, *, timeout):
            calls.append((url, timeout))
            return SlowResponse()

        public = self.root / "published"
        with self.assertRaisesRegex(ValueError, "time limit elapsed"):
            PROMOTION.wait_for_public_packages(
                self.released, public, self.inventory, VERSION,
                attempts=2, retry_delay=0, max_wait=3, opener=open_public,
                sleep=lambda _: self.fail("A completed deadline must not be retried."),
                clock=lambda: now[0])
        self.assertEqual(len(calls), 1)
        self.assertEqual(now[0], 3)
        self.assertEqual(list(public.iterdir()), [])

    def test_public_wait_rejects_mismatched_or_invalid_public_bytes(self):
        urls = self.public_urls()
        first_url = next(url for url in urls if "/openusd/" in url)
        different = self.root / "different.nupkg"
        self.write_package(different, payload=b"not the released payload")
        for body, message in ((different.read_bytes(), "Public package content differs"),
                              (b"not an archive", "Invalid package payload")):
            with self.subTest(message=message):
                public = self.root / message.replace(" ", "-")
                calls = []

                def open_public(url, *, timeout):
                    calls.append(url)
                    return io.BytesIO(body if url == first_url else urls[url].read_bytes())

                with self.assertRaisesRegex(ValueError, message):
                    PROMOTION.wait_for_public_packages(
                        self.released, public, self.inventory, VERSION,
                        attempts=3, retry_delay=0, opener=open_public, sleep=lambda _: None)
                self.assertEqual(calls.count(first_url), 1)
                self.assertFalse((public / urls[first_url].name).exists())
                self.assertFalse(any(path.name.endswith(".partial") for path in public.iterdir()))

    def test_public_wait_rejects_nonretryable_http_status_without_sleeping(self):
        calls = []

        def forbidden(url, *, timeout):
            calls.append(url)
            raise urllib.error.HTTPError(url, 403, "forbidden", {}, None)

        with self.assertRaisesRegex(ValueError, "HTTP 403"):
            PROMOTION.wait_for_public_packages(
                self.released, self.root / "published", self.inventory, VERSION,
                attempts=3, retry_delay=1, opener=forbidden,
                sleep=lambda _: self.fail("HTTP 403 must not be retried."))
        self.assertEqual(len(calls), 1)

    def test_public_wait_removes_partial_download_before_retry(self):
        urls = self.public_urls()
        first_url = next(url for url in urls if "/openusd/" in url)
        calls = {url: 0 for url in urls}

        class Interrupted(io.BytesIO):
            def read1(self, size=-1):
                raise ConnectionResetError("connection interrupted")

        def open_public(url, *, timeout):
            calls[url] += 1
            if url == first_url and calls[url] == 1:
                return Interrupted()
            return io.BytesIO(urls[url].read_bytes())

        public = self.root / "published"
        PROMOTION.wait_for_public_packages(
            self.released, public, self.inventory, VERSION,
            attempts=2, retry_delay=0, opener=open_public, sleep=lambda _: None)
        self.assertEqual(calls[first_url], 2)
        self.assertFalse(any(path.name.endswith(".partial") for path in public.iterdir()))
        self.assertTrue(self.verify(public_packages=public)["publicComplete"])

    def test_public_wait_retries_incomplete_chunked_response(self):
        urls = self.public_urls()
        first_url = next(url for url in urls if "/openusd/" in url)
        calls = []

        class IncompleteResponse(io.BytesIO):
            def read1(self, size=-1):
                raise http.client.IncompleteRead(b"truncated")

        def open_public(url, *, timeout):
            calls.append(url)
            if url == first_url and calls.count(first_url) == 1:
                return IncompleteResponse()
            return io.BytesIO(urls[url].read_bytes())

        public = self.root / "published"
        PROMOTION.wait_for_public_packages(
            self.released, public, self.inventory, VERSION,
            attempts=2, retry_delay=0, opener=open_public, sleep=lambda _: None)
        self.assertEqual(calls.count(first_url), 2)
        self.assertFalse(any(path.name.endswith(".partial") for path in public.iterdir()))
        self.assertTrue(self.verify(public_packages=public)["publicComplete"])

    def test_public_wait_retries_a_truncated_response_and_removes_partial_file(self):
        urls = self.public_urls()
        first_url = next(url for url in urls if "/openusd/" in url)
        calls = []

        class Response(io.BytesIO):
            def __init__(self, content, length):
                super().__init__(content)
                self.headers = {"Content-Length": str(length)}

        def open_public(url, *, timeout):
            calls.append(url)
            content = urls[url].read_bytes()
            if url == first_url and calls.count(first_url) == 1:
                return Response(content[:20], len(content))
            return Response(content, len(content))

        public = self.root / "published"
        PROMOTION.wait_for_public_packages(
            self.released, public, self.inventory, VERSION,
            attempts=2, retry_delay=0, opener=open_public, sleep=lambda _: None)
        self.assertEqual(calls.count(first_url), 2)
        self.assertFalse(any(path.name.endswith(".partial") for path in public.iterdir()))
        self.assertTrue(self.verify(public_packages=public)["publicComplete"])

    def test_public_wait_rejects_invalid_options_and_stale_public_directory(self):
        public = self.public_packages()
        for overrides in ({"attempts": 0}, {"attempts": True}, {"retry_delay": -1},
                          {"retry_delay": float("nan")}, {"max_wait": 0},
                          {"max_wait": float("inf")}, {"max_wait": True}):
            with self.subTest(overrides=overrides):
                with self.assertRaises(ValueError):
                    PROMOTION.wait_for_public_packages(
                        self.released, self.root / "empty", self.inventory, VERSION, **overrides)
        with self.assertRaisesRegex(ValueError, "must not contain"):
            PROMOTION.wait_for_public_packages(
                self.released, public, self.inventory, VERSION,
                opener=lambda *_args, **_kwargs: self.fail("Stale files must not prove availability."))

    def test_cli_wait_verifies_inputs_and_complete_public_inventory(self):
        urls = self.public_urls()
        arguments = self.cli_arguments()
        progress = self.root / "public-progress.json"
        arguments.extend(("--public-packages", str(self.root / "published"), "--wait-for-public",
                          "--public-progress", str(progress)))
        output = io.StringIO()
        errors = io.StringIO()

        with mock.patch.object(sys, "argv", arguments), \
                mock.patch.object(
                    PROMOTION.urllib.request, "urlopen",
                    side_effect=lambda url, timeout: io.BytesIO(self.repository_signed(urls[url]))
                ) as open_public, redirect_stdout(output), redirect_stderr(errors):
            self.assertEqual(PROMOTION.main(), 0, errors.getvalue())

        result = json.loads(output.getvalue())
        self.assertEqual(result["commit"], COMMIT)
        self.assertEqual(result["publicPackages"], 2)
        self.assertEqual(result["publicSymbols"], 1)
        self.assertTrue(result["publicComplete"])
        self.assertEqual(open_public.call_count, len(urls))
        recorded = json.loads(progress.read_text(encoding="utf-8"))
        self.assertEqual(recorded["version"], VERSION)
        self.assertEqual(recorded["commit"], COMMIT)
        self.assertEqual(recorded["runId"], "123")
        self.assertTrue(recorded["complete"])
        self.assertEqual(len(recorded["verified"]), 3)
        self.assertEqual(recorded["notVerified"], [])
        self.assertIsNone(recorded["error"])
        self.assertTrue(all(re.fullmatch(r"[0-9a-f]{64}", item["publicSha256"])
                            and re.fullmatch(r"[0-9a-f]{64}", item["releaseSha256"])
                            for item in recorded["verified"]))

    def test_cli_wait_records_verified_subset_when_publication_is_incomplete(self):
        urls = self.public_urls()
        missing_url = next(url for url in urls if url.endswith(".snupkg"))
        arguments = self.cli_arguments()
        progress = self.root / "public-progress.json"
        arguments.extend(("--public-packages", str(self.root / "published"), "--wait-for-public",
                          "--public-attempts", "1", "--public-progress", str(progress)))
        output, errors = io.StringIO(), io.StringIO()

        def open_public(url, *, timeout):
            if url == missing_url:
                raise urllib.error.HTTPError(url, 404, "not indexed", {}, None)
            return io.BytesIO(self.repository_signed(urls[url]))

        with mock.patch.object(sys, "argv", arguments), \
                mock.patch.object(PROMOTION.urllib.request, "urlopen", side_effect=open_public), \
                redirect_stdout(output), redirect_stderr(errors):
            self.assertEqual(PROMOTION.main(), 1)

        recorded = json.loads(progress.read_text(encoding="utf-8"))
        self.assertFalse(recorded["complete"])
        self.assertEqual({item["file"] for item in recorded["verified"]},
                         {path.name for url, path in urls.items() if url != missing_url})
        self.assertEqual(recorded["notVerified"], [urls[missing_url].name])
        self.assertIn("HTTP 404", recorded["error"])
        self.assertIn("HTTP 404", errors.getvalue())
        self.assertEqual(output.getvalue(), "")

    def test_cli_wait_records_progress_for_corrupt_compressed_public_payload(self):
        compressed = io.BytesIO()
        with zipfile.ZipFile(compressed, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            archive.writestr("lib/payload.bin", b"compressed content" * 100)
        corrupt = bytearray(compressed.getvalue())
        with zipfile.ZipFile(io.BytesIO(corrupt)) as archive:
            entry = archive.getinfo("lib/payload.bin")
            name_length, extra_length = struct.unpack_from(
                "<HH", corrupt, entry.header_offset + 26)
            corrupt[entry.header_offset + 30 + name_length + extra_length] = 0xff

        progress = self.root / "public-progress.json"
        arguments = [*self.cli_arguments(), "--public-packages", str(self.root / "published"),
                     "--wait-for-public", "--public-attempts", "1",
                     "--public-progress", str(progress)]
        output, errors = io.StringIO(), io.StringIO()
        with mock.patch.object(sys, "argv", arguments), \
                mock.patch.object(PROMOTION.urllib.request, "urlopen",
                                  return_value=io.BytesIO(corrupt)), \
                redirect_stdout(output), redirect_stderr(errors):
            self.assertEqual(PROMOTION.main(), 1)

        recorded = json.loads(progress.read_text(encoding="utf-8"))
        self.assertFalse(recorded["complete"])
        self.assertEqual(recorded["verified"], [])
        self.assertEqual(len(recorded["notVerified"]), 3)
        self.assertIn("Invalid package payload", recorded["error"])
        self.assertIn("Invalid package payload", errors.getvalue())
        self.assertEqual(output.getvalue(), "")

    def test_public_progress_cannot_report_success_after_public_content_changes(self):
        public = self.public_packages()
        self.write_package(public / f"openusd.{VERSION}.nupkg", payload=b"changed")
        progress = self.root / "public-progress.json"
        with self.assertRaisesRegex(ValueError, "changed while recording"):
            PROMOTION.write_public_progress(
                progress, self.released, public, self.inventory, VERSION, COMMIT, "123", None)
        self.assertFalse(progress.exists())

    def test_cli_wait_reports_both_publication_and_progress_write_failures(self):
        urls = self.public_urls()
        missing_url = next(url for url in urls if url.endswith(".snupkg"))
        arguments = self.cli_arguments()
        arguments.extend(("--public-packages", str(self.root / "published"), "--wait-for-public",
                          "--public-attempts", "1",
                          "--public-progress", str(self.root / "absent" / "progress.json")))
        output, errors = io.StringIO(), io.StringIO()

        def open_public(url, *, timeout):
            if url == missing_url:
                raise urllib.error.HTTPError(url, 404, "not indexed", {}, None)
            return io.BytesIO(urls[url].read_bytes())

        with mock.patch.object(sys, "argv", arguments), \
                mock.patch.object(PROMOTION.urllib.request, "urlopen", side_effect=open_public), \
                redirect_stdout(output), redirect_stderr(errors):
            self.assertEqual(PROMOTION.main(), 1)

        self.assertIn("HTTP 404", errors.getvalue())
        self.assertIn("Could not record public package progress", errors.getvalue())
        self.assertEqual(output.getvalue(), "")

    def test_cli_wait_refuses_invalid_inputs_before_accessing_public_feed(self):
        arguments = self.cli_arguments()
        for extra in (
                ["--wait-for-public"],
                ["--wait-for-public", "--public-packages", str(self.root / "published"),
                 "--allow-partial-public"],
                ["--public-progress", str(self.root / "public-progress.json")]):
            with self.subTest(extra=extra):
                output, errors = io.StringIO(), io.StringIO()
                with mock.patch.object(sys, "argv", [*arguments, *extra]), \
                        mock.patch.object(PROMOTION.urllib.request, "urlopen") as open_public, \
                        redirect_stdout(output), redirect_stderr(errors):
                    self.assertEqual(PROMOTION.main(), 1)
                open_public.assert_not_called()
                self.assertIn("requires", errors.getvalue())
                self.assertEqual(output.getvalue(), "")

        self.write_package(self.packages / f"openusd.{VERSION}.nupkg", payload=b"changed")
        output, errors = io.StringIO(), io.StringIO()
        with mock.patch.object(
                sys, "argv",
                [*arguments, "--wait-for-public", "--public-packages", str(self.root / "published")]
             ), mock.patch.object(PROMOTION.urllib.request, "urlopen") as open_public, \
                redirect_stdout(output), redirect_stderr(errors):
            self.assertEqual(PROMOTION.main(), 1)
        open_public.assert_not_called()
        self.assertIn("Promotion bytes differ", errors.getvalue())
        self.assertEqual(output.getvalue(), "")

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

    def test_workflow_requires_fresh_complete_public_verification_after_push(self):
        workflow = (ROOT / ".github" / "workflows" / "nuget.yml").read_text(encoding="utf-8")
        push = workflow.index("- name: Push to nuget.org")
        verify = workflow.index("- name: Verify published nuget.org packages and symbols")
        retain = workflow.index("- name: Retain public package verification")
        self.assertLess(push, verify)
        self.assertLess(verify, retain)
        block = workflow[verify:retain]
        self.assertIn("github.event.inputs.dry_run != 'true'", block)
        self.assertIn("!cancelled()", block)
        self.assertIn("steps.push.outcome == 'failure'", block)
        self.assertIn("--public-packages public-published", block)
        self.assertIn("--wait-for-public", block)
        self.assertIn("--public-progress", block)
        self.assertIn("--public-attempts", block)
        self.assertNotIn("--allow-partial-public", block)
        self.assertIn("/commits/v$env:PROMOTION_VERSION", block)
        self.assertIn("public-verification.json", block)
        retained = workflow[retain:workflow.index("- name: Dry-run result", retain)]
        self.assertIn("!cancelled()", retained)
        for evidence in ("public-verification.json", "public-verification-progress.json",
                         "promotion-inventory.json",
                         "release-run.json", "release-gate.json"):
            self.assertIn(evidence, retained)
        self.assertIn("if-no-files-found: error", retained)
        summary = workflow[workflow.index("- name: Summary"):]
        self.assertNotIn("Packages promoted", summary)
        self.assertIn("steps.verify_public.outcome", summary)

    def test_required_gate_names_match_release_aggregate(self):
        workflow = (ROOT / ".github" / "workflows" / "release.yml").read_text(encoding="utf-8")
        aggregate = workflow.split("\n  aggregate:\n", 1)[1].split("\n  pack:\n", 1)[0]
        needs = re.search(r"needs:\s*\[([^]]+)]", aggregate)[1]
        self.assertEqual({name.strip() for name in needs.split(",")},
                         PROMOTION.REQUIRED_GATES)


if __name__ == "__main__":
    unittest.main()
