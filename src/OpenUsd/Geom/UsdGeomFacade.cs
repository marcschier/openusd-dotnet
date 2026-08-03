// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS1591

namespace OpenUsd.Geom;

/// <summary>Shared helpers for concrete UsdGeom schema facades.</summary>
[ExcludeFromCodeCoverage(Justification = "Exercised by clean native and NativeAOT integration probes.")]
internal static class UsdGeomFacade
{
    internal static void Define(UsdStage stage, string path, UsdGeomSchemaKind kind)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(path);
        stage.Native.DefineGeomSchema(path, (int)kind);
    }

    internal static bool TryWrap(UsdPrim prim, UsdGeomSchemaKind kind, out UsdStage? stage) =>
        UsdGeomSchema.TryValidate(prim, kind, out stage);

    internal static UsdStage Validate(UsdPrim prim, UsdGeomSchemaKind kind, string schemaName) =>
        UsdGeomSchema.Validate(prim, kind, schemaName);

    internal static UsdStage Require(UsdStage? stage) =>
        stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}

