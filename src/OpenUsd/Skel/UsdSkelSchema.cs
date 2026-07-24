// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Skel;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
internal static class UsdSkelSchema
{
    internal static bool TryValidate(
        UsdPrim prim,
        OpenUsdNativeSkelSchemaKind schemaKind,
        out UsdStage? stage)
    {
        stage = null;
        if (!UsdPath.IsAbsolutePrimPath(prim.Path))
        {
            return false;
        }
        try
        {
            stage = prim.OwningStage;
            return stage.Native.IsSkelSchema(prim.Path, schemaKind);
        }
        catch (OpenUsdNativeException exception)
            when (exception.Status == OpenUsdNativeStatus.NotFound)
        {
            stage = null;
            return false;
        }
    }

    internal static UsdStage Validate(
        UsdPrim prim,
        OpenUsdNativeSkelSchemaKind schemaKind,
        string schemaName)
    {
        UsdStage stage = ValidateAttachedPrim(prim);
        if (!stage.Native.IsSkelSchema(prim.Path, schemaKind))
        {
            throw new ArgumentException(
                $"Prim '{prim.Path}' is not a {schemaName}.",
                nameof(prim));
        }
        return stage;
    }

    internal static bool TryGetStage(UsdPrim prim, out UsdStage? stage)
    {
        stage = null;
        if (!UsdPath.IsAbsolutePrimPath(prim.Path))
        {
            return false;
        }
        stage = prim.OwningStage;
        return true;
    }

    internal static UsdStage ValidateAttachedPrim(UsdPrim prim)
    {
        try
        {
            UsdPath.ValidateAbsolutePrimPath(prim.Path, nameof(prim));
            return prim.OwningStage;
        }
        catch (InvalidOperationException exception)
        {
            throw new ArgumentException(
                "The prim is not attached to a stage.",
                nameof(prim),
                exception);
        }
    }

    internal static OpenUsdNativeMatrix4d[] ToNative(ReadOnlySpan<UsdMatrix4d> values)
    {
        var result = new OpenUsdNativeMatrix4d[values.Length];
        for (int index = 0; index < values.Length; ++index)
        {
            result[index] = values[index].ToNative();
        }
        return result;
    }

    internal static UsdMatrix4d[] FromNative(OpenUsdNativeMatrix4d[] values)
    {
        var result = new UsdMatrix4d[values.Length];
        for (int index = 0; index < values.Length; ++index)
        {
            result[index] = UsdMatrix4d.FromNative(values[index]);
        }
        return result;
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

    internal static void ValidateSameStage(UsdStage stage, UsdStage target)
    {
        if (!ReferenceEquals(stage, target))
        {
            throw new ArgumentException("Skeleton bindings must remain within one stage.");
        }
    }
}
