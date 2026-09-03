// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Draws one prototype as the four composed instances a two-level nested
/// instancer publishes and requires the ABI v21 link table to reach each of them
/// by its composed identity.
/// </summary>
/// <remarks>
/// <para>
/// hdSilk publishes a nested instance under the composed index
/// <c>outerIndex * innerInstanceCount + innerIndex</c>, so composed 0 and 1 are
/// the two inner instances of the first outer instance and composed 2 and 3 are
/// the two inner instances of the second. This case authors exactly that
/// numbering and splits it the way a UsdLux collection on the outer instance
/// splits it: the first outer instance keeps the red light and loses the dome,
/// the second keeps the blue light and the dome.
/// </para>
/// <para>
/// The three masks are complementary and independent on purpose. Resolving the
/// inner index instead of the composed one would give composed 0 and 2 the same
/// masks, which swaps the middle two columns. Resolving only the prototype row
/// would leave every column magenta. Intersecting the shadow mask into the light
/// mask would darken two columns that must stay lit. Each of those is a
/// different measured image here.
/// </para>
/// <para>
/// The shadow half is a caster restriction and never a receiver restriction, so
/// changing only the shadow masks must reproduce the previous image byte for
/// byte while the retained table still reports the new casters. That is the
/// property an implementation that folded the caster collection into the lit
/// result would fail without changing any other measurement.
/// </para>
/// <para>
/// It runs on the D3D12 WARP and Vulkan SwiftShader devices, so the evidence is
/// cross-backend and needs no GPU.
/// </para>
/// </remarks>
internal static class SilkNestedInstanceLinkConformance
{
    private const string NestedPrototype = "/World/Nested/Inner/Leaf";
    private const uint Size = 64;
    private const int SampleY = 32;

    /// <summary>The composed identity of each published instance and its column.</summary>
    private static readonly double[] Positions = [-0.6, -0.2, 0.2, 0.6];

    /// <summary>
    /// The texel column at the centre of each composed instance's quad, and
    /// columns that fall in the gaps between them and outside them all.
    /// </summary>
    /// <remarks>
    /// The quads are 0.3 wide in clip space at x = -0.6, -0.2, 0.2 and 0.6, so
    /// they cover [8.0, 17.6], [20.8, 30.4], [33.6, 43.2] and [46.4, 56.0] of a
    /// 64-texel row with clear gaps between them. The gap columns are what make
    /// the coverage claim mean something: an instance drawn at another
    /// instance's transform leaves its own columns background and doubles up on
    /// the other's, which changes both sets.
    /// </remarks>
    private static readonly int[] Centres = [13, 25, 38, 51];

    private static readonly int[] Gaps = [3, 19, 32, 45, 60];

    internal static async Task ComposedInstancesResolveTheirOwnMasks(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);

        // One prototype path with a payload record on composed index 0 and three
        // lightweight instance references on 1, 2 and 3. That is the shape a
        // nested instancer publishes, and the only shape a per-instance mask can
        // tear: the geometry is retained once and every instance is a transform.
        var page = new List<byte[]>
        {
            Frame(),
            Quad(1, NestedPrototype, Positions[0]),
        };
        for (int index = 1; index < Positions.Length; index++)
        {
            page.Add(InstanceReference(NestedPrototype, index, Positions[index]));
        }
        SilkMeshRendererConformance.Apply(renderer, revision: 1, [.. page]);
        SilkMeshRenderResult unlinkedResult = renderer.Render(color, depth);
        byte[] unlinked = ReadPixels(color);

        await Assert.That(renderer.Scene.LightLinks.HasLinks)
            .IsFalse()
            .Because("No link table was published, so nothing may be retained.");
        await Assert.That(unlinkedResult.DrawCount)
            .IsEqualTo(1)
            .Because("Four instances of one prototype must batch into one draw.");

        // The coverage claim is anchored to the cleared background rather than
        // to any absolute value: a texel is covered when it differs from the
        // corner of the same frame, which the quads never reach. Comparing an
        // alpha channel against a threshold instead measured the opaque clear
        // and was true of every texel in the image.
        bool[] unlinkedColumns = CoveredColumns(unlinked);
        await AssertRegionsAreExactlyTheFourQuads(unlinkedColumns, "unlinked");
        for (int index = 0; index < Centres.Length; index++)
        {
            await AssertLit(
                unlinked,
                Centres[index],
                red: true,
                green: true,
                blue: true,
                $"unlinked composed instance {index}");
        }

        // Composed 0 and 1 are the first outer instance: red light only, no dome.
        // Composed 2 and 3 are the second: blue light only, plus the dome. The
        // caster collection is the complement of the light collection again, so
        // no pair of masks can be derived from another.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 2,
            LightLink(
                lightCount: 2,
                domeCount: 1,
                (NestedPrototype, SilkLightLinkCommand.AllInstances, 0b11u, 0b11u, 0b1u),
                (NestedPrototype, 0, 0b01u, 0b00u, 0b0u),
                (NestedPrototype, 1, 0b01u, 0b00u, 0b0u),
                (NestedPrototype, 2, 0b10u, 0b11u, 0b1u),
                (NestedPrototype, 3, 0b10u, 0b11u, 0b1u)));
        SilkMeshRenderResult splitResult = renderer.Render(color, depth);
        byte[] split = ReadPixels(color);

        await Assert.That(renderer.Scene.LightLinks.HasLinks).IsTrue();
        await Assert.That(renderer.Scene.LightLinks.LightCount).IsEqualTo(2u);
        await Assert.That(renderer.Scene.LightLinks.DomeCount).IsEqualTo(1u);
        await Assert.That(splitResult.DrawCount)
            .IsEqualTo(2)
            .Because(
                "The composed masks must split one geometry into two instanced " +
                "batches rather than collapsing onto one mask.");

        // Every composed identity keeps its own column and its own lighting.
        await AssertLit(split, Centres[0], red: true, green: false, blue: false, "composed 0");
        await AssertLit(split, Centres[1], red: true, green: false, blue: false, "composed 1");
        await AssertLit(split, Centres[2], red: false, green: true, blue: true, "composed 2");
        await AssertLit(split, Centres[3], red: false, green: true, blue: true, "composed 3");

        // Nothing moved. The split covers exactly the same texels as the
        // unlinked frame, column for column and texel for texel, so no instance
        // was drawn twice at another instance's transform and none vanished.
        // Sharing one mutable instance-transform table across the two batches
        // collapses the first batch's columns onto the second's, which changes
        // both the covered set and the gaps.
        bool[] splitColumns = CoveredColumns(split);
        await AssertRegionsAreExactlyTheFourQuads(splitColumns, "split");
        for (int column = 0; column < (int)Size; column++)
        {
            await Assert.That(splitColumns[column])
                .IsEqualTo(unlinkedColumns[column])
                .Because(
                    $"Column {column} changed coverage when the masks split the " +
                    "batch, so a composed instance moved.");
        }
        await Assert.That(CoveredTexelCount(split))
            .IsEqualTo(CoveredTexelCount(unlinked))
            .Because(
                "Splitting one geometry across two instanced batches must not " +
                "duplicate or drop a composed instance transform.");

        // The retained table answers per composed identity, and the caster
        // restriction is a separate answer from the light one.
        for (int index = 0; index < Centres.Length; index++)
        {
            SilkLightLinkMasks masks =
                renderer.Scene.LightLinks.Resolve(NestedPrototype, index);
            bool second = index >= 2;
            await Assert.That(masks.IsLit(0)).IsEqualTo(!second);
            await Assert.That(masks.IsLit(1)).IsEqualTo(second);
            await Assert.That(masks.CastsShadow(0)).IsEqualTo(second);
            await Assert.That(masks.IsDomeLit(0)).IsEqualTo(second);
        }

        // Moving only the caster collection must not move a pixel: UsdLux shadow
        // linking restricts who casts, never who receives. The retained table
        // still has to report the new casters, which is what separates "the
        // masks are independent" from "the shadow mask is ignored".
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 3,
            LightLink(
                lightCount: 2,
                domeCount: 1,
                (NestedPrototype, SilkLightLinkCommand.AllInstances, 0b11u, 0b11u, 0b1u),
                (NestedPrototype, 0, 0b01u, 0b11u, 0b0u),
                (NestedPrototype, 1, 0b01u, 0b11u, 0b0u),
                (NestedPrototype, 2, 0b10u, 0b00u, 0b1u),
                (NestedPrototype, 3, 0b10u, 0b00u, 0b1u)));
        _ = renderer.Render(color, depth);
        byte[] recast = ReadPixels(color);

        await Assert.That(recast.AsSpan().SequenceEqual(split))
            .IsTrue()
            .Because(
                "A caster restriction must not change what a composed instance " +
                "receives.");
        for (int index = 0; index < Centres.Length; index++)
        {
            await Assert.That(
                    renderer.Scene.LightLinks.Resolve(NestedPrototype, index).CastsShadow(0))
                .IsEqualTo(index < 2)
                .Because("The retained caster restriction must follow the table.");
        }

        // Retiring the table restores the unlinked image exactly, which is what
        // proves the composed masks are applied per draw rather than baked into a
        // resource that survives the collections being removed.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 4,
            LightLink(lightCount: 0, domeCount: 0));
        _ = renderer.Render(color, depth);
        byte[] retired = ReadPixels(color);

        await Assert.That(renderer.Scene.LightLinks.HasLinks).IsFalse();
        await Assert.That(retired.AsSpan().SequenceEqual(unlinked))
            .IsTrue()
            .Because("Retiring the link table must reproduce the unlinked image exactly.");
    }

    /// <summary>
    /// Requires the covered columns of one row to be exactly the four quads:
    /// every centre covered, every gap and the margins clear.
    /// </summary>
    private static async Task AssertRegionsAreExactlyTheFourQuads(
        bool[] columns,
        string what)
    {
        foreach (int centre in Centres)
        {
            await Assert.That(columns[centre])
                .IsTrue()
                .Because(
                    $"The {what} frame drew nothing at column {centre}, which " +
                    "is the centre of a composed instance's quad.");
        }
        foreach (int gap in Gaps)
        {
            await Assert.That(columns[gap])
                .IsFalse()
                .Because(
                    $"The {what} frame drew into column {gap}, which lies " +
                    "between the composed instances and must stay cleared.");
        }

        // Four separate runs of covered columns, one per composed instance. A
        // frame that drew two instances at one transform has three.
        int runs = 0;
        bool inside = false;
        int covered = 0;
        foreach (bool column in columns)
        {
            if (column)
            {
                covered++;
                if (!inside)
                {
                    runs++;
                }
            }
            inside = column;
        }
        await Assert.That(runs)
            .IsEqualTo(4)
            .Because(
                $"The {what} frame covered {runs} separate regions; the four " +
                "composed instances occupy four disjoint spans.");
        await Assert.That(covered)
            .IsGreaterThan(24)
            .Because($"The {what} frame covered only {covered} columns.");
    }

    /// <summary>
    /// The columns of the sampled row that differ from the cleared background,
    /// which is read from the same frame rather than assumed.
    /// </summary>
    private static bool[] CoveredColumns(byte[] pixels)
    {
        var columns = new bool[(int)Size];
        int backgroundOffset = SampleY * (int)Size * 4;
        for (int x = 0; x < (int)Size; x++)
        {
            int offset = ((SampleY * (int)Size) + x) * 4;
            columns[x] =
                pixels[offset] != pixels[backgroundOffset] ||
                pixels[offset + 1] != pixels[backgroundOffset + 1] ||
                pixels[offset + 2] != pixels[backgroundOffset + 2] ||
                pixels[offset + 3] != pixels[backgroundOffset + 3];
        }
        return columns;
    }

    private static async Task AssertLit(
        byte[] pixels,
        int x,
        bool red,
        bool green,
        bool blue,
        string what)
    {
        int offset = checked((((SampleY * (int)Size) + x) * 4));
        int backgroundOffset = SampleY * (int)Size * 4;
        string evidence =
            $"The {what} at ({x},{SampleY}) was rgba({pixels[offset]}," +
            $"{pixels[offset + 1]},{pixels[offset + 2]},{pixels[offset + 3]}).";
        await Assert.That(pixels[offset] > 24).IsEqualTo(red).Because(evidence);
        await Assert.That(pixels[offset + 1] > 24).IsEqualTo(green).Because(evidence);
        await Assert.That(pixels[offset + 2] > 24).IsEqualTo(blue).Because(evidence);

        // The sample has to be on a quad rather than on the clear, whatever the
        // clear happens to be. Asserting an alpha threshold instead measured an
        // opaque background and was true everywhere.
        bool differs =
            pixels[offset] != pixels[backgroundOffset] ||
            pixels[offset + 1] != pixels[backgroundOffset + 1] ||
            pixels[offset + 2] != pixels[backgroundOffset + 2] ||
            pixels[offset + 3] != pixels[backgroundOffset + 3];
        await Assert.That(differs).IsTrue().Because(evidence);
    }

    /// <summary>
    /// Counts the texels the quads cover, whatever lit them, against the cleared
    /// corner of the same frame.
    /// </summary>
    private static int CoveredTexelCount(byte[] pixels)
    {
        int count = 0;
        for (int index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index] != pixels[0] ||
                pixels[index + 1] != pixels[1] ||
                pixels[index + 2] != pixels[2] ||
                pixels[index + 3] != pixels[3])
            {
                count++;
            }
        }
        return count;
    }

    private static byte[] ReadPixels(ISilkGraphicsTexture color)
    {
        var pixels = new byte[checked((int)(color.Width * color.Height * 4))];
        color.ReadbackForTesting(pixels);
        return pixels;
    }

    /// <summary>
    /// Builds the ABI v21 frame: a red distant light at index 0, a blue one at
    /// index 1, and one untextured dome carrying a green ambient term so all
    /// three masks are separately measurable in a separate channel.
    /// </summary>
    private static byte[] Frame()
    {
        const int frameSize = 2248;
        const int lightCountOffset = 536;
        const int lightTableOffset = 552;
        const int lightEntrySize = 176;
        const int ambientOffset = 536 + 16 + (8 * lightEntrySize);
        const int domeCountOffset = 1976;
        const int domeTableOffset = 1992;
        var bytes = new byte[frameSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), checked((int)Size));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), checked((int)Size));
        double[] identity = SilkMeshRendererConformance.Identity();
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(16 + (element * 8)),
                identity[element]);
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(144 + (element * 8)),
                identity[element]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(lightCountOffset), 2);

        float[][] colors = [[1, 0, 0], [0, 0, 1]];
        for (int light = 0; light < 2; light++)
        {
            int entry = lightTableOffset + (light * lightEntrySize);

            // OPENUSD_SILK_LIGHT_DISTANT. The frame light table carries the raw
            // ABI value; there is no managed enum for it.
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry), 1u);
            for (int component = 0; component < 3; component++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(
                    bytes.AsSpan(entry + 16 + (component * 4)),
                    colors[light][component]);
            }
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 28), 1f);
            for (int element = 0; element < 16; element++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(entry + 32 + (element * 8)),
                    identity[element]);
            }
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 164), 1f);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 168), 1f);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 172), 0.5f);
        }

        // The one untextured dome carries the whole ambient term, so summing the
        // published domes reproduces the scene-wide value exactly.
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(ambientOffset), 0f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(ambientOffset + 4), 0.6f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(ambientOffset + 8), 0f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(ambientOffset + 12), 1f);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(domeCountOffset), 1);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(domeTableOffset), 0f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(domeTableOffset + 4), 0.6f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(domeTableOffset + 8), 0f);

        // OPENUSD_SILK_DOME_FLAG_PRESENT without TEXTURED: an untextured dome
        // publishes no environment record and is entirely this ambient summand.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(domeTableOffset + 16), 1u);
        return bytes;
    }

    private static byte[] Quad(ulong id, string path, double x) =>
        SilkMeshRendererConformance.CreateMeshCommand(
            id,
            path,
            [
                -0.15f, -0.35f, 0.4f,
                 0.15f, -0.35f, 0.4f,
                 0.15f,  0.35f, 0.4f,
                -0.15f,  0.35f, 0.4f,
            ],
            [0, 2, 1, 0, 3, 2],
            x,
            0,
            [1, 1, 1, 1]);

    /// <summary>
    /// Builds a lightweight ABI v8 instance record that reuses the prototype's
    /// geometry and carries only its own composed identity and transform.
    /// </summary>
    private static byte[] InstanceReference(string path, int instanceIndex, double x)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        byte[] instancerPathBytes = Encoding.UTF8.GetBytes("/Instancer");
        int size = 268 + pathBytes.Length + instancerPathBytes.Length +
            8 + instancerPathBytes.Length;
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), ComputeStableHash(path));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);

        // A non-zero instancer id and a positive instance index with no geometry
        // is what makes this a reference to the payload record at index zero.
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), 11);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), instanceIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)pathBytes.Length);
        for (int component = 0; component < 4; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64 + (component * 4)), 1f);
        }
        double[] transform = SilkMeshRendererConformance.Identity();
        transform[12] = x;
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (element * 8)),
                transform[element]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(260),
            (uint)instancerPathBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(264), 1);
        pathBytes.CopyTo(bytes, 268);
        instancerPathBytes.CopyTo(bytes, 268 + pathBytes.Length);
        int contextOffset = 268 + pathBytes.Length + instancerPathBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(contextOffset),
            (uint)instancerPathBytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(contextOffset + 4),
            instanceIndex);
        instancerPathBytes.CopyTo(bytes, contextOffset + 8);
        return bytes;
    }

    private static byte[] LightLink(
        uint lightCount,
        uint domeCount,
        params (string Path, int InstanceIndex, uint LightMask, uint ShadowMask, uint DomeMask)[] entries)
    {
        List<byte> payload =
        [
            .. BitConverter.GetBytes((uint)entries.Length),
            .. BitConverter.GetBytes(lightCount),
            .. BitConverter.GetBytes((uint)SilkLightLinkUnsupportedFeatures.None),
            .. BitConverter.GetBytes(domeCount),
        ];
        foreach ((string path, int instanceIndex, uint lightMask, uint shadowMask, uint domeMask)
            in entries)
        {
            byte[] pathBytes = Encoding.UTF8.GetBytes(path);
            payload.AddRange(BitConverter.GetBytes(lightMask));
            payload.AddRange(BitConverter.GetBytes(shadowMask));
            payload.AddRange(BitConverter.GetBytes(domeMask));
            payload.AddRange(BitConverter.GetBytes(instanceIndex));
            payload.AddRange(BitConverter.GetBytes((uint)pathBytes.Length));
            payload.AddRange(pathBytes);
        }

        var bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.LightLink);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    private static ulong ComputeStableHash(string value)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte item in Encoding.UTF8.GetBytes(value))
        {
            hash ^= item;
            hash *= 1099511628211UL;
        }
        return hash;
    }
}
