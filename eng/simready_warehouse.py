# Copyright (c) marcschier. Licensed under the MIT License.

"""Pinned, bounded acquisition of the complete SimReady warehouse snapshot."""

from __future__ import annotations

import argparse
from contextlib import contextmanager
from dataclasses import asdict, dataclass
from email.utils import parsedate_to_datetime
import hashlib
from http.client import IncompleteRead, RemoteDisconnected
import json
import math
import os
from pathlib import Path
import re
import shutil
import ssl
import stat
import sys
import tempfile
import time
from urllib.error import HTTPError, URLError
from urllib.parse import parse_qs, quote, urlsplit
from urllib.request import Request, urlopen


MAX_FILES = 50_000
MAX_PAGES = 100
MAX_METADATA_BYTES = 8 * 1024 * 1024
CHUNK_BYTES = 1024 * 1024
DATASET = "nvidia/PhysicalAI-SimReady-Warehouse-01"
MAX_DOWNLOAD_ATTEMPTS = 5
MAX_RETRY_DELAY_SECONDS = 300


class WarehouseError(RuntimeError):
    pass


class IncompleteDownloadError(WarehouseError):
    pass


class StorageBlockedError(WarehouseError):
    def __init__(self, report):
        self.report = report
        super().__init__(
            "Insufficient storage: "
            f"{report['requiredFreeBytes']} bytes required, "
            f"{report['availableFreeBytes']} bytes available. "
            "No further asset payload will be requested; free space on the approved data volume. "
            "Already verified files and resumable partials are preserved."
        )


def validate_relative_path(path: str) -> tuple[str, ...]:
    if not isinstance(path, str) or not path or len(path.encode("utf-8")) > 4096:
        raise WarehouseError("An asset path must be a bounded relative string.")
    parts = tuple(path.split("/"))
    for part in parts:
        stem = part.split(".", 1)[0].upper()
        if (part in ("", ".", "..") or part.endswith((" ", "."))
                or len(part) > 255 or re.search(r'[<>:"\\|?*\x00-\x1f]', part)
                or stem in {"CON", "PRN", "AUX", "NUL"}
                or re.fullmatch(r"(COM|LPT)[0-9]", stem)):
            raise WarehouseError(f"Unsafe or nonportable asset path: {path}")
    return parts


def integer(value, name: str) -> int:
    if type(value) is not int or not 0 <= value <= (1 << 63) - 1:
        raise WarehouseError(f"{name} must be a nonnegative 64-bit integer.")
    return value


@dataclass(frozen=True)
class FileEntry:
    path: str
    size: int
    oid: str
    lfs_oid: str | None

    def __post_init__(self):
        validate_relative_path(self.path)
        integer(self.size, "File size")
        if not isinstance(self.oid, str) or not re.fullmatch(r"[0-9a-f]{40}", self.oid):
            raise WarehouseError("A file must have its exact Git blob identity.")
        if self.lfs_oid is not None and (
                not isinstance(self.lfs_oid, str)
                or not re.fullmatch(r"[0-9a-f]{64}", self.lfs_oid)):
            raise WarehouseError("An LFS file must have its exact SHA256 identity.")

    @classmethod
    def from_remote(cls, value):
        lfs = value.get("lfs")
        if lfs is not None and (
                not isinstance(lfs, dict) or lfs.get("size") != value.get("size")
                or not isinstance(lfs.get("oid"), str)):
            raise WarehouseError("LFS and tree metadata disagree on the file extent.")
        return cls(value["path"], value["size"], value["oid"],
                   None if lfs is None else lfs.get("oid"))


def ordered_entries(entries):
    if not 1 <= len(entries) <= MAX_FILES:
        raise WarehouseError("The file inventory exceeds its entry limit or is empty.")
    ordered = sorted(entries, key=lambda item: item.path)
    names = {item.path.casefold() for item in ordered}
    if len(names) != len(ordered):
        raise WarehouseError("The inventory contains duplicate or case-colliding paths.")
    spellings = {}
    for item in ordered:
        parts = item.path.split("/")
        for index in range(1, len(parts) + 1):
            spelling = "/".join(parts[:index])
            key = spelling.casefold()
            if key in spellings and spellings[key] != spelling:
                raise WarehouseError("The inventory contains case-colliding directory paths.")
            spellings[key] = spelling
        if any("/".join(parts[:index]).casefold() in names
               for index in range(1, len(parts))):
            raise WarehouseError("The inventory contains a file/directory conflict.")
    return ordered


def inventory_digest(entries) -> str:
    digest = hashlib.sha256()
    for item in ordered_entries(entries):
        digest.update(
            f"{item.path}\0{item.size}\0{item.oid}\0{item.lfs_oid or ''}\n".encode("utf-8")
        )
    return digest.hexdigest()


def validate_inventory(profile, entries):
    pinned = profile["inventory"]
    if (len(entries) != pinned["fileCount"]
            or sum(item.size for item in entries) != pinned["sourceBytes"]
            or inventory_digest(entries) != pinned["sha256"]):
        raise WarehouseError("The file list does not match the repository-pinned inventory.")
    names = {item.path for item in entries}
    if profile["rootScene"] not in names or profile["catalog"] not in names:
        raise WarehouseError("The inventory is missing its root scene or catalogue.")


def load_profile(path: Path):
    value = json.loads(path.read_text(encoding="utf-8"))
    if (value.get("schemaVersion") != 1 or value.get("dataset") != DATASET
            or value.get("id") != "warehouse-01"
            or not re.fullmatch(r"[0-9a-f]{40}", value.get("revision", ""))):
        raise WarehouseError("The warehouse profile identity is invalid.")
    validate_relative_path(value["rootScene"])
    validate_relative_path(value["catalog"])
    pinned = value["inventory"]
    if not 1 <= integer(pinned["fileCount"], "File count") <= MAX_FILES:
        raise WarehouseError("The pinned file count exceeds its bound.")
    integer(pinned["sourceBytes"], "Source bytes")
    if not re.fullmatch(r"[0-9a-f]{64}", pinned["sha256"]):
        raise WarehouseError("The inventory requires a pinned SHA256.")
    for name in ("cacheBytes", "outputBytes", "safetyBytes"):
        integer(value["budgets"][name], name)
    return value


def reject_reparse(path: Path):
    for current in (path, *path.parents):
        try:
            info = current.lstat()
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
            raise WarehouseError(f"Reparse points are not admitted in the data tree: {current}")


def make_directory(path: Path):
    reject_reparse(path)
    path.mkdir(parents=True, exist_ok=True)
    reject_reparse(path)


class Layout:
    def __init__(self, data_root: Path, profile):
        if not data_root.is_absolute():
            raise WarehouseError("DataRoot must be an absolute physical directory.")
        reject_reparse(data_root)
        self.base = data_root.joinpath(profile["id"])
        self.source = self.base.joinpath("source", profile["revision"])
        self.parts = self.base.joinpath("parts", profile["revision"])
        self.metadata = self.base.joinpath("metadata", profile["revision"])

    def source_path(self, item: FileEntry):
        path = self.source.joinpath(*validate_relative_path(item.path))
        if os.name == "nt" and len(str(path)) >= 260:
            raise WarehouseError(f"Use a shorter physical data root for this asset: {path}")
        reject_reparse(path)
        return path

    def part_path(self, item: FileEntry):
        path = self.parts.joinpath(hashlib.sha256(item.path.encode("utf-8")).hexdigest() + ".partial")
        reject_reparse(path)
        return path


@contextmanager
def snapshot_lock(layout):
    make_directory(layout.metadata)
    path = layout.metadata.joinpath("acquisition.lock")
    reject_reparse(path)
    with path.open("a+b") as stream:
        if stream.tell() == 0:
            stream.write(b"\0")
            stream.flush()
        stream.seek(0)
        if os.name == "nt":
            import msvcrt

            acquire = lambda: msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
            release = lambda: msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
        else:
            import fcntl

            acquire = lambda: fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
            release = lambda: fcntl.flock(stream.fileno(), fcntl.LOCK_UN)
        try:
            acquire()
        except OSError as error:
            raise WarehouseError(
                "Could not acquire the snapshot lock; another acquisition may be active."
            ) from error
        try:
            yield
        finally:
            stream.seek(0)
            release()


def tree_url(profile):
    return (f"https://huggingface.co/api/datasets/{profile['dataset']}/tree/"
            f"{profile['revision']}?recursive=true&expand=false&limit=1000")


def validate_tree_url(url, profile):
    parsed = urlsplit(url)
    expected = urlsplit(tree_url(profile))
    query = parse_qs(parsed.query)
    if (parsed.scheme != "https" or parsed.netloc != "huggingface.co"
            or parsed.path != expected.path or parsed.fragment
            or query.get("recursive") != ["true"]
            or query.get("expand") != ["false"] or query.get("limit") != ["1000"]):
        raise WarehouseError("A metadata continuation left the pinned complete dataset tree.")


def open_http(request):
    return urlopen(request, timeout=60)


def fetch_inventory(profile, opener=open_http):
    url = tree_url(profile)
    seen = set()
    entries = []
    while url:
        validate_tree_url(url, profile)
        if url in seen or len(seen) >= MAX_PAGES:
            raise WarehouseError("Metadata pagination repeated or exceeded its page bound.")
        seen.add(url)
        request = Request(url, headers={
            "Accept": "application/json", "User-Agent": "OpenUsd-Warehouse-Acquisition"
        })
        with opener(request) as response:
            data = response.read(MAX_METADATA_BYTES + 1)
            if len(data) > MAX_METADATA_BYTES:
                raise WarehouseError("The metadata response exceeded its byte bound.")
            rows = json.loads(data)
            link = response.headers.get("Link", "")
        if not isinstance(rows, list) or len(rows) > 1000:
            raise WarehouseError("The metadata page shape is invalid.")
        for row in rows:
            if not isinstance(row, dict):
                raise WarehouseError("A metadata tree entry is not an object.")
            if row.get("type") == "file":
                entries.append(FileEntry.from_remote(row))
            elif row.get("type") != "directory":
                raise WarehouseError("The dataset contains an unsupported tree entry.")
            if len(entries) > MAX_FILES:
                raise WarehouseError("The inventory exceeded its file bound.")
        matches = re.findall(r'<([^>]+)>;\s*rel="next"', link)
        if len(matches) > 1 or (link and not matches):
            raise WarehouseError("The metadata continuation is malformed.")
        url = matches[0] if matches else None
    validate_inventory(profile, entries)
    return ordered_entries(entries)


def write_json(path: Path, value):
    make_directory(path.parent)
    reject_reparse(path)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(
                mode="w", encoding="utf-8", newline="\n",
                dir=path.parent, prefix=".inventory-", delete=False) as stream:
            temporary = Path(stream.name)
            json.dump(value, stream, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def save_inventory(profile, entries, layout):
    validate_inventory(profile, entries)
    write_json(layout.metadata.joinpath("inventory.json"), {
        "schemaVersion": 1,
        "dataset": profile["dataset"],
        "revision": profile["revision"],
        "inventorySha256": inventory_digest(entries),
        "attribution": profile["attribution"],
        "license": profile["license"],
        "licenseUrl": profile["licenseUrl"],
        "files": [asdict(item) for item in ordered_entries(entries)],
    })


def load_inventory(profile, layout):
    path = layout.metadata.joinpath("inventory.json")
    reject_reparse(path)
    with path.open("rb") as stream:
        data = stream.read(32 * 1024 * 1024 + 1)
    if len(data) > 32 * 1024 * 1024:
        raise WarehouseError("The saved inventory exceeded its byte bound.")
    value = json.loads(data)
    if (value.get("schemaVersion") != 1 or value.get("dataset") != profile["dataset"]
            or value.get("revision") != profile["revision"]):
        raise WarehouseError("The saved inventory belongs to a different snapshot.")
    if not isinstance(value.get("files"), list) or not 1 <= len(value["files"]) <= MAX_FILES:
        raise WarehouseError("The saved inventory has an invalid file count.")
    entries = [FileEntry(**item) for item in value["files"]]
    validate_inventory(profile, entries)
    return ordered_entries(entries)


def verify_file(path: Path, item: FileEntry):
    reject_reparse(path)
    with path.open("rb") as stream:
        before = os.fstat(stream.fileno())
        if not stat.S_ISREG(before.st_mode) or before.st_size != item.size:
            raise WarehouseError(f"File extent changed; existing source will not be overwritten: {path}")
        if item.lfs_oid is not None:
            digest = hashlib.sha256()
            expected = item.lfs_oid
        else:
            digest = hashlib.sha1(usedforsecurity=False)
            digest.update(f"blob {item.size}\0".encode("ascii"))
            expected = item.oid
        remaining = item.size
        while remaining:
            chunk = stream.read(min(CHUNK_BYTES, remaining))
            if not chunk:
                raise WarehouseError(f"The file changed while being verified: {path}")
            digest.update(chunk)
            remaining -= len(chunk)
        if stream.read(1):
            raise WarehouseError(f"The file grew while being verified: {path}")
        after = os.fstat(stream.fileno())
        if (after.st_size != before.st_size or after.st_mtime_ns != before.st_mtime_ns
                or digest.hexdigest() != expected):
            raise WarehouseError(f"File digest changed; existing source will not be overwritten: {path}")


def available_space(layout):
    path = layout.base
    while not path.exists():
        if path.parent == path:
            raise WarehouseError(f"The approved data volume is unavailable: {path}")
        path = path.parent
    reject_reparse(path)
    return shutil.disk_usage(path).free


def storage_report(profile, entries, layout, free_bytes=None):
    validate_inventory(profile, entries)
    missing = 0
    partial_bytes = 0
    verified = 0
    for item in entries:
        destination = layout.source_path(item)
        if destination.exists():
            verify_file(destination, item)
            verified += 1
            continue
        part = layout.part_path(item)
        length = part.stat().st_size if part.exists() else 0
        if length > item.size:
            raise WarehouseError(f"A partial file exceeds its pinned size: {part}")
        partial_bytes += length
        missing += item.size - length
    reserve = sum(integer(profile["budgets"][key], key)
                  for key in ("cacheBytes", "outputBytes", "safetyBytes"))
    free = integer(free_bytes() if free_bytes else available_space(layout), "Free bytes")
    return {
        "dataset": profile["dataset"], "revision": profile["revision"],
        "sourceRoot": str(layout.source), "files": len(entries),
        "sourceBytes": sum(item.size for item in entries),
        "verifiedFiles": verified, "resumablePartialBytes": partial_bytes,
        "missingSourceBytes": missing, "reservedCacheOutputSafetyBytes": reserve,
        "requiredFreeBytes": missing + reserve, "availableFreeBytes": free,
        "downloadAdmitted": free >= missing + reserve,
        "snapshotVerified": verified == len(entries),
    }


def retryable_transfer(error):
    if isinstance(error, HTTPError):
        return error.code in (429, 500, 502, 503, 504)
    if isinstance(error, URLError):
        return retryable_transfer(error.reason)
    return isinstance(error, (ConnectionError, TimeoutError, IncompleteRead,
                              RemoteDisconnected, ssl.SSLEOFError, IncompleteDownloadError))


def transfer_retry_delay(error, attempt):
    delay = 2 ** (attempt + 1)
    if isinstance(error, HTTPError) and error.headers.get("Retry-After") is not None:
        value = error.headers["Retry-After"].strip()
        if value.isdigit():
            requested = int(value)
        else:
            try:
                requested = math.ceil(parsedate_to_datetime(value).timestamp() - time.time())
            except (ValueError, TypeError, OverflowError) as invalid:
                raise WarehouseError("The server supplied an invalid Retry-After value.") from invalid
        if requested > MAX_RETRY_DELAY_SECONDS:
            raise WarehouseError(
                "The server requested a retry delay beyond this command's bounded retry window. "
                "Wait for its Retry-After interval before resuming."
            ) from error
        delay = max(delay, requested)
    return delay


def download_file(item, profile, layout, opener=open_http, *, max_attempts=MAX_DOWNLOAD_ATTEMPTS,
                  source_url=None):
    if type(max_attempts) is not int or not 1 <= max_attempts <= MAX_DOWNLOAD_ATTEMPTS:
        raise WarehouseError("A transfer requires one to five bounded attempts.")
    for attempt in range(max_attempts):
        try:
            download_file_once(item, profile, layout, opener, source_url=source_url)
            return
        except (URLError, ConnectionError, TimeoutError, IncompleteRead,
                RemoteDisconnected, ssl.SSLEOFError, IncompleteDownloadError) as error:
            if not retryable_transfer(error) or attempt + 1 == max_attempts:
                if isinstance(error, HTTPError):
                    error.close()
                raise
            try:
                delay = transfer_retry_delay(error, attempt)
            finally:
                if isinstance(error, HTTPError):
                    error.close()
            print(
                f"WAREHOUSE_RETRY file={item.path} attempt={attempt + 2}/{max_attempts} "
                f"delaySeconds={delay} cause={type(error).__name__}",
                file=sys.stderr, flush=True,
            )
            time.sleep(delay)


def download_file_once(item, profile, layout, opener, *, source_url=None):
    destination = layout.source_path(item)
    if destination.exists():
        verify_file(destination, item)
        return
    part = layout.part_path(item)
    make_directory(part.parent)
    offset = part.stat().st_size if part.exists() else 0
    if offset > item.size:
        raise WarehouseError(f"A partial file exceeds its pinned extent: {part}")
    if offset != item.size or not part.exists():
        url = source_url or (
            f"https://huggingface.co/datasets/{profile['dataset']}/resolve/"
            f"{profile['revision']}/{quote(item.path, safe='/')}")
        headers = {"User-Agent": "OpenUsd-Warehouse-Acquisition", "Accept-Encoding": "identity"}
        if offset:
            headers["Range"] = f"bytes={offset}-"
        with opener(Request(url, headers=headers)) as response:
            if offset:
                expected_range = f"bytes {offset}-{item.size - 1}/{item.size}"
                if response.status != 206 or response.headers.get("Content-Range") != expected_range:
                    raise WarehouseError("The download server did not honor the exact pinned resume range.")
            elif response.status != 200:
                raise WarehouseError("The download server did not return a complete file response.")
            length = response.headers.get("Content-Length")
            if length is not None:
                if not re.fullmatch(r"[0-9]+", length) or int(length) != item.size - offset:
                    raise WarehouseError("The download response disagrees with its pinned extent.")
            reject_reparse(part)
            with part.open("ab" if part.exists() else "xb") as stream:
                remaining = item.size - offset
                while chunk := response.read(min(CHUNK_BYTES, remaining + 1)):
                    if len(chunk) > remaining:
                        raise WarehouseError("The download exceeded its pinned extent.")
                    stream.write(chunk)
                    remaining -= len(chunk)
                if remaining:
                    raise IncompleteDownloadError(
                        "The download ended before its pinned extent; its partial is resumable."
                    )
                stream.flush()
                os.fsync(stream.fileno())
    verify_file(part, item)
    make_directory(destination.parent)
    reject_reparse(destination)
    # Both paths are on the approved volume. A hard link publishes create-only,
    # without overwriting an existing source or temporarily duplicating the file.
    os.link(part, destination)
    part.unlink()


def verify_snapshot(entries, layout):
    for item in entries:
        verify_file(layout.source_path(item), item)
    return len(entries)


def download_snapshot(profile, entries, layout, opener=open_http, free_bytes=None, *,
                      max_attempts=MAX_DOWNLOAD_ATTEMPTS, progress=None):
    report = storage_report(profile, entries, layout, free_bytes)
    if not report["downloadAdmitted"]:
        raise StorageBlockedError(report)
    remaining = report["missingSourceBytes"]
    reserve = report["reservedCacheOutputSafetyBytes"]
    completed = report["verifiedFiles"]
    published_bytes = sum(item.size for item in entries if layout.source_path(item).exists())
    if progress is not None:
        progress(completed, len(entries), published_bytes, report["sourceBytes"])
    for item in ordered_entries(entries):
        destination = layout.source_path(item)
        if destination.exists():
            continue
        free = free_bytes() if free_bytes else available_space(layout)
        if free < remaining + reserve:
            raise StorageBlockedError({
                **report, "requiredFreeBytes": remaining + reserve,
                "availableFreeBytes": free, "downloadAdmitted": False,
            })
        part = layout.part_path(item)
        offset = part.stat().st_size if part.exists() else 0
        download_file(item, profile, layout, opener, max_attempts=max_attempts)
        remaining -= item.size - offset
        completed += 1
        published_bytes += item.size
        if progress is not None and (completed % 64 == 0 or completed == len(entries)):
            progress(completed, len(entries), published_bytes, report["sourceBytes"])
    verify_snapshot(entries, layout)
    write_json(layout.metadata.joinpath("verified.json"), {
        "schemaVersion": 1, "dataset": profile["dataset"], "revision": profile["revision"],
        "inventorySha256": inventory_digest(entries), "verifiedFiles": len(entries),
        "sourceBytes": sum(item.size for item in entries),
        "note": "A verification receipt, not authority to skip integrity checks after changes.",
    })
    return storage_report(profile, entries, layout, free_bytes)


def report_progress(completed, total, published_bytes, total_bytes):
    print(
        f"WAREHOUSE_PROGRESS verifiedFiles={completed}/{total} "
        f"verifiedBytes={published_bytes}/{total_bytes}",
        file=sys.stderr, flush=True,
    )


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--operation", choices=("Inventory", "Download", "Verify"), default="Inventory")
    parser.add_argument("--data-root", type=Path, default=Path(r"D:\simready"))
    parser.add_argument("--profile", type=Path,
                        default=Path(__file__).with_name("datasets").joinpath("simready-warehouse-01.json"))
    args = parser.parse_args(argv)
    try:
        profile = load_profile(args.profile)
        layout = Layout(args.data_root, profile)
        with snapshot_lock(layout):
            if args.operation == "Inventory":
                entries = fetch_inventory(profile)
                report = storage_report(profile, entries, layout)
                save_inventory(profile, entries, layout)
            else:
                entries = load_inventory(profile, layout)
                if args.operation == "Download":
                    report = download_snapshot(profile, entries, layout, progress=report_progress)
                else:
                    verify_snapshot(entries, layout)
                    report = storage_report(profile, entries, layout)
        print(json.dumps({"operation": args.operation, **report}, indent=2))
        return 0
    except StorageBlockedError as error:
        print(json.dumps(error.report, indent=2))
        print(f"WAREHOUSE_STORAGE_BLOCKED: {error}", file=sys.stderr)
        return 3
    except (WarehouseError, OSError, URLError, ValueError, KeyError, TypeError) as error:
        print(f"WAREHOUSE_ACQUISITION_FAILED: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
