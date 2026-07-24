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
}
