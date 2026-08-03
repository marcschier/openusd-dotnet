// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Geom;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
internal static class UsdGeomSchema
{
    internal static bool TryValidate(
        UsdPrim prim,
        UsdGeomSchemaKind schemaKind,
        out UsdStage? stage)
    {
        stage = null;
        if (string.IsNullOrWhiteSpace(prim.Path))
        {
            return false;
        }
        try
        {
            stage = prim.OwningStage;
            return stage.Native.IsGeomSchema(prim.Path, (int)schemaKind);
        }
        catch (OpenUsdNativeException exception)
            when (exception.Status == OpenUsdNativeStatus.NotFound)
        {
            stage = null;
            return false;
        }
    }

    internal static UsdStage Validate(UsdPrim prim, UsdGeomSchemaKind schemaKind, string schemaName)
    {
        if (string.IsNullOrWhiteSpace(prim.Path))
        {
            throw new ArgumentException("The prim is not attached to a stage.", nameof(prim));
        }
        UsdStage stage = prim.OwningStage;
        if (!stage.Native.IsGeomSchema(prim.Path, (int)schemaKind))
        {
            throw new ArgumentException(
                $"Prim '{prim.Path}' is not compatible with {schemaName}.",
                nameof(prim));
        }
        return stage;
    }

    internal static OpenUsdNativeVec3f[] ToNative(ReadOnlySpan<UsdVec3f> values)
    {
        var result = new OpenUsdNativeVec3f[values.Length];
        for (int index = 0; index < values.Length; ++index)
        {
            result[index] = values[index].ToNative();
        }
        return result;
    }

    internal static UsdVec3f[] FromNative(OpenUsdNativeVec3f[] values)
    {
        var result = new UsdVec3f[values.Length];
        for (int index = 0; index < values.Length; ++index)
        {
            result[index] = UsdVec3f.FromNative(values[index]);
        }
        return result;
    }

    internal static OpenUsdNativeQuatf[] ToNative(ReadOnlySpan<UsdQuatf> values)
    {
        var result = new OpenUsdNativeQuatf[values.Length];
        for (int index = 0; index < values.Length; ++index)
        {
            result[index] = values[index].ToNative();
        }

        return result;
    }

    internal static UsdQuatf[] FromNative(OpenUsdNativeQuatf[] values)
    {
        var result = new UsdQuatf[values.Length];
        for (int index = 0; index < values.Length; ++index)
        {
            result[index] = UsdQuatf.FromNative(values[index]);
        }

        return result;
    }

    internal static string ToToken(UsdGeomAxis axis) => axis switch
    {
        UsdGeomAxis.X => "X",
        UsdGeomAxis.Y => "Y",
        UsdGeomAxis.Z => "Z",
        _ => throw new ArgumentOutOfRangeException(nameof(axis))
    };

    internal static UsdGeomAxis ToAxis(string token) => token switch
    {
        "X" => UsdGeomAxis.X,
        "Y" => UsdGeomAxis.Y,
        "Z" => UsdGeomAxis.Z,
        _ => throw new InvalidOperationException("OpenUSD returned an unsupported axis token.")
    };

    internal static void SetExtent(UsdPrim prim, UsdExtent3f extent) =>
        prim.SetVec3fArray("extent", [extent.Minimum, extent.Maximum]);

    internal static void SetExtent(UsdPrim prim, UsdExtent3f extent, double timeCode) =>
        prim.SetVec3fArray("extent", [extent.Minimum, extent.Maximum], timeCode);

    internal static UsdExtent3f GetExtent(UsdPrim prim)
    {
        UsdVec3f[] values = prim.GetVec3fArray("extent");
        return ToExtent(values);
    }

    internal static UsdExtent3f GetExtent(UsdPrim prim, double timeCode)
    {
        UsdVec3f[] values = prim.GetVec3fArray("extent", timeCode);
        return ToExtent(values);
    }

    private static UsdExtent3f ToExtent(UsdVec3f[] values)
    {
        if (values.Length != 2)
        {
            throw new InvalidOperationException("A UsdGeom extent must contain exactly two corners.");
        }
        return new UsdExtent3f(values[0], values[1]);
    }
}
