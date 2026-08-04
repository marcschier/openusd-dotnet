# Copyright (c) marcschier. Licensed under the MIT License.

import argparse
import pathlib
import re
import sys


FUNCTION_PATTERN = re.compile(
    r"^OPENUSD_DOTNET_API[ \t]+([a-zA-Z0-9_]+)[ \t]+"
    r"(openusd_[a-z0-9_]+)\s*\((.*?)\);",
    re.DOTALL | re.MULTILINE,
)


def pascal_case(value: str) -> str:
    return "".join(part.capitalize() for part in value.split("_"))


def camel_case(value: str) -> str:
    converted = pascal_case(value)
    return converted[0].lower() + converted[1:]


def normalize(value: str) -> str:
    return " ".join(value.replace("\n", " ").split())


def map_return_type(value: str) -> str:
    mapping = {
        "void": "void",
        "uint32_t": "uint",
        "uint64_t": "ulong",
        "openusd_status": "OpenUsdNativeStatus",
    }
    return mapping[normalize(value)]


def map_parameter(declaration: str) -> tuple[str, str]:
    declaration = normalize(declaration)
    match = re.match(r"(.+?)([a-zA-Z_][a-zA-Z0-9_]*)$", declaration)
    if match is None:
        raise ValueError(f"Could not parse parameter: {declaration}")

    native_type = match.group(1).strip()
    native_name = match.group(2)
    managed_name = camel_case(native_name)

    nullable_names = {"type_name", "target_prim_path", "variant_selection", "string_value"}
    if native_type == "const char*":
        nullable = "?" if native_name in nullable_names else ""
        return f"string{nullable}", managed_name
    if native_type == "char*":
        return "byte*", managed_name
    if native_type in {"const openusd_stage*", "openusd_stage*", "openusd_stage* const",
                       "const openusd_stage_access*", "openusd_stage_access*",
                       "openusd_stage_access* const",
                       "const openusd_layer*", "openusd_layer*",
                       "openusd_string_list*", "openusd_payload_arc_list*",
                       "openusd_pcp_prim_index_list*", "openusd_ts_spline*",
                       "const openusd_ts_spline*",
                       "openusd_validation_metadata_list*",
                       "openusd_validation_error_list*"}:
        return "nint", managed_name
    if native_type in {"openusd_stage**", "openusd_stage_access**",
                       "openusd_layer**", "openusd_string_list**",
                       "openusd_payload_arc_list**",
                       "openusd_pcp_prim_index_list**",
                       "openusd_ts_spline**",
                       "openusd_validation_metadata_list**",
                       "openusd_validation_error_list**"}:
        return "out nint", managed_name
    if native_type == "openusd_error_buffer*":
        return "ref NativeErrorBuffer", managed_name
    if native_type == "const openusd_string_list_view*":
        return "ref NativeStringListView", managed_name
    if native_type == "openusd_string_list_view*":
        return "ref NativeStringListView", managed_name
    if native_type == "openusd_payload_arc_list_view*":
        return "ref NativePayloadArcListView", managed_name
    if native_type == "openusd_pcp_prim_index_view*":
        return "ref NativePcpPrimIndexView", managed_name
    if native_type == "const openusd_ts_spline_data_view*":
        return "ref NativeTsSplineDataView", managed_name
    if native_type == "openusd_ts_spline_data_view*":
        return "ref NativeTsSplineDataView", managed_name
    if native_type == "openusd_validation_metadata_view*":
        return "ref NativeValidationMetadataView", managed_name
    if native_type == "openusd_validation_error_view*":
        return "ref NativeValidationErrorView", managed_name
    if native_type == "const openusd_vec2f*":
        return ("OpenUsdNativeVec2f*" if native_name == "values"
                else "ref OpenUsdNativeVec2f"), managed_name
    if native_type == "openusd_vec2f*":
        return ("OpenUsdNativeVec2f*" if native_name == "values"
                else "out OpenUsdNativeVec2f"), managed_name
    if native_type == "const openusd_vec3f*":
        return ("OpenUsdNativeVec3f*" if native_name in {"values", "offsets", "normal_offsets"}
                else "ref OpenUsdNativeVec3f"), managed_name
    if native_type == "openusd_vec3f*":
        return ("OpenUsdNativeVec3f*" if native_name in {"values", "offsets", "normal_offsets"}
                else "out OpenUsdNativeVec3f"), managed_name
    if native_type == "openusd_vec3f":
        return "OpenUsdNativeVec3f", managed_name
    if native_type == "const openusd_matrix4d*":
        return ("OpenUsdNativeMatrix4d*" if native_name == "values"
                else "ref OpenUsdNativeMatrix4d"), managed_name
    if native_type == "openusd_matrix4d*":
        return ("OpenUsdNativeMatrix4d*" if native_name == "values"
                else "out OpenUsdNativeMatrix4d"), managed_name
    if native_type == "const openusd_quatf*":
        return "OpenUsdNativeQuatf*", managed_name
    if native_type == "openusd_quatf*":
        return ("out OpenUsdNativeQuatf" if native_name == "value"
                else "OpenUsdNativeQuatf*"), managed_name
    if native_type == "openusd_quatf":
        return "OpenUsdNativeQuatf", managed_name
    if native_type == "const openusd_extent3f*":
        return "ref OpenUsdNativeExtent3f", managed_name
    if native_type == "openusd_extent3f*":
        return "out OpenUsdNativeExtent3f", managed_name
    if native_type == "const openusd_metadata_value*":
        return "ref OpenUsdNativeMetadataValue", managed_name
    if native_type == "openusd_metadata_value*":
        return "ref OpenUsdNativeMetadataValue", managed_name
    if native_type == "openusd_scalar_value*":
        return "ref OpenUsdNativeScalarValue", managed_name
    if native_type == "openusd_bounds3d*":
        return "ref OpenUsdNativeBounds3d", managed_name
    if native_type == "openusd_oriented_bounds3d*":
        return "ref OpenUsdNativeOrientedBounds3d", managed_name
    if native_type == "openusd_prim_classification*":
        return "ref OpenUsdNativePrimClassification", managed_name
    if native_type == "openusd_geom_camera_state*":
        return "ref OpenUsdNativeCameraState", managed_name
    if native_type == "openusd_image_info*":
        return "ref OpenUsdNativeImageInfo", managed_name
    if native_type == "size_t":
        return "nuint", managed_name
    if native_type == "size_t*":
        return "out nuint", managed_name
    if native_type == "uint64_t*":
        return "out ulong", managed_name
    if native_type == "uint32_t":
        return "uint", managed_name
    if native_type == "int32_t":
        return "int", managed_name
    if native_type == "const int32_t*":
        return "int*", managed_name
    if native_type == "int32_t*":
        return ("int*" if native_name in {"values", "joint_indices"}
                else "out int"), managed_name
    if native_type == "int64_t":
        return "long", managed_name
    if native_type == "int64_t*":
        return "out long", managed_name
    if native_type == "double":
        return "double", managed_name
    if native_type == "float":
        return "float", managed_name
    if native_type == "const double*":
        return "double*", managed_name
    if native_type == "double*":
        return ("out double" if native_name == "value" else "double*"), managed_name
    if native_type == "const float*":
        return "float*", managed_name
    if native_type == "float*":
        return ("out float" if native_name in {"value", "weight"} else "float*"), managed_name
    if native_type == "const openusd_ts_knot_record*":
        return "OpenUsdNativeTsKnotRecord*", managed_name
    if native_type == "const uint8_t*":
        return "byte*", managed_name
    if native_type == "uint8_t*":
        return "byte*", managed_name
    enum_types = {
        "openusd_shade_value_type",
        "openusd_shade_attribute_type",
        "openusd_shade_material_terminal",
        "openusd_shade_binding_strength",
        "openusd_shade_material_purpose",
        "openusd_lux_schema_kind",
        "openusd_lux_float_property",
        "openusd_lux_bool_property",
        "openusd_lux_shape_property",
        "openusd_lux_asset_property",
        "openusd_lux_shaping_property",
        "openusd_skel_schema_kind",
        "openusd_skel_matrix_property",
        "openusd_skel_animation_vec3_property",
        "openusd_skel_binding_relationship",
        "openusd_skel_interpolation",
        "openusd_skel_skinning_method",
        "openusd_skel_blend_shape_vec3_property",
        "openusd_pcp_arc_type",
        "openusd_ts_interp_mode",
        "openusd_ts_curve_type",
        "openusd_ts_extrap_mode",
        "openusd_ts_tangent_algorithm",
        "openusd_validation_severity",
        "openusd_physics_schema_kind",
        "openusd_physics_api_kind",
        "openusd_physics_float_property",
        "openusd_physics_bool_property",
        "openusd_physics_vec3f_property",
        "openusd_physics_quatf_property",
        "openusd_physics_token_property",
        "openusd_physics_string_property",
        "openusd_media_asset_property",
        "openusd_media_time_property",
        "openusd_vol_asset_property",
        "openusd_ui_schema_kind",
        "openusd_ui_vec2f_property",
        "openusd_proc_schema_kind",
        "openusd_media_schema_kind",
        "openusd_render_schema_kind",
        "openusd_vol_schema_kind",
    }
    if native_type in enum_types:
        return "int", managed_name
    if native_type.removesuffix("*") in enum_types and native_type.endswith("*"):
        return "out int", managed_name

    raise ValueError(f"Unsupported parameter type: {native_type}")


def generate(header: str) -> str:
    methods: list[str] = []
    for return_type, native_name, parameter_text in FUNCTION_PATTERN.findall(header):
        parameters = []
        if normalize(parameter_text) != "void":
            parameters = [
                map_parameter(parameter)
                for parameter in parameter_text.split(",")
            ]

        method_name = pascal_case(native_name.removeprefix("openusd_"))
        managed_return = map_return_type(return_type)
        has_string = "const char*" in parameter_text
        attribute_lines = [
            "        [LibraryImport(",
            "            OpenUsdNativeContract.LibraryName,",
            f"            EntryPoint = \"{native_name}\""
            + ("," if has_string else ")]"),
        ]
        if has_string:
            attribute_lines.extend(
                [
                    "            StringMarshalling = StringMarshalling.Custom,",
                    "            StringMarshallingCustomType = typeof(NativeUtf8StringMarshaller))]",
                ]
            )
        attribute = "\n".join(attribute_lines)
        lines = [
            attribute,
            "        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]",
        ]
        if not parameters:
            lines.append(f"        internal static partial {managed_return} {method_name}();")
        else:
            lines.append(f"        internal static partial {managed_return} {method_name}(")
            for index, (parameter_type, parameter_name) in enumerate(parameters):
                suffix = "," if index < len(parameters) - 1 else ");"
                lines.append(f"            {parameter_type} {parameter_name}{suffix}")
        methods.append("\n".join(lines))

    body = "\n\n".join(methods)
    return f"""// Copyright (c) marcschier. Licensed under the MIT License.
// <auto-generated />
#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

public static unsafe partial class OpenUsdNativeRuntime
{{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeStringListView
    {{
        internal uint StructSize;
        internal byte* Data;
        internal nuint DataSize;
        internal nuint* Offsets;
        internal nuint OffsetsSize;
        internal nuint Count;
    }}

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePayloadArcListView
    {{
        internal uint StructSize;
        internal uint Version;
        internal byte* Data;
        internal nuint DataSize;
        internal nuint* Offsets;
        internal nuint OffsetsSize;
        internal nuint Count;
    }}

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePcpPrimIndexView
    {{
        internal uint StructSize;
        internal uint Version;
        internal OpenUsdNativePcpNodeRecord* Nodes;
        internal nuint NodesSize;
        internal nuint NodeCount;
        internal byte* Data;
        internal nuint DataSize;
        internal nuint* Offsets;
        internal nuint OffsetsSize;
        internal nuint StringCount;
        internal nuint ErrorOffset;
        internal nuint ErrorCount;
    }}

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeTsExtrapolationRecord
    {{
        internal int Mode;
        internal double Slope;
    }}

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeTsSplineDataView
    {{
        internal uint StructSize;
        internal uint Version;
        internal int CurveType;
        internal int IsTimeValued;
        internal NativeTsExtrapolationRecord PreExtrapolation;
        internal NativeTsExtrapolationRecord PostExtrapolation;
        internal OpenUsdNativeTsKnotRecord* Knots;
        internal nuint KnotsSize;
        internal nuint KnotCount;
    }}

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeValidationMetadataView
    {{
        internal uint StructSize;
        internal uint Version;
        internal OpenUsdNativeValidationMetadataRecord* Records;
        internal nuint RecordsSize;
        internal nuint Count;
        internal byte* Data;
        internal nuint DataSize;
        internal nuint* Offsets;
        internal nuint OffsetsSize;
        internal nuint StringCount;
    }}

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeValidationErrorView
    {{
        internal uint StructSize;
        internal uint Version;
        internal OpenUsdNativeValidationErrorRecord* Records;
        internal nuint RecordsSize;
        internal nuint Count;
        internal byte* Data;
        internal nuint DataSize;
        internal nuint* Offsets;
        internal nuint OffsetsSize;
        internal nuint StringCount;
    }}

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeErrorBuffer
    {{
        internal NativeErrorBuffer(byte* data, nuint capacity)
        {{
            Data = data;
            Capacity = capacity;
            Required = 0;
        }}

        internal readonly byte* Data;
        internal readonly nuint Capacity;
        internal readonly nuint Required;
    }}

    private static partial class NativeMethods
    {{
{body}
    }}
}}
"""


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()

    root = pathlib.Path(__file__).resolve().parents[1]
    header_path = root / "native" / "openusd_dotnet" / "include" / "openusd_dotnet.h"
    output_path = root / "src" / "OpenUsd.Interop" / "OpenUsdNativeMethods.g.cs"
    generated = generate(header_path.read_text(encoding="utf-8")).replace("\r\n", "\n")

    if args.verify:
        current = output_path.read_text(encoding="utf-8").replace("\r\n", "\n")
        if current != generated:
            print(f"Generated interop is out of date: {output_path}", file=sys.stderr)
            return 1
        return 0

    output_path.write_text(generated, encoding="utf-8", newline="\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
