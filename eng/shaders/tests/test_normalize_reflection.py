# Copyright (c) marcschier. Licensed under the MIT License.

import copy
import importlib.util
import pathlib
import unittest


SCRIPT_PATH = (
    pathlib.Path(__file__).resolve().parents[1]
    / "scripts"
    / "normalize-reflection.py"
)
SPEC = importlib.util.spec_from_file_location("normalize_reflection", SCRIPT_PATH)
assert SPEC is not None
assert SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def scalar(name: str) -> dict:
    return {"kind": "scalar", "scalarType": name}


def vector(name: str, count: int) -> dict:
    return {
        "kind": "vector",
        "elementCount": count,
        "elementType": scalar(name),
    }


def matrix() -> dict:
    return {
        "kind": "matrix",
        "rowCount": 4,
        "columnCount": 4,
        "elementType": scalar("float32"),
    }


def field(name: str, type_info: dict, offset: int, size: int, stride: int) -> dict:
    return {
        "name": name,
        "type": type_info,
        "binding": {
            "kind": "uniform",
            "offset": offset,
            "size": size,
            "elementStride": stride,
        },
    }


def constant_buffer() -> dict:
    nested = {
        "kind": "struct",
        "name": "Nested",
        "fields": [
            field(
                "weights",
                {
                    "kind": "array",
                    "elementCount": 4,
                    "elementType": scalar("float32"),
                },
                0,
                16,
                4,
            ),
            field("direction", vector("float32", 4), 16, 16, 4),
        ],
    }
    element = {
        "kind": "struct",
        "name": "Parameters",
        "fields": [
            field("transform", matrix(), 0, 64, 0),
            field("nested", nested, 64, 32, 0),
        ],
    }
    return {
        "kind": "constantBuffer",
        "elementType": element,
        "elementVarLayout": {
            "binding": {
                "kind": "uniform",
                "offset": 0,
                "size": 96,
                "elementStride": 0,
            }
        },
    }


def resource_parameters(target: str) -> list[dict]:
    if target == "dxil":
        bindings = {
            "values": {"kind": "unorderedAccess", "space": 2, "index": 1},
            "textures": {"kind": "shaderResource", "space": 3, "index": 2},
            "sampler": {"kind": "samplerState", "space": 3, "index": 7},
            "Parameters": {"kind": "constantBuffer", "space": 1, "index": 0},
        }
    else:
        bindings = {
            "values": {"kind": "descriptorTableSlot", "space": 4, "index": 3},
            "textures": {"kind": "descriptorTableSlot", "space": 5, "index": 4},
            "sampler": {"kind": "descriptorTableSlot", "space": 5, "index": 6},
            "Parameters": {"kind": "descriptorTableSlot", "space": 2, "index": 0},
        }
    return [
        {
            "name": "values",
            "binding": bindings["values"],
            "type": {
                "kind": "resource",
                "baseShape": "structuredBuffer",
                "access": "readWrite",
                "resultType": vector("float32", 4),
            },
        },
        {
            "name": "textures",
            "binding": bindings["textures"],
            "type": {
                "kind": "array",
                "elementCount": 4,
                "elementType": {
                    "kind": "resource",
                    "baseShape": "texture2D",
                    "access": "read",
                    "resultType": vector("float32", 4),
                },
            },
        },
        {
            "name": "sampler",
            "binding": bindings["sampler"],
            "type": {"kind": "samplerState"},
        },
        {
            "name": "Parameters",
            "binding": bindings["Parameters"],
            "type": constant_buffer(),
        },
    ]


def entry_point() -> dict:
    return {
        "name": "main",
        "stage": "vertex",
        "parameters": [
            {
                "name": "input",
                "type": {
                    "kind": "struct",
                    "fields": [
                        {
                            "name": "position",
                            "semanticName": "POSITION",
                            "binding": {"kind": "varyingInput", "index": 0},
                            "type": vector("float32", 3),
                        },
                        {
                            "name": "uv",
                            "semanticName": "TEXCOORD",
                            "semanticIndex": 1,
                            "binding": {"kind": "varyingInput", "index": 2},
                            "type": vector("float32", 2),
                        },
                    ],
                },
            }
        ],
        "result": {
            "type": {
                "kind": "struct",
                "fields": [
                    {
                        "name": "position",
                        "semanticName": "SV_POSITION",
                        "type": vector("float32", 4),
                    },
                    {
                        "name": "normal",
                        "semanticName": "NORMAL",
                        "binding": {"kind": "varyingOutput", "index": 1},
                        "type": vector("float32", 3),
                    },
                ],
            }
        },
    }


def raw(target: str) -> dict:
    return {
        "parameters": resource_parameters(target),
        "entryPoints": [entry_point()],
    }


def program() -> dict:
    return {
        "name": "rich",
        "source": "eng/shaders/sources/rich.slang",
        "entryPoint": "main",
        "stage": "vertex",
        "profile": "vs_6_0",
        "resources": [
            {
                "name": "values",
                "access": "readWrite",
                "d3d": {"registerClass": "u", "register": 1, "space": 2},
                "vulkan": {"binding": 3, "set": 4},
            },
            {
                "name": "textures",
                "access": "read",
                "d3d": {"registerClass": "t", "register": 2, "space": 3},
                "vulkan": {"binding": 4, "set": 5},
            },
            {
                "name": "sampler",
                "access": "read",
                "d3d": {"registerClass": "s", "register": 7, "space": 3},
                "vulkan": {"binding": 6, "set": 5},
            },
            {
                "name": "Parameters",
                "access": "constant",
                "d3d": {"registerClass": "b", "register": 0, "space": 1},
                "vulkan": {"binding": 0, "set": 2},
            },
        ],
    }


class NormalizeReflectionTests(unittest.TestCase):
    def test_preserves_resources_layout_and_stage_io(self) -> None:
        result = MODULE.normalize(
            raw("dxil"),
            raw("spirv"),
            program(),
            "row-major",
        )

        self.assertEqual(2, result["schemaVersion"])
        values = result["resources"][0]
        self.assertEqual("readWrite", values["shape"]["access"])
        self.assertEqual(16, values["shape"]["elementStride"])
        self.assertEqual(4, result["resources"][1]["shape"]["arrayCount"])
        self.assertEqual("texture2D", result["resources"][1]["shape"]["kind"])
        self.assertEqual("read", result["resources"][1]["shape"]["access"])
        self.assertEqual("sampler", result["resources"][2]["shape"]["kind"])
        parameters = result["resources"][3]
        self.assertEqual(1, parameters["bindings"]["d3d"]["space"])
        self.assertEqual(2, parameters["bindings"]["vulkan"]["set"])
        transform = parameters["shape"]["elementType"]["fields"][0]
        self.assertEqual(16, transform["layout"]["matrixStride"])
        nested = parameters["shape"]["elementType"]["fields"][1]
        self.assertEqual("struct", nested["type"]["kind"])
        self.assertEqual("array", nested["type"]["fields"][0]["type"]["kind"])

        self.assertEqual(1, result["stageInputs"][1]["semantic"]["index"])
        self.assertEqual(2, result["stageInputs"][1]["location"])
        system_output = result["stageOutputs"][0]
        self.assertEqual("SV_POSITION", system_output["semantic"]["name"])
        self.assertIsNone(system_output["location"])
        self.assertEqual(1, result["stageOutputs"][1]["location"])

    def test_rejects_absent_resource_binding(self) -> None:
        dxil = raw("dxil")
        del dxil["parameters"][0]["binding"]
        with self.assertRaisesRegex(ValueError, "Missing binding"):
            MODULE.normalize(
                dxil,
                raw("spirv"),
                program(),
                "row-major",
            )

    def test_rejects_absent_user_varying_location(self) -> None:
        dxil = raw("dxil")
        del dxil["entryPoints"][0]["parameters"][0]["type"]["fields"][1]["binding"]
        with self.assertRaisesRegex(ValueError, "Missing binding for user varying"):
            MODULE.normalize(
                dxil,
                raw("spirv"),
                program(),
                "row-major",
            )

    def test_rejects_omitted_resource_access(self) -> None:
        dxil = raw("dxil")
        del dxil["parameters"][0]["type"]["access"]
        with self.assertRaisesRegex(ValueError, "Missing access"):
            MODULE.normalize(
                dxil,
                raw("spirv"),
                program(),
                "row-major",
            )

    def test_infers_read_access_for_slang_read_only_textures(self) -> None:
        dxil = raw("dxil")
        spirv = raw("spirv")
        del dxil["parameters"][1]["type"]["elementType"]["access"]
        del spirv["parameters"][1]["type"]["elementType"]["access"]

        result = MODULE.normalize(dxil, spirv, program(), "row-major")

        self.assertEqual("read", result["resources"][1]["shape"]["access"])
        self.assertEqual(
            "read",
            result["resources"][1]["vulkanLayout"]["access"],
        )

    def test_rejects_invalid_resource_access(self) -> None:
        dxil = raw("dxil")
        dxil["parameters"][0]["type"]["access"] = "sideways"
        with self.assertRaisesRegex(ValueError, "does not match contract"):
            MODULE.normalize(
                dxil,
                raw("spirv"),
                program(),
                "row-major",
            )

    def test_requires_three_positive_compute_group_dimensions(self) -> None:
        dxil = raw("dxil")
        spirv = raw("spirv")
        compute_program = program()
        compute_program["stage"] = "compute"
        compute_program["profile"] = "cs_6_0"
        dxil["entryPoints"][0]["stage"] = "compute"
        spirv["entryPoints"][0]["stage"] = "compute"
        with self.assertRaisesRegex(ValueError, "three positive"):
            MODULE.normalize(
                dxil,
                spirv,
                compute_program,
                "row-major",
            )
        dxil["entryPoints"][0]["threadGroupSize"] = [8, 0, 1]
        with self.assertRaisesRegex(ValueError, "three positive"):
            MODULE.normalize(
                dxil,
                spirv,
                compute_program,
                "row-major",
            )
        dxil["entryPoints"][0]["threadGroupSize"] = [True, 1, 1]
        with self.assertRaisesRegex(ValueError, "three positive"):
            MODULE.normalize(
                dxil,
                spirv,
                compute_program,
                "row-major",
            )

    def test_rectangular_matrix_stride_honors_matrix_layout(self) -> None:
        rectangular = {
            "kind": "struct",
            "fields": [
                field(
                    "transform",
                    {
                        "kind": "matrix",
                        "rowCount": 2,
                        "columnCount": 3,
                        "elementType": scalar("float32"),
                    },
                    0,
                    24,
                    0,
                )
            ],
        }
        row = MODULE.normalize_type(rectangular, "row-major")
        column = MODULE.normalize_type(rectangular, "column-major")

        self.assertEqual(12, row["fields"][0]["layout"]["matrixStride"])
        self.assertEqual("row-major", row["fields"][0]["layout"]["matrixLayout"])
        self.assertEqual(8, column["fields"][0]["layout"]["matrixStride"])
        self.assertEqual(
            "column-major",
            column["fields"][0]["layout"]["matrixLayout"],
        )


if __name__ == "__main__":
    unittest.main()
