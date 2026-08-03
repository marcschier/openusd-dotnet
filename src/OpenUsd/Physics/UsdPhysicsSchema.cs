// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Physics;

#pragma warning disable CS1591

[ExcludeFromCodeCoverage(Justification = "Exercised by native and NativeAOT integration probes.")]
internal static class UsdPhysicsSchema
{
    internal static bool TryValidate(
        UsdPrim prim,
        OpenUsdNativePhysicsSchemaKind schemaKind,
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
            return stage.Native.IsPhysicsSchema(prim.Path, schemaKind);
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
        OpenUsdNativePhysicsSchemaKind schemaKind,
        string schemaName)
    {
        if (string.IsNullOrWhiteSpace(prim.Path))
        {
            throw new ArgumentException("The prim is not attached to a stage.", nameof(prim));
        }
        UsdStage stage = prim.OwningStage;
        if (!stage.Native.IsPhysicsSchema(prim.Path, schemaKind))
        {
            throw new ArgumentException($"Prim '{prim.Path}' is not a {schemaName}.", nameof(prim));
        }
        return stage;
    }

    internal static bool TryValidateApi(
        UsdPrim prim,
        OpenUsdNativePhysicsApiKind apiKind,
        string? instanceName,
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
            return stage.Native.HasPhysicsApi(prim.Path, apiKind, instanceName);
        }
        catch (OpenUsdNativeException exception)
            when (exception.Status == OpenUsdNativeStatus.NotFound)
        {
            stage = null;
            return false;
        }
    }

    internal static UsdStage ValidateApi(
        UsdPrim prim,
        OpenUsdNativePhysicsApiKind apiKind,
        string? instanceName,
        string schemaName)
    {
        if (string.IsNullOrWhiteSpace(prim.Path))
        {
            throw new ArgumentException("The prim is not attached to a stage.", nameof(prim));
        }
        UsdStage stage = prim.OwningStage;
        if (!stage.Native.HasPhysicsApi(prim.Path, apiKind, instanceName))
        {
            throw new ArgumentException(
                $"Prim '{prim.Path}' does not have {schemaName} applied.",
                nameof(prim));
        }
        return stage;
    }

    internal static void ValidateFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value must be finite.");
        }
    }

    internal static void ValidateVec3f(UsdVec3f value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Every vector component must be finite.");
        }
    }

    internal static void ValidateInstanceName(string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        if (instanceName.Contains(':', StringComparison.Ordinal) ||
            instanceName.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException("Multiple-apply instance names must be simple tokens.", nameof(instanceName));
        }
    }
}
