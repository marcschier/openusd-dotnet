# Copyright (c) marcschier. Licensed under the MIT License.

import argparse
import hashlib
import json
import os
import pathlib
import re
import subprocess
import sys


DIRECTORY = pathlib.Path(__file__).resolve().parent
REPOSITORY = DIRECTORY.parent.parent.parent
CHECKED = DIRECTORY / "checked"
SOURCE = DIRECTORY / "primitive-id.fragment.slang"
CONTRACT = DIRECTORY / "primitive-id.manifest.json"
LOCK = REPOSITORY / "eng" / "shaders" / "toolchain.lock.json"
NORMALIZER = REPOSITORY / "eng" / "shaders" / "scripts" / "normalize-reflection.py"
SHADER_MODEL = REPOSITORY / "eng" / "shaders" / "scripts" / "shader_model.py"
PROGRAM = "primitive-id.fragment"
ENTRY_POINT = "primitiveIdProbeFragmentMain"
ARTIFACTS = (
    f"{PROGRAM}.dxil",
    f"{PROGRAM}.spv",
    f"{PROGRAM}.metal",
    f"{PROGRAM}.reflection.json",
    f"{PROGRAM}.dxil-reflection.json",
    f"{PROGRAM}.spirv-reflection.json",
)
INPUTS = (
    SOURCE,
    CONTRACT,
    pathlib.Path(__file__).resolve(),
    LOCK,
    NORMALIZER,
    SHADER_MODEL,
)


def describe(paths: tuple[pathlib.Path, ...]) -> list[dict[str, object]]:
    entries = []
    for path in paths:
        content = path.read_bytes()
        if not content:
            raise ValueError(f"Empty shader input or artifact: {path}")
        entries.append({
            "path": path.relative_to(REPOSITORY).as_posix(),
            "sha256": hashlib.sha256(content).hexdigest(),
            "size": len(content),
        })
    return sorted(entries, key=lambda entry: str(entry["path"]))


def read_contract() -> dict[str, object]:
    contract = json.loads(CONTRACT.read_text(encoding="utf-8"))
    lock = json.loads(LOCK.read_text(encoding="utf-8"))
    expected_toolchain = {
        "slangVersion": lock["slang"]["version"],
        "slangCommit": lock["slang"]["commit"],
        "spirvToolsCommit": lock["spirvTools"]["commit"],
        "shaderModel": lock["targets"]["direct3D"]["shaderModel"],
        "vulkanVersion": lock["targets"]["vulkan"]["apiVersion"],
        "spirvVersion": lock["targets"]["vulkan"]["spirvVersion"],
        "metalVersion": lock["targets"]["metal"]["languageVersion"],
    }
    expected_program = {
        "name": PROGRAM,
        "source": SOURCE.name,
        "entryPoint": ENTRY_POINT,
        "stage": "fragment",
        "profile": "ps_6_0",
        "resources": [],
    }
    if (
        contract.get("schemaVersion") != 1
        or contract.get("matrixLayout") != "row-major"
        or contract.get("toolchain") != expected_toolchain
        or contract.get("programs") != [expected_program]
        or expected_toolchain["shaderModel"] != "6.0"
        or expected_toolchain["vulkanVersion"] != "1.2"
        or expected_toolchain["spirvVersion"] != "1.5"
        or expected_toolchain["metalVersion"] != "2.4"
    ):
        raise ValueError("The test-only primitive-ID shader contract does not match the toolchain lock.")
    return contract


def inspect_artifacts() -> None:
    if not (CHECKED / f"{PROGRAM}.dxil").read_bytes().startswith(b"DXBC"):
        raise ValueError("The primitive-ID DXIL container is invalid.")
    metal = (CHECKED / f"{PROGRAM}.metal").read_text(encoding="utf-8")
    if (
        "#include <metal_stdlib>" not in metal
        or not re.search(r"\[\[\s*primitive_id\s*\]\]", metal)
        or not re.search(
            rf'\[\[\s*host_name\s*\(\s*"main"\s*\)\s*\]\]\s*'
            rf"\[\[\s*fragment\s*\]\][^\r\n]*\b{ENTRY_POINT}\s*\(",
            metal,
        )
    ):
        raise ValueError("The Metal probe must expose primitive_id and the named host entry.")
    reflection = json.loads((CHECKED / f"{PROGRAM}.reflection.json").read_text(encoding="utf-8"))
    inputs = reflection.get("stageInputs")
    primitive_inputs = [
        value for value in inputs
        if value.get("semantic", {}).get("name") == "SV_PRIMITIVEID"
    ] if isinstance(inputs, list) else []
    if (
        reflection.get("source") != SOURCE.name
        or reflection.get("entryPoint", {}).get("name") != ENTRY_POINT
        or reflection.get("entryPoint", {}).get("stage") != "fragment"
        or reflection.get("entryPoint", {}).get("profile") != "ps_6_0"
        or reflection.get("resources") != []
        or len(primitive_inputs) != 1
        or primitive_inputs[0].get("semantic", {}).get("systemValue") is not True
        or primitive_inputs[0].get("type") != {"kind": "scalar", "scalarType": "uint32"}
    ):
        raise ValueError("The normalized primitive-ID reflection has an unexpected program contract.")


def expected_receipt(contract: dict[str, object]) -> dict[str, object]:
    return {
        "schemaVersion": 2,
        "programs": [PROGRAM],
        "toolchain": contract["toolchain"],
        "inputs": describe(INPUTS),
        "artifacts": describe(tuple(CHECKED / name for name in ARTIFACTS)),
    }


def verify() -> None:
    contract = read_contract()
    receipt = json.loads((CHECKED / "manifest.json").read_text(encoding="utf-8"))
    if receipt != expected_receipt(contract):
        raise ValueError("The test-only primitive-ID shader payload or its inputs are stale.")
    actual = {path.name for path in CHECKED.iterdir() if path.is_file()}
    if actual != {*ARTIFACTS, "manifest.json"}:
        raise ValueError("The test-only primitive-ID shader payload has unexpected files.")
    for name in (*ARTIFACTS, "manifest.json"):
        if name.endswith((".json", ".metal")):
            content = (CHECKED / name).read_bytes()
            if b"\r" in content or not content.endswith(b"\n") or content.endswith(b"\n\n"):
                raise ValueError(f"The checked shader text must use LF and one final newline: {name}")
    inspect_artifacts()
    print("Primitive-ID test-only shader payload verified.")


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
        raise ValueError("The full test-only shader payload must be generated on Windows win-x64.")
    contract = read_contract()
    inputs_before = describe(INPUTS)
    slang = tool_root / "win-x64" / "slang" / "bin" / "slangc.exe"
    validator = tool_root / "win-x64" / "spirv-tools" / "bin" / "spirv-val.exe"
    if not slang.is_file() or not validator.is_file():
        raise ValueError("The locked Slang compiler and spirv-val must already be installed.")
    if first_line(slang, "-version") != contract["toolchain"]["slangVersion"]:
        raise ValueError("Slang does not match the test-only shader lock.")
    if contract["toolchain"]["spirvToolsCommit"] not in first_line(validator, "--version"):
        raise ValueError("spirv-val does not match the test-only shader lock.")

    CHECKED.mkdir(exist_ok=True)
    (CHECKED / "manifest.json").unlink(missing_ok=True)
    for target, suffix, profile in (
        ("dxil", "dxil", "ps_6_0"),
        ("spirv", "spv", "ps_6_0+spirv_1_5"),
        ("metal", "metal", "ps_6_0+METAL_2_4"),
    ):
        arguments = [
            str(slang), SOURCE.name, "-entry", ENTRY_POINT,
            "-matrix-layout-row-major", "-warnings-as-errors", "all", "-g0",
            "-line-directive-mode", "none", "-source-embed-style", "none", "-O2",
            "-DVOLUME_DENSITY_TEXTURES=0",
            f"-DOPENUSD_TARGET_DXIL={int(target == 'dxil')}",
            f"-DOPENUSD_TARGET_SPIRV={int(target == 'spirv')}",
            f"-DOPENUSD_TARGET_METAL={int(target == 'metal')}",
            "-target", target, "-profile", profile,
            "-o", str(CHECKED / f"{PROGRAM}.{suffix}"),
        ]
        if target == "spirv":
            arguments.append("-fvk-use-entrypoint-name")
        if target != "metal":
            arguments += ["-reflection-json", str(CHECKED / f"{PROGRAM}.{target}-reflection.json")]
        subprocess.run(arguments, cwd=DIRECTORY, check=True)

    metal_path = CHECKED / f"{PROGRAM}.metal"
    metal_source = metal_path.read_text(encoding="utf-8")
    entry_pattern = re.compile(
        rf"(?m)^\[\[\s*fragment\s*\]\](?=[^\r\n]*\b{ENTRY_POINT}\s*\()"
    )
    metal_source, replacements = entry_pattern.subn(
        lambda match: f'[[host_name("main")]] {match.group(0)}', metal_source)
    if replacements != 1:
        raise ValueError("The generated MSL does not contain exactly one named fragment entry point.")
    metal_path.write_bytes(metal_source.encode("utf-8"))
    subprocess.run(
        [str(validator), "--target-env", "vulkan1.2", str(CHECKED / f"{PROGRAM}.spv")],
        cwd=DIRECTORY, check=True)
    subprocess.run([
        sys.executable, "-B", str(NORMALIZER),
        "--dxil-input", str(CHECKED / f"{PROGRAM}.dxil-reflection.json"),
        "--spirv-input", str(CHECKED / f"{PROGRAM}.spirv-reflection.json"),
        "--output", str(CHECKED / f"{PROGRAM}.reflection.json"),
        "--manifest", str(CONTRACT),
        "--program", PROGRAM,
    ], cwd=DIRECTORY, check=True)
    if describe(INPUTS) != inputs_before:
        raise ValueError("Primitive-ID shader inputs changed while the payload was generated.")
    for name in ARTIFACTS:
        if name.endswith((".json", ".metal")):
            path = CHECKED / name
            content = path.read_bytes().replace(b"\r\n", b"\n")
            if b"\r" in content:
                raise ValueError(f"The generated shader text contains a bare carriage return: {name}")
            path.write_bytes(content.rstrip(b"\n") + b"\n")
    inspect_artifacts()
    (CHECKED / "manifest.json").write_bytes(
        (json.dumps(expected_receipt(contract), indent=2) + "\n").encode("utf-8"))
    verify()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=("build", "verify"))
    parser.add_argument("--tool-root", type=pathlib.Path, default=REPOSITORY / "eng" / "shaders" / ".tools")
    arguments = parser.parse_args()
    if arguments.action == "build":
        build(arguments.tool_root.resolve())
    else:
        verify()


if __name__ == "__main__":
    main()
