# Copyright (c) marcschier. Licensed under the MIT License.

import copy
import re
from typing import Any


STAGE_PREFIXES = {
    "vertex": "vs",
    "fragment": "ps",
    "compute": "cs",
}
REGISTER_CLASSES = {"b", "s", "t", "u"}
RESOURCE_ACCESS = {"constant", "read", "readWrite", "write"}
METAL_LIBRARY_ENTRIES = (
    ("mesh.vertex", "vertexMain", "vertex"),
    ("mesh.fragment", "fragmentMain", "fragment"),
    ("pick.vertex", "pickVertexMain", "vertex"),
    ("pick.fragment", "pickFragmentMain", "fragment"),
    ("selection.mask.vertex", "selectionMaskVertexMain", "vertex"),
    ("selection.mask.fragment", "selectionMaskFragmentMain", "fragment"),
    ("selection.outline.vertex", "selectionOutlineVertexMain", "vertex"),
    (
        "selection.outline.fragment",
        "selectionOutlineFragmentMain",
        "fragment",
    ),
    ("compute.fill", "fillMain", "compute"),
    ("compute.scale", "scaleMain", "compute"),
)
ARTIFACT_SCOPES = ("full", "spirv")
COMMAND_NAMES_BY_ARTIFACT_SCOPE = {
    "full": ("dxil", "spirv", "metal", "reflection", "spirvValidation"),
    "spirv": ("spirv", "spirvValidation"),
}
ARTIFACT_SUFFIXES_BY_SCOPE = {
    "full": ("dxil", "spv", "metal", "reflection.json"),
    "spirv": ("spv",),
}
REQUIRED_CHECKED_INPUTS = (
    ".github/workflows/ci.yml",
    ".github/workflows/native.yml",
    ".github/workflows/package.yml",
    ".github/workflows/performance.yml",
    ".github/workflows/release.yml",
    ".github/workflows/render.yml",
    ".github/workflows/shaders.yml",
    "eng/shaders/.gitignore",
    "eng/shaders/build-shaders.ps1",
    "eng/shaders/build-toolchain.ps1",
    "eng/shaders/expand-verified-archive.ps1",
    "eng/shaders/fetch-toolchain.ps1",
    "eng/shaders/prepare-metal-library.ps1",
    "eng/shaders/select-xcode.ps1",
    "eng/shaders/shader-manifest.json",
    "eng/shaders/test-checked-corruption.ps1",
    "eng/shaders/test-checked-input-line-endings.ps1",
    "eng/shaders/test-expand-verified-archive.ps1",
    "eng/shaders/toolchain.lock.json",
    "eng/shaders/update-checked.ps1",
    "eng/shaders/validate-checked-payload.ps1",
    "eng/shaders/validate-workflow-paths.ps1",
    "eng/shaders/verify-checked.ps1",
    "eng/shaders/verify-reproducibility.ps1",
    "eng/shaders/verify-shaders.ps1",
    "eng/shaders/write-host-toolchain.ps1",
    "eng/shaders/scripts/checked_payload.py",
    "eng/shaders/scripts/metal_sidecar.py",
    "eng/shaders/scripts/normalize-reflection.py",
    "eng/shaders/scripts/publish-checked.py",
    "eng/shaders/scripts/shader-commands.py",
    "eng/shaders/scripts/shader_model.py",
    "eng/shaders/scripts/verify-artifacts.py",
    "eng/shaders/scripts/verify-metal-package.py",
    "src/OpenUsd.Rendering.Silk.Metal/OpenUsd.Rendering.Silk.Metal.csproj",
    "tests/OpenUsd.Package.Tests/RuntimePackageTests.cs",
)


def validate_artifact_scope(value: str) -> str:
    if value not in ARTIFACT_SCOPES:
        raise ValueError(f"Unsupported shader artifact scope: {value!r}")
    return value


def command_names_for_artifact_scope(value: str) -> tuple[str, ...]:
    return COMMAND_NAMES_BY_ARTIFACT_SCOPE[validate_artifact_scope(value)]


def artifact_suffixes_for_scope(
    value: str,
    require_metallib: bool = False,
) -> tuple[str, ...]:
    scope = validate_artifact_scope(value)
    if require_metallib and scope != "full":
        raise ValueError("A Metal library requires the full artifact scope")
    suffixes = ARTIFACT_SUFFIXES_BY_SCOPE[scope]
    return (*suffixes, "metallib") if require_metallib else suffixes


def validate_checked_input_bytes(relative_path: str, content: bytes) -> None:
    if b"\r" in content:
        raise ValueError(
            f"Required checked input must use LF-only line endings: {relative_path}"
        )


def version_pair(value: str, name: str) -> tuple[int, int]:
    match = re.fullmatch(r"(\d+)\.(\d+)", value)
    if match is None:
        raise ValueError(f"{name} must be a major.minor version, got {value!r}")
    return int(match.group(1)), int(match.group(2))


def version_token(value: str, name: str) -> str:
    major, minor = version_pair(value, name)
    return f"{major}_{minor}"


def build_lock_model(lock: dict[str, Any]) -> dict[str, Any]:
    targets = lock["targets"]
    direct3d_format = str(targets["direct3D"]["format"])
    if direct3d_format != "DXIL":
        raise ValueError(f"Unsupported Direct3D shader format: {direct3d_format}")
    shader_model = str(targets["direct3D"]["shaderModel"])
    vulkan_version = str(targets["vulkan"]["apiVersion"])
    spirv_version = str(targets["vulkan"]["spirvVersion"])
    metal_version = str(targets["metal"]["languageVersion"])
    xcode_version = str(targets["metal"]["xcodeVersion"])
    version_pair(shader_model, "Shader Model")
    version_pair(vulkan_version, "Vulkan")
    version_pair(spirv_version, "SPIR-V")
    version_pair(metal_version, "Metal")
    version_pair(xcode_version, "Xcode")
    return {
        "slangVersion": str(lock["slang"]["version"]),
        "slangCommit": str(lock["slang"]["commit"]),
        "spirvToolsVersion": str(lock["spirvTools"]["version"]),
        "spirvToolsCommit": str(lock["spirvTools"]["commit"]),
        "shaderModel": shader_model,
        "shaderModelToken": version_token(shader_model, "Shader Model"),
        "direct3DFormat": direct3d_format,
        "vulkanVersion": vulkan_version,
        "spirvVersion": spirv_version,
        "spirvCapability": (
            f"spirv_{version_token(spirv_version, 'SPIR-V')}"
        ),
        "spirvTargetEnv": f"vulkan{vulkan_version}",
        "metalVersion": metal_version,
        "metalCapability": (
            f"METAL_{version_token(metal_version, 'Metal')}"
        ),
        "metalStandard": f"metal{metal_version}",
        "xcodeVersion": xcode_version,
    }


def expected_profile(stage: str, model: dict[str, Any]) -> str:
    if stage not in STAGE_PREFIXES:
        raise ValueError(f"Unsupported shader stage: {stage}")
    return f"{STAGE_PREFIXES[stage]}_{model['shaderModelToken']}"


def validate_resource_contract(resource: dict[str, Any], program_name: str) -> None:
    name = resource.get("name")
    if not isinstance(name, str) or not name:
        raise ValueError(f"{program_name} has a resource without a name")
    access = resource.get("access")
    if access not in RESOURCE_ACCESS:
        raise ValueError(f"{program_name}:{name} has invalid or missing access")
    d3d = resource.get("d3d")
    vulkan = resource.get("vulkan")
    if not isinstance(d3d, dict) or not isinstance(vulkan, dict):
        raise ValueError(f"{program_name}:{name} must define D3D and Vulkan bindings")
    if set(d3d) != {"registerClass", "register", "space"}:
        raise ValueError(f"{program_name}:{name} has an incomplete D3D binding")
    if set(vulkan) != {"binding", "set"}:
        raise ValueError(f"{program_name}:{name} has an incomplete Vulkan binding")
    if d3d["registerClass"] not in REGISTER_CLASSES:
        raise ValueError(f"{program_name}:{name} has an invalid register class")
    for key in ("register", "space"):
        if not isinstance(d3d[key], int) or d3d[key] < 0:
            raise ValueError(f"{program_name}:{name} has an invalid D3D {key}")
    for key in ("binding", "set"):
        if not isinstance(vulkan[key], int) or vulkan[key] < 0:
            raise ValueError(f"{program_name}:{name} has an invalid Vulkan {key}")


def validate_manifest(
    manifest: dict[str, Any],
    model: dict[str, Any],
) -> dict[str, Any]:
    validated = copy.deepcopy(manifest)
    matrix_layout = validated.get("matrixLayout")
    if matrix_layout not in {"row-major", "column-major"}:
        raise ValueError(f"Unsupported matrix layout: {matrix_layout!r}")
    names = set()
    for program in validated.get("programs", []):
        name = program.get("name")
        if not isinstance(name, str) or not name or name in names:
            raise ValueError(f"Invalid or duplicate shader program name: {name!r}")
        names.add(name)
        for key in ("source", "entryPoint", "profile"):
            if not isinstance(program.get(key), str) or not program[key]:
                raise ValueError(f"{name} has an invalid {key}")
        stage = str(program.get("stage", ""))
        locked_profile = expected_profile(stage, model)
        if program.get("profile") != locked_profile:
            raise ValueError(
                f"{name} profile {program.get('profile')!r} does not match "
                f"locked profile {locked_profile!r}"
            )
        resources = program.get("resources")
        if not isinstance(resources, list):
            raise ValueError(f"{name} must define an explicit resource contract")
        resource_names = set()
        for resource in resources:
            validate_resource_contract(resource, name)
            if resource["name"] in resource_names:
                raise ValueError(f"{name} has duplicate resource {resource['name']}")
            resource_names.add(resource["name"])
    if not names:
        raise ValueError("The shader manifest has no programs")
    return validated


def required_checked_inputs(manifest: dict[str, Any]) -> tuple[str, ...]:
    if len(REQUIRED_CHECKED_INPUTS) != len(set(REQUIRED_CHECKED_INPUTS)):
        raise ValueError("The centralized checked input set contains duplicates")
    sources = {
        str(program["source"]).replace("\\", "/")
        for program in manifest.get("programs", [])
    }
    required = set(REQUIRED_CHECKED_INPUTS)
    required.update(sources)
    required.add("eng/shaders/checked/executed-commands.json")
    return tuple(sorted(required))


def metal_library_programs(manifest: dict[str, Any]) -> list[dict[str, Any]]:
    programs = manifest.get("programs")
    if not isinstance(programs, list):
        raise ValueError("The shader manifest programs value is invalid")
    selected = []
    for program_name, entry_point, stage in METAL_LIBRARY_ENTRIES:
        matches = [
            program
            for program in programs
            if program.get("name") == program_name
        ]
        if len(matches) != 1:
            raise ValueError(
                f"Expected exactly one {program_name} shader program"
            )
        program = matches[0]
        if (
            program.get("entryPoint") != entry_point
            or program.get("stage") != stage
        ):
            raise ValueError(
                f"{program_name} must declare {stage} entry point {entry_point}"
            )
        selected.append(program)
    return selected


def metal_library_contract() -> list[dict[str, str]]:
    return [
        {
            "programName": program_name,
            "entryPoint": entry_point,
            "stage": stage,
        }
        for program_name, entry_point, stage in METAL_LIBRARY_ENTRIES
    ]


def generate_plan(
    lock: dict[str, Any],
    manifest: dict[str, Any],
    output_root: str,
    artifact_scope: str = "full",
) -> dict[str, Any]:
    model = build_lock_model(lock)
    validated = validate_manifest(manifest, model)
    command_names = command_names_for_artifact_scope(artifact_scope)
    output_root = output_root.replace("\\", "/").rstrip("/")
    raw_root = f"{output_root}/.raw-reflection"
    common_options = [
        f"-matrix-layout-{validated['matrixLayout']}",
        "-warnings-as-errors",
        "all",
        "-g0",
        "-line-directive-mode",
        "none",
        "-source-embed-style",
        "none",
        "-O2",
    ]
    programs = []
    for program in validated["programs"]:
        base = f"{output_root}/{program['name']}"
        raw_dxil = f"{raw_root}/{program['name']}.dxil.json"
        raw_spirv = f"{raw_root}/{program['name']}.spirv.json"
        common = [
            program["source"],
            "-entry",
            program["entryPoint"],
            *common_options,
        ]
        spirv_reflection_options = (
            []
            if artifact_scope == "spirv"
            else ["-reflection-json", raw_spirv]
        )
        programs.append(
            {
                "name": program["name"],
                "source": program["source"],
                "entryPoint": program["entryPoint"],
                "stage": program["stage"],
                "profile": program["profile"],
                "commands": {
                    "dxil": {
                        "executable": "slangc",
                        "arguments": [
                            *common,
                            "-target",
                            "dxil",
                            "-profile",
                            program["profile"],
                            "-o",
                            f"{base}.dxil",
                            "-reflection-json",
                            raw_dxil,
                        ],
                    },
                    "spirv": {
                        "executable": "slangc",
                        "arguments": [
                            *common,
                            "-target",
                            "spirv",
                            "-profile",
                            (
                                f"{program['profile']}+"
                                f"{model['spirvCapability']}"
                            ),
                            "-o",
                            f"{base}.spv",
                            *spirv_reflection_options,
                        ],
                    },
                    "metal": {
                        "executable": "slangc",
                        "arguments": [
                            *common,
                            "-target",
                            "metal",
                            "-profile",
                            (
                                f"{program['profile']}+"
                                f"{model['metalCapability']}"
                            ),
                            "-o",
                            f"{base}.metal",
                        ],
                    },
                    "reflection": {
                        "executable": "python",
                        "arguments": [
                            "eng/shaders/scripts/normalize-reflection.py",
                            "--dxil-input",
                            raw_dxil,
                            "--spirv-input",
                            raw_spirv,
                            "--output",
                            f"{base}.reflection.json",
                            "--manifest",
                            "eng/shaders/shader-manifest.json",
                            "--program",
                            program["name"],
                        ],
                    },
                    "spirvValidation": {
                        "executable": "spirv-val",
                        "arguments": [
                            "--target-env",
                            model["spirvTargetEnv"],
                            f"{base}.spv",
                        ],
                    },
                },
            }
        )
        programs[-1]["commands"] = {
            name: programs[-1]["commands"][name]
            for name in command_names
        }
    plan = {
        "schemaVersion": 1,
        "toolchain": model,
        "outputRoot": output_root,
    }
    if artifact_scope != "full":
        plan["artifactScope"] = artifact_scope
    plan["programs"] = programs
    return plan
