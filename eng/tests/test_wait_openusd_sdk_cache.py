# Copyright (c) marcschier. Licensed under the MIT License.

import contextlib
import http.client
import importlib.util
import io
from pathlib import Path
import unittest
from unittest import mock
import urllib.error


SCRIPT = Path(__file__).resolve().parents[1] / "wait-openusd-sdk-cache.py"
SPEC = importlib.util.spec_from_file_location("wait_openusd_sdk_cache", SCRIPT)
readiness = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(readiness)

REPOSITORY = "owner/repository"
TOKEN = "test-token-must-not-be-logged"
JOBS_PATH = "actions/runs/123/jobs?per_page=100&page=1"


def http_error(status):
    return urllib.error.HTTPError("https://api.github.com/test", status, "test failure", None, None)


class SdkReadinessApiTests(unittest.TestCase):
    def test_server_error_retries_the_same_operation_and_returns_its_response(self):
        response = io.BytesIO(b'{"total_count": 0, "jobs": []}')
        stderr = io.StringIO()
        with (
            mock.patch.object(readiness.urllib.request, "urlopen", side_effect=[http_error(500), response]) as opener,
            mock.patch.object(readiness.time, "sleep") as sleep,
            contextlib.redirect_stderr(stderr),
        ):
            result = readiness.read_api(REPOSITORY, TOKEN, JOBS_PATH)

        self.assertEqual(result, {"total_count": 0, "jobs": []})
        self.assertEqual(opener.call_count, 2)
        self.assertEqual(opener.call_args_list[0].args[0].full_url, opener.call_args_list[1].args[0].full_url)
        self.assertEqual(sleep.call_count, 1)
        self.assertIn("native-jobs", stderr.getvalue())
        self.assertIn("HTTP 500", stderr.getvalue())
        self.assertNotIn(TOKEN, stderr.getvalue())

    def test_repeated_server_errors_end_in_a_bounded_failure(self):
        stderr = io.StringIO()
        with (
            mock.patch.object(
                readiness.urllib.request, "urlopen", side_effect=[http_error(500), http_error(502), http_error(503)]
            ) as opener,
            mock.patch.object(readiness.time, "sleep") as sleep,
            contextlib.redirect_stderr(stderr),
            self.assertRaises(readiness.CacheReadinessError) as raised,
        ):
            readiness.read_api(REPOSITORY, TOKEN, JOBS_PATH)

        self.assertEqual(opener.call_count, 3)
        self.assertEqual(sleep.call_count, 2)
        self.assertIn("native-jobs", str(raised.exception))
        self.assertIn("HTTP 503", str(raised.exception))
        self.assertNotIn(TOKEN, stderr.getvalue() + str(raised.exception))

    def test_transport_error_retries_without_exposing_its_details(self):
        errors = (urllib.error.URLError(TOKEN), TimeoutError(TOKEN), ConnectionResetError(TOKEN))
        for error in errors:
            with self.subTest(error=type(error).__name__):
                response = io.BytesIO(b'{"total_count": 0, "jobs": []}')
                stderr = io.StringIO()
                with (
                    mock.patch.object(
                        readiness.urllib.request,
                        "urlopen",
                        side_effect=[error, response],
                    ) as opener,
                    mock.patch.object(readiness.time, "sleep") as sleep,
                    contextlib.redirect_stderr(stderr),
                ):
                    result = readiness.read_api(REPOSITORY, TOKEN, JOBS_PATH)

                self.assertEqual(result["jobs"], [])
                self.assertEqual(opener.call_count, 2)
                sleep.assert_called_once_with(1)
                self.assertIn("transport unavailable", stderr.getvalue())
                self.assertNotIn(TOKEN, stderr.getvalue())

    def test_truncated_response_retries_without_exposing_partial_content(self):
        class TruncatedResponse(io.BytesIO):
            def read(self, size=-1):
                raise http.client.IncompleteRead(TOKEN.encode(), 1)

        response = io.BytesIO(b'{"total_count": 0, "jobs": []}')
        stderr = io.StringIO()
        with (
            mock.patch.object(
                readiness.urllib.request,
                "urlopen",
                side_effect=[TruncatedResponse(), response],
            ) as opener,
            mock.patch.object(readiness.time, "sleep") as sleep,
            contextlib.redirect_stderr(stderr),
        ):
            result = readiness.read_api(REPOSITORY, TOKEN, JOBS_PATH)

        self.assertEqual(result["jobs"], [])
        self.assertEqual(opener.call_count, 2)
        sleep.assert_called_once_with(1)
        self.assertIn("transport unavailable", stderr.getvalue())
        self.assertNotIn(TOKEN, stderr.getvalue())

    def test_repeated_transport_errors_end_in_a_bounded_failure(self):
        stderr = io.StringIO()
        with (
            mock.patch.object(
                readiness.urllib.request,
                "urlopen",
                side_effect=[urllib.error.URLError(TOKEN), TimeoutError(TOKEN), ConnectionResetError(TOKEN)],
            ) as opener,
            mock.patch.object(readiness.time, "sleep") as sleep,
            contextlib.redirect_stderr(stderr),
            self.assertRaises(readiness.CacheReadinessError) as raised,
        ):
            readiness.read_api(REPOSITORY, TOKEN, JOBS_PATH)

        self.assertEqual(opener.call_count, 3)
        self.assertEqual(sleep.call_args_list, [mock.call(1), mock.call(2)])
        self.assertIn("transport unavailable", str(raised.exception))
        self.assertNotIn(TOKEN, stderr.getvalue() + str(raised.exception))

    def test_client_errors_are_not_retried(self):
        for status in (400, 401, 403, 404):
            with self.subTest(status=status):
                with (
                    mock.patch.object(readiness.urllib.request, "urlopen", side_effect=http_error(status)) as opener,
                    mock.patch.object(readiness.time, "sleep") as sleep,
                    self.assertRaises(readiness.CacheReadinessError) as raised,
                ):
                    readiness.read_api(REPOSITORY, TOKEN, JOBS_PATH)
                self.assertEqual(opener.call_count, 1)
                sleep.assert_not_called()
                self.assertIn(f"HTTP {status}", str(raised.exception))
                self.assertNotIn(TOKEN, str(raised.exception))

    def test_invalid_or_oversized_response_is_not_retried(self):
        for raw in (b"{", b"x" * 4194305):
            with self.subTest(length=len(raw)):
                with (
                    mock.patch.object(readiness.urllib.request, "urlopen", return_value=io.BytesIO(raw)) as opener,
                    mock.patch.object(readiness.time, "sleep") as sleep,
                    self.assertRaises(readiness.CacheReadinessError),
                ):
                    readiness.read_api(REPOSITORY, TOKEN, JOBS_PATH)
                self.assertEqual(opener.call_count, 1)
                sleep.assert_not_called()

    def test_unexpected_operations_are_rejected_without_network_access(self):
        with (
            mock.patch.object(readiness.urllib.request, "urlopen") as opener,
            self.assertRaises(readiness.CacheReadinessError),
        ):
            readiness.read_api(REPOSITORY, TOKEN, "unrelated/metadata")
        opener.assert_not_called()


if __name__ == "__main__":
    unittest.main()
