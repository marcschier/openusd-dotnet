// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Shade;

/// <summary>A small helper for a UsdUVTexture shader with an asset input.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdUvTexture : IUsdStageBound
{
    private UsdUvTexture(UsdShadeShader shader)
    {
        Shader = shader;
    }

    /// <summary>Gets the underlying shader.</summary>
    public UsdShadeShader Shader { get; }

    /// <summary>Gets the file asset input.</summary>
    public UsdShadeInput File => Shader.GetInput("file");

    /// <summary>Gets the canonical roleless float3 rgb output.</summary>
    public UsdShadeOutput Rgb => Shader.GetOutput("rgb");

    /// <summary>Defines a UsdUVTexture shader and authors its file asset path.</summary>
    public static UsdUvTexture Create(
        UsdStage stage,
        string shaderPath,
        UsdAssetPath assetPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdShadeShader shader = stage.DefineShader(shaderPath);
        shader.SourceId = "UsdUVTexture";
        shader.CreateInputAsset("file").SetAssetPath(assetPath);
        shader.CreateOutput("rgb", UsdShadeValueType.Float3);
        return new UsdUvTexture(shader);
    }
}
