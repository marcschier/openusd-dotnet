#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.

import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest import mock
import warnings
import zipfile


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "verify_spirv_cross_provenance", ROOT / "eng" / "verify-spirv-cross-provenance.py",
)
PROVENANCE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROVENANCE)
PROJECT_COMMIT = "a" * 40


class SpirvCrossProvenanceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.project = self.root / "project"
        self.producer = self.root / "producer"
        self.source = self.root / "source"
        self.project.mkdir()
        self.source.mkdir()
        (self.producer / "build" / "nuke" / "Native").mkdir(parents=True)
        (self.project / "Directory.Packages.props").write_text(
            f'<Project><ItemGroup><PackageVersion Include="{PROVENANCE.PACKAGE_ID}" '
            f'Version="{PROVENANCE.PACKAGE_VERSION}" /></ItemGroup></Project>',
            encoding="utf-8",
        )
        self.recipe = 'const char *name = "checked";\n'
        encoded_recipe = self.recipe.replace('"', '""')
        (self.producer / PROVENANCE.RECIPE_PATH).write_text(
            f'const string SPIRVCrossBuildScript = @"{encoded_recipe}";',
            encoding="utf-8",
        )
        self.targets = copy.deepcopy(PROVENANCE.TARGETS)
        self.assets = {}
        for rid, target in self.targets.items():
            asset = f"verified-{rid}".encode("ascii")
            self.assets[rid] = asset
            target["sha256"] = hashlib.sha256(asset).hexdigest()
            target["gitBlob"] = hashlib.sha1(
                f"blob {len(asset)}\0".encode("ascii") + asset,
            ).hexdigest()
        self.package = self.root / "native.nupkg"
        self.write_package()
        self.zig_root = self.root / "zig-sdk"
        (self.zig_root / "lib").mkdir(parents=True)
        self.zig = self.zig_root / "zig"
        self.zig.write_bytes(b"zig executable")
        self.zig_library = self.zig_root / "lib" / "std.zig"
        self.zig_library.write_bytes(b"zig library")
        self.zig_archive = self.root / "zig.tar.xz"
        with tarfile.open(self.zig_archive, "w:xz") as bundle:
            for archive_path, path in (
                    (PROVENANCE.ZIG_ARCHIVE_BINARY, self.zig),
                    ("zig-x86_64-linux-0.15.2/lib/std.zig", self.zig_library)):
                data = path.read_bytes()
                entry = tarfile.TarInfo(archive_path)
                entry.size = len(data)
                bundle.addfile(entry, io.BytesIO(data))
        self.receipt = self.root / "evidence" / "receipt.json"
        self.rebuilt_asset = None
        self.build_commands = []
        for name, value in (
            ("TARGETS", self.targets),
            ("PACKAGE_SHA256", PROVENANCE.file_sha256(self.package)),
            ("RECIPE_SHA256", hashlib.sha256(self.recipe.encode("utf-8")).hexdigest()),
            ("ZIG_ARCHIVE_SHA256", PROVENANCE.file_sha256(self.zig_archive)),
        ):
            patcher = mock.patch.object(PROVENANCE, name, value)
            patcher.start()
            self.addCleanup(patcher.stop)
        self.git_patch = mock.patch.object(PROVENANCE, "git", side_effect=self.fake_git)
        self.git_mock = self.git_patch.start()
        self.addCleanup(self.git_patch.stop)
        self.process_patch = mock.patch.object(
            PROVENANCE.subprocess, "run", side_effect=self.fake_process,
        )
        self.process_mock = self.process_patch.start()
        self.addCleanup(self.process_patch.stop)

    def write_package(self, *, package_id=None, version=None, commit=None, repository_url=None,
                      assets=None, duplicate=None):
        package_id = package_id or PROVENANCE.PACKAGE_ID
        version = version or PROVENANCE.PACKAGE_VERSION
        commit = commit or PROVENANCE.PACKAGE_SOURCE_COMMIT
        repository_url = repository_url or "https://github.com/KhronosGroup/SPIRV-Cross"
        assets = self.assets if assets is None else assets
        nuspec = (
            '<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd">'
            f"<metadata><id>{package_id}</id><version>{version}</version>"
            f'<repository url="{repository_url}" '
            f'commit="{commit}" /></metadata></package>'
        )
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            with zipfile.ZipFile(self.package, "w") as archive:
                archive.writestr("Silk.NET.SPIRV.Cross.Native.nuspec", nuspec)
                for rid, content in assets.items():
                    archive.writestr(self.targets[rid]["package"], content)
                if duplicate:
                    archive.writestr(self.targets[duplicate]["package"], assets[duplicate])

    def fake_git(self, root, *arguments):
        if arguments == ("rev-parse", "HEAD"):
            return {
                self.project: PROJECT_COMMIT,
                self.producer: PROVENANCE.PRODUCER_COMMIT,
                self.source: PROVENANCE.PRODUCER_SOURCE_COMMIT,
            }[root]
        if arguments[:2] == ("status", "--porcelain=v1"):
            return ""
        if root == self.producer and arguments == ("ls-tree", "HEAD", "--", PROVENANCE.GITLINK_PATH):
            return (
                f"160000 commit {PROVENANCE.PRODUCER_SOURCE_COMMIT}\t"
                f"{PROVENANCE.GITLINK_PATH}"
            )
        if root == self.producer and arguments == (
                "ls-tree", PROVENANCE.PACKAGE_SOURCE_COMMIT, "--", PROVENANCE.GITLINK_PATH):
            return (
                f"160000 commit {PROVENANCE.PACKAGE_SOURCE_GITLINK}\t"
                f"{PROVENANCE.GITLINK_PATH}"
            )
        if root == self.producer and arguments == ("cat-file", "-p", PROVENANCE.PRODUCER_BINARY_COMMIT):
            return f"tree {'b' * 40}\nparent {PROVENANCE.PRODUCER_COMMIT}\n\nGenerated binaries"
        if root == self.producer and arguments[:3] == (
                "ls-tree", PROVENANCE.PRODUCER_BINARY_COMMIT, "--"):
            path = arguments[3]
            target = next(value for value in self.targets.values()
                          if path == f"src/Native/{PROVENANCE.PACKAGE_ID}/{value['package']}")
            return f"100755 blob {target['gitBlob']}\t{path}"
        raise AssertionError(f"Unexpected git call: {root}, {arguments}")

    def fake_process(self, arguments, **options):
        if arguments[1:] == ["version"]:
            return mock.Mock(stdout=f"{PROVENANCE.ZIG_VERSION}\n")
        self.build_commands.append((arguments, options))
        target = next(value.removeprefix("-Dtarget=") for value in arguments
                      if value.startswith("-Dtarget="))
        rid = next(key for key, value in self.targets.items() if value["target"] == target)
        output = self.source / self.targets[rid]["built"]
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_bytes(
            self.assets[rid] if self.rebuilt_asset is None else self.rebuilt_asset,
        )
        return mock.Mock(returncode=0)

    def verify(self, rid="win-x64", **overrides):
        args = {
            "project": self.project, "project_commit": PROJECT_COMMIT,
            "producer": self.producer, "source": self.source,
            "package": self.package, "zig_archive": self.zig_archive,
            "zig": self.zig, "rid": rid, "receipt": self.receipt,
            "run_id": "123", "run_attempt": "2",
        }
        args.update(overrides)
        return PROVENANCE.verify(**args)

    def refresh_package_pin(self):
        PROVENANCE.PACKAGE_SHA256 = PROVENANCE.file_sha256(self.package)

    def test_exact_pins_reproduce_and_bind_receipt_for_each_supported_rid(self):
        for rid in self.targets:
            with self.subTest(rid=rid):
                result = self.verify(rid)
                self.assertEqual(json.loads(self.receipt.read_text(encoding="utf-8")), result)
                self.assertEqual(result["projectCommit"], PROJECT_COMMIT)
                self.assertEqual(result["producerCommit"], PROVENANCE.PRODUCER_COMMIT)
                self.assertEqual(result["producerSourceCommit"], PROVENANCE.PRODUCER_SOURCE_COMMIT)
                self.assertEqual(result["packageCommitGitlink"], PROVENANCE.PACKAGE_SOURCE_GITLINK)
                self.assertEqual(result["workflowRunId"], "123")
                self.assertEqual(result["workflowRunAttempt"], "2")
                self.assertEqual(result["rid"], rid)
                self.assertEqual(result["packageAssetSha256"], self.targets[rid]["sha256"])
                self.assertEqual(result["rebuiltAssetSha256"], self.targets[rid]["sha256"])
                self.assertTrue(result["byteIdentical"])
                self.assertEqual(result["licenseReview"], "not-evaluated")
                command, options = self.build_commands[-1]
                self.assertEqual(command[1:], [
                    "build", "-Doptimize=ReleaseFast",
                    f"-Dtarget={self.targets[rid]['target']}", "-j2",
                ])
                self.assertEqual(options["cwd"], self.source)
                self.receipt.unlink()
                (self.source / "build.zig").unlink()
                for output in (self.source / "zig-out").rglob("*"):
                    if output.is_file():
                        output.unlink()
                for output in sorted((self.source / "zig-out").rglob("*"), reverse=True):
                    if output.is_dir():
                        output.rmdir()
                (self.source / "zig-out").rmdir()

    def test_mismatched_rebuild_retains_failure_receipt(self):
        self.rebuilt_asset = b"different native asset"
        with self.assertRaisesRegex(ValueError, "binary differs"):
            self.verify()
        receipt = json.loads(self.receipt.read_text(encoding="utf-8"))
        self.assertFalse(receipt["byteIdentical"])
        self.assertEqual(receipt["packageAssetSha256"], self.targets["win-x64"]["sha256"])
        self.assertEqual(receipt["rebuiltAssetSha256"], hashlib.sha256(self.rebuilt_asset).hexdigest())

    def test_wrong_project_revision_version_or_dirty_source_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "source commit"):
            self.verify(project_commit="b" * 40)
        props = self.project / "Directory.Packages.props"
        props.write_text(props.read_text().replace(PROVENANCE.PACKAGE_VERSION, "9.0.0"))
        with self.assertRaisesRegex(ValueError, "package version"):
            self.verify()
        props.write_text(props.read_text().replace("9.0.0", PROVENANCE.PACKAGE_VERSION))
        self.git_mock.side_effect = lambda root, *args: (
            " M Directory.Packages.props" if root == self.project and args[0] == "status"
            else self.fake_git(root, *args)
        )
        with self.assertRaisesRegex(ValueError, "modified tracked files"):
            self.verify()
        self.assertFalse(self.build_commands)

    def test_wrong_source_commit_dirty_checkout_or_gitlink_is_rejected(self):
        for mode in ("source", "dirty", "gitlink"):
            with self.subTest(mode=mode):
                def altered(root, *args):
                    if mode == "source" and root == self.source and args == ("rev-parse", "HEAD"):
                        return PROVENANCE.PACKAGE_SOURCE_GITLINK
                    if mode == "dirty" and root == self.source and args[0] == "status":
                        return " M spirv_cross.cpp"
                    if mode == "gitlink" and root == self.producer and args[0] == "ls-tree":
                        return f"160000 commit {PROVENANCE.PACKAGE_SOURCE_GITLINK}\t{PROVENANCE.GITLINK_PATH}"
                    return self.fake_git(root, *args)
                self.git_mock.side_effect = altered
                with self.assertRaisesRegex(ValueError, "pinned commit|not clean|gitlink"):
                    self.verify()
                self.assertFalse(self.build_commands)

    def test_binary_handoff_requires_parent_package_gitlink_and_matching_asset(self):
        for mode in ("package-gitlink", "binary-parent", "binary-asset"):
            with self.subTest(mode=mode):
                def altered(root, *args):
                    if (mode == "package-gitlink" and root == self.producer
                            and args[:2] == ("ls-tree", PROVENANCE.PACKAGE_SOURCE_COMMIT)):
                        return f"160000 commit {'0' * 40}\t{PROVENANCE.GITLINK_PATH}"
                    if (mode == "binary-parent" and root == self.producer
                            and args[:2] == ("cat-file", "-p")):
                        return f"tree {'b' * 40}\nparent {'0' * 40}\n\nGenerated binaries"
                    if (mode == "binary-asset" and root == self.producer
                            and args[:2] == ("ls-tree", PROVENANCE.PRODUCER_BINARY_COMMIT)):
                        return f"100755 blob {'0' * 40}\t{args[3]}"
                    return self.fake_git(root, *args)
                self.git_mock.side_effect = altered
                with self.assertRaisesRegex(ValueError, "gitlink|produced directly|binary commit"):
                    self.verify()
                self.assertFalse(self.build_commands)

    def test_archive_hash_and_package_identity_are_both_required(self):
        with self.package.open("ab") as stream:
            stream.write(b"changed")
        with self.assertRaisesRegex(ValueError, "archive hash"):
            self.verify()
        for mutation in (
                {"package_id": "Other.Package"},
                {"version": "9.0.0"},
                {"commit": "b" * 40},
                {"repository_url": "https://github.com/dotnet/Silk.NET"}):
            with self.subTest(mutation=mutation):
                self.write_package(**mutation)
                self.refresh_package_pin()
                with self.assertRaisesRegex(ValueError, "recorded source metadata"):
                    self.verify()
        self.assertFalse(self.build_commands)

    def test_package_asset_must_be_unique_and_match_both_binary_pins(self):
        self.write_package(assets={"linux-x64": self.assets["linux-x64"]})
        self.refresh_package_pin()
        with self.assertRaisesRegex(ValueError, "exactly one"):
            self.verify()
        self.write_package(duplicate="win-x64")
        self.refresh_package_pin()
        with self.assertRaisesRegex(ValueError, "exactly one"):
            self.verify()
        self.write_package(assets={**self.assets, "win-x64": b"modified"})
        self.refresh_package_pin()
        with self.assertRaisesRegex(ValueError, "binary pins"):
            self.verify()
        self.write_package()
        self.refresh_package_pin()
        target = self.targets["win-x64"]
        original_blob, original_hash = target["gitBlob"], target["sha256"]
        target["gitBlob"] = "0" * 40
        with self.assertRaisesRegex(ValueError, "binary pins"):
            self.verify()
        target["gitBlob"], target["sha256"] = original_blob, "0" * 64
        with self.assertRaisesRegex(ValueError, "binary pins"):
            self.verify()
        target["sha256"] = original_hash
        self.assertFalse(self.build_commands)

    def test_recipe_must_be_unique_and_match_pinned_bytes(self):
        path = self.producer / PROVENANCE.RECIPE_PATH
        original = path.read_text(encoding="utf-8")
        path.write_text(original + "\n" + original, encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "exactly one"):
            self.verify()
        path.write_text(original.replace("checked", "changed"), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "recipe does not match"):
            self.verify()
        self.assertFalse(self.build_commands)

    def test_zig_archive_binary_and_version_must_match(self):
        with self.zig_archive.open("ab") as stream:
            stream.write(b"changed")
        with self.assertRaisesRegex(ValueError, "Zig archive hash"):
            self.verify()
        PROVENANCE.ZIG_ARCHIVE_SHA256 = PROVENANCE.file_sha256(self.zig_archive)
        self.zig.write_bytes(b"different compiler")
        with self.assertRaisesRegex(ValueError, "does not match the archive"):
            self.verify()
        self.zig.write_bytes(b"zig executable")
        self.zig_library.write_bytes(b"altered library")
        with self.assertRaisesRegex(ValueError, "does not match the archive"):
            self.verify()
        self.zig_library.write_bytes(b"zig library")
        self.zig_library.unlink()
        with self.assertRaisesRegex(ValueError, "does not match the archive"):
            self.verify()
        self.zig_library.write_bytes(b"zig library")
        extra = self.zig_root / "lib" / "extra.zig"
        extra.write_bytes(b"untracked library")
        with self.assertRaisesRegex(ValueError, "does not match the pinned archive"):
            self.verify()
        extra.unlink()
        self.process_mock.side_effect = lambda *args, **kwargs: mock.Mock(stdout="0.14.0\n")
        with self.assertRaisesRegex(ValueError, "unexpected version"):
            self.verify()
        self.assertFalse(self.build_commands)

    def test_existing_build_or_receipt_is_not_overwritten(self):
        self.receipt.parent.mkdir()
        self.receipt.write_text("existing", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "receipt already exists"):
            self.verify()
        self.assertEqual(self.receipt.read_text(encoding="utf-8"), "existing")
        self.assertFalse(self.build_commands)
        self.receipt.unlink()
        recipe = self.source / "build.zig"
        recipe.write_text("existing", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "generated build files"):
            self.verify()
        self.assertEqual(recipe.read_text(encoding="utf-8"), "existing")
        self.assertFalse(self.build_commands)

    def test_invalid_run_identity_is_rejected_before_build(self):
        for value in ("", "0", "-1", "1/2"):
            with self.subTest(value=value):
                with self.assertRaisesRegex(ValueError, "Positive workflow"):
                    self.verify(run_id=value)
                with self.assertRaisesRegex(ValueError, "Positive workflow"):
                    self.verify(run_attempt=value)
        self.assertFalse(self.build_commands)


if __name__ == "__main__":
    unittest.main()
