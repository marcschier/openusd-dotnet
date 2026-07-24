// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Lux;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
internal static class UsdLuxSchema
{
    internal static void ValidateFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value must be finite.");
        }
    }

    internal static void ValidateNonNegative(float value, string parameterName)
    {
        ValidateFinite(value, parameterName);
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The value must be non-negative.");
        }
    }

    internal static void ValidateRange(
        float value,
        float minimum,
        float maximum,
        string parameterName,
        bool maximumInclusive = true)
    {
        ValidateFinite(value, parameterName);
        if (value < minimum || (maximumInclusive ? value > maximum : value >= maximum))
        {
            string upper = maximumInclusive ? "at most" : "less than";
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"The value must be at least {minimum} and {upper} {maximum}.");
        }
    }

    internal static void ValidateColor(UsdVec3f value, string parameterName)
    {
        if (!float.IsFinite(value.X) ||
            !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Every color component must be finite.");
        }
    }

    internal static bool TryValidate(
        UsdPrim prim,
        OpenUsdNativeLuxSchemaKind schemaKind,
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
            return stage.Native.IsLuxSchema(prim.Path, schemaKind);
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
        OpenUsdNativeLuxSchemaKind schemaKind,
        string schemaName)
    {
        if (string.IsNullOrWhiteSpace(prim.Path))
        {
            throw new ArgumentException("The prim is not attached to a stage.", nameof(prim));
        }
        UsdStage stage = prim.OwningStage;
        if (!stage.Native.IsLuxSchema(prim.Path, schemaKind))
        {
            throw new ArgumentException(
                $"Prim '{prim.Path}' is not a {schemaName}.",
                nameof(prim));
        }
        return stage;
    }
}
