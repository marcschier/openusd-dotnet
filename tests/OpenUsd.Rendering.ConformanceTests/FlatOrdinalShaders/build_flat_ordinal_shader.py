# Copyright (c) marcschier. Licensed under the MIT License.

import argparse
import hashlib
import json
import os
import pathlib
import re
import struct
import subprocess
import sys


DIRECTORY = pathlib.Path(__file__).resolve().parent
REPOSITORY = DIRECTORY.parent.parent.parent
CHECKED = DIRECTORY / "checked"
SOURCE = DIRECTORY / "flat-ordinal.fragment.slang"
VERTEX_SOURCE = DIRECTORY / "flat-ordinal.vertex.slang"
CONTRACT = DIRECTORY / "flat-ordinal.manifest.json"
LOCK = REPOSITORY / "eng" / "shaders" / "toolchain.lock.json"
NORMALIZER = REPOSITORY / "eng" / "shaders" / "scripts" / "normalize-reflection.py"
SHADER_MODEL = REPOSITORY / "eng" / "shaders" / "scripts" / "shader_model.py"
PROGRAM = "flat-ordinal.fragment"
ENTRY_POINT = "flatOrdinalTransportFragmentMain"
VERTEX_PROGRAM = "flat-ordinal.vertex"
VERTEX_ENTRY_POINT = "flatOrdinalTransportVertexMain"
METAL_HOST_ENTRY_POINT = "main"
ARTIFACTS = (
    f"{PROGRAM}.dxil",
    f"{PROGRAM}.spv",
    f"{PROGRAM}.metal",
    f"{PROGRAM}.reflection.json",
    f"{PROGRAM}.dxil-reflection.json",
    f"{PROGRAM}.spirv-reflection.json",
    f"{VERTEX_PROGRAM}.dxil",
    f"{VERTEX_PROGRAM}.dxil-reflection.json",
)
INPUTS = (
    SOURCE,
    VERTEX_SOURCE,
    CONTRACT,
    pathlib.Path(__file__).resolve(),
    LOCK,
    NORMALIZER,
    SHADER_MODEL,
)


def fail(message: str) -> None:
    raise ValueError(message)


def describe(paths: tuple[pathlib.Path, ...]) -> list[dict[str, object]]:
    entries = []
    for path in paths:
        content = path.read_bytes()
        if not content:
            fail(f"Empty shader input or artifact: {path}")
        entries.append(
            {
                "path": path.relative_to(REPOSITORY).as_posix(),
                "sha256": hashlib.sha256(content).hexdigest(),
                "size": len(content),
            }
        )
    return sorted(entries, key=lambda entry: str(entry["path"]))


def read_contract() -> dict[str, object]:
    contract = json.loads(CONTRACT.read_text(encoding="utf-8"))
    lock = json.loads(LOCK.read_text(encoding="utf-8"))
    program = {
        "name": PROGRAM,
        "source": SOURCE.name,
        "entryPoint": ENTRY_POINT,
        "stage": "fragment",
        "profile": "ps_6_0",
        "resources": [],
    }
    vertex_program = {
        "name": VERTEX_PROGRAM,
        "source": VERTEX_SOURCE.name,
        "entryPoint": VERTEX_ENTRY_POINT,
        "stage": "vertex",
        "profile": "vs_6_0",
        "resources": [
            {
                "name": "flatOrdinalInstances",
                "access": "read",
                "d3d": {"registerClass": "t", "register": 6, "space": 0},
                "vulkan": {"binding": 6, "set": 0},
            }
        ],
    }
    if (
        contract.get("schemaVersion") != 1
        or contract.get("matrixLayout") != "row-major"
        or contract.get("programs") != [program, vertex_program]
    ):
        fail("The test-only flat-ordinal shader contract is invalid.")

    expected_toolchain = {
        "slangVersion": lock["slang"]["version"],
        "slangCommit": lock["slang"]["commit"],
        "spirvToolsCommit": lock["spirvTools"]["commit"],
        "shaderModel": lock["targets"]["direct3D"]["shaderModel"],
        "vulkanVersion": lock["targets"]["vulkan"]["apiVersion"],
        "spirvVersion": lock["targets"]["vulkan"]["spirvVersion"],
        "metalVersion": lock["targets"]["metal"]["languageVersion"],
    }
    if contract.get("toolchain") != expected_toolchain:
        fail("The test-only shader lock does not match the repository toolchain lock.")
    if (
        expected_toolchain["shaderModel"] != "6.0"
        or expected_toolchain["vulkanVersion"] != "1.2"
        or expected_toolchain["spirvVersion"] != "1.5"
        or expected_toolchain["metalVersion"] != "2.4"
    ):
        fail("The flat-ordinal shader commands do not support the changed target lock.")
    return contract


def inspect_spirv(path: pathlib.Path) -> None:
    data = path.read_bytes()
    if len(data) < 20 or len(data) % 4:
        fail(f"Invalid SPIR-V size: {path}")
    words = struct.unpack(f"<{len(data) // 4}I", data)
    if words[0] != 0x07230203 or words[1] != 0x00010500:
        fail(f"Expected SPIR-V 1.5: {path}")

    flat = set()
    location_zero = set()
    inputs = set()
    loads = set()
    fragment_entries = 0
    offset = 5
    while offset < len(words):
        instruction = words[offset]
        count = instruction >> 16
        opcode = instruction & 0xFFFF
        if count == 0 or offset + count > len(words):
            fail(f"Truncated SPIR-V instruction: {path}")
        operands = words[offset + 1 : offset + count]
        if opcode == 17 and len(operands) >= 1 and operands[0] == 2:
            fail("The flat-ordinal shader must not declare Geometry capability.")
        if opcode == 15 and len(operands) >= 3 and operands[0] == 4:
            name = b"".join(struct.pack("<I", word) for word in operands[2:])
            if b"\0" not in name or name.split(b"\0", 1)[0] != ENTRY_POINT.encode("utf-8"):
                fail("SPIR-V must use the named flat-ordinal fragment entry point.")
            fragment_entries += 1
        if opcode == 71 and len(operands) >= 2:
            target, decoration = operands[:2]
            if decoration == 14:
                flat.add(target)
            elif decoration == 30 and len(operands) >= 3 and operands[2] == 0:
                location_zero.add(target)
            elif decoration == 11 and len(operands) >= 3 and operands[2] == 7:
                fail("The flat-ordinal shader must not use BuiltIn PrimitiveId.")
        if opcode == 59 and len(operands) >= 3 and operands[2] == 1:
            inputs.add(operands[1])
        if opcode == 61 and len(operands) >= 3:
            loads.add(operands[2])
        offset += count
    if fragment_entries != 1 or len(flat & location_zero & inputs & loads) != 1:
        fail("SPIR-V must contain one named fragment entry and load one flat input at location 0.")


def dxil_part(data: bytes, kind: bytes) -> bytes:
    if len(data) < 32 or data[:4] != b"DXBC":
        fail("The flat-ordinal DXIL container is invalid.")
    size, count = struct.unpack_from("<II", data, 24)
    if size != len(data) or count > (len(data) - 32) // 4:
        fail("The flat-ordinal DXIL container has an invalid part table.")
    matches = []
    for index in range(count):
        offset = struct.unpack_from("<I", data, 32 + (index * 4))[0]
        if offset > len(data) - 8:
            fail("The flat-ordinal DXIL part header is truncated.")
        part_kind = data[offset : offset + 4]
        part_size = struct.unpack_from("<I", data, offset + 4)[0]
        if part_size > len(data) - offset - 8:
            fail("The flat-ordinal DXIL part is truncated.")
        if part_kind == kind:
            matches.append(data[offset + 8 : offset + 8 + part_size])
    if len(matches) != 1:
        fail(f"The flat-ordinal DXIL container must have one {kind!r} part.")
    return matches[0]


def dxil_signature(data: bytes, kind: bytes) -> dict[tuple[str, int], tuple[int, ...]]:
    part = dxil_part(data, kind)
    if len(part) < 8:
        fail("The flat-ordinal DXIL signature is truncated.")
    count, start = struct.unpack_from("<II", part)
    if start > len(part) or count > (len(part) - start) // 32:
        fail("The flat-ordinal DXIL signature has an invalid element table.")
    result = {}
    for index in range(count):
        stream, name, semantic_index, system, component, register, mask, usage, _, precision = (
            struct.unpack_from("<6IBBHI", part, start + (index * 32))
        )
        end = part.find(b"\0", name)
        if end < 0:
            fail("The flat-ordinal DXIL signature has an invalid semantic name.")
        semantic = part[name:end].decode("ascii").upper()
        key = (semantic, semantic_index)
        if key in result:
            fail("The flat-ordinal DXIL signature has a duplicate semantic.")
        result[key] = (register, mask, component, system, precision, stream, usage)
    return result


def dxil_psv(data: bytes) -> tuple[int, list[tuple[int, ...]], dict[tuple[str, int, str], int]]:
    part = dxil_part(data, b"PSV0")
    offset = 0

    def take(size: int) -> bytes:
        nonlocal offset
        if size < 0 or size > len(part) - offset:
            fail("The flat-ordinal DXIL pipeline validation data is truncated.")
        value = part[offset : offset + size]
        offset += size
        return value

    def uint32() -> int:
        return struct.unpack("<I", take(4))[0]

    runtime = take(uint32())
    if len(runtime) < 36:
        fail("The flat-ordinal DXIL pipeline validation data lacks signature information.")
    stage = runtime[24]
    input_count, output_count, patch_count = runtime[28:31]
    resource_count = uint32()
    resources = []
    if resource_count:
        resource_size = uint32()
        if resource_size < 20:
            fail("The flat-ordinal DXIL resource record is too small.")
        for _ in range(resource_count):
            resources.append(struct.unpack_from("<5I", take(resource_size)))
    strings = take(uint32())
    semantic_count = uint32()
    indexes = struct.unpack(f"<{semantic_count}I", take(semantic_count * 4))
    element_size = uint32() if input_count or output_count or patch_count else 0
    if element_size < 16:
        fail("The flat-ordinal DXIL signature record is too small.")
    interpolation = {}
    for direction, count in (("input", input_count), ("output", output_count), ("patch", patch_count)):
        for _ in range(count):
            name, semantic_offset, rows, _, _, _, _, mode, _, _ = struct.unpack_from(
                "<IIBBBBBBBB", take(element_size)
            )
            if name >= len(strings) or semantic_offset + rows > len(indexes):
                fail("The flat-ordinal DXIL signature references invalid metadata.")
            end = strings.find(b"\0", name)
            if end < 0:
                fail("The flat-ordinal DXIL signature semantic is unterminated.")
            semantic = strings[name:end].decode("ascii").upper()
            for semantic_index in indexes[semantic_offset : semantic_offset + rows]:
                key = (semantic, semantic_index, direction)
                if key in interpolation:
                    fail("The flat-ordinal DXIL validation data has a duplicate semantic.")
                interpolation[key] = mode
    return stage, resources, interpolation


def inspect_dxil_pair(vertex: bytes, fragment: bytes) -> None:
    vertex_output = dxil_signature(vertex, b"OSG1")
    fragment_input = dxil_signature(fragment, b"ISG1")
    expected = {
        ("SV_POSITION", 0): (0, 0xF, 3, 1, 0, 0),
        ("TEXCOORD", 0): (1, 0x3, 3, 0, 0, 0),
    }
    if (
        set(vertex_output) != set(expected)
        or set(fragment_input) != set(expected)
        or any(vertex_output[key][:6] != value for key, value in expected.items())
        or any(fragment_input[key][:6] != value for key, value in expected.items())
        or any(vertex_output[key][6] & fragment_input[key][6] for key in expected)
    ):
        fail(
            "The DXIL vertex output and fragment input registers, masks, or types do not match: "
            f"vertex={vertex_output}, fragment={fragment_input}"
        )

    vertex_stage, vertex_resources, vertex_modes = dxil_psv(vertex)
    fragment_stage, fragment_resources, fragment_modes = dxil_psv(fragment)
    # PSV encodes a structured SRV as type 5/kind 12 and constant interpolation as 1.
    if (
        vertex_stage != 1
        or fragment_stage != 0
        or vertex_resources != [(5, 0, 6, 6, 12)]
        or fragment_resources
        or vertex_modes.get(("TEXCOORD", 0, "output")) != 1
        or fragment_modes.get(("TEXCOORD", 0, "input")) != 1
    ):
        fail(
            "The DXIL pair must use t6 and constant TEXCOORD0 interpolation in both stages: "
            f"vertexStage={vertex_stage}, vertexResources={vertex_resources}, "
            f"vertexModes={vertex_modes}, fragmentStage={fragment_stage}, "
            f"fragmentResources={fragment_resources}, fragmentModes={fragment_modes}"
        )


def inspect_artifacts() -> None:
    dxil = (CHECKED / f"{PROGRAM}.dxil").read_bytes()
    vertex_dxil = (CHECKED / f"{VERTEX_PROGRAM}.dxil").read_bytes()
    inspect_dxil_pair(vertex_dxil, dxil)
    vertex_reflection = json.loads(
        (CHECKED / f"{VERTEX_PROGRAM}.dxil-reflection.json").read_text(encoding="utf-8")
    )
    vertex_entries = vertex_reflection.get("entryPoints")
    if (
        not isinstance(vertex_entries, list)
        or len(vertex_entries) != 1
        or vertex_entries[0].get("name") != VERTEX_ENTRY_POINT
        or vertex_entries[0].get("stage") != "vertex"
    ):
        fail("The test-only DXIL vertex reflection has an unexpected entry point.")
    inspect_spirv(CHECKED / f"{PROGRAM}.spv")

    metal = (CHECKED / f"{PROGRAM}.metal").read_text(encoding="utf-8")
    flat_input = any(
        "user(TEXCOORD)" in re.sub(r"\s+", "", attributes)
        and re.search(r"\bflat\b", attributes)
        for line in metal.splitlines()
        if (attributes := " ".join(re.findall(r"\[\[([^\]]*)\]\]", line)))
    )
    if (
        "#include <metal_stdlib>" not in metal
        or not flat_input
        or not re.search(
            rf'\[\[\s*host_name\s*\(\s*"{METAL_HOST_ENTRY_POINT}"\s*\)\s*\]\]\s*'
            rf"\[\[\s*fragment\s*\]\][^\r\n]*\b{ENTRY_POINT}\s*\(",
            metal,
        )
        or re.search(r"\[\[\s*primitive_id\s*\]\]", metal)
    ):
        fail("MSL must expose a flat TEXCOORD and the named fragment with the Metal host alias.")

    reflection = json.loads((CHECKED / f"{PROGRAM}.reflection.json").read_text(encoding="utf-8"))
    inputs = reflection.get("stageInputs")
    if (
        reflection.get("source") != SOURCE.name
        or reflection.get("entryPoint", {}).get("name") != ENTRY_POINT
        or reflection.get("entryPoint", {}).get("stage") != "fragment"
        or reflection.get("entryPoint", {}).get("profile") != "ps_6_0"
        or reflection.get("resources") != []
        or not isinstance(inputs, list)
        or len(inputs) != 2
    ):
        fail("The normalized flat-ordinal reflection has an unexpected program contract.")
    position, varying = inputs
    if (
        position.get("semantic") != {"name": "SV_POSITION", "index": 0, "systemValue": True}
        or position.get("location") is not None
        or position.get("type")
        != {
            "kind": "vector",
            "elementCount": 4,
            "elementType": {"kind": "scalar", "scalarType": "float32"},
        }
    ):
        fail("The fragment must declare SV_Position before TEXCOORD0.")
    if (
        varying.get("semantic") != {"name": "TEXCOORD", "index": 0, "systemValue": False}
        or varying.get("location") != 0
        or varying.get("type")
        != {
            "kind": "vector",
            "elementCount": 2,
            "elementType": {"kind": "scalar", "scalarType": "float32"},
        }
    ):
        fail("The fragment must read TEXCOORD0 as a float32 pair.")


def expected_receipt(contract: dict[str, object]) -> dict[str, object]:
    return {
        "schemaVersion": 2,
        "programs": [PROGRAM, VERTEX_PROGRAM],
        "toolchain": contract["toolchain"],
        "inputs": describe(INPUTS),
        "artifacts": describe(tuple(CHECKED / name for name in ARTIFACTS)),
    }


def canonicalize_text_artifacts() -> None:
    for name in ARTIFACTS:
        if not name.endswith((".json", ".metal")):
            continue
        path = CHECKED / name
        content = path.read_bytes().replace(b"\r\n", b"\n")
        if b"\r" in content:
            fail(f"The generated shader text contains a bare carriage return: {path}")
        path.write_bytes(content.rstrip(b"\n") + b"\n")


def verify_text_artifacts() -> None:
    for name in (*ARTIFACTS, "manifest.json"):
        if not name.endswith((".json", ".metal")):
            continue
        path = CHECKED / name
        content = path.read_bytes()
        if b"\r" in content or not content.endswith(b"\n") or content.endswith(b"\n\n"):
            fail(f"The checked shader text must use LF and one final newline: {path}")


def verify() -> None:
    contract = read_contract()
    receipt_path = CHECKED / "manifest.json"
    if not receipt_path.is_file():
        fail("The test-only flat-ordinal shader payload has not been generated.")
    receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
    if receipt.get("schemaVersion") != 2 or receipt.get("programs") != [PROGRAM, VERTEX_PROGRAM]:
        fail("The test-only flat-ordinal shader payload is stale; regenerate it.")
    if receipt != expected_receipt(contract):
        fail("The test-only flat-ordinal shader payload or its inputs are stale.")
    verify_text_artifacts()
    inspect_artifacts()
    print("Flat-ordinal test-only shader payload verified.")


def first_line(executable: pathlib.Path, option: str) -> str:
    output = subprocess.run(
        [str(executable), option],
        cwd=DIRECTORY,
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
    ).stdout
    return output.splitlines()[0].strip() if output.splitlines() else ""


def build(tool_root: pathlib.Path) -> None:
    if os.name != "nt":
        fail("The full test-only shader payload must be generated on Windows win-x64.")
    contract = read_contract()
    inputs_before = describe(INPUTS)
    toolchain = contract["toolchain"]
    slang = tool_root / "win-x64" / "slang" / "bin" / "slangc.exe"
    validator = tool_root / "win-x64" / "spirv-tools" / "bin" / "spirv-val.exe"
    if not slang.is_file() or not validator.is_file():
        fail("The locked Slang compiler and spirv-val must already be installed.")
    if first_line(slang, "-version") != toolchain["slangVersion"]:
        fail("Slang does not match the test-only shader lock.")
    if toolchain["spirvToolsCommit"] not in first_line(validator, "--version"):
        fail("spirv-val does not match the test-only shader lock.")

    CHECKED.mkdir(exist_ok=True)
    (CHECKED / "manifest.json").unlink(missing_ok=True)
    common = [
        "-matrix-layout-row-major",
        "-warnings-as-errors",
        "all",
        "-g0",
        "-line-directive-mode",
        "none",
        "-source-embed-style",
        "none",
        "-O2",
        "-DVOLUME_DENSITY_TEXTURES=0",
    ]

    def compile_target(
        source: pathlib.Path,
        entry_point: str,
        target: str,
        profile: str,
        defines: list[str],
        output: str,
        raw: str | None,
    ) -> None:
        arguments = [
            str(slang),
            source.name,
            "-entry",
            entry_point,
            *common,
            *defines,
            "-target",
            target,
            "-profile",
            profile,
            "-o",
            str(CHECKED / output),
        ]
        if target == "spirv":
            arguments.append("-fvk-use-entrypoint-name")
        if raw is not None:
            arguments += ["-reflection-json", str(CHECKED / raw)]
        subprocess.run(arguments, cwd=DIRECTORY, check=True)

    compile_target(
        SOURCE,
        ENTRY_POINT,
        "dxil",
        "ps_6_0",
        ["-DOPENUSD_TARGET_DXIL=1", "-DOPENUSD_TARGET_SPIRV=0", "-DOPENUSD_TARGET_METAL=0"],
        f"{PROGRAM}.dxil",
        f"{PROGRAM}.dxil-reflection.json",
    )
    compile_target(
        SOURCE,
        ENTRY_POINT,
        "spirv",
        "ps_6_0+spirv_1_5",
        ["-DOPENUSD_TARGET_DXIL=0", "-DOPENUSD_TARGET_SPIRV=1", "-DOPENUSD_TARGET_METAL=0"],
        f"{PROGRAM}.spv",
        f"{PROGRAM}.spirv-reflection.json",
    )
    compile_target(
        SOURCE,
        ENTRY_POINT,
        "metal",
        "ps_6_0+METAL_2_4",
        ["-DOPENUSD_TARGET_DXIL=0", "-DOPENUSD_TARGET_SPIRV=0", "-DOPENUSD_TARGET_METAL=1"],
        f"{PROGRAM}.metal",
        None,
    )
    compile_target(
        VERTEX_SOURCE,
        VERTEX_ENTRY_POINT,
        "dxil",
        "vs_6_0",
        ["-DOPENUSD_TARGET_DXIL=1", "-DOPENUSD_TARGET_SPIRV=0", "-DOPENUSD_TARGET_METAL=0"],
        f"{VERTEX_PROGRAM}.dxil",
        f"{VERTEX_PROGRAM}.dxil-reflection.json",
    )
    # The generic Metal pipeline compiles source only for the host-visible name "main".
    metal_path = CHECKED / f"{PROGRAM}.metal"
    metal_source = metal_path.read_text(encoding="utf-8")
    entry_pattern = re.compile(
        rf"(?m)^\[\[\s*fragment\s*\]\](?=[^\r\n]*\b{ENTRY_POINT}\s*\()"
    )
    metal_source, replacements = entry_pattern.subn(
        lambda match: f'[[host_name("{METAL_HOST_ENTRY_POINT}")]] {match.group(0)}',
        metal_source,
    )
    if replacements != 1:
        fail("The generated MSL does not contain exactly one named fragment entry point.")
    metal_path.write_bytes(metal_source.encode("utf-8"))
    subprocess.run(
        [str(validator), "--target-env", "vulkan1.2", str(CHECKED / f"{PROGRAM}.spv")],
        cwd=DIRECTORY,
        check=True,
    )
    subprocess.run(
        [
            sys.executable,
            "-B",
            str(NORMALIZER),
            "--dxil-input",
            str(CHECKED / f"{PROGRAM}.dxil-reflection.json"),
            "--spirv-input",
            str(CHECKED / f"{PROGRAM}.spirv-reflection.json"),
            "--output",
            str(CHECKED / f"{PROGRAM}.reflection.json"),
            "--manifest",
            str(CONTRACT),
            "--program",
            PROGRAM,
        ],
        cwd=DIRECTORY,
        check=True,
    )
    if describe(INPUTS) != inputs_before:
        fail("Flat-ordinal shader inputs changed while the payload was generated.")
    canonicalize_text_artifacts()
    inspect_artifacts()
    receipt = expected_receipt(contract)
    (CHECKED / "manifest.json").write_bytes(
        (json.dumps(receipt, indent=2) + "\n").encode("utf-8")
    )
    verify()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=("build", "verify"))
    parser.add_argument(
        "--tool-root",
        type=pathlib.Path,
        default=REPOSITORY / "eng" / "shaders" / ".tools",
    )
    arguments = parser.parse_args()
    if arguments.action == "build":
        build(arguments.tool_root.resolve())
    else:
        verify()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
