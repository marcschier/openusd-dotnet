# Copyright (c) marcschier. Licensed under the MIT License.

import copy
import itertools
import re
from typing import Any


STAGE_PREFIXES = {
    "vertex": "vs",
    "fragment": "ps",
    "compute": "cs",
}
REGISTER_CLASSES = {"b", "s", "t", "u"}
RESOURCE_ACCESS = {"constant", "read", "readWrite", "write"}
PERMUTATION_TOKEN_PATTERN = re.compile(r"[a-z][a-z0-9]*")
PERMUTATION_ENTRY_PATTERN = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")
DEFINE_NAME_PATTERN = re.compile(r"[A-Z][A-Z0-9_]*")
# Preprocessor switches every program is compiled with, so a program that does
# not opt in still declares the switch explicitly rather than relying on an
# undefined macro evaluating to zero. A program overrides one by declaring it in
# its manifest "defines" object; the value is identical for every target, so a
# resource can never exist in one backend's binary and be absent from another's.
DEFAULT_DEFINES = {
    "VOLUME_DENSITY_TEXTURES": 0,
}
BASE_METAL_LIBRARY_ENTRIES = (
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
    ("deform.compute", "deformMain", "compute"),
)
METAL_LIBRARY_ENTRIES = BASE_METAL_LIBRARY_ENTRIES
ARTIFACT_SCOPES = ("full", "spirv", "metal")
COMMAND_NAMES_BY_ARTIFACT_SCOPE = {
    "full": ("dxil", "spirv", "metal", "reflection", "spirvValidation"),
    "spirv": ("spirv", "spirvValidation"),
    "metal": ("metal",),
}
ARTIFACT_SUFFIXES_BY_SCOPE = {
    "full": ("dxil", "spv", "metal", "reflection.json"),
    "spirv": ("spv",),
    "metal": ("metal",),
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
    if require_metallib and scope not in ("full", "metal"):
        raise ValueError(
            "A Metal library requires the full or metal artifact scope"
        )
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
        # Xcode 16 rejects the unprefixed spelling, so the macOS platform prefix is
        # part of the standard the metal compiler is invoked with.
        "metalStandard": f"macos-metal{metal_version}",
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


def validate_feature_bits(manifest: dict[str, Any]) -> dict[str, dict[str, Any]]:
    features = manifest.get("featureBits", [])
    if not isinstance(features, list):
        raise ValueError("featureBits must be a list")
    by_name = {}
    tokens = set()
    for feature in features:
        if not isinstance(feature, dict):
            raise ValueError("Feature bit declarations must be objects")
        if set(feature) != {"name", "token", "requires"}:
            raise ValueError("Feature bits must declare name, token, and requires")
        name = feature["name"]
        token = feature["token"]
        requires = feature["requires"]
        if not isinstance(name, str) or not name or name in by_name:
            raise ValueError(f"Invalid or duplicate feature bit: {name!r}")
        if (
            not isinstance(token, str)
            or PERMUTATION_TOKEN_PATTERN.fullmatch(token) is None
            or token in tokens
        ):
            raise ValueError(f"{name} has an invalid or duplicate token")
        if not isinstance(requires, list) or not all(
            isinstance(value, str)
            for value in requires
        ):
            raise ValueError(f"{name} has invalid feature requirements")
        by_name[name] = {
            "name": name,
            "token": token,
            "requires": tuple(requires),
        }
        tokens.add(token)
    for feature in by_name.values():
        missing = [
            requirement
            for requirement in feature["requires"]
            if requirement not in by_name
        ]
        if missing:
            raise ValueError(
                f"{feature['name']} requires undeclared feature bits: {missing}"
            )
    return by_name


def feature_suffix(bits: set[str], feature_order: list[str], features: dict[str, Any]) -> str:
    return "+".join(
        features[name]["token"]
        for name in feature_order
        if name in bits
    )


def entry_point_suffix(suffix: str) -> str:
    return suffix.replace("+", "_")


def valid_feature_sets(
    selected_features: list[str],
    features: dict[str, dict[str, Any]],
) -> list[set[str]]:
    selected = set(selected_features)
    for name in selected_features:
        if name not in features:
            raise ValueError(f"Unknown permutation feature bit: {name}")
        missing = [
            requirement
            for requirement in features[name]["requires"]
            if requirement not in selected
        ]
        if missing:
            raise ValueError(
                f"{name} requires feature bits not selected by the stage: {missing}"
            )
    result = []
    for count in range(len(selected_features) + 1):
        for values in itertools.combinations(selected_features, count):
            bits = set(values)
            if all(
                set(features[name]["requires"]).issubset(bits)
                for name in bits
            ):
                result.append(bits)
    return result


def resource_applies(resource: dict[str, Any], bits: set[str]) -> bool:
    feature = resource.get("feature")
    features_any = resource.get("featuresAny")
    if feature is not None and features_any is not None:
        raise ValueError("Permutation resources cannot mix feature and featuresAny")
    if feature is not None:
        if not isinstance(feature, str):
            raise ValueError("Permutation resource feature must be a string")
        return feature in bits
    if features_any is not None:
        if not isinstance(features_any, list) or not all(
            isinstance(value, str)
            for value in features_any
        ):
            raise ValueError("Permutation resource featuresAny must be a string list")
        return any(value in bits for value in features_any)
    raise ValueError("Permutation resources must declare feature or featuresAny")


def expand_program(
    program: dict[str, Any],
    feature_order: list[str],
    features: dict[str, dict[str, Any]],
) -> list[dict[str, Any]]:
    selected_features = program.get("permutationFeatures")
    if selected_features is None:
        if "permutationFamily" in program:
            raise ValueError(f"{program['name']} is missing permutationFeatures")
        return [copy.deepcopy(program)]
    if not isinstance(selected_features, list) or not all(
        isinstance(value, str)
        for value in selected_features
    ):
        raise ValueError(f"{program['name']} has invalid permutationFeatures")
    family = program.get("permutationFamily")
    if not isinstance(family, str) or not family:
        raise ValueError(f"{program['name']} is missing a permutationFamily")
    resources = program.get("resources", [])
    permutation_resources = program.get("permutationResources", [])
    if not isinstance(permutation_resources, list):
        raise ValueError(f"{program['name']} has invalid permutationResources")
    expanded = []
    for bits in valid_feature_sets(selected_features, features):
        suffix = feature_suffix(bits, feature_order, features)
        public_suffix = suffix if suffix else "none"
        variant = copy.deepcopy(program)
        if suffix:
            variant["name"] = f"{program['name']}.{suffix}"
            variant["entryPoint"] = (
                f"{program['entryPoint']}_{entry_point_suffix(suffix)}"
            )
        else:
            variant["name"] = program["name"]
            variant["entryPoint"] = program["entryPoint"]
        variant["permutationFamily"] = family
        variant["permutationBits"] = [
            name
            for name in feature_order
            if name in bits
        ]
        variant["permutationBaseName"] = program["name"]
        variant["permutationSuffix"] = public_suffix
        variant["resources"] = copy.deepcopy(resources)
        for resource in permutation_resources:
            if resource_applies(resource, bits):
                resource_contract = {
                    key: copy.deepcopy(value)
                    for key, value in resource.items()
                    if key not in {"feature", "featuresAny"}
                }
                variant["resources"].append(resource_contract)
        variant.pop("permutationFeatures", None)
        variant.pop("permutationResources", None)
        expanded.append(variant)
    return expanded


def validate_permutation_budgets(
    manifest: dict[str, Any],
    programs: list[dict[str, Any]],
) -> None:
    budgets = manifest.get("permutationBudgets", [])
    if not isinstance(budgets, list):
        raise ValueError("permutationBudgets must be a list")
    by_family_stage = {}
    for budget in budgets:
        if not isinstance(budget, dict) or set(budget) != {
            "family",
            "stage",
            "maxPermutations",
        }:
            raise ValueError("Permutation budgets must declare family, stage, and max")
        family = budget["family"]
        stage = budget["stage"]
        maximum = budget["maxPermutations"]
        key = (family, stage)
        if (
            not isinstance(family, str)
            or not isinstance(stage, str)
            or stage not in STAGE_PREFIXES
            or not isinstance(maximum, int)
            or isinstance(maximum, bool)
            or maximum <= 0
            or key in by_family_stage
        ):
            raise ValueError(f"Invalid permutation budget: {budget!r}")
        by_family_stage[key] = maximum

    counts: dict[tuple[str, str], int] = {}
    for program in programs:
        family = program.get("permutationFamily")
        if family is None:
            continue
        key = (family, program["stage"])
        counts[key] = counts.get(key, 0) + 1
    if set(counts) != set(by_family_stage):
        missing = sorted(set(counts) - set(by_family_stage))
        unused = sorted(set(by_family_stage) - set(counts))
        raise ValueError(
            "Permutation budgets do not cover the expanded families: "
            f"missing={missing}, unused={unused}"
        )
    for key, count in counts.items():
        maximum = by_family_stage[key]
        if count > maximum:
            family, stage = key
            raise ValueError(
                f"Permutation budget exceeded for {family} {stage}: "
                f"{count} > {maximum}"
            )


def expanded_manifest(manifest: dict[str, Any]) -> dict[str, Any]:
    validated = copy.deepcopy(manifest)
    features = validate_feature_bits(validated)
    feature_order = [
        feature["name"]
        for feature in validated.get("featureBits", [])
    ]
    programs = []
    for program in validated.get("programs", []):
        programs.extend(expand_program(program, feature_order, features))
    validated["programs"] = programs
    validate_permutation_budgets(validated, programs)
    return validated


def validate_manifest(
    manifest: dict[str, Any],
    model: dict[str, Any],
) -> dict[str, Any]:
    validated = expanded_manifest(manifest)
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
        floating_point_mode = program.get("floatingPointMode")
        if floating_point_mode not in {None, "precise"}:
            raise ValueError(
                f"{name} has unsupported floatingPointMode "
                f"{floating_point_mode!r}"
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
        program_defines(program)
    if not names:
        raise ValueError("The shader manifest has no programs")
    return validated


def program_defines(program: dict[str, Any]) -> dict[str, int]:
    """Resolves the complete preprocessor switch set a program compiles with."""
    declared = program.get("defines", {})
    if not isinstance(declared, dict):
        raise ValueError(f"{program.get('name')!r} has invalid defines")
    resolved = dict(DEFAULT_DEFINES)
    for key, value in declared.items():
        if (
            not isinstance(key, str)
            or not DEFINE_NAME_PATTERN.fullmatch(key)
            or key not in DEFAULT_DEFINES
        ):
            raise ValueError(
                f"{program.get('name')!r} declares an unknown define {key!r}"
            )
        if not isinstance(value, int) or isinstance(value, bool):
            raise ValueError(
                f"{program.get('name')!r} define {key} must be an integer"
            )
        resolved[key] = value
    return resolved


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
    has_permutations = bool(
        manifest.get("featureBits")
        or any(
            isinstance(program, dict) and "permutationFamily" in program
            for program in manifest.get("programs", [])
        )
    )
    programs = (
        expanded_manifest(manifest).get("programs")
        if has_permutations
        else manifest.get("programs")
    )
    if not isinstance(programs, list) or not programs:
        raise ValueError("The shader manifest programs value is invalid")
    if has_permutations:
        return copy.deepcopy(programs)

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


def metal_library_contract(
    manifest: dict[str, Any] | None = None,
) -> list[dict[str, str]]:
    if manifest is None:
        entries = BASE_METAL_LIBRARY_ENTRIES
    else:
        entries = [
            (program["name"], program["entryPoint"], program["stage"])
            for program in metal_library_programs(manifest)
        ]
    return [
        {
            "programName": program_name,
            "entryPoint": entry_point,
            "stage": stage,
        }
        for program_name, entry_point, stage in entries
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
        defines = []
        permutation_bits = program.get("permutationBits")
        if permutation_bits is not None:
            if program["stage"] == "vertex":
                defines.append(f"-DVERTEX_ENTRY_POINT={program['entryPoint']}")
                defines.append("-DVERTEX_STAGE=1")
            elif program["stage"] == "fragment":
                defines.append(f"-DFRAGMENT_ENTRY_POINT={program['entryPoint']}")
                defines.append("-DFRAGMENT_STAGE=1")
            else:
                raise ValueError(
                    f"{program['name']} has unsupported permutation stage"
                )
            defines.extend(
                f"-D{feature}=1"
                for feature in permutation_bits
            )
        switches = program_defines(program)
        floating_point_options = (
            []
            if program.get("floatingPointMode") is None
            else ["-fp-mode", program["floatingPointMode"]]
        )
        common = [
            program["source"],
            "-entry",
            program["entryPoint"],
            *defines,
            *common_options,
            *floating_point_options,
            *(
                f"-D{name}={value}"
                for name, value in sorted(switches.items())
            ),
        ]
        spirv_reflection_options = (
            []
            if artifact_scope == "spirv"
            else ["-reflection-json", raw_spirv]
        )
        # Slang defines no reliable per-target macro, so the target a program is
        # being compiled for is stated explicitly. A stage that must differ per
        # target -- the subprim pick vertex writes a point size that Vulkan and
        # Metal require and DXIL rejects -- branches on exactly one of these
        # rather than on an undocumented compiler internal.
        dxil_target_defines = [
            "-DOPENUSD_TARGET_DXIL=1",
            "-DOPENUSD_TARGET_SPIRV=0",
            "-DOPENUSD_TARGET_METAL=0",
        ]
        spirv_target_defines = [
            "-DOPENUSD_TARGET_DXIL=0",
            "-DOPENUSD_TARGET_SPIRV=1",
            "-DOPENUSD_TARGET_METAL=0",
        ]
        metal_target_defines = [
            "-DOPENUSD_TARGET_DXIL=0",
            "-DOPENUSD_TARGET_SPIRV=0",
            "-DOPENUSD_TARGET_METAL=1",
        ]
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
                            *dxil_target_defines,
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
                            *spirv_target_defines,
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
                            *metal_target_defines,
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
