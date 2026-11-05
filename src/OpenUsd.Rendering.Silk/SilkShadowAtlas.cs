// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Where one shadow map lives inside the single shadow atlas, in both texels and
/// atlas texture coordinates.
/// </summary>
internal readonly record struct SilkShadowTilePlacement(
    uint PixelX,
    uint PixelY,
    uint Resolution,
    float OffsetU,
    float OffsetV,
    float ScaleU,
    float ScaleV);

/// <summary>
/// The tile layout of the single shadow atlas the checked mesh fragment samples.
/// </summary>
/// <remarks>
/// <para>
/// Maps occupy a square grid with at least two columns, sized by the largest
/// published resolution. The 24-map budget fits in a five-by-five grid.
/// A smaller map occupies the lower-left corner of its cell, so mixed resolutions lay out exactly and
/// no map ever samples another map's texels.
/// </para>
/// <para>
/// The layout is a pure function of the published descriptors, so it is resolved
/// and asserted without a graphics device.
/// </para>
/// </remarks>
internal sealed class SilkShadowAtlasLayout
{
    private readonly SilkShadowTilePlacement[] _tiles;

    private SilkShadowAtlasLayout(uint edge, SilkShadowTilePlacement[] tiles)
    {
        Edge = edge;
        _tiles = tiles;
    }

    /// <summary>Gets the square atlas edge length in texels.</summary>
    public uint Edge { get; }

    /// <summary>Gets one atlas texel in atlas texture coordinates.</summary>
    public float TexelSize => 1f / Edge;

    /// <summary>Gets the placement of every published map, in map order.</summary>
    public IReadOnlyList<SilkShadowTilePlacement> Tiles => _tiles;

    /// <summary>
    /// Resolves the atlas layout of one published shadow table, or
    /// <see langword="null"/> when the table describes no map.
    /// </summary>
    public static SilkShadowAtlasLayout? Create(
        IReadOnlyList<SilkShadowDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        if (descriptors.Count == 0)
        {
            return null;
        }
        if (descriptors.Count > SilkShadowCommand.MaximumMaps)
        {
            throw new ArgumentOutOfRangeException(
                nameof(descriptors),
                "The shadow table exceeds the page map budget.");
        }

        uint quadrant = 0;
        foreach (SilkShadowDescriptor descriptor in descriptors)
        {
            if (descriptor.Resolution < SilkShadowCommand.MinimumResolution ||
                descriptor.Resolution > SilkShadowCommand.MaximumResolution)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptors),
                    "A shadow descriptor resolution is outside the ABI bounds.");
            }
            quadrant = Math.Max(quadrant, descriptor.Resolution);
        }

        // A single map still gets a two-by-two atlas. The alternative -- sizing
        // the atlas to the map count -- would re-create the texture whenever a
        // light started or stopped casting, which is exactly the case a retained
        // map exists to survive.
        uint columns = Math.Max(2u, (uint)Math.Ceiling(Math.Sqrt(descriptors.Count)));
        uint edge = quadrant * columns;
        var tiles = new SilkShadowTilePlacement[descriptors.Count];
        for (int index = 0; index < descriptors.Count; index++)
        {
            uint resolution = descriptors[index].Resolution;
            uint pixelX = ((uint)index % columns) * quadrant;
            uint pixelY = ((uint)index / columns) * quadrant;
            tiles[index] = new SilkShadowTilePlacement(
                pixelX,
                pixelY,
                resolution,
                pixelX / (float)edge,
                pixelY / (float)edge,
                resolution / (float)edge,
                resolution / (float)edge);
        }
        return new SilkShadowAtlasLayout(edge, tiles);
    }
}

internal static class SilkPointShadowDepthEncoding
{
    private const float MinimumNormalFloat = 1.17549435E-38f;

    internal static void Prepare(
        IReadOnlyList<SilkShadowDescriptor> descriptors,
        Span<float> scales)
    {
        if (scales.Length != SilkFrameState.MaximumLights)
        {
            throw new ArgumentException("Point-shadow scales must cover the light table.", nameof(scales));
        }

        scales.Clear();
        Span<double> farthest = stackalloc double[SilkFrameState.MaximumLights];
        farthest.Clear();
        for (int index = 0; index < descriptors.Count; index++)
        {
            SilkShadowDescriptor descriptor = descriptors[index];
            if ((descriptor.Flags & SilkShadowDescriptorOptions.CubeFace) == 0)
            {
                continue;
            }
            if (descriptor.LightIndex >= SilkFrameState.MaximumLights)
            {
                throw new InvalidDataException("A point-shadow descriptor names an invalid light.");
            }
            double far = ReadDepthRange(descriptor).Far;
            int light = (int)descriptor.LightIndex;
            farthest[light] = Math.Max(farthest[light], far);
        }

        for (int light = 0; light < farthest.Length; light++)
        {
            if (farthest[light] == 0)
            {
                continue;
            }
            float farBound = MathF.BitIncrement((float)farthest[light]);
            if (!float.IsFinite(farBound))
            {
                throw new InvalidDataException(
                    "The point-shadow depth range is not representable by the GPU.");
            }
            float scale = (float)(1d / farBound);
            if ((double)scale * farBound >= 1)
            {
                scale = MathF.BitDecrement(scale);
            }
            if (!float.IsFinite(scale) || scale < MinimumNormalFloat)
            {
                throw new InvalidDataException(
                    "The point-shadow depth range is not representable by the GPU.");
            }
            scales[light] = scale;
        }

        for (int index = 0; index < descriptors.Count; index++)
        {
            SilkShadowDescriptor descriptor = descriptors[index];
            if ((descriptor.Flags & SilkShadowDescriptorOptions.CubeFace) != 0)
            {
                float scale = scales[(int)descriptor.LightIndex];
                if (ReadDepthRange(descriptor).Near * scale < MinimumNormalFloat)
                {
                    throw new InvalidDataException(
                        "A point-shadow face has a depth range that is not representable by the GPU.");
                }
                _ = ConvertBias(descriptor, scale);
            }
        }
    }

    internal static float ConvertBias(SilkShadowDescriptor descriptor, float scale)
    {
        (double near, double far) = ReadDepthRange(descriptor);
        if (!float.IsFinite(descriptor.DepthBias) || descriptor.DepthBias < 0 ||
            !float.IsFinite(scale) || scale < MinimumNormalFloat)
        {
            throw new InvalidDataException("The point-shadow depth bias is invalid.");
        }

        // For projected depth A - B / distance, preserve the producer's depth
        // bias after replacing the stored depth with distance * scale.
        double bias = ((double)descriptor.DepthBias / near) *
            ((far - near) / far) / scale;
        float converted = (float)bias;
        if (!double.IsFinite(bias) || !float.IsFinite(converted) ||
            converted > float.MaxValue / 3 ||
            (descriptor.DepthBias > 0 && converted < MinimumNormalFloat))
        {
            throw new InvalidDataException(
                "The point-shadow depth bias is not representable by the GPU.");
        }
        return converted;
    }

    private static (double Near, double Far) ReadDepthRange(SilkShadowDescriptor descriptor)
    {
        double[] projection = descriptor.Projection;
        double[] view = descriptor.View;
        if (projection is null || view is null || projection.Length != 16 || view.Length != 16 ||
            !AllFinite(projection) || !AllFinite(view) ||
            projection[2] != 0 || projection[3] != 0 ||
            projection[6] != 0 || projection[7] != 0 ||
            projection[10] >= -1 || projection[11] != -1 ||
            projection[14] >= 0 || projection[15] != 0 ||
            view[3] != 0 || view[7] != 0 || view[11] != 0 || view[15] != 1)
        {
            throw new InvalidDataException(
                "A point-shadow face requires a finite affine view and perspective depth projection.");
        }

        double near = projection[14] / (projection[10] - 1);
        double far = projection[14] / (projection[10] + 1);
        if (!double.IsFinite(near) || !double.IsFinite(far) ||
            near < MinimumNormalFloat || far <= near)
        {
            throw new InvalidDataException("The point-shadow depth range is invalid.");
        }
        return (near, far);
    }

    private static bool AllFinite(double[] values)
    {
        foreach (double value in values)
        {
            if (!double.IsFinite(value))
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// The resolved shadow state one frame's constants carry: the light-space clip
/// matrices, the atlas tiles they sample, the producer's bias and filter policy,
/// and the map slot each direct light reads.
/// </summary>
internal sealed class SilkShadowFrameBinding
{
    private readonly Matrix4x4[] _worldToLightClip;
    private readonly Vector4[] _tiles;
    private readonly Vector4[] _controls;
    private readonly int[] _slotsByLight;
    private readonly float[] _pointDepthScalesByLight;

    private SilkShadowFrameBinding(
        Matrix4x4[] worldToLightClip,
        Vector4[] tiles,
        Vector4[] controls,
        int[] slotsByLight,
        float[] pointDepthScalesByLight)
    {
        _worldToLightClip = worldToLightClip;
        _tiles = tiles;
        _controls = controls;
        _slotsByLight = slotsByLight;
        _pointDepthScalesByLight = pointDepthScalesByLight;
    }

    /// <summary>Gets the binding of a frame that casts no shadow at all.</summary>
    public static SilkShadowFrameBinding None { get; } = new(
        new Matrix4x4[SilkShadowCommand.MaximumMaps],
        new Vector4[SilkShadowCommand.MaximumMaps],
        new Vector4[SilkShadowCommand.MaximumMaps],
        CreateEmptySlots(),
        new float[SilkFrameState.MaximumLights]);

    /// <summary>Gets the number of bound maps.</summary>
    public int Count { get; private init; }

    /// <summary>Gets one map's world-to-light-clip matrix.</summary>
    public Matrix4x4 GetWorldToLightClip(int slot) => _worldToLightClip[slot];

    /// <summary>Gets one map's atlas tile as (offsetU, offsetV, scaleU, scaleV).</summary>
    public Vector4 GetTile(int slot) => _tiles[slot];

    /// <summary>
    /// Gets one map's controls as (depthBias, normalBias, pcfRadius, texelSize).
    /// </summary>
    /// <remarks>A negative radius encodes active transmission as -(pcfRadius + 1).</remarks>
    public Vector4 GetControls(int slot) => _controls[slot];

    /// <summary>Gets the map slot a direct light samples, or <c>-1</c>.</summary>
    public int GetSlotForLight(int lightIndex) => _slotsByLight[lightIndex];

    /// <summary>Gets the number of consecutive maps sampled by one light.</summary>
    public int GetMapCountForLight(int lightIndex) =>
        _slotsByLight[lightIndex] < 0 ? 0 : _mapCountsByLight[lightIndex];

    internal float GetPointDepthScaleForLight(int lightIndex) => _pointDepthScalesByLight[lightIndex];

    private readonly int[] _mapCountsByLight = new int[SilkFrameCommand.MaximumLights];

    /// <summary>
    /// Resolves the frame binding of one retained shadow table and atlas layout.
    /// </summary>
    /// <remarks>
    /// The matrix is the light-space camera converted to the [0, +w] clip depth
    /// every backend consumes, and it is deliberately never Y-mirrored: the
    /// mirror belongs to rasterization, and the shadow pass already applies the
    /// device's own clip convention when it renders the map, so the stored map is
    /// identical on every backend. The colour pass therefore reconstructs the
    /// atlas coordinate from the unmirrored clip position with one convention
    /// everywhere.
    /// </remarks>
    public static SilkShadowFrameBinding Create(
        IReadOnlyList<SilkShadowDescriptor> descriptors,
        SilkShadowAtlasLayout layout,
        bool transmission = false)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(layout);
        var matrices = new Matrix4x4[SilkShadowCommand.MaximumMaps];
        var tiles = new Vector4[SilkShadowCommand.MaximumMaps];
        var controls = new Vector4[SilkShadowCommand.MaximumMaps];
        int[] slots = CreateEmptySlots();
        var pointDepthScales = new float[SilkFrameState.MaximumLights];
        SilkPointShadowDepthEncoding.Prepare(descriptors, pointDepthScales);
        for (int index = 0; index < descriptors.Count; index++)
        {
            SilkShadowDescriptor descriptor = descriptors[index];
            matrices[index] = SilkShadowMatrix.CreateWorldToLightClip(descriptor);
            SilkShadowTilePlacement tile = layout.Tiles[index];
            tiles[index] = new Vector4(
                tile.OffsetU,
                tile.OffsetV,
                tile.ScaleU,
                tile.ScaleV);
            float depthBias = (descriptor.Flags & SilkShadowDescriptorOptions.CubeFace) != 0
                ? SilkPointShadowDepthEncoding.ConvertBias(
                    descriptor, pointDepthScales[(int)descriptor.LightIndex])
                : descriptor.DepthBias;
            controls[index] = new Vector4(
                depthBias,
                descriptor.NormalBias,
                transmission ? -1 - descriptor.PcfRadius : descriptor.PcfRadius,
                layout.TexelSize);
            if (slots[(int)descriptor.LightIndex] < 0)
            {
                slots[(int)descriptor.LightIndex] = index;
            }
        }
        var binding = new SilkShadowFrameBinding(matrices, tiles, controls, slots, pointDepthScales)
        {
            Count = descriptors.Count,
        };
        foreach (SilkShadowDescriptor descriptor in descriptors)
        {
            binding._mapCountsByLight[(int)descriptor.LightIndex]++;
        }
        return binding;
    }

    private static int[] CreateEmptySlots()
    {
        int[] slots = new int[SilkFrameCommand.MaximumLights];
        Array.Fill(slots, -1);
        return slots;
    }
}

/// <summary>
/// Composes the light-space clip matrices a shadow map is rendered and sampled
/// with.
/// </summary>/// <remarks>
/// The page publishes the light-space view and projection in exactly the
/// conventions the FRAME camera uses -- row-major, row-vector, OpenGL
/// <c>[-w, +w]</c> clip depth -- so the same depth conversion and the same clip-Y
/// mirroring that the camera path applies are the whole of the backend
/// adaptation here.
/// </remarks>
internal static class SilkShadowMatrix
{
    /// <summary>
    /// Composes a caster's object-to-light-clip matrix for the depth-only pass.
    /// </summary>
    /// <param name="descriptor">The published shadow descriptor.</param>
    /// <param name="objectToWorld">The caster's row-major world transform.</param>
    /// <param name="flipClipSpaceY">Whether the backend's clip space points down.</param>
    /// <returns>The column-vector matrix the shadow vertex stage multiplies by.</returns>
    public static Matrix4x4 CreateObjectToLightClip(
        SilkShadowDescriptor descriptor,
        ReadOnlySpan<double> objectToWorld,
        bool flipClipSpaceY)
    {
        if (objectToWorld.Length != 16)
        {
            throw new ArgumentException(
                "An object transform must contain exactly 16 values.",
                nameof(objectToWorld));
        }

        Span<double> objectToLight = stackalloc double[16];
        Span<double> projected = stackalloc double[16];
        Span<double> clip = stackalloc double[16];
        Multiply(objectToWorld, descriptor.View, objectToLight);
        Multiply(objectToLight, descriptor.Projection, projected);
        ConvertOpenGlDepthToZeroToOne(projected, clip);
        if (flipClipSpaceY)
        {
            MirrorClipSpaceY(clip);
        }
        return ToMatrix4x4(clip);
    }

    /// <summary>
    /// Composes the world-to-light-clip matrix the colour pass samples the atlas
    /// with.
    /// </summary>
    /// <remarks>
    /// There is deliberately no clip-Y parameter. The mirror belongs to
    /// rasterization: <see cref="CreateObjectToLightClip"/> applies the device's
    /// own convention when the map is rendered, so every backend stores the same
    /// image and the colour pass reconstructs the atlas coordinate from the
    /// unmirrored clip position everywhere. Accepting a flag here would let the
    /// two halves disagree, which a Y-symmetric scene cannot detect.
    /// </remarks>
    public static Matrix4x4 CreateWorldToLightClip(SilkShadowDescriptor descriptor)
    {
        Span<double> projected = stackalloc double[16];
        Span<double> clip = stackalloc double[16];
        Multiply(descriptor.View, descriptor.Projection, projected);
        ConvertOpenGlDepthToZeroToOne(projected, clip);
        return ToMatrix4x4(clip);
    }

    private static void Multiply(
        ReadOnlySpan<double> left,
        ReadOnlySpan<double> right,
        Span<double> result)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                double value = 0;
                for (int inner = 0; inner < 4; inner++)
                {
                    value += left[(row * 4) + inner] * right[(inner * 4) + column];
                }
                result[(row * 4) + column] = value;
            }
        }
    }

    private static void ConvertOpenGlDepthToZeroToOne(
        ReadOnlySpan<double> source,
        Span<double> destination)
    {
        for (int row = 0; row < 4; row++)
        {
            int offset = row * 4;
            destination[offset] = source[offset];
            destination[offset + 1] = source[offset + 1];
            destination[offset + 2] = (source[offset + 2] + source[offset + 3]) * 0.5;
            destination[offset + 3] = source[offset + 3];
        }
    }

    private static void MirrorClipSpaceY(Span<double> matrix)
    {
        for (int row = 0; row < 4; row++)
        {
            matrix[(row * 4) + 1] = -matrix[(row * 4) + 1];
        }
    }

    private static Matrix4x4 ToMatrix4x4(ReadOnlySpan<double> matrix)
    {
        Span<float> values = stackalloc float[16];
        for (int index = 0; index < 16; index++)
        {
            double value = matrix[index];
            if (!double.IsFinite(value) ||
                value > float.MaxValue ||
                value < -float.MaxValue)
            {
                throw new InvalidDataException(
                    $"The shadow matrix element {index} is invalid.");
            }
            values[index] = (float)value;
        }
        return new Matrix4x4(
            values[0], values[1], values[2], values[3],
            values[4], values[5], values[6], values[7],
            values[8], values[9], values[10], values[11],
            values[12], values[13], values[14], values[15]);
    }
}

/// <summary>
/// Writes one caster's entry in the depth-only shadow instance table.
/// </summary>
/// <remarks>
/// The table uses the mesh instance layout. Its final float4 carries the
/// point-shadow depth scale instead of a colour tint.
/// </remarks>
internal static class SilkShadowInstanceWriter
{
    internal static void Write(
        Matrix4x4 objectToLightClip,
        Span<byte> destination,
        float pointDepthScale = 0)
    {
        if (destination.Length != 80)
        {
            throw new ArgumentException(
                "Shadow instance constants must be exactly 80 bytes.",
                nameof(destination));
        }
        if (!float.IsFinite(pointDepthScale) || pointDepthScale < 0)
        {
            throw new InvalidDataException("The point-shadow instance depth scale is invalid.");
        }

        Span<float> values =
        [
            objectToLightClip.M11, objectToLightClip.M12,
            objectToLightClip.M13, objectToLightClip.M14,
            objectToLightClip.M21, objectToLightClip.M22,
            objectToLightClip.M23, objectToLightClip.M24,
            objectToLightClip.M31, objectToLightClip.M32,
            objectToLightClip.M33, objectToLightClip.M34,
            objectToLightClip.M41, objectToLightClip.M42,
            objectToLightClip.M43, objectToLightClip.M44
        ];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                float value = values[(column * 4) + row];
                if (!float.IsFinite(value))
                {
                    throw new InvalidDataException(
                        $"The shadow instance matrix element [{column},{row}] is invalid.");
                }
                BinaryPrimitives.WriteInt32LittleEndian(
                    destination.Slice(((row * 4) + column) * sizeof(float), sizeof(float)),
                    BitConverter.SingleToInt32Bits(value));
            }
        }
        BinaryPrimitives.WriteSingleLittleEndian(destination[64..68], pointDepthScale);
        destination[68..80].Clear();
    }
}
