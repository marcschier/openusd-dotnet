#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.
"""Independently verify the normal main-loop harness evidence and actual captured PNG pixels."""

import argparse
import binascii
import hashlib
import json
from pathlib import Path
import struct
import zlib


def png_pixels(path):
    if not 0 < path.stat().st_size <= 2 * 1024 * 1024:
        raise ValueError("The captured PNG exceeds the bounded image size.")
    raw = path.read_bytes()
    if raw[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("The native capture is not a PNG.")
    cursor = 8
    header = None
    compressed = bytearray()
    ended = False
    while cursor + 12 <= len(raw):
        size = struct.unpack_from(">I", raw, cursor)[0]
        kind = raw[cursor + 4:cursor + 8]
        end = cursor + 12 + size
        if end > len(raw):
            raise ValueError("The captured PNG contains an out-of-bounds chunk.")
        data = raw[cursor + 8:end - 4]
        if binascii.crc32(kind + data) & 0xFFFFFFFF != struct.unpack_from(">I", raw, end - 4)[0]:
            raise ValueError("The captured PNG chunk CRC is invalid.")
        if kind == b"IHDR":
            if header is not None or cursor != 8 or len(data) != 13:
                raise ValueError("The captured PNG has an invalid header.")
            header = struct.unpack(">IIBBBBB", data)
        elif kind == b"IDAT":
            compressed.extend(data)
        elif kind == b"IEND":
            if size or end != len(raw):
                raise ValueError("The captured PNG has invalid trailing data.")
            ended = True
            break
        cursor = end
    if header is None or not ended:
        raise ValueError("The captured PNG is incomplete.")
    width, height, depth, color, compression, filtering, interlace = header
    if (not 100 <= width <= 512 or not 100 <= height <= 512 or depth != 8
            or color not in (2, 6) or compression or filtering or interlace):
        raise ValueError("The native PNG must be bounded, noninterlaced RGB8/RGBA8.")
    channels = 3 if color == 2 else 4
    stride = width * channels
    expected = (stride + 1) * height
    inflater = zlib.decompressobj()
    packed = inflater.decompress(compressed, expected + 1)
    if (len(packed) != expected or not inflater.eof or inflater.unconsumed_tail or inflater.unused_data):
        raise ValueError("The captured PNG has an invalid or oversized image stream.")
    previous = bytearray(stride)
    interior = bytearray()
    for y in range(height):
        row_start = y * (stride + 1)
        method = packed[row_start]
        if method not in range(5):
            raise ValueError("The captured PNG has an invalid row filter.")
        row = bytearray(packed[row_start + 1:row_start + 1 + stride])
        for x in range(stride):
            left = row[x - channels] if x >= channels else 0
            above = previous[x]
            corner = previous[x - channels] if x >= channels else 0
            if method == 1:
                predictor = left
            elif method == 2:
                predictor = above
            elif method == 3:
                predictor = (left + above) // 2
            elif method == 4:
                value = left + above - corner
                distances = (abs(value - left), abs(value - above), abs(value - corner))
                predictor = (left, above, corner)[distances.index(min(distances))]
            else:
                predictor = 0
            row[x] = (row[x] + predictor) & 255
        if height // 5 <= y < height * 4 // 5:
            for x in range(width // 5, width * 4 // 5):
                interior.extend(row[x * channels:x * channels + 3])
        previous = row
    first = interior[:3]
    if all(interior[index:index + 3] == first for index in range(0, len(interior), 3)):
        raise ValueError("The actual compositor viewport interior is uniform or blank.")
    return width, height, bytes(interior)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--source-sha", required=True)
    parser.add_argument("--mode", choices=("managed", "aot"), required=True)
    args = parser.parse_args()
    proof = json.loads((args.output_root / "evidence.json").read_text())
    if (proof["sourceSha"] != args.source_sha or proof["mode"] != args.mode or proof["backend"] != "Metal"
            or proof["mainThread"] is not True or proof["nonuniform"] is not True
            or proof["decodedPixelsDiffer"] is not True or proof["presentedFrames"] < 6
            or any(proof[key] != 0 for key in ("nativeSessions", "nativePages", "activeFrames", "activeGenerations"))):
        raise ValueError("The normal main-loop evidence is incomplete or degraded.")
    before = png_pixels(args.output_root / "before.png")
    after = png_pixels(args.output_root / "after.png")
    if before[:2] != after[:2] or before[2] == after[2]:
        raise ValueError("The in-place retained stage/camera update did not change actual decoded viewport pixels.")
    result = {
        "sourceSha": args.source_sha, "mode": args.mode, "width": before[0], "height": before[1],
        "beforeInteriorSha256": hashlib.sha256(before[2]).hexdigest(),
        "afterInteriorSha256": hashlib.sha256(after[2]).hexdigest(),
    }
    (args.output_root / "verified-pixels.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print("NORMAL_METAL_MAIN_LOOP_PIXELS_AND_LIFETIME_VERIFIED " + json.dumps(result))


if __name__ == "__main__":
    main()
