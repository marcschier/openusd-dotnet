// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Shade;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
internal static class UsdShadeSchema
{
    internal static bool TryValidate(
        UsdPrim prim,
        Func<OpenUsdNativeStage, string, bool> validator,
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
            return validator(stage.Native, prim.Path);
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
        Func<OpenUsdNativeStage, string, bool> validator,
        string schemaName)
    {
        if (string.IsNullOrWhiteSpace(prim.Path))
        {
            throw new ArgumentException("The prim is not attached to a stage.", nameof(prim));
        }
        UsdStage stage = prim.OwningStage;
        if (!validator(stage.Native, prim.Path))
        {
            throw new ArgumentException(
                $"Prim '{prim.Path}' is not compatible with {schemaName}.",
                nameof(prim));
        }
        return stage;
    }

    internal static OpenUsdNativeShadeValueType ToNative(UsdShadeValueType value) =>
        (OpenUsdNativeShadeValueType)value;

    internal static OpenUsdNativeShadeAttributeType ToNative(UsdShadeAttributeType value) =>
        (OpenUsdNativeShadeAttributeType)value;

    internal static UsdShadeConnection FromNative(OpenUsdNativeShadeConnection value) =>
        new(
            value.SourcePrimPath,
            value.SourceName,
            (UsdShadeAttributeType)value.SourceType);

    internal static void ValidateSameStage(UsdStage stage, UsdStage other)
    {
        if (!ReferenceEquals(stage, other))
        {
            throw new ArgumentException(
                "Shading connections and bindings must belong to the same stage.");
        }
    }
}
