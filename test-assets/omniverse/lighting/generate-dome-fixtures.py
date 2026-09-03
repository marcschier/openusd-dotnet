#!/usr/bin/env python3
"""Regenerates the synthetic Radiance HDR dome-light fixtures.

Run from the repository root:

    python test-assets/omniverse/lighting/generate-dome-fixtures.py

The images are deliberately tiny and every authored radiance is a power of two,
because Radiance RGBE stores one shared exponent and an 8-bit mantissa per
channel: a power of two round-trips exactly, so each fixture has an *exact*
analytic solid-angle-weighted mean rather than one that has to be asserted with
a tolerance loose enough to hide a real error.

Scanlines are written uncompressed. A Radiance reader selects the run-length
encoding only when a scanline starts with 0x02 0x02, which none of these do.
"""

import pathlib
import sys

HEADER = b"#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n"


def encode_rgbe(red: float, green: float, blue: float) -> bytes:
    """Encodes one linear RGB triple as Radiance RGBE."""
    peak = max(red, green, blue)
    if peak < 1e-32:
        return bytes((0, 0, 0, 0))

    exponent = 0
    mantissa = peak
    while mantissa >= 1.0:
        mantissa /= 2.0
        exponent += 1
    while mantissa < 0.5:
        mantissa *= 2.0
        exponent -= 1

    scale = mantissa * 256.0 / peak
    values = [int(round(channel * scale)) for channel in (red, green, blue)]
    if any(value > 255 for value in values):
        raise ValueError(f"RGBE mantissa overflow for {(red, green, blue)}")
    return bytes(values + [exponent + 128])


def write_hdr(path: pathlib.Path, width: int, height: int, texel) -> None:
    """Writes an uncompressed latitude/longitude Radiance HDR image."""
    body = bytearray()
    for row in range(height):
        for column in range(width):
            body += encode_rgbe(*texel(column, row))
    path.write_bytes(
        HEADER + f"-Y {height} +X {width}\n".encode("ascii") + bytes(body)
    )
    print(f"wrote {path} ({width}x{height}, {len(body)} payload bytes)")


def main() -> int:
    directory = pathlib.Path(__file__).resolve().parent

    # A constant unit-white environment. Its mean radiance is exactly 1.0 under
    # any solid-angle weighting, which is what makes it the parity fixture: a
    # dome lit by it must resolve to the same ambient as the untextured unit
    # dome it replaces.
    write_hdr(
        directory / "dome-white-latlong.hdr",
        8,
        4,
        lambda column, row: (1.0, 1.0, 1.0),
    )

    # A bright sky over a dim ground, split exactly at the equator. The row
    # weights of the two hemispheres are equal by symmetry, so the mean is the
    # plain average of the two radiances, (1.25, 0.625, 0.3125). It proves the
    # per-channel accumulation; the polar fixture below is what proves the
    # solid-angle weighting.
    def sky_ground(column: int, row: int) -> tuple[float, float, float]:
        return (2.0, 1.0, 0.5) if row < 4 else (0.5, 0.25, 0.125)

    write_hdr(directory / "dome-sky-ground-latlong.hdr", 16, 8, sky_ground)

    # A single lit row at the pole. Its solid angle is far smaller than an
    # equatorial row's, so the correct mean is well below the 1/8 an unweighted
    # average would report.
    def polar_cap(column: int, row: int) -> tuple[float, float, float]:
        return (1.0, 1.0, 1.0) if row == 0 else (0.0, 0.0, 0.0)

    write_hdr(directory / "dome-polar-cap-latlong.hdr", 8, 8, polar_cap)
    return 0


if __name__ == "__main__":
    sys.exit(main())
