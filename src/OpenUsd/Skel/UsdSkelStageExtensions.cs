// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Skel;

/// <summary>Focused UsdSkel schema-definition conveniences for <see cref="UsdStage"/>.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public static class UsdSkelStageExtensions
{
    /// <summary>Defines a UsdSkelRoot.</summary>
    public static UsdSkelRoot DefineSkelRoot(this UsdStage stage, string path)
    {
        Define(stage, path, OpenUsdNativeSkelSchemaKind.Root);
        return new UsdSkelRoot(stage, path);
    }

    /// <summary>Defines a UsdSkelSkeleton.</summary>
    public static UsdSkelSkeleton DefineSkeleton(this UsdStage stage, string path)
    {
        Define(stage, path, OpenUsdNativeSkelSchemaKind.Skeleton);
        return new UsdSkelSkeleton(stage, path);
    }

    /// <summary>Defines a UsdSkelAnimation.</summary>
    public static UsdSkelAnimation DefineAnimation(this UsdStage stage, string path)
    {
        Define(stage, path, OpenUsdNativeSkelSchemaKind.Animation);
        return new UsdSkelAnimation(stage, path);
    }

    /// <summary>Defines a UsdSkelBlendShape.</summary>
    public static UsdSkelBlendShape DefineBlendShape(this UsdStage stage, string path)
    {
        Define(stage, path, OpenUsdNativeSkelSchemaKind.BlendShape);
        return new UsdSkelBlendShape(stage, path);
    }

    private static void Define(
        UsdStage stage,
        string path,
        OpenUsdNativeSkelSchemaKind schemaKind)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(path);
        stage.Native.DefineSkel(path, schemaKind);
    }
}
