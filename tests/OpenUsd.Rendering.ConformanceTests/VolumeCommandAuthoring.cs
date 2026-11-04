// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Builds sampled-volume scene command streams without the native shim.
/// </summary>
/// <remarks>
/// The volume gates that read a real <c>.vdb</c> prove the whole chain, but they can only
/// run where the native runtime and OpenUSD's <c>hioOpenVDB</c> reader are present, and
/// they can only exercise the one grid that asset happens to contain. Authoring the
/// command stream directly gives the renderer-neutral half of the contract a device-real
/// but runtime-free gate, and -- more importantly here -- lets a test choose the grid,
/// which is the only way to cover a density field whose resolution differs from the one
/// checked-in asset.
/// </remarks>
internal static class VolumeCommandAuthoring
{
    /// <summary>Writes a tightly packed single-channel R32 density grid to disk.</summary>
    /// <remarks>
    /// The layout matches what the native shim caches and what
    /// <c>SilkSceneGpuResources</c> uploads: width-major, then height, then depth, with no
    /// header, so <paramref name="depth"/> layers of <paramref name="width"/> x
    /// <paramref name="height"/> floats.
    /// </remarks>
    internal static string WriteDensityGrid(
        string directory,
        string name,
        int width,
        int height,
        int depth,
        Func<int, int, int, float> density)
    {
        ArgumentNullException.ThrowIfNull(density);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{name}-{width}x{height}x{depth}.r32");
        byte[] bytes = new byte[checked(width * height * depth * sizeof(float))];
        int offset = 0;
        for (int z = 0; z < depth; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(
                        bytes.AsSpan(offset),
                        density(x, y, z));
                    offset += sizeof(float);
                }
            }
        }
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Builds a <c>VolumeDensity</c> material upsert command.</summary>
    /// <param name="materialPath">The material prim path.</param>
    /// <param name="authoredDensity">The uniform density multiplier.</param>
    /// <param name="volumeGrid">
    /// The cached R32 grid to sample, or <see langword="null"/> for an authored-uniform
    /// volume that binds no 3D texture.
    /// </param>
    /// <param name="extent">The grid's <c>width,height,depth</c> extent.</param>
    /// <param name="additionalTextures">Extra 2D maps, used only by rejection cases.</param>
    internal static byte[] CreateVolumeMaterialCommand(
        string materialPath,
        float authoredDensity,
        string? volumeGrid,
        string? extent,
        IReadOnlyList<TextureSpec>? additionalTextures = null,
        float? opacity = null)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(materialPath);
        var textures = new List<TextureSpec>();
        if (volumeGrid is not null)
        {
            textures.Add(new TextureSpec(
                volumeGrid,
                SilkMaterialParameter.VolumeDensity,
                extent ?? throw new ArgumentNullException(nameof(extent)),
                1,
                SilkTextureChannel.R));
        }
        if (additionalTextures is not null)
        {
            textures.AddRange(additionalTextures);
        }

        int textureBytes = 0;
        foreach (TextureSpec texture in textures)
        {
            textureBytes = checked(
                textureBytes +
                TextureHeaderByteCount +
                Encoding.UTF8.GetByteCount(texture.Asset) +
                Encoding.UTF8.GetByteCount(texture.UvPrimvar));
        }
        int scalarBytes = 8 + (3 * sizeof(float)) + 8 + sizeof(float) +
            (opacity.HasValue ? 8 + sizeof(float) : 0);
        const int uvTransformBytes = (12 * sizeof(float)) + sizeof(uint);
        byte[] bytes = new byte[
            32 + pathBytes.Length + scalarBytes + textureBytes +
            (2 * sizeof(uint)) + uvTransformBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MaterialUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)bytes.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), ComputeStableHash(materialPath));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), checked((uint)pathBytes.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), (uint)SilkSurfaceKind.VolumeDensity);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), opacity.HasValue ? 3u : 2u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), checked((uint)textures.Count));
        pathBytes.CopyTo(bytes.AsSpan(32));

        int offset = 32 + pathBytes.Length;
        WriteScalar(bytes, ref offset, SilkMaterialParameter.DiffuseColor, [0.9f, 0.36f, 0.12f]);
        WriteScalar(bytes, ref offset, SilkMaterialParameter.VolumeDensity, [authoredDensity]);
        if (opacity is { } authoredOpacity)
        {
            WriteScalar(bytes, ref offset, SilkMaterialParameter.Opacity, [authoredOpacity]);
        }
        foreach (TextureSpec texture in textures)
        {
            WriteTexture(bytes, ref offset, texture);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + sizeof(uint)), 0);
        offset += 2 * sizeof(uint);
        float[] identityUvTransform = [1, 0, 0, 1, 0, 0];
        for (int index = 0; index < identityUvTransform.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(offset + (index * sizeof(float))),
                identityUvTransform[index]);
        }
        offset += 6 * sizeof(float) + sizeof(uint);
        for (int index = 0; index < identityUvTransform.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(offset + (index * sizeof(float))),
                identityUvTransform[index]);
        }
        return bytes;
    }

    /// <summary>Builds a cube whose full unit chord is visible to the sampling-test camera.</summary>
    internal static byte[] CreateVolumeProxyMeshCommand(string meshPath, string materialPath)
    {
        byte[] mesh = CreateVolumeCameraProxyMeshCommand(meshPath, materialPath, zOffset: -1);
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(40), 0);
        return mesh;
    }

    internal static byte[] CreateVolumeSamplingFrameCommand(uint width, uint height)
    {
        byte[] frame = SilkMeshRendererConformance.CreateFrameCommand(
            width, height, SilkMeshRendererConformance.Identity());
        BinaryPrimitives.WriteDoubleLittleEndian(frame.AsSpan(144 + 10 * sizeof(double)), -2.0 / 1.9);
        BinaryPrimitives.WriteDoubleLittleEndian(frame.AsSpan(144 + 14 * sizeof(double)), -2.1 / 1.9);
        return frame;
    }

    /// <summary>Builds a native-style cube proxy for camera-ray volume tests.</summary>
    /// <remarks>
    /// The cube permits ray intersections through the volume, including when the
    /// camera is inside it.
    /// </remarks>
    internal static byte[] CreateVolumeCameraProxyMeshCommand(
        string meshPath,
        string materialPath,
        float zOffset = 0.7f,
        ulong id = 1)
    {
        float[] points =
        [
            -0.5f, -0.5f, -0.5f,
             0.5f, -0.5f, -0.5f,
             0.5f,  0.5f, -0.5f,
            -0.5f,  0.5f, -0.5f,
            -0.5f, -0.5f,  0.5f,
             0.5f, -0.5f,  0.5f,
             0.5f,  0.5f,  0.5f,
            -0.5f,  0.5f,  0.5f,
        ];
        uint[] indices =
        [
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6,
            3, 0, 4, 3, 4, 7,
        ];
        byte[] mesh = SilkMeshRendererConformance.CreateMeshCommand(
            id,
            meshPath,
            points,
            indices);
        BinaryPrimitives.WriteDoubleLittleEndian(
            mesh.AsSpan(80 + (14 * sizeof(double))),
            zOffset);
        return WithMaterialBinding(mesh, materialPath);
    }

    internal static byte[] CreateDepthOccluderMeshCommand(
        string meshPath,
        float z,
        ulong id = 100)
    {
        float[] points =
        [
            -2, -2, 0,
             2, -2, 0,
             2,  2, 0,
            -2,  2, 0,
        ];
        uint[] indices = [0, 1, 2, 0, 2, 3];
        byte[] mesh = SilkMeshRendererConformance.CreateMeshCommand(
            id,
            meshPath,
            points,
            indices,
            color: [0, 1, 0, 1]);
        BinaryPrimitives.WriteUInt32LittleEndian(
            mesh.AsSpan(44),
            (uint)SilkMeshCullStyle.Nothing);
        BinaryPrimitives.WriteDoubleLittleEndian(
            mesh.AsSpan(80 + (14 * sizeof(double))),
            z);
        return mesh;
    }

    internal static byte[] WithTransform(byte[] mesh, ReadOnlySpan<double> transform)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (transform.Length != 16)
        {
            throw new ArgumentException("A mesh transform must contain 16 elements.", nameof(transform));
        }
        byte[] transformed = [.. mesh];
        for (int index = 0; index < transform.Length; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                transformed.AsSpan(80 + (index * sizeof(double))),
                transform[index]);
        }
        return transformed;
    }

    /// <summary>
    /// Appends a material path to a mesh upsert produced without one.
    /// </summary>
    /// <remarks>
    /// The material path is the last variable-length field before the attribute table,
    /// and the shared helper writes zero attributes, so appending the bytes and filling
    /// in the declared length is the whole edit.
    /// </remarks>
    internal static byte[] WithMaterialBinding(byte[] mesh, string materialPath)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        byte[] pathBytes = Encoding.UTF8.GetBytes(materialPath);
        byte[] bound = new byte[mesh.Length + pathBytes.Length];
        int materialOffset = checked(276 +
            (int)BinaryPrimitives.ReadUInt32LittleEndian(mesh.AsSpan(48)) +
            ((int)BinaryPrimitives.ReadUInt32LittleEndian(mesh.AsSpan(52)) * 12) +
            ((int)BinaryPrimitives.ReadUInt32LittleEndian(mesh.AsSpan(56)) * 4) +
            ((int)BinaryPrimitives.ReadUInt32LittleEndian(mesh.AsSpan(60)) * 4));
        mesh.AsSpan(0, materialOffset).CopyTo(bound);
        pathBytes.CopyTo(bound, materialOffset);
        mesh.AsSpan(materialOffset).CopyTo(bound.AsSpan(materialOffset + pathBytes.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(bound.AsSpan(4), checked((uint)bound.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(
            bound.AsSpan(208),
            ComputeStableHash(materialPath));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bound.AsSpan(216),
            checked((uint)pathBytes.Length));
        return bound;
    }

    internal static ulong ComputeStableHash(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ulong hash = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private static void WriteScalar(
        byte[] bytes,
        ref int offset,
        SilkMaterialParameter parameter,
        ReadOnlySpan<float> values)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), (uint)parameter);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(offset + 4),
            checked((uint)values.Length));
        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(offset + 8 + (index * sizeof(float))),
                values[index]);
        }
        offset += 8 + (values.Length * sizeof(float));
    }

    private static void WriteTexture(byte[] bytes, ref int offset, TextureSpec texture)
    {
        byte[] assetBytes = Encoding.UTF8.GetBytes(texture.Asset);
        byte[] uvBytes = Encoding.UTF8.GetBytes(texture.UvPrimvar);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), (uint)texture.Parameter);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4), (uint)SilkTextureWrap.Clamp);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 8), (uint)SilkTextureWrap.Clamp);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 12), (uint)SilkColorSpace.Raw);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 16), checked((uint)assetBytes.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 20), checked((uint)uvBytes.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 24), checked((uint)texture.ComponentCount));
        for (int component = 0; component < 4; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(offset + 28 + (component * sizeof(float))),
                1f);
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(offset + 44 + (component * sizeof(float))),
                0f);
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(offset + 60 + (component * sizeof(float))),
                component == 3 ? 1f : 0.5f);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 76), (uint)texture.Channel);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(offset + 80),
            (uint)texture.CompositeOperator);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 84), texture.CompositeFactor);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 88), (uint)SilkTextureSourceKind.Standard);
        assetBytes.CopyTo(bytes.AsSpan(offset + TextureHeaderByteCount));
        uvBytes.CopyTo(bytes.AsSpan(offset + TextureHeaderByteCount + assetBytes.Length));
        offset += TextureHeaderByteCount + assetBytes.Length + uvBytes.Length;
    }

    /// <summary>
    /// The fixed part of a material texture entry, before its asset and primvar strings.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>SilkMaterialTextureEntry</c>: the channel ends at 80 and the composite
    /// operator and factor occupy 80..88; the source kind occupies 88..92. The
    /// offsets in <see cref="WriteTexture"/> and the buffer sizing above move together.
    /// </remarks>
    private const int TextureHeaderByteCount = 92;

    internal readonly record struct TextureSpec(
        string Asset,
        SilkMaterialParameter Parameter,
        string UvPrimvar,
        int ComponentCount,
        SilkTextureChannel Channel,
        SilkCompositeOperator CompositeOperator = SilkCompositeOperator.None,
        float CompositeFactor = 0);
}
