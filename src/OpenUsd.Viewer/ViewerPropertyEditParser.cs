// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using OpenUsd.Editing;

namespace OpenUsd.Viewer;

internal static class ViewerPropertyEditParser
{
    internal static bool Supports(string typeName) =>
        typeName == "asset" || ComponentCount(typeName) != 0 || Kind(typeName) != ViewerPhysicsValueKind.Unsupported;

    internal static bool TryParse(
        string typeName, string text, [NotNullWhen(true)] out UsdLayerEditValue? value, out string error)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(text);
        value = null;
        if (text.Length > 4096 || text.Contains('\0', StringComparison.Ordinal))
        {
            error = "The value exceeds its text limit or contains NUL.";
            return false;
        }
        int componentCount = ComponentCount(typeName);
        if (componentCount != 0)
        {
            Span<double> components = stackalloc double[componentCount];
            if (!ViewerNumericComponents.TryParse(text, components, out error))
            {
                return false;
            }
            if (typeName == "matrix4d")
            {
                value = UsdLayerEditValue.FromMatrix4d(new UsdMatrix4d(
                    components[0], components[1], components[2], components[3],
                    components[4], components[5], components[6], components[7],
                    components[8], components[9], components[10], components[11],
                    components[12], components[13], components[14], components[15]));
                return true;
            }
            foreach (double component in components)
            {
                if (!float.IsFinite((float)component))
                {
                    error = "Enter finite components within the float range.";
                    return false;
                }
            }
            value = componentCount == 2
                ? UsdLayerEditValue.FromVec2f(new UsdVec2f((float)components[0], (float)components[1]))
                : typeName == "quatf"
                    ? UsdLayerEditValue.FromQuatf(new UsdQuatf(
                        (float)components[0], (float)components[1], (float)components[2], (float)components[3]))
                    : UsdLayerEditValue.FromVec4f(new UsdVec4f(
                        (float)components[0], (float)components[1], (float)components[2], (float)components[3]));
            return true;
        }
        ViewerPhysicsValueKind kind = Kind(typeName);
        if (typeName == "asset" || kind is ViewerPhysicsValueKind.Text or ViewerPhysicsValueKind.Token)
        {
            try
            {
                value = typeName == "asset" ? UsdLayerEditValue.FromAssetPath(text) :
                    kind == ViewerPhysicsValueKind.Text
                        ? UsdLayerEditValue.FromString(text) : UsdLayerEditValue.FromToken(text);
                error = string.Empty;
                return true;
            }
            catch (ArgumentException exception)
            {
                error = exception.Message;
                return false;
            }
        }
        if (!ViewerPhysicsValueParser.TryParse(kind, [], text, out ViewerPhysicsValue parsed, out error))
        {
            return false;
        }
        if (typeName == "float" && !float.IsFinite((float)parsed.NumberValue))
        {
            error = "Enter a finite value within the float range.";
            return false;
        }
        if (typeName == "int" && parsed.IntegerValue is < int.MinValue or > int.MaxValue)
        {
            error = "Enter a value within the signed 32-bit integer range.";
            return false;
        }
        if (kind == ViewerPhysicsValueKind.Vector3 && IsFloatVector(typeName) &&
            (!float.IsFinite((float)parsed.VectorValue.X) || !float.IsFinite((float)parsed.VectorValue.Y) ||
            !float.IsFinite((float)parsed.VectorValue.Z)))
        {
            error = "Enter three finite values within the float range.";
            return false;
        }
        value = kind switch
        {
            ViewerPhysicsValueKind.Bool => UsdLayerEditValue.FromBoolean(parsed.BoolValue),
            ViewerPhysicsValueKind.Integer => typeName == "int"
                ? UsdLayerEditValue.FromInt32((int)parsed.IntegerValue)
                : UsdLayerEditValue.FromInt64(parsed.IntegerValue),
            ViewerPhysicsValueKind.Number => typeName == "float"
                ? UsdLayerEditValue.FromFloat((float)parsed.NumberValue)
                : UsdLayerEditValue.FromDouble(parsed.NumberValue),
            ViewerPhysicsValueKind.Vector3 => IsFloatVector(typeName)
                ? UsdLayerEditValue.FromVec3f(new UsdVec3f(
                    (float)parsed.VectorValue.X, (float)parsed.VectorValue.Y, (float)parsed.VectorValue.Z))
                : UsdLayerEditValue.FromVec3d(new UsdVec3d(
                    parsed.VectorValue.X, parsed.VectorValue.Y, parsed.VectorValue.Z)),
            _ => null
        };
        if (value is null)
        {
            error = "This declared type remains read-only in the focused editor.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    internal static string Format(UsdLayerEditValue value) => value.Kind switch
    {
        UsdLayerEditValueKind.Boolean => value.AsBoolean() ? "true" : "false",
        UsdLayerEditValueKind.Int32 => value.AsInt32().ToString(CultureInfo.InvariantCulture),
        UsdLayerEditValueKind.Int64 => value.AsInt64().ToString(CultureInfo.InvariantCulture),
        UsdLayerEditValueKind.Float => value.AsFloat().ToString("R", CultureInfo.InvariantCulture),
        UsdLayerEditValueKind.Double => value.AsDouble().ToString("R", CultureInfo.InvariantCulture),
        UsdLayerEditValueKind.Token => value.AsToken(),
        UsdLayerEditValueKind.String => value.AsString(),
        UsdLayerEditValueKind.AssetPath => value.AsAssetPath().AuthoredPath,
        UsdLayerEditValueKind.Vec2f => FormatVector(value.AsVec2f()),
        UsdLayerEditValueKind.Vec3f => FormatVector(
            value.AsVec3f().X, value.AsVec3f().Y, value.AsVec3f().Z),
        UsdLayerEditValueKind.Vec3d => FormatVector(
            value.AsVec3d().X, value.AsVec3d().Y, value.AsVec3d().Z),
        UsdLayerEditValueKind.Vec4f => FormatVector(
            value.AsVec4f().X, value.AsVec4f().Y, value.AsVec4f().Z, value.AsVec4f().W),
        UsdLayerEditValueKind.Quatf => FormatVector(
            value.AsQuatf().Real, value.AsQuatf().X, value.AsQuatf().Y, value.AsQuatf().Z),
        UsdLayerEditValueKind.Matrix4d => FormatMatrix(value.AsMatrix4d()),
        _ => string.Empty
    };

    private static string FormatVector(double x, double y, double z) =>
        string.Create(CultureInfo.InvariantCulture, $"({x:R}, {y:R}, {z:R})");

    private static string FormatVector(UsdVec2f value) =>
        FormattableString.Invariant($"({value.X:R}, {value.Y:R})");

    private static string FormatVector(float x, float y, float z, float w) =>
        FormattableString.Invariant($"({x:R}, {y:R}, {z:R}, {w:R})");

    private static int ComponentCount(string typeName) => typeName switch
    {
        "float2" or "texCoord2f" => 2,
        "float4" or "color4f" or "quatf" => 4,
        "matrix4d" => 16,
        _ => 0
    };

    private static string FormatMatrix(UsdMatrix4d value)
    {
        var text = new StringBuilder("(");
        for (int row = 0; row < 4; row++)
        {
            if (row != 0)
            {
                text.Append(", ");
            }
            text.Append('(');
            for (int column = 0; column < 4; column++)
            {
                if (column != 0)
                {
                    text.Append(", ");
                }
                text.Append(value[row, column].ToString("R", CultureInfo.InvariantCulture));
            }
            text.Append(')');
        }
        return text.Append(')').ToString();
    }

    private static ViewerPhysicsValueKind Kind(string typeName) => typeName switch
    {
        "bool" => ViewerPhysicsValueKind.Bool,
        "int" or "int64" => ViewerPhysicsValueKind.Integer,
        "float" or "double" => ViewerPhysicsValueKind.Number,
        "string" => ViewerPhysicsValueKind.Text,
        "token" => ViewerPhysicsValueKind.Token,
        "float3" or "double3" or "point3f" or "point3d" or "vector3f" or "vector3d" or
            "normal3f" or "normal3d" or "color3f" or "color3d" => ViewerPhysicsValueKind.Vector3,
        _ => ViewerPhysicsValueKind.Unsupported
    };

    private static bool IsFloatVector(string typeName) => typeName is
        "float3" or "point3f" or "vector3f" or "normal3f" or "color3f";
}
