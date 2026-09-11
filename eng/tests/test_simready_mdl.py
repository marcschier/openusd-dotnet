# Copyright (c) marcschier. Licensed under the MIT License.

import hashlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import fetch_simready_mdl as modules
import simready_warehouse as acquisition


class Response(io.BytesIO):
    status = 200
    headers = {}


class SimReadyMdlTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="openusd-mdl-acquisition-")
        self.addCleanup(self.temporary.cleanup)
        # Match the acquisition interface's physical-root requirement.
        self.root = Path(self.temporary.name).resolve(strict=True)
        self.content = (
            b"// Redistribution and use in source and binary forms\n"
            b"// Neither the name of NVIDIA\n"
            b"mdl 1.7;\nexport material fixture() = material();\n"
        )
        self.profile = {
            "schemaVersion": 1, "id": "simready-mdl-core", "scope": "Synthetic local fixture",
            "files": [{
                "path": "fixture.mdl", "repository": "NVIDIA/MDL-SDK", "revision": "a" * 40,
                "sourcePath": "fixtures/fixture.mdl", "size": len(self.content),
                "gitBlobSha1": hashlib.sha1(
                    b"blob " + str(len(self.content)).encode("ascii") + b"\0" + self.content
                ).hexdigest(),
            }],
        }
        self.path = self.root.joinpath("profile.json")
        self.write_profile()

    def write_profile(self):
        self.path.write_text(json.dumps(self.profile), encoding="utf-8")

    def test_download_verifies_pinned_source_and_keeps_licence_without_installing_kit(self):
        requests = []

        def opener(request):
            requests.append(request.full_url)
            return Response(self.content)

        report = modules.run(self.path, self.root, "Download", opener)
        self.assertEqual(requests, [
            "https://raw.githubusercontent.com/NVIDIA/MDL-SDK/" + "a" * 40 + "/fixtures/fixture.mdl"
        ])
        self.assertEqual(Path(report["moduleRoot"]).joinpath("fixture.mdl").read_bytes(), self.content)
        self.assertEqual(report["filesVerified"], 1)
        self.assertTrue(report["licenceNoticesPreserved"])
        self.assertFalse(report["kitInstalled"])
        self.assertFalse(report["mdlCompilationVerified"])
        modules.run(self.path, self.root, "Verify", lambda request: self.fail("Verification used the network."))
        modules.run(self.path, self.root, "Download", lambda request: self.fail("Verified file was downloaded again."))

    def test_changed_source_is_refused_without_overwrite_or_network(self):
        report = modules.run(self.path, self.root, "Download", lambda request: Response(self.content))
        path = Path(report["moduleRoot"]).joinpath("fixture.mdl")
        path.write_bytes(b"changed after acquisition")
        with self.assertRaises(acquisition.WarehouseError):
            modules.run(self.path, self.root, "Download", lambda request: self.fail("Changed file was overwritten."))
        self.assertEqual(path.read_bytes(), b"changed after acquisition")

    def test_wrong_git_blob_never_publishes_a_module(self):
        with self.assertRaises(acquisition.WarehouseError):
            modules.run(self.path, self.root, "Download",
                        lambda request: Response(b"x" * len(self.content)))
        self.assertEqual(list(self.root.glob("warehouse-01/dependencies/*/modules/*.mdl")), [])

    def test_profile_refuses_unpinned_foreign_or_non_module_inputs(self):
        for field, value in (
                ("repository", "other/unknown"), ("revision", "main"),
                ("path", "../outside.mdl"), ("sourcePath", "../outside.mdl"),
                ("path", "run.py"), ("size", 1048577)):
            with self.subTest(field=field, value=value):
                original = self.profile["files"][0][field]
                self.profile["files"][0][field] = value
                self.write_profile()
                with self.assertRaises(acquisition.WarehouseError):
                    modules.read_files(self.path)
                self.profile["files"][0][field] = original
        self.write_profile()

    def test_missing_licence_does_not_generate_a_verification_receipt(self):
        self.content = b"mdl 1.7;\nexport material fixture() = material();\n"
        item = self.profile["files"][0]
        item["size"] = len(self.content)
        item["gitBlobSha1"] = hashlib.sha1(
            b"blob " + str(len(self.content)).encode("ascii") + b"\0" + self.content
        ).hexdigest()
        self.write_profile()
        with self.assertRaisesRegex(acquisition.WarehouseError, "licence"):
            modules.run(self.path, self.root, "Download", lambda request: Response(self.content))
        self.assertEqual(list(self.root.glob("warehouse-01/dependencies/*/metadata/verified.json")), [])


if __name__ == "__main__":
    unittest.main()
