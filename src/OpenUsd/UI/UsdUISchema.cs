// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.UI;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
internal static class UsdUISchema
{
    internal static bool TryValidate(UsdPrim prim, OpenUsdNativeUiSchemaKind schemaKind, out UsdStage? stage)
    {
        stage = null;
        if (string.IsNullOrWhiteSpace(prim.Path))
        {
            return false;
        }
        try
        {
            stage = prim.OwningStage;
            return stage.Native.IsUiSchema(prim.Path, schemaKind);
        }
        catch (OpenUsdNativeException exception) when (exception.Status == OpenUsdNativeStatus.NotFound)
        {
            stage = null;
            return false;
        }
    }

    internal static UsdStage Validate(UsdPrim prim, OpenUsdNativeUiSchemaKind schemaKind, string schemaName)
    {
        if (string.IsNullOrWhiteSpace(prim.Path))
        {
            throw new ArgumentException("The prim is not attached to a stage.", nameof(prim));
        }
        UsdStage stage = prim.OwningStage;
        if (!stage.Native.IsUiSchema(prim.Path, schemaKind))
        {
            throw new ArgumentException($"Prim '{prim.Path}' is not a {schemaName}.", nameof(prim));
        }
        return stage;
    }

    internal static UsdStage ValidateAttachedPrim(UsdPrim prim)
    {
        if (string.IsNullOrWhiteSpace(prim.Path))
        {
            throw new ArgumentException("The prim is not attached to a stage.", nameof(prim));
        }
        return prim.OwningStage;
    }
}


