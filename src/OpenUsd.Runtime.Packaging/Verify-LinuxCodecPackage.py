#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.
"""Verify the pinned Linux Zstandard/Blosc pair in the SDK and packed Core runtime."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile


ZSTD_API = re.compile(
    r"(?m)^[ \t]*(?:ZSTDLIB_API|ZDICTLIB_API|ZSTDERRORLIB_API)[ \t]+"
    r"[^;\n]*(?:\n[ \t]+[^#;\n]+)*?"
    r"\b((?:ZSTD|ZDICT)_[A-Za-z0-9_]+)\s*\("
)
BLOSC_API = re.compile(
    r"\bBLOSC_EXPORT\s+[^;\n]*(?:\n[ \t]+[^#;\n]+)*?"
    r"\b(blosc_[A-Za-z0-9_]+)\s*\(",
    re.MULTILINE,
)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def readelf_symbols(path: Path) -> dict[str, str]:
    output = subprocess.check_output(
        ["readelf", "--dyn-syms", "--wide", str(path)],
        text=True,
        stderr=subprocess.STDOUT,
    )
    symbols: dict[str, str] = {}
    for line in output.splitlines():
        fields = line.split()
        if (
            len(fields) >= 8
            and fields[0].endswith(":")
            and fields[6] != "UND"
            and fields[4] in ("GLOBAL", "WEAK")
        ):
            symbols[fields[7].split("@", 1)[0]] = fields[5]
    if not symbols:
        raise ValueError(f"ELF dynamic symbol table is empty: {path.name}")
    return symbols


def readelf_dynamic(path: Path) -> str:
    return subprocess.check_output(
        ["readelf", "-d", str(path)], text=True, stderr=subprocess.STDOUT
    )


def declared_apis(sdk_root: Path) -> tuple[set[str], set[str]]:
    include = sdk_root / "include"
    zstd_api: set[str] = set()
    for name in ("zstd.h", "zdict.h", "zstd_errors.h"):
        header = include / name
        if not header.is_file():
            raise ValueError(f"Pinned public Zstandard header is missing: {name}")
        zstd_api.update(ZSTD_API.findall(header.read_text(encoding="utf-8")))
    blosc_header = include / "blosc.h"
    if not blosc_header.is_file():
        raise ValueError("Pinned public Blosc header is missing: blosc.h")
    blosc_api = set(BLOSC_API.findall(blosc_header.read_text(encoding="utf-8")))
    if not zstd_api or not blosc_api:
        raise ValueError("Could not derive the pinned codec APIs from installed headers.")
    return zstd_api, blosc_api


def verify_elf_pair(
    blosc: Path, zstd: Path, zstd_api: set[str], blosc_api: set[str]
) -> dict[str, object]:
    for path in (blosc, zstd):
        if not path.is_file():
            raise ValueError(f"Required packaged codec library is missing: {path.name}")

    zstd_symbols = readelf_symbols(zstd)
    missing_zstd = sorted(zstd_api.difference(zstd_symbols))
    unprotected = sorted(
        name
        for name in zstd_api
        if zstd_symbols.get(name) != "PROTECTED"
    )
    unprotected_codec_exports = sorted(
        name
        for name, visibility in zstd_symbols.items()
        if name.startswith(("ZSTD_", "ZDICT_")) and visibility != "PROTECTED"
    )
    if missing_zstd or unprotected or unprotected_codec_exports:
        raise ValueError(
            "Zstandard ELF API/visibility mismatch: "
            f"missing={missing_zstd}, wrong_visibility={unprotected}, "
            f"unprotected_exports={unprotected_codec_exports}"
        )

    blosc_symbols = readelf_symbols(blosc)
    missing_blosc = sorted(blosc_api.difference(blosc_symbols))
    leaked_zstd = sorted(
        name
        for name in blosc_symbols
        if name.startswith(("ZSTD_", "ZDICT_"))
    )
    if missing_blosc or leaked_zstd:
        raise ValueError(
            f"Blosc ELF API mismatch: missing={missing_blosc}, "
            f"embedded_Zstandard_exports={leaked_zstd}"
        )

    dynamic = readelf_dynamic(blosc)
    if not re.search(r"\(NEEDED\).*Shared library: \[libzstd\.so\.1\]", dynamic):
        raise ValueError("Blosc does not declare the pinned libzstd.so.1 runtime dependency.")

    env = os.environ.copy()
    env["LD_LIBRARY_PATH"] = str(zstd.parent)
    loaded = subprocess.check_output(
        ["ldd", str(blosc)], text=True, stderr=subprocess.STDOUT, env=env
    )
    provider = re.search(r"^\s*libzstd\.so\.1\s+=>\s+(\S+)", loaded, re.MULTILINE)
    if provider is None or Path(provider.group(1)).resolve() != zstd.resolve():
        raise ValueError("Blosc runtime provider is not the exact adjacent Zstandard library.")

    return {
        "bloscSha256": sha256(blosc),
        "zstdSha256": sha256(zstd),
        "bloscPublicApiCount": len(blosc_api),
        "zstdPublicApiCount": len(zstd_api),
        "bloscZstdDefinedExports": len(leaked_zstd),
        "providerSoname": "libzstd.so.1",
        "providerMatchesPinnedLibrary": True,
    }


def verify_package(
    package: Path,
    evidence: dict[str, object],
    zstd_api: set[str],
    blosc_api: set[str],
) -> None:
    entry_root = "runtimes/linux-x64/native/"
    with zipfile.ZipFile(package) as archive, tempfile.TemporaryDirectory(
        prefix="openusd-linux-codec-"
    ) as temporary:
        root = Path(temporary)
        extracted: dict[str, Path] = {}
        for name in ("libblosc.so.1", "libzstd.so.1"):
            entry_name = entry_root + name
            try:
                entry = archive.getinfo(entry_name)
            except KeyError as exc:
                raise ValueError(f"Core package is missing {entry_name}") from exc
            target = root / name
            with archive.open(entry) as source, target.open("wb") as destination:
                shutil.copyfileobj(source, destination)
            expected = evidence["bloscSha256" if name.startswith("libblosc") else "zstdSha256"]
            if sha256(target) != expected:
                raise ValueError(f"Packed {name} bytes differ from the verified SDK install.")
            extracted[name] = target

        # Resolve DT_NEEDED against the two exact libraries extracted from the package.
        pair = verify_elf_pair(
            extracted["libblosc.so.1"],
            extracted["libzstd.so.1"],
            zstd_api,
            blosc_api,
        )
        if pair["bloscSha256"] != evidence["bloscSha256"]:
            raise ValueError("Packed Blosc SHA-256 differs from its SDK evidence.")
        if pair["zstdSha256"] != evidence["zstdSha256"]:
            raise ValueError("Packed Zstandard SHA-256 differs from its SDK evidence.")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--sdk-root", required=True, type=Path)
    parser.add_argument("--evidence", required=True, type=Path)
    parser.add_argument("--package", type=Path)
    args = parser.parse_args()

    sdk_root = args.sdk_root.resolve()
    blosc = sdk_root / "lib" / "libblosc.so.1"
    zstd = sdk_root / "lib" / "libzstd.so.1"
    zstd_api, blosc_api = declared_apis(sdk_root)
    evidence = verify_elf_pair(blosc, zstd, zstd_api, blosc_api)

    if args.package:
        if not args.evidence.is_file():
            raise ValueError("Pre-pack codec evidence is missing.")
        recorded = json.loads(args.evidence.read_text(encoding="utf-8"))
        for key in ("bloscSha256", "zstdSha256", "bloscPublicApiCount", "zstdPublicApiCount"):
            if recorded.get(key) != evidence[key]:
                raise ValueError(f"SDK codec identity changed after packing: {key}")
        verify_package(args.package.resolve(), recorded, zstd_api, blosc_api)
        print(
            "LINUX_CODEC_PACKAGE_VERIFIED: "
            f"Blosc API={evidence['bloscPublicApiCount']}, "
            f"Zstandard API={evidence['zstdPublicApiCount']}, "
            "packed bytes match the verified SDK and runtime provider."
        )
    else:
        args.evidence.parent.mkdir(parents=True, exist_ok=True)
        args.evidence.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
        print(
            "LINUX_CODEC_SDK_VERIFIED: "
            f"Blosc API={evidence['bloscPublicApiCount']}, "
            f"Zstandard API={evidence['zstdPublicApiCount']}, "
            "all public Zstandard exports protected, no Blosc Zstandard definitions."
        )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, subprocess.CalledProcessError, ValueError, zipfile.BadZipFile) as error:
        print(f"Linux codec package verification failed: {error}", file=sys.stderr)
        raise SystemExit(1)
