#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.
"""Decode the bounded, manual-only Metal qualification payload without extracting arbitrary paths."""

import argparse
import base64
import binascii
import hashlib
import io
import json
import os
from pathlib import Path
import stat
import struct
import xml.etree.ElementTree as ET
import zipfile
import zlib

MAX_ENCODED = 43692
MAX_ZIP = 32768
MAX_EXPANDED = 65536
FILE_LIMITS = {"Program.cs": 61440, "Harness.csproj": 4096}
APPROVED_PROJECT = """<Project>
  <PropertyGroup>
    <DirectoryPackagesPropsPath>$(QualificationRepositoryRoot)/Directory.Packages.props</DirectoryPackagesPropsPath>
  </PropertyGroup>
  <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsApplicationProject>true</IsApplicationProject>
    <IsPackable>false</IsPackable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <AssemblyName>OpenUsd.Metal.MainLoopQualification</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$(QualificationRepositoryRoot)/src/OpenUsd.Viewer/OpenUsd.Viewer.csproj" />
  </ItemGroup>
  <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />
</Project>"""


def xml_shape(node):
    return (
        node.tag, tuple(sorted(node.attrib.items())), (node.text or "").strip(), (node.tail or "").strip(),
        tuple(xml_shape(child) for child in node),
    )


def decode(payload):
    if not payload or len(payload) > MAX_ENCODED:
        raise ValueError("Metal qualification input is empty or exceeds the encoded size limit.")
    raw = base64.b64decode(payload, validate=True)
    if len(raw) > MAX_ZIP or base64.b64encode(raw).decode("ascii") != payload:
        raise ValueError("Metal qualification input exceeds the ZIP limit or is not canonical base64.")
    files = {}
    spans = []
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        entries = archive.infolist()
        if len(entries) != 2 or {entry.filename for entry in entries} != set(FILE_LIMITS):
            raise ValueError("The payload must contain exactly Program.cs and Harness.csproj at its root.")
        if sum(entry.file_size for entry in entries) > MAX_EXPANDED:
            raise ValueError("Metal qualification input exceeds the total expanded size limit.")
        for entry in entries:
            mode = stat.S_IFMT(entry.external_attr >> 16)
            if (entry.orig_filename != entry.filename or entry.filename in files
                    or entry.is_dir() or entry.external_attr & 0x10
                    or mode not in (0, stat.S_IFREG) or entry.create_system not in (0, 3)):
                raise ValueError("The payload contains a duplicate, nonregular, or ambiguous ZIP entry.")
            limit = FILE_LIMITS[entry.filename]
            if not 0 < entry.file_size <= limit or entry.compress_size <= 0:
                raise ValueError("Metal qualification file sizes are outside their bounds.")
            if entry.file_size > entry.compress_size * 100:
                raise ValueError("Metal qualification input has an excessive compression ratio.")
            if entry.flag_bits & 9 or entry.extract_version >= 45:
                raise ValueError("Encrypted, data-descriptor, and ZIP64 entries are not permitted.")
            start = entry.header_offset
            if start < 0 or start + 30 > archive.start_dir:
                raise ValueError("Metal qualification input has an invalid local ZIP header.")
            header = struct.unpack_from("<IHHHHHIIIHH", raw, start)
            if (header[0] != 0x04034B50 or header[2] != entry.flag_bits
                    or header[3] != entry.compress_type or header[6] != entry.CRC
                    or header[7] != entry.compress_size or header[8] != entry.file_size):
                raise ValueError("Local and central ZIP entry metadata disagree.")
            name_end = start + 30 + header[9]
            local_name = raw[start + 30:name_end]
            if local_name != entry.filename.encode("ascii"):
                raise ValueError("Local and central ZIP entry names disagree.")
            data_start = name_end + header[10]
            end = data_start + entry.compress_size
            if end > archive.start_dir or any(start < other_end and end > other for other, other_end in spans):
                raise ValueError("Metal qualification input has overlapping or out-of-bounds ZIP entries.")
            spans.append((start, end))
            compressed = raw[data_start:end]
            if entry.compress_type == zipfile.ZIP_STORED:
                content = compressed
            elif entry.compress_type == zipfile.ZIP_DEFLATED:
                inflater = zlib.decompressobj(-15)
                content = inflater.decompress(compressed, limit + 1)
                if not inflater.eof or inflater.unconsumed_tail or inflater.unused_data:
                    raise ValueError("Metal qualification input contains an oversized or incomplete deflate stream.")
            else:
                raise ValueError("Only stored or deflated Metal qualification entries are permitted.")
            if len(content) != entry.file_size or binascii.crc32(content) & 0xFFFFFFFF != entry.CRC:
                raise ValueError("Metal qualification expanded size or CRC does not match its ZIP metadata.")
            content.decode("utf-8")
            files[entry.filename] = content
    project = files["Harness.csproj"]
    if b"<!DOCTYPE" in project or b"<!ENTITY" in project:
        raise ValueError("Metal qualification project declarations are not permitted.")
    if xml_shape(ET.fromstring(project)) != xml_shape(ET.fromstring(APPROVED_PROJECT)):
        raise ValueError("Metal qualification must use the approved existing project and dependency bindings.")
    metadata = {
        "zipSha256": hashlib.sha256(raw).hexdigest(),
        "encodedBytes": len(payload),
        "zipBytes": len(raw),
        "expandedBytes": sum(map(len, files.values())),
        "files": {name: hashlib.sha256(content).hexdigest() for name, content in files.items()},
    }
    return files, metadata


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-root", type=Path, required=True)
    args = parser.parse_args()
    files, metadata = decode(os.environ.get("OPENUSD_METAL_QUALIFICATION_ZIP", ""))
    runner_temp = Path(os.environ["RUNNER_TEMP"]).resolve(strict=True)
    output = args.output_root
    if output.parent.resolve(strict=True) != runner_temp or output.exists() or output.is_symlink():
        raise ValueError("The qualification output must be a new direct child of RUNNER_TEMP.")
    output.mkdir(mode=0o700)
    for name, content in files.items():
        (output / name).write_bytes(content)
    (output / "harness-identity.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(metadata, sort_keys=True))


if __name__ == "__main__":
    main()
