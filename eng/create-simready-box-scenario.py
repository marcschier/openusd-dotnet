# Copyright (c) marcschier. Licensed under the MIT License.

"""Create a removable one-box demonstration over a localized Warehouse-01 root."""

import argparse
import hashlib
import json
import math
from pathlib import Path
import sys


def _focused_camera_layer() -> str:
    eye = (-20.1080493927, -39.1122589111, 4.9)
    target = (-24.6080493927, -43.1122589111, 1.9)

    def normalize(vector):
        length = math.sqrt(sum(value * value for value in vector))
        return tuple(value / length for value in vector)

    back = normalize(tuple(a - b for a, b in zip(eye, target)))
    right = normalize((-back[1], back[0], 0.0))
    up = (back[1] * right[2] - back[2] * right[1],
          back[2] * right[0] - back[0] * right[2],
          back[0] * right[1] - back[1] * right[0])
    rows = [(*right, 0), (*up, 0), (*back, 0), (*eye, 1)]
    matrix = "(" + ", ".join(
        "(" + ", ".join(format(value, ".17g") for value in row) + ")" for row in rows
    ) + ")"
    focal_length = 20.25 / (2 * math.tan(math.pi / 8))
    return f'''
# Focused full-root view of the moving box; no source geometry or visibility is filtered.
def Camera "WarehouseDemoCamera"
{{
    token projection = "perspective"
    float horizontalAperture = 36
    float verticalAperture = 20.25
    float focalLength = {focal_length:.17g}
    float2 clippingRange = (0.05, 20)
    matrix4d xformOp:transform:warehouseCamera = {matrix}
    uniform token[] xformOpOrder = ["xformOp:transform:warehouseCamera"]
}}
'''


def create_scenario(source: Path, output: Path, *, focused_camera: bool = False) -> dict:
    source = source.resolve(strict=True)
    output = output.absolute()
    if not source.is_file() or source.suffix.lower() not in (".usd", ".usda", ".usdc"):
        raise ValueError("An existing localized USD root is required.")
    if output.suffix.lower() != ".usda" or output.exists() or output.is_symlink():
        raise ValueError("Use a new .usda scenario path; existing files are never overwritten.")
    asset = source.as_posix()
    if any(character in asset for character in "@\r\n\0"):
        raise ValueError("The source path cannot be represented as a USDA asset identifier.")
    template = Path(__file__).with_name("datasets").joinpath("simready-box-drop.usda.template")
    layer = template.read_text(encoding="utf-8").replace("@LOCALIZED_ROOT@", f"@{asset}@")
    if focused_camera:
        layer += _focused_camera_layer()
    with output.open("x", encoding="utf-8", newline="\n") as stream:
        stream.write(layer)
    return {
        "scenario": str(output),
        "scenarioSha256": hashlib.sha256(layer.encode("utf-8")).hexdigest(),
        "source": str(source),
        "cameraPath": "/WarehouseDemoCamera" if focused_camera else None,
        "stageSettings": {
            "metersPerUnit": 1, "kilogramsPerUnit": 1, "upAxis": "Z",
            "startTimeCode": 0, "endTimeCode": 240,
            "timeCodesPerSecond": 60, "framesPerSecond": 60,
        },
        "scope": "Removable demonstration layer; assumes the verified localized Warehouse-01 dataset. Does not validate or modify its assets.",
        "remove": "Open the original localized root without this scenario layer.",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--localized-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--focused-camera", action="store_true",
                        help="Author /WarehouseDemoCamera for a 16:9, 45-degree vertical-FOV box-stack view.")
    args = parser.parse_args()
    try:
        print(json.dumps(create_scenario(
            args.localized_root, args.output, focused_camera=args.focused_camera), indent=2))
        return 0
    except (OSError, ValueError) as error:
        print(f"WAREHOUSE_SCENARIO_FAILED: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
