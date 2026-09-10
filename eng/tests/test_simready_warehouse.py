# Copyright (c) marcschier. Licensed under the MIT License.

import hashlib
import importlib.util
import io
import json
from pathlib import Path
import ssl
import sys
import tempfile
import unittest
from unittest.mock import patch
from urllib.error import HTTPError, URLError


ENG = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location(
    "simready_warehouse", ENG.joinpath("simready_warehouse.py")
)
warehouse = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = warehouse
SPEC.loader.exec_module(warehouse)


class Response(io.BytesIO):
    def __init__(self, content, status=200, headers=None):
        super().__init__(content)
        self.status = status
        self.headers = headers or {}


def entry(path, data, lfs=True):
    return warehouse.FileEntry(
        path,
        len(data),
        hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest(),
        hashlib.sha256(data).hexdigest() if lfs else None,
    )


def profile(entries):
    return {
        "schemaVersion": 1,
        "id": "warehouse-01",
        "dataset": "nvidia/PhysicalAI-SimReady-Warehouse-01",
        "revision": "c7fe115cb79c7ddbd0532630d7768b5736b0ecc4",
        "rootScene": "scene.usd",
        "catalog": "catalog.csv",
        "license": "CC-BY-4.0",
        "licenseUrl": "https://creativecommons.org/licenses/by/4.0/",
        "attribution": "Synthetic test data",
        "inventory": {
            "fileCount": len(entries),
            "sourceBytes": sum(item.size for item in entries),
            "sha256": warehouse.inventory_digest(entries),
        },
        "budgets": {"cacheBytes": 20, "outputBytes": 30, "safetyBytes": 10},
    }


class WarehouseAcquisitionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="openusd-warehouse-test-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.data = b"synthetic-warehouse"
        self.entries = [entry("scene.usd", self.data), entry("catalog.csv", b"catalog", False)]
        self.profile = profile(self.entries)
        self.layout = warehouse.Layout(self.root, self.profile)

    def test_inventory_digest_is_order_independent_but_binds_every_identity(self):
        digest = warehouse.inventory_digest(self.entries)
        self.assertEqual(digest, warehouse.inventory_digest(list(reversed(self.entries))))
        altered = [entry("scene.usd", b"another-scene"), self.entries[1]]
        self.assertNotEqual(digest, warehouse.inventory_digest(altered))
        with self.assertRaisesRegex(warehouse.WarehouseError, "pinned inventory"):
            warehouse.validate_inventory(self.profile, altered)

    def test_unsafe_and_case_colliding_paths_are_rejected(self):
        for path in ("../escape", "/absolute", r"C:\outside", r"a\b", "a//b",
                     "dir/CON.txt", "trailing.", "dir/end ", "a:b", "dir/../asset"):
            with self.subTest(path=path), self.assertRaises(warehouse.WarehouseError):
                warehouse.validate_relative_path(path)
        with self.assertRaisesRegex(warehouse.WarehouseError, "collid"):
            warehouse.inventory_digest([entry("A.usd", b"a"), entry("a.usd", b"b")])
        with self.assertRaisesRegex(warehouse.WarehouseError, "collid"):
            warehouse.inventory_digest([entry("A/first.usd", b"a"), entry("a/second.usd", b"b")])
        with self.assertRaisesRegex(warehouse.WarehouseError, "file/directory"):
            warehouse.inventory_digest([entry("a", b"a"), entry("a/b.usd", b"b")])

    def test_file_metadata_requires_exact_integer_sizes_and_correct_lfs_identity(self):
        for size in (-1, True, 1.5, "2"):
            with self.subTest(size=size), self.assertRaises(warehouse.WarehouseError):
                warehouse.FileEntry.from_remote(
                    {"path": "asset.usd", "size": size, "oid": "a" * 40}
                )
        with self.assertRaises(warehouse.WarehouseError):
            warehouse.FileEntry.from_remote(
                {"path": "asset.usd", "size": 2, "oid": "a" * 40,
                 "lfs": {"oid": "b" * 64, "size": 3}}
            )

    def test_low_space_refuses_before_any_payload_or_source_directory(self):
        calls = []
        required = sum(item.size for item in self.entries) + 60
        with self.assertRaises(warehouse.StorageBlockedError) as failure:
            warehouse.download_snapshot(
                self.profile, self.entries, self.layout,
                opener=lambda request: calls.append(request),
                free_bytes=lambda: required - 1,
            )
        self.assertEqual(failure.exception.report["requiredFreeBytes"], required)
        self.assertFalse(calls)
        self.assertFalse(self.layout.source.exists())
        self.assertFalse(self.layout.parts.exists())
        report = warehouse.storage_report(
            self.profile, self.entries, self.layout, free_bytes=lambda: required
        )
        self.assertTrue(report["downloadAdmitted"])

    def test_budgets_and_pinned_profile_cannot_be_weakened_by_cached_metadata(self):
        cached = self.layout.metadata.joinpath("inventory.json")
        cached.parent.mkdir(parents=True)
        warehouse.save_inventory(self.profile, self.entries, self.layout)
        value = json.loads(cached.read_text(encoding="utf-8"))
        value["files"].pop()
        cached.write_text(json.dumps(value), encoding="utf-8")
        with self.assertRaisesRegex(warehouse.WarehouseError, "pinned inventory"):
            warehouse.load_inventory(self.profile, self.layout)

    def test_partial_download_resumes_at_exact_offset_and_publishes_verified_file(self):
        item = self.entries[0]
        part = self.layout.part_path(item)
        part.parent.mkdir(parents=True)
        part.write_bytes(self.data[:5])
        seen = []

        def opener(request):
            seen.append(request)
            return Response(self.data[5:], 206, {
                "Content-Range": f"bytes 5-{len(self.data) - 1}/{len(self.data)}",
                "Content-Length": str(len(self.data) - 5),
            })

        warehouse.download_file(item, self.profile, self.layout, opener)
        self.assertEqual(seen[0].get_header("Range"), "bytes=5-")
        self.assertEqual(self.layout.source_path(item).read_bytes(), self.data)
        self.assertFalse(part.exists())
        warehouse.verify_file(self.layout.source_path(item), item)

    def test_transient_remote_reset_retries_without_losing_the_verified_prefix(self):
        requests = []
        delays = []

        class ResetResponse(Response):
            def read(inner, count=-1):
                if inner.tell() == 0:
                    return super().read(5)
                raise ConnectionResetError(10054, "Remote reset during file transfer")

        def opener(request):
            requests.append(request)
            if len(requests) == 1:
                return ResetResponse(self.data)
            return Response(self.data[5:], 206, {
                "Content-Range": f"bytes 5-{len(self.data) - 1}/{len(self.data)}"
            })

        with patch("time.sleep", side_effect=delays.append):
            warehouse.download_file(self.entries[0], self.profile, self.layout, opener)
        self.assertEqual(len(requests), 2)
        self.assertEqual(requests[1].get_header("Range"), "bytes=5-")
        self.assertEqual(delays, [2])
        self.assertEqual(self.layout.source_path(self.entries[0]).read_bytes(), self.data)

    def test_reset_during_connection_establishment_has_bounded_retries(self):
        calls = []
        delays = []

        def opener(request):
            calls.append(request)
            raise URLError(ConnectionResetError(10054, "Remote reset"))

        with patch("time.sleep", side_effect=delays.append):
            with self.assertRaises(URLError):
                warehouse.download_file(self.entries[0], self.profile, self.layout, opener)
        self.assertEqual(len(calls), 5)
        self.assertEqual(delays, [2, 4, 8, 16])
        self.assertFalse(self.layout.source_path(self.entries[0]).exists())

    def test_server_retry_after_is_respected(self):
        calls = []
        delays = []

        def opener(request):
            calls.append(request)
            if len(calls) == 1:
                raise HTTPError(request.full_url, 429, "Rate limited", {"Retry-After": "17"}, None)
            return Response(self.data)

        with patch("time.sleep", side_effect=delays.append):
            warehouse.download_file(self.entries[0], self.profile, self.layout, opener)
        self.assertEqual(delays, [17])
        self.assertEqual(len(calls), 2)

    def test_permission_errors_are_not_retried(self):
        calls = []
        delays = []

        def opener(request):
            calls.append(request)
            raise HTTPError(request.full_url, 403, "Denied", {}, None)

        with patch("time.sleep", side_effect=delays.append):
            with self.assertRaises(HTTPError):
                warehouse.download_file(self.entries[0], self.profile, self.layout, opener)
        self.assertEqual(len(calls), 1)
        self.assertFalse(delays)

    def test_certificate_and_digest_failures_are_not_retried(self):
        for defect in ("certificate", "digest"):
            with self.subTest(defect=defect):
                calls = []
                delays = []
                item = entry(f"{defect}.usd", self.data)

                def opener(request):
                    calls.append(request)
                    if defect == "certificate":
                        raise URLError(ssl.SSLCertVerificationError("Invalid certificate"))
                    return Response(b"x" * len(self.data))

                with patch("time.sleep", side_effect=delays.append):
                    with self.assertRaises((URLError, warehouse.WarehouseError)):
                        warehouse.download_file(item, self.profile, self.layout, opener)
                self.assertEqual(len(calls), 1)
                self.assertFalse(delays)
                self.assertFalse(self.layout.source_path(item).exists())

    def test_long_retry_after_stops_instead_of_retrying_too_early(self):
        calls = []
        delays = []

        def opener(request):
            calls.append(request)
            raise HTTPError(request.full_url, 429, "Rate limited", {"Retry-After": "600"}, None)

        with patch("time.sleep", side_effect=delays.append):
            with self.assertRaisesRegex(warehouse.WarehouseError, "bounded retry window"):
                warehouse.download_file(self.entries[0], self.profile, self.layout, opener)
        self.assertEqual(len(calls), 1)
        self.assertFalse(delays)

    def test_empty_interrupted_partial_can_resume(self):
        item = self.entries[0]
        part = self.layout.part_path(item)
        part.parent.mkdir(parents=True)
        part.write_bytes(b"")
        warehouse.download_file(item, self.profile, self.layout,
                                lambda request: Response(self.data))
        self.assertEqual(self.layout.source_path(item).read_bytes(), self.data)
        self.assertFalse(part.exists())

    def test_lfs_metadata_cannot_fall_back_to_a_pointer_blob_hash(self):
        with self.assertRaises(warehouse.WarehouseError):
            warehouse.FileEntry.from_remote({
                "path": "asset.usd", "size": 2, "oid": "a" * 40, "lfs": {"size": 2}
            })

    def test_unavailable_volume_is_reported_instead_of_an_unbounded_parent_walk(self):
        with patch.object(Path, "exists", side_effect=[False] * 32):
            with self.assertRaisesRegex(warehouse.WarehouseError, "volume"):
                warehouse.available_space(self.layout)

    def test_interrupted_snapshot_has_no_success_receipt_and_resumes_only_missing_bytes(self):
        calls = []

        def incomplete(request):
            calls.append(request.full_url)
            return Response(b"catalog" if request.full_url.endswith("catalog.csv")
                            else self.data[:5])

        with self.assertRaisesRegex(warehouse.WarehouseError, "ended before"):
            warehouse.download_snapshot(
                self.profile, self.entries, self.layout,
                opener=incomplete, free_bytes=lambda: 1000000, max_attempts=1
            )
        self.assertTrue(self.layout.source_path(self.entries[1]).exists())
        self.assertFalse(self.layout.metadata.joinpath("verified.json").exists())
        resumed = []

        def complete(request):
            resumed.append(request)
            return Response(self.data[5:], 206, {
                "Content-Range": f"bytes 5-{len(self.data) - 1}/{len(self.data)}"
            })

        result = warehouse.download_snapshot(
            self.profile, self.entries, self.layout,
            opener=complete, free_bytes=lambda: 1000000
        )
        self.assertEqual(len(calls), 2)
        self.assertEqual(len(resumed), 1)
        self.assertEqual(resumed[0].get_header("Range"), "bytes=5-")
        self.assertTrue(result["snapshotVerified"])
        self.assertEqual(result["verifiedFiles"], 2)
        self.assertTrue(self.layout.metadata.joinpath("verified.json").exists())

    def test_unexpected_range_response_does_not_append_or_publish(self):
        item = self.entries[0]
        part = self.layout.part_path(item)
        part.parent.mkdir(parents=True)
        part.write_bytes(self.data[:5])
        with self.assertRaisesRegex(warehouse.WarehouseError, "range"):
            warehouse.download_file(item, self.profile, self.layout,
                                    lambda request: Response(self.data))
        self.assertEqual(part.read_bytes(), self.data[:5])
        self.assertFalse(self.layout.source_path(item).exists())

    def test_wrong_payload_length_or_digest_never_publishes(self):
        for data in (self.data[:-1], self.data + b"x", b"x" * len(self.data)):
            with self.subTest(length=len(data)):
                item = entry(f"asset-{len(data)}.usd", self.data)
                with self.assertRaises(warehouse.WarehouseError):
                    warehouse.download_file(
                        item, self.profile, self.layout, lambda request: Response(data), max_attempts=1
                    )
                self.assertFalse(self.layout.source_path(item).exists())

    def test_existing_changed_source_is_never_overwritten(self):
        item = self.entries[0]
        destination = self.layout.source_path(item)
        destination.parent.mkdir(parents=True)
        destination.write_bytes(b"user-authored-change")
        with self.assertRaises(warehouse.WarehouseError):
            warehouse.storage_report(
                self.profile, self.entries, self.layout, free_bytes=lambda: 1000000
            )
        self.assertEqual(destination.read_bytes(), b"user-authored-change")

    def test_git_blob_verification_is_not_plain_file_sha1(self):
        item = self.entries[1]
        path = self.root.joinpath("catalog.csv")
        path.write_bytes(b"catalog")
        warehouse.verify_file(path, item)
        altered = warehouse.FileEntry(item.path, item.size, hashlib.sha1(b"catalog").hexdigest(), None)
        with self.assertRaises(warehouse.WarehouseError):
            warehouse.verify_file(path, altered)

    def test_pagination_cannot_leave_revision_or_reduce_recursive_scope(self):
        initial = warehouse.tree_url(self.profile)
        warehouse.validate_tree_url(initial, self.profile)
        for url in (initial.replace("https://", "http://"),
                    initial.replace("huggingface.co", "example.com"),
                    initial.replace(self.profile["revision"], "main"),
                    initial.replace("recursive=true", "recursive=false")):
            with self.subTest(url=url), self.assertRaises(warehouse.WarehouseError):
                warehouse.validate_tree_url(url, self.profile)

    def test_all_files_can_be_verified_without_native_runtime_or_network(self):
        for item, data in zip(self.entries, (self.data, b"catalog")):
            destination = self.layout.source_path(item)
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(data)
        warehouse.save_inventory(self.profile, self.entries, self.layout)
        loaded = warehouse.load_inventory(self.profile, self.layout)
        self.assertEqual(warehouse.verify_snapshot(loaded, self.layout), 2)
        report = warehouse.storage_report(
            self.profile, loaded, self.layout, free_bytes=lambda: 60
        )
        self.assertEqual(report["missingSourceBytes"], 0)
        self.assertEqual(report["verifiedFiles"], 2)

    def test_only_one_acquisition_owns_a_snapshot_and_release_is_retryable(self):
        with warehouse.snapshot_lock(self.layout):
            with self.assertRaisesRegex(warehouse.WarehouseError, "snapshot lock"):
                with warehouse.snapshot_lock(self.layout):
                    self.fail("Two acquisitions acquired the same snapshot.")
        with warehouse.snapshot_lock(self.layout):
            self.assertFalse(self.layout.source.exists())


if __name__ == "__main__":
    unittest.main()
