// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Shade;

/// <summary>A small schema-correct helper for a UsdPreviewSurface material network.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdPreviewSurface : IUsdStageBound
{
    private UsdPreviewSurface(UsdShadeMaterial material, UsdShadeShader shader)
    {
        Material = material;
        Shader = shader;
    }

    /// <summary>Gets the material.</summary>
    public UsdShadeMaterial Material { get; }

    /// <summary>Gets the UsdPreviewSurface shader.</summary>
    public UsdShadeShader Shader { get; }

    /// <summary>Defines and connects a Preview Surface material network.</summary>
    public static UsdPreviewSurface Create(
        UsdStage stage,
        string materialPath,
        string shaderPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdShadeMaterial material = stage.DefineMaterial(materialPath);
        UsdShadeShader shader = stage.DefineShader(shaderPath);
        shader.SourceId = "UsdPreviewSurface";
        UsdShadeOutput surface = shader.CreateOutput("surface", UsdShadeValueType.Token);
        material.ConnectSurface(surface);
        return new UsdPreviewSurface(material, shader);
    }

    /// <summary>Authors diffuseColor.</summary>
    public void SetDiffuseColor(UsdVec3f value) =>
        Shader.CreateInputColor3f("diffuseColor").SetColor(value);

    /// <summary>Authors emissiveColor.</summary>
    public void SetEmissiveColor(UsdVec3f value) =>
        Shader.CreateInputColor3f("emissiveColor").SetColor(value);

    /// <summary>Authors metallic.</summary>
    public void SetMetallic(float value) =>
        Shader.CreateInputFloat("metallic").Set(value);

    /// <summary>Authors roughness.</summary>
    public void SetRoughness(float value) =>
        Shader.CreateInputFloat("roughness").Set(value);

    /// <summary>Authors opacity.</summary>
    public void SetOpacity(float value) =>
        Shader.CreateInputFloat("opacity").Set(value);

    /// <summary>Authors opacityThreshold.</summary>
    public void SetOpacityThreshold(float value) =>
        Shader.CreateInputFloat("opacityThreshold").Set(value);

    /// <summary>Authors normal.</summary>
    public void SetNormal(UsdVec3f value) =>
        Shader.CreateInputNormal3f("normal").SetNormal(value);

    /// <summary>Authors displacement.</summary>
    public void SetDisplacement(float value) =>
        Shader.CreateInputFloat("displacement").Set(value);

    /// <summary>Connects diffuseColor to a color3f source.</summary>
    public void ConnectDiffuseColor(UsdShadeOutput source) =>
        Shader.CreateInputColor3f("diffuseColor").ConnectToSource(source);
}
