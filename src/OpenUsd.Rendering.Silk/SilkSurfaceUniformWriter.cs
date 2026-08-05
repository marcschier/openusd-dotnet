// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Packs the constant UsdPreviewSurface inputs and the single deterministic light
/// into the surface constant block the mesh fragment shader reads.
/// </summary>
/// <remarks>
/// Deliberately separate from the scene constants. Those are pinned at exactly 80
/// bytes by contract and their layout is mirrored by the instance table, so
/// widening them to carry material values would change the instancing wire format
/// for no benefit. The light rides here rather than in its own binding because it
/// is four floats and a second slot would cost a second upload path everywhere.
/// </remarks>
internal static class SilkSurfaceUniformWriter
{
    /// <summary>Byte size of the surface constant block.</summary>
    /// <remarks>
    /// float4 diffuseColor+opacity, emissiveColor+occlusion, specularColor+ior,
    /// (metallic, roughness, opacityThreshold, useSpecularWorkflow),
    /// (clearcoat, clearcoatRoughness, shaded, 0), lightDirection+intensity,
    /// lightColor+ambient, and one reserved vector.
    /// </remarks>
    internal const int ByteSize = 128;

    // UsdPreviewSurface authored defaults, used when a material omits an input.
    private const float DefaultDiffuse = 0.18f;
    private const float DefaultRoughness = 0.5f;
    private const float DefaultOpacity = 1;
    private const float DefaultOcclusion = 1;
    private const float DefaultIor = 1.5f;
    private const float DefaultClearcoatRoughness = 0.01f;

    /// <summary>
    /// Writes the block for one material. A mesh with no supported material bound
    /// gets the shared default block, whose shaded flag is zero so the shader falls
    /// back to the scene display colour -- which is what Storm shows for a prim
    /// whose material is absent or unsupported.
    /// </summary>
    internal static void Write(
        SilkMaterialData? material,
        RenderHeadlight light,
        Span<byte> destination,
        bool supportsVolumeTextures = false)
    {
        if (destination.Length != ByteSize)
        {
            throw new ArgumentException(
                $"Surface constants must be exactly {ByteSize} bytes.",
                nameof(destination));
        }

        SilkMaterialData? shaded = material is { IsSupported: true } ? material : null;
        Vector3 diffuse = Vector(
            shaded,
            SilkMaterialParameter.DiffuseColor,
            new Vector3(DefaultDiffuse, DefaultDiffuse, DefaultDiffuse));
        float opacity = Scalar(shaded, SilkMaterialParameter.Opacity, DefaultOpacity);
        Vector3 emissive = Vector(shaded, SilkMaterialParameter.EmissiveColor, Vector3.Zero);
        float occlusion = Scalar(shaded, SilkMaterialParameter.Occlusion, DefaultOcclusion);
        Vector3 specular = Vector(shaded, SilkMaterialParameter.SpecularColor, Vector3.Zero);
        float ior = Scalar(shaded, SilkMaterialParameter.Ior, DefaultIor);
        float metallic = Scalar(shaded, SilkMaterialParameter.Metallic, 0);
        float roughness = Scalar(shaded, SilkMaterialParameter.Roughness, DefaultRoughness);
        float opacityThreshold = Scalar(shaded, SilkMaterialParameter.OpacityThreshold, 0);
        float specularWorkflow = Scalar(shaded, SilkMaterialParameter.UseSpecularWorkflow, 0);
        float clearcoat = Scalar(shaded, SilkMaterialParameter.Clearcoat, 0);
        float clearcoatRoughness = Scalar(
            shaded,
            SilkMaterialParameter.ClearcoatRoughness,
            DefaultClearcoatRoughness);
        bool volumeDensity = shaded?.SurfaceKind == SilkSurfaceKind.VolumeDensity;
        bool sampledVolume = supportsVolumeTextures &&
            shaded?.GetTexture(SilkMaterialParameter.VolumeDensity) is not null;
        float density = Scalar(shaded, SilkMaterialParameter.VolumeDensity, 0);

        WriteVector4(destination, 0, diffuse.X, diffuse.Y, diffuse.Z, opacity);
        WriteVector4(destination, 16, emissive.X, emissive.Y, emissive.Z, occlusion);
        WriteVector4(destination, 32, specular.X, specular.Y, specular.Z, ior);
        WriteVector4(destination, 48, metallic, roughness, opacityThreshold, specularWorkflow);
        WriteVector4(
            destination,
            64,
            clearcoat,
            clearcoatRoughness,
            shaded is null ? 0 : 1,
            0);

        Vector3 direction = Normalize(light.Direction);
        WriteVector4(
            destination,
            80,
            direction.X,
            direction.Y,
            direction.Z,
            Finite(light.Intensity, "light intensity"));
        WriteVector4(
            destination,
            96,
            Finite(light.Color.X, "light red"),
            Finite(light.Color.Y, "light green"),
            Finite(light.Color.Z, "light blue"),
            Finite(light.Ambient, "light ambient"));
        WriteVector4(destination, 112, volumeDensity ? 1 : 0, density, 2, sampledVolume ? 1 : 0);
    }

    private static Vector3 Normalize(Vector3 direction)
    {
        // A zero or non-finite direction would produce NaN lighting across the whole
        // surface, which is far harder to diagnose than an obviously wrong constant.
        float lengthSquared = direction.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= float.Epsilon)
        {
            return new Vector3(0, 0, 1);
        }

        return direction / MathF.Sqrt(lengthSquared);
    }

    private static bool TryReadVector(
        SilkMaterialData material,
        SilkMaterialParameter parameter,
        out Vector3 value)
    {
        ReadOnlySpan<float> values = material.GetScalar(parameter);
        if (values.Length < 3)
        {
            value = default;
            return false;
        }

        value = new Vector3(
            Finite(values[0], parameter, 0),
            Finite(values[1], parameter, 1),
            Finite(values[2], parameter, 2));
        return true;
    }

    private static Vector3 Vector(
        SilkMaterialData? material,
        SilkMaterialParameter parameter,
        Vector3 fallback) =>
        material is not null && TryReadVector(material, parameter, out Vector3 value)
            ? value
            : fallback;

    private static float Scalar(
        SilkMaterialData? material,
        SilkMaterialParameter parameter,
        float fallback)
    {
        if (material is null)
        {
            return fallback;
        }

        ReadOnlySpan<float> values = material.GetScalar(parameter);
        return values.Length == 0 ? fallback : Finite(values[0], parameter, 0);
    }

    private static float Finite(float value, SilkMaterialParameter parameter, int component)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException(
                $"Material parameter {parameter} component {component} is not finite.");
        }

        return value;
    }

    private static float Finite(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException($"The {name} is not finite.");
        }

        return value;
    }

    private static void WriteVector4(
        Span<byte> destination,
        int offset,
        float x,
        float y,
        float z,
        float w)
    {
        WriteSingle(destination, offset, x);
        WriteSingle(destination, offset + 4, y);
        WriteSingle(destination, offset + 8, z);
        WriteSingle(destination, offset + 12, w);
    }

    private static void WriteSingle(Span<byte> destination, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(
            destination.Slice(offset, sizeof(float)),
            BitConverter.SingleToInt32Bits(value));
}
