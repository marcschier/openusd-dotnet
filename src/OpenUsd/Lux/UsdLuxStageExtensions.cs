// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Lux;

/// <summary>Focused UsdLux schema-definition conveniences for <see cref="UsdStage"/>.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public static class UsdLuxStageExtensions
{
    /// <summary>Defines a UsdLuxDistantLight.</summary>
    public static UsdLuxDistantLight DefineDistantLight(this UsdStage stage, string path)
    {
        Define(stage, path, OpenUsdNativeLuxSchemaKind.DistantLight);
        return new UsdLuxDistantLight(stage, path);
    }

    /// <summary>Defines a UsdLuxSphereLight.</summary>
    public static UsdLuxSphereLight DefineSphereLight(this UsdStage stage, string path)
    {
        Define(stage, path, OpenUsdNativeLuxSchemaKind.SphereLight);
        return new UsdLuxSphereLight(stage, path);
    }

    /// <summary>Defines a UsdLuxRectLight.</summary>
    public static UsdLuxRectLight DefineRectLight(this UsdStage stage, string path)
    {
        Define(stage, path, OpenUsdNativeLuxSchemaKind.RectLight);
        return new UsdLuxRectLight(stage, path);
    }

    /// <summary>Defines a UsdLuxDiskLight.</summary>
    public static UsdLuxDiskLight DefineDiskLight(this UsdStage stage, string path)
    {
        Define(stage, path, OpenUsdNativeLuxSchemaKind.DiskLight);
        return new UsdLuxDiskLight(stage, path);
    }

    /// <summary>Defines a UsdLuxDomeLight.</summary>
    public static UsdLuxDomeLight DefineDomeLight(this UsdStage stage, string path)
    {
        Define(stage, path, OpenUsdNativeLuxSchemaKind.DomeLight);
        return new UsdLuxDomeLight(stage, path);
    }

    /// <summary>Defines a UsdLuxCylinderLight.</summary>
    public static UsdLuxCylinderLight DefineCylinderLight(this UsdStage stage, string path)
    {
        Define(stage, path, OpenUsdNativeLuxSchemaKind.CylinderLight);
        return new UsdLuxCylinderLight(stage, path);
    }

    private static void Define(
        UsdStage stage,
        string path,
        OpenUsdNativeLuxSchemaKind schemaKind)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(path);
        stage.Native.DefineLux(path, schemaKind);
    }
}
