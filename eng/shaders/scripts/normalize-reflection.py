# Copyright (c) marcschier. Licensed under the MIT License.

import argparse
import json
import pathlib
import re
from typing import Any

from shader_model import expanded_manifest


D3D_REGISTER_CLASSES = {
    "constantBuffer": "b",
    "samplerState": "s",
    "shaderResource": "t",
    "unorderedAccess": "u",
}


def require(mapping: dict[str, Any], key: str, context: str) -> Any:
    if key not in mapping:
        raise ValueError(f"Missing {key} in {context}")
    return mapping[key]


def scalar_size(scalar_type: str) -> int:
    match = re.search(r"(\d+)$", scalar_type)
    if match is None:
        raise ValueError(f"Scalar type has no bit width: {scalar_type}")
    return int(match.group(1)) // 8


def type_size(type_info: dict[str, Any]) -> int | None:
    kind = require(type_info, "kind", "type")
    if kind == "scalar":
        return scalar_size(require(type_info, "scalarType", "scalar type"))
    if kind == "vector":
        element_size = type_size(require(type_info, "elementType", "vector"))
        count = require(type_info, "elementCount", "vector")
        return None if element_size is None else element_size * count
    if kind == "matrix":
        element_size = type_size(require(type_info, "elementType", "matrix"))
        rows = require(type_info, "rowCount", "matrix")
        columns = require(type_info, "columnCount", "matrix")
        return None if element_size is None else element_size * rows * columns
    if kind == "array":
        element_size = type_size(require(type_info, "elementType", "array"))
        count = require(type_info, "elementCount", "array")
        return None if element_size is None else element_size * count
    if kind == "struct":
        fields = require(type_info, "fields", "struct")
        if not fields:
            return 0
        sizes = []
        for field in fields:
            binding = require(field, "binding", f"field {field.get('name')}")
            offset = require(binding, "offset", f"field {field.get('name')}")
            size = require(binding, "size", f"field {field.get('name')}")
            sizes.append(offset + size)
        return max(sizes)
    return None


def normalize_type(
    type_info: dict[str, Any],
    matrix_layout: str,
) -> dict[str, Any]:
    kind = require(type_info, "kind", "type")
    if kind == "scalar":
        return {
            "kind": "scalar",
            "scalarType": require(type_info, "scalarType", "scalar type"),
        }
    if kind == "vector":
        return {
            "kind": "vector",
            "elementCount": require(type_info, "elementCount", "vector"),
            "elementType": normalize_type(
                require(type_info, "elementType", "vector"),
                matrix_layout,
            ),
        }
    if kind == "matrix":
        return {
            "kind": "matrix",
            "rowCount": require(type_info, "rowCount", "matrix"),
            "columnCount": require(type_info, "columnCount", "matrix"),
            "elementType": normalize_type(
                require(type_info, "elementType", "matrix"),
                matrix_layout,
            ),
        }
    if kind == "array":
        return {
            "kind": "array",
            "elementCount": require(type_info, "elementCount", "array"),
            "elementType": normalize_type(
                require(type_info, "elementType", "array"),
                matrix_layout,
            ),
        }
    if kind == "struct":
        fields = []
        for field in require(type_info, "fields", "struct"):
            name = require(field, "name", "struct field")
            binding = require(field, "binding", f"field {name}")
            field_type = require(field, "type", f"field {name}")
            size = require(binding, "size", f"field {name}")
            layout = {
                "offset": require(binding, "offset", f"field {name}"),
                "size": size,
                "elementStride": require(
                    binding,
                    "elementStride",
                    f"field {name}",
                ),
                "matrixStride": None,
            }
            if field_type.get("kind") == "matrix":
                rows = require(field_type, "rowCount", f"matrix field {name}")
                columns = require(
                    field_type,
                    "columnCount",
                    f"matrix field {name}",
                )
                major_count = rows if matrix_layout == "row-major" else columns
                if size % major_count != 0:
                    raise ValueError(f"Matrix field {name} has invalid size {size}")
                layout["matrixStride"] = size // major_count
                layout["matrixLayout"] = matrix_layout
            fields.append(
                {
                    "name": name,
                    "type": normalize_type(field_type, matrix_layout),
                    "layout": layout,
                }
            )
        return {
            "kind": "struct",
            "name": type_info.get("name"),
            "fields": fields,
        }
    raise ValueError(f"Unsupported reflected type kind: {kind}")


def unwrap_array(type_info: dict[str, Any]) -> tuple[dict[str, Any], list[int]]:
    counts = []
    current = type_info
    while current.get("kind") == "array":
        counts.append(require(current, "elementCount", "resource array"))
        current = require(current, "elementType", "resource array")
    return current, counts


def normalize_resource_shape(
    type_info: dict[str, Any],
    expected_access: str,
    matrix_layout: str,
) -> dict[str, Any]:
    base_type, array_counts = unwrap_array(type_info)
    kind = require(base_type, "kind", "resource type")
    array_count: int | list[int] | None
    if not array_counts:
        array_count = None
    elif len(array_counts) == 1:
        array_count = array_counts[0]
    else:
        array_count = array_counts

    if kind == "constantBuffer":
        if expected_access != "constant":
            raise ValueError("Constant buffer access contract must be constant")
        element_type = require(base_type, "elementType", "constant buffer")
        layout = require(base_type, "elementVarLayout", "constant buffer")
        binding = require(layout, "binding", "constant buffer layout")
        return {
            "kind": "constantBuffer",
            "access": "constant",
            "arrayCount": array_count,
            "elementType": normalize_type(element_type, matrix_layout),
            "elementStride": require(
                binding,
                "elementStride",
                "constant buffer layout",
            ),
            "size": require(binding, "size", "constant buffer layout"),
        }
    if kind == "resource":
        base_shape = require(base_type, "baseShape", "resource")
        result_type = require(base_type, "resultType", "resource")
        access = base_type.get("access")
        if access is None and expected_access == "read" and base_shape.startswith("texture"):
            access = "read"
        if access is None:
            raise ValueError(f"Missing access in {base_shape} resource")
        if access != expected_access:
            raise ValueError(
                f"Resource access {access} does not match contract {expected_access}"
            )
        stride = type_size(result_type) if base_shape == "structuredBuffer" else None
        if base_shape == "structuredBuffer" and stride is None:
            raise ValueError("Structured buffer element stride is unavailable")
        return {
            "kind": base_shape,
            "access": access,
            "arrayCount": array_count,
            "elementType": normalize_type(result_type, matrix_layout),
            "elementStride": stride,
            "size": None,
        }
    if kind == "samplerState":
        if expected_access != "read":
            raise ValueError("Sampler access contract must be read")
        return {
            "kind": "sampler",
            "access": "read",
            "arrayCount": array_count,
            "elementType": None,
            "elementStride": None,
            "size": None,
        }
    raise ValueError(f"Unsupported resource shape: {kind}")


def parameter_map(data: dict[str, Any], target: str) -> dict[str, dict[str, Any]]:
    parameters = {}
    for parameter in data.get("parameters", []):
        name = require(parameter, "name", f"{target} parameter")
        if name in parameters:
            raise ValueError(f"Duplicate {target} parameter {name}")
        parameters[name] = parameter
    return parameters


def reflected_space(
    binding: dict[str, Any],
    expected: int,
    context: str,
) -> int:
    if "space" in binding:
        return binding["space"]
    if expected != 0:
        raise ValueError(f"Missing non-zero space in {context}")
    return expected


def normalize_resources(
    dxil: dict[str, Any],
    spirv: dict[str, Any],
    contract: list[dict[str, Any]],
    matrix_layout: str,
) -> list[dict[str, Any]]:
    dxil_parameters = parameter_map(dxil, "DXIL")
    spirv_parameters = parameter_map(spirv, "SPIR-V")
    contract_names = {resource["name"] for resource in contract}
    if set(dxil_parameters) != contract_names:
        raise ValueError(
            "DXIL resources do not match the manifest contract: "
            f"missing={sorted(contract_names - set(dxil_parameters))}, "
            f"unexpected={sorted(set(dxil_parameters) - contract_names)}"
        )
    if set(spirv_parameters) != contract_names:
        raise ValueError(
            "SPIR-V resources do not match the manifest contract: "
            f"missing={sorted(contract_names - set(spirv_parameters))}, "
            f"unexpected={sorted(set(spirv_parameters) - contract_names)}"
        )

    resources = []
    for expected in contract:
        name = expected["name"]
        dxil_parameter = dxil_parameters[name]
        spirv_parameter = spirv_parameters[name]
        dxil_binding = require(dxil_parameter, "binding", f"DXIL resource {name}")
        spirv_binding = require(
            spirv_parameter,
            "binding",
            f"SPIR-V resource {name}",
        )
        dxil_kind = require(dxil_binding, "kind", f"DXIL resource {name}")
        register_class = D3D_REGISTER_CLASSES.get(dxil_kind)
        if register_class is None:
            raise ValueError(f"Unsupported DXIL binding kind {dxil_kind}")
        dxil_index = require(dxil_binding, "index", f"DXIL resource {name}")
        dxil_space = reflected_space(
            dxil_binding,
            expected["d3d"]["space"],
            f"DXIL resource {name}",
        )
        spirv_kind = require(spirv_binding, "kind", f"SPIR-V resource {name}")
        if spirv_kind != "descriptorTableSlot":
            raise ValueError(f"Unexpected SPIR-V binding kind {spirv_kind}")
        spirv_index = require(
            spirv_binding,
            "index",
            f"SPIR-V resource {name}",
        )
        spirv_set = reflected_space(
            spirv_binding,
            expected["vulkan"]["set"],
            f"SPIR-V resource {name}",
        )
        actual_d3d = {
            "registerClass": register_class,
            "register": dxil_index,
            "space": dxil_space,
        }
        actual_vulkan = {
            "binding": spirv_index,
            "set": spirv_set,
        }
        if actual_d3d != expected["d3d"]:
            raise ValueError(f"DXIL binding mismatch for resource {name}")
        if actual_vulkan != expected["vulkan"]:
            raise ValueError(f"SPIR-V binding mismatch for resource {name}")

        dxil_type = require(dxil_parameter, "type", f"DXIL resource {name}")
        spirv_type = require(spirv_parameter, "type", f"SPIR-V resource {name}")
        expected_access = require(expected, "access", f"resource contract {name}")
        dxil_shape = normalize_resource_shape(
            dxil_type,
            expected_access,
            matrix_layout,
        )
        spirv_shape = normalize_resource_shape(
            spirv_type,
            expected_access,
            matrix_layout,
        )
        if dxil_shape["kind"] != spirv_shape["kind"]:
            raise ValueError(f"Target resource shape mismatch for {name}")
        resources.append(
            {
                "name": name,
                "bindings": {
                    "d3d": actual_d3d,
                    "vulkan": actual_vulkan,
                },
                "shape": dxil_shape,
                "vulkanLayout": spirv_shape,
            }
        )
    return resources


def semantic_parts(value: str) -> tuple[str, int, bool]:
    match = re.fullmatch(r"(.+?)(\d+)?", value.upper())
    if match is None:
        raise ValueError(f"Invalid semantic: {value}")
    return match.group(1), int(match.group(2) or 0), value.upper().startswith("SV_")


def interface_leaf(
    value: dict[str, Any],
    fallback_name: str,
    direction: str,
    matrix_layout: str,
) -> dict[str, Any]:
    semantic_value = require(value, "semanticName", f"{direction} {fallback_name}")
    semantic_name, semantic_index, system_value = semantic_parts(semantic_value)
    binding = value.get("binding")
    if system_value:
        location = None
    else:
        if binding is None:
            raise ValueError(f"Missing binding for user varying {fallback_name}")
        location = require(binding, "index", f"user varying {fallback_name}")
    return {
        "name": value.get("name", fallback_name),
        "semantic": {
            "name": semantic_name,
            "index": value.get("semanticIndex", semantic_index),
            "systemValue": system_value,
        },
        "location": location,
        "type": normalize_type(
            require(value, "type", f"{direction} {fallback_name}"),
            matrix_layout,
        ),
    }


def normalize_interface(
    values: list[dict[str, Any]],
    direction: str,
    matrix_layout: str,
) -> list[dict[str, Any]]:
    normalized = []
    for value in values:
        value_type = require(value, "type", f"{direction} value")
        fields = value_type.get("fields")
        if fields is None:
            normalized.append(
                interface_leaf(
                    value,
                    value.get("name", "result"),
                    direction,
                    matrix_layout,
                )
            )
            continue
        for field in fields:
            normalized.append(
                interface_leaf(
                    field,
                    field.get("name", value.get("name", "value")),
                    direction,
                    matrix_layout,
                )
            )
    return normalized


def find_entry(
    data: dict[str, Any],
    entry_point_name: str,
    stage: str,
    target: str,
) -> dict[str, Any]:
    matching = [
        entry
        for entry in data.get("entryPoints", [])
        if entry.get("name") == entry_point_name
    ]
    if len(matching) != 1:
        raise ValueError(f"Expected one {target} entry point named {entry_point_name}")
    entry = matching[0]
    if entry.get("stage") != stage:
        raise ValueError(f"Expected {stage} {target} entry point")
    return entry


def normalize(
    dxil: dict[str, Any],
    spirv: dict[str, Any],
    program: dict[str, Any],
    matrix_layout: str,
) -> dict[str, Any]:
    entry_name = program["entryPoint"]
    stage = program["stage"]
    dxil_entry = find_entry(dxil, entry_name, stage, "DXIL")
    spirv_entry = find_entry(spirv, entry_name, stage, "SPIR-V")
    thread_group_size = dxil_entry.get("threadGroupSize")
    if stage == "compute":
        if (
            not isinstance(thread_group_size, list)
            or len(thread_group_size) != 3
            or any(
                not isinstance(dimension, int)
                or isinstance(dimension, bool)
                or dimension <= 0
                for dimension in thread_group_size
            )
        ):
            raise ValueError(
                "Compute threadGroupSize must contain three positive integers"
            )
        if spirv_entry.get("threadGroupSize") != thread_group_size:
            raise ValueError("Target threadGroupSize values do not match")
    elif thread_group_size is not None:
        raise ValueError("Non-compute entry point has a threadGroupSize")
    result = dxil_entry.get("result")
    outputs = [] if result is None else [result]
    return {
        "schemaVersion": 2,
        "source": program["source"].replace("\\", "/"),
        "entryPoint": {
            "name": entry_name,
            "stage": stage,
            "profile": program["profile"],
            "threadGroupSize": thread_group_size,
        },
        "resources": normalize_resources(
            dxil,
            spirv,
            program["resources"],
            matrix_layout,
        ),
        "stageInputs": normalize_interface(
            dxil_entry.get("parameters", []),
            "input",
            matrix_layout,
        ),
        "stageOutputs": normalize_interface(
            outputs,
            "output",
            matrix_layout,
        ),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dxil-input", required=True, type=pathlib.Path)
    parser.add_argument("--spirv-input", required=True, type=pathlib.Path)
    parser.add_argument("--output", required=True, type=pathlib.Path)
    parser.add_argument("--manifest", required=True, type=pathlib.Path)
    parser.add_argument("--program", required=True)
    args = parser.parse_args()

    manifest = expanded_manifest(
        json.loads(args.manifest.read_text(encoding="utf-8"))
    )
    programs = [
        program
        for program in manifest["programs"]
        if program["name"] == args.program
    ]
    if len(programs) != 1:
        raise ValueError(f"Expected one program named {args.program}")
    normalized = normalize(
        json.loads(args.dxil_input.read_text(encoding="utf-8")),
        json.loads(args.spirv_input.read_text(encoding="utf-8")),
        programs[0],
        manifest["matrixLayout"],
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(normalized, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
