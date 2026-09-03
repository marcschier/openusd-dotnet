// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Pins the ABI v19 shadow command's wire contract, its bounds, and the retained
/// table it produces.
/// </summary>
/// <remarks>
/// The descriptor table decides what a renderer allocates and what it renders
/// from light space, so a malformed one has to be rejected whole rather than
/// applied part way: a table that named an unpublished light, or that carried a
/// resolution outside the ABI bounds, would allocate a map from values no
/// producer promised and shadow the scene with them.
/// </remarks>
public sealed class SilkShadowWireTests
{
    private const int FixedSize = 24;
    private const int DescriptorSize = 288;

    [Test]
    public async Task ShadowRoundTripsEveryDescriptorFieldAtItsOwnOffset()
    {
        byte[] page = CreateShadow(
            lightCount: 3,
            descriptors:
            [
                new Descriptor(2, 0, 512, SilkShadowDescriptorOptions.Orthographic, 0.25f, 0.5f, 2f),
                new Descriptor(
                    0,
                    1,
                    2048,
                    SilkShadowDescriptorOptions.Orthographic |
                        SilkShadowDescriptorOptions.CasterLinked,
                    0.125f,
                    0.75f,
                    0f),
            ]);

        // The ref struct command cannot cross an await, so every field is read
        // into an ordinary record before a single assertion runs.
        (uint DescriptorCount, uint LightCount, SilkShadowUnsupportedFeatures Unsupported,
            SilkShadowDescriptor First, SilkShadowDescriptor Second) read = Read(page);

        await Assert.That(read.DescriptorCount).IsEqualTo(2u);
        await Assert.That(read.LightCount).IsEqualTo(3u);
        await Assert.That(read.Unsupported).IsEqualTo(SilkShadowUnsupportedFeatures.None);

        SilkShadowDescriptor first = read.First;
        await Assert.That(first.LightIndex).IsEqualTo(2u);
        await Assert.That(first.MapIndex).IsEqualTo(0u);
        await Assert.That(first.Resolution).IsEqualTo(512u);
        await Assert.That(first.Flags).IsEqualTo(SilkShadowDescriptorOptions.Orthographic);
        await Assert.That(first.DepthBias).IsEqualTo(0.25f);
        await Assert.That(first.NormalBias).IsEqualTo(0.5f);
        await Assert.That(first.PcfRadius).IsEqualTo(2f);

        // The matrices are written as ascending element values so a transposed,
        // swapped, or off-by-one read is visible rather than plausible.
        for (int element = 0; element < 16; element++)
        {
            await Assert.That(first.View[element]).IsEqualTo(element + 1.0);
            await Assert.That(first.Projection[element]).IsEqualTo(100.0 + element);
        }

        SilkShadowDescriptor second = read.Second;
        await Assert.That(second.LightIndex).IsEqualTo(0u);
        await Assert.That(second.MapIndex).IsEqualTo(1u);
        await Assert.That(second.Resolution).IsEqualTo(2048u);
        await Assert.That(second.Flags).IsEqualTo(
            SilkShadowDescriptorOptions.Orthographic |
            SilkShadowDescriptorOptions.CasterLinked);
        await Assert.That(second.PcfRadius).IsEqualTo(0f);
    }

    private static (uint DescriptorCount, uint LightCount,
        SilkShadowUnsupportedFeatures Unsupported,
        SilkShadowDescriptor First, SilkShadowDescriptor Second) Read(byte[] page)
    {
        using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(page, 1);
        if (!commands.MoveNext())
        {
            throw new InvalidOperationException("The page published no command.");
        }
        SilkShadowCommand command = commands.Current.AsShadow();
        return (
            command.DescriptorCount,
            command.LightCount,
            command.UnsupportedFeatures,
            command.GetDescriptor(0),
            command.GetDescriptor(1));
    }

    [Test]
    public async Task AnEmptyShadowTableRetiresEveryRetainedMap()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            CreateShadow(
                lightCount: 1,
                descriptors: [new Descriptor(0, 0, 1024, SilkShadowDescriptorOptions.Orthographic)]),
            1,
            1);
        await Assert.That(scene.Shadows.HasShadows).IsTrue();
        await Assert.That(scene.Shadows.Count).IsEqualTo(1);
        await Assert.That(scene.Shadows.ResolveSlot(0)).IsEqualTo(0);
        ulong revision = scene.Shadows.Revision;

        _ = scene.Apply(CreateShadow(lightCount: 0, descriptors: []), 1, 2);
        await Assert.That(scene.Shadows.HasShadows).IsFalse();
        await Assert.That(scene.Shadows.Count).IsEqualTo(0);
        await Assert.That(scene.Shadows.ResolveSlot(0)).IsEqualTo(-1);
        await Assert.That(scene.Shadows.Revision).IsGreaterThan(revision);
    }

    [Test]
    public async Task EveryLightResolvesToItsOwnMapSlot()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            CreateShadow(
                lightCount: 4,
                descriptors:
                [
                    new Descriptor(3, 0, 1024, SilkShadowDescriptorOptions.Orthographic),
                    new Descriptor(1, 1, 1024, SilkShadowDescriptorOptions.Orthographic),
                ]),
            1,
            1);

        await Assert.That(scene.Shadows.ResolveSlot(3)).IsEqualTo(0);
        await Assert.That(scene.Shadows.ResolveSlot(1)).IsEqualTo(1);
        await Assert.That(scene.Shadows.ResolveSlot(0)).IsEqualTo(-1);
        await Assert.That(scene.Shadows.ResolveSlot(2)).IsEqualTo(-1);

        // A light index outside the fixed frame table resolves to no map rather
        // than reading past the slot array.
        await Assert.That(scene.Shadows.ResolveSlot(-1)).IsEqualTo(-1);
        await Assert.That(scene.Shadows.ResolveSlot(64)).IsEqualTo(-1);
    }

    [Test]
    public async Task ADescriptorNamingAnUnpublishedLightIsRejected()
    {
        byte[] page = CreateShadow(
            lightCount: 1,
            descriptors: [new Descriptor(1, 0, 1024, SilkShadowDescriptorOptions.Orthographic)]);
        await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task DescriptorMapIndicesMustAscendFromZero()
    {
        byte[] page = CreateShadow(
            lightCount: 2,
            descriptors:
            [
                new Descriptor(0, 1, 1024, SilkShadowDescriptorOptions.Orthographic),
                new Descriptor(1, 0, 1024, SilkShadowDescriptorOptions.Orthographic),
            ]);
        await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task AResolutionOutsideTheAbiBoundsIsRejected()
    {
        foreach (uint resolution in (uint[])[128, 4096, 1000, 0])
        {
            byte[] page = CreateShadow(
                lightCount: 1,
                descriptors:
                [
                    new Descriptor(0, 0, resolution, SilkShadowDescriptorOptions.Orthographic),
                ]);
            await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
                .Throws<InvalidDataException>();
        }
    }

    [Test]
    public async Task ATableOverTheMapBudgetIsRejected()
    {
        var descriptors = new Descriptor[SilkShadowCommand.MaximumMaps + 1];
        for (uint index = 0; index < descriptors.Length; index++)
        {
            descriptors[index] = new Descriptor(
                0,
                index,
                1024,
                SilkShadowDescriptorOptions.Orthographic);
        }

        byte[] page = CreateShadow(lightCount: 1, descriptors: descriptors);
        await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task ATruncatedOrOverlongDescriptorTableIsRejected()
    {
        byte[] page = CreateShadow(
            lightCount: 1,
            descriptors: [new Descriptor(0, 0, 1024, SilkShadowDescriptorOptions.Orthographic)]);

        byte[] truncated = page[..^1];
        BinaryPrimitives.WriteUInt32LittleEndian(truncated.AsSpan(4), (uint)truncated.Length);
        await Assert.That(() => new SilkSceneState().Apply(truncated, 1, 1))
            .Throws<InvalidDataException>();

        byte[] padded = [.. page, 0];
        BinaryPrimitives.WriteUInt32LittleEndian(padded.AsSpan(4), (uint)padded.Length);
        await Assert.That(() => new SilkSceneState().Apply(padded, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task AnUnknownFlagOrUnsupportedBitIsRejected()
    {
        byte[] flagged = CreateShadow(
            lightCount: 1,
            descriptors: [new Descriptor(0, 0, 1024, (SilkShadowDescriptorOptions)0x8u)]);
        await Assert.That(() => new SilkSceneState().Apply(flagged, 1, 1))
            .Throws<InvalidDataException>();

        byte[] unsupported = CreateShadow(
            lightCount: 1,
            descriptors: [new Descriptor(0, 0, 1024, SilkShadowDescriptorOptions.Orthographic)]);
        BinaryPrimitives.WriteUInt32LittleEndian(unsupported.AsSpan(16), 0xFFu);
        await Assert.That(() => new SilkSceneState().Apply(unsupported, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task ANonFiniteMatrixOrNegativeBiasIsRejected()
    {
        byte[] nonFinite = CreateShadow(
            lightCount: 1,
            descriptors: [new Descriptor(0, 0, 1024, SilkShadowDescriptorOptions.Orthographic)]);
        BinaryPrimitives.WriteDoubleLittleEndian(
            nonFinite.AsSpan(FixedSize + 16),
            double.NaN);
        await Assert.That(() => new SilkSceneState().Apply(nonFinite, 1, 1))
            .Throws<InvalidDataException>();

        byte[] negativeBias = CreateShadow(
            lightCount: 1,
            descriptors: [new Descriptor(0, 0, 1024, SilkShadowDescriptorOptions.Orthographic)]);
        BinaryPrimitives.WriteSingleLittleEndian(
            negativeBias.AsSpan(FixedSize + 272),
            -0.001f);
        await Assert.That(() => new SilkSceneState().Apply(negativeBias, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task AReservedDescriptorFieldMustBeZero()
    {
        byte[] page = CreateShadow(
            lightCount: 1,
            descriptors: [new Descriptor(0, 0, 1024, SilkShadowDescriptorOptions.Orthographic)]);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(FixedSize + 284), 1u);
        await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task ANonZeroHeaderReservedFieldIsRejected()
    {
        // Bytes 20..23 of the command header are reserved and always published as
        // zero. Accepting a non-zero value would silently consume a page written
        // by a producer that put meaning there, which is exactly how a reserved
        // field stops being reserved by accident.
        foreach (uint reserved in (uint[])[1, 0x8000_0000u, uint.MaxValue])
        {
            byte[] page = CreateShadow(
                lightCount: 1,
                descriptors:
                [
                    new Descriptor(0, 0, 1024, SilkShadowDescriptorOptions.Orthographic),
                ]);
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(20), reserved);
            await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
                .Throws<InvalidDataException>();
        }

        // The negative control: the same page with the field left at zero applies.
        byte[] valid = CreateShadow(
            lightCount: 1,
            descriptors: [new Descriptor(0, 0, 1024, SilkShadowDescriptorOptions.Orthographic)]);
        var scene = new SilkSceneState();
        _ = scene.Apply(valid, 1, 1);
        await Assert.That(scene.Shadows.HasShadows).IsTrue();
    }

    [Test]
    public async Task AnEmptyShadowTableStillRejectsANonZeroHeaderReservedField()
    {
        // The empty table is the retirement path and carries no descriptor to
        // validate, so its header is the only thing left to check.
        byte[] page = CreateShadow(lightCount: 0, descriptors: []);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(20), 7u);
        await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task AnOpacityMaskedMaterialDisqualifiesACaster()
    {
        // The depth-only caster program binds no material and cannot discard, so
        // any material whose visible coverage differs from its geometry is
        // disqualified rather than allowed to cast a solid shadow. All three
        // authored forms count: an alpha-tested cutout, a blended surface, and an
        // opacity map.
        await Assert.That(IsMasked(null)).IsFalse();
        await Assert.That(IsMasked([(SilkMaterialParameter.Opacity, 1f)])).IsFalse();
        await Assert.That(IsMasked([(SilkMaterialParameter.Roughness, 0.4f)])).IsFalse();
        await Assert.That(IsMasked([(SilkMaterialParameter.OpacityThreshold, 0.5f)])).IsTrue();
        await Assert.That(IsMasked([(SilkMaterialParameter.Opacity, 0.5f)])).IsTrue();
        await Assert.That(
                IsMasked(
                [
                    (SilkMaterialParameter.Opacity, 1f),
                    (SilkMaterialParameter.OpacityThreshold, 0.25f),
                ]))
            .IsTrue();
    }

    [Test]
    public async Task AMaterialEditMovesOnlyTheMaterialRevision()
    {
        // Caster selection reads the material, so the retained shadow map has to
        // be keyed on material state as well as geometry. A material can be
        // re-authored in place -- turning masked or opaque -- without any mesh
        // command, so nothing else in the scene moves for that edit.
        const string materialPath = "/World/Materials/Caster";
        var scene = new SilkSceneState();
        byte[] opaque = CreateMaterial(materialPath, [(SilkMaterialParameter.Roughness, 0.4f)]);
        byte[] masked = CreateMaterial(
            materialPath,
            [(SilkMaterialParameter.OpacityThreshold, 0.5f)]);

        _ = scene.Apply(opaque, 1, 1);
        ulong afterOpaque = scene.MaterialRevision;
        ulong geometry = scene.GeometryRevision;
        await Assert.That(afterOpaque).IsGreaterThan(0ul);

        _ = scene.Apply(masked, 1, 2);
        await Assert.That(scene.MaterialRevision).IsGreaterThan(afterOpaque);
        await Assert.That(scene.GeometryRevision).IsEqualTo(geometry);

        ulong afterMasked = scene.MaterialRevision;
        _ = scene.Apply(opaque, 1, 3);
        await Assert.That(scene.MaterialRevision).IsGreaterThan(afterMasked);
        await Assert.That(scene.GeometryRevision).IsEqualTo(geometry);
    }

    [Test]
    public async Task OnlyARemovalThatRemovedSomethingMovesTheMaterialRevision()
    {
        // A removal for a material that was never published leaves caster
        // selection exactly as it was, so it must not invalidate a retained map.
        const string materialPath = "/World/Materials/Caster";
        var scene = new SilkSceneState();
        _ = scene.Apply(
            CreateMaterial(materialPath, [(SilkMaterialParameter.Roughness, 0.4f)]),
            1,
            1);
        ulong published = scene.MaterialRevision;

        _ = scene.Apply(CreateMaterialRemoval("/World/Materials/Absent"), 1, 2);
        await Assert.That(scene.MaterialRevision)
            .IsEqualTo(published)
            .Because("Removing a material that was never published changes no caster.");

        _ = scene.Apply(CreateMaterialRemoval(materialPath), 1, 3);
        await Assert.That(scene.MaterialRevision)
            .IsGreaterThan(published)
            .Because("Removing a bound material can change which prims cast shadows.");
    }

    private static byte[] CreateMaterialRemoval(string path)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        var bytes = new byte[20 + pathBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MaterialRemove);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), ComputeStableHash(path));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)pathBytes.Length);
        pathBytes.CopyTo(bytes.AsSpan(20));
        return bytes;
    }

    /// <summary>
    /// Applies a mesh, optionally bound to a material with the given scalar
    /// inputs, and reports whether the shadow pass would disqualify it.
    /// </summary>
    private static bool IsMasked((SilkMaterialParameter Parameter, float Value)[]? scalars)
    {
        const string meshPath = "/World/Caster";
        const string materialPath = "/World/Materials/Caster";
        var scene = new SilkSceneState();
        List<byte[]> page = [];
        if (scalars is not null)
        {
            page.Add(CreateMaterial(materialPath, scalars));
        }
        page.Add(CreateMesh(meshPath, scalars is null ? string.Empty : materialPath));

        var bytes = new List<byte>();
        foreach (byte[] command in page)
        {
            bytes.AddRange(command);
        }
        _ = scene.Apply(bytes.ToArray(), (uint)page.Count, 1);
        SilkMeshData mesh = scene.MeshesByPath[(meshPath, 0)];
        return SilkShadowMapCache.IsOpacityMasked(scene, mesh);
    }

    private static byte[] CreateMaterial(
        string path,
        (SilkMaterialParameter Parameter, float Value)[] scalars)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        List<byte> payload =
        [
            .. BitConverter.GetBytes(ComputeStableHash(path)),
            .. BitConverter.GetBytes((uint)pathBytes.Length),
            .. BitConverter.GetBytes((uint)SilkSurfaceKind.PreviewSurface),
            .. BitConverter.GetBytes((uint)scalars.Length),
            .. BitConverter.GetBytes(0u),
            .. pathBytes,
        ];
        foreach ((SilkMaterialParameter parameter, float value) in scalars)
        {
            payload.AddRange(BitConverter.GetBytes((uint)parameter));
            payload.AddRange(BitConverter.GetBytes(1u));
            payload.AddRange(BitConverter.GetBytes(value));
        }

        // Empty generated SPIR-V and MSL payloads, then the identity folded
        // texture-coordinate transform.
        payload.AddRange(BitConverter.GetBytes(0u));
        payload.AddRange(BitConverter.GetBytes(0u));
        foreach (float element in (float[])[1, 0, 0, 1, 0, 0])
        {
            payload.AddRange(BitConverter.GetBytes(element));
        }

        var bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MaterialUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    private static byte[] CreateMesh(string path, string materialPath)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        byte[] materialBytes = Encoding.UTF8.GetBytes(materialPath);
        float[] points = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        uint[] indices = [0, 1, 2];
        int size = 268 +
            pathBytes.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            sizeof(uint) +
            materialBytes.Length;
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), ComputeStableHash(path));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)pathBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 1);
        for (int component = 0; component < 4; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(64 + (component * 4)),
                1f);
        }
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (element * 8)),
                element % 5 == 0 ? 1 : 0);
        }
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(208),
            materialBytes.Length == 0 ? 0 : ComputeStableHash(materialPath));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(216),
            (uint)materialBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(220), 0);
        pathBytes.CopyTo(bytes, 268);
        int offset = 268 + pathBytes.Length;
        foreach (float value in points)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
            offset += sizeof(float);
        }
        foreach (uint index in indices)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), index);
            offset += sizeof(uint);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), 0);
        offset += sizeof(uint);
        materialBytes.CopyTo(bytes, offset);
        return bytes;
    }

    private static ulong ComputeStableHash(string value)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offsetBasis;
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= prime;
        }
        return hash;
    }

    [Test]
    public async Task TheManagedBudgetsMatchTheNativeOnes()
    {
        string header = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "native",
            "hdSilk",
            "include",
            "openusd_hdsilk.h"));

        await Assert.That(ReadDefine(header, "OPENUSD_SILK_MAX_SHADOW_MAPS"))
            .IsEqualTo(SilkShadowCommand.MaximumMaps);
        await Assert.That(ReadDefine(header, "OPENUSD_SILK_MIN_SHADOW_MAP_RESOLUTION"))
            .IsEqualTo(SilkShadowCommand.MinimumResolution);
        await Assert.That(ReadDefine(header, "OPENUSD_SILK_MAX_SHADOW_MAP_RESOLUTION"))
            .IsEqualTo(SilkShadowCommand.MaximumResolution);
        await Assert.That(ReadDefine(header, "OPENUSD_SILK_COMMAND_SHADOW"))
            .IsEqualTo((uint)SilkCommandType.Shadow);

        // The four unsupported bits and the two descriptor flags are named on both
        // sides; a managed enum that drifted would silently accept a page bit no
        // producer sets, or reject one it does.
        await Assert.That(ReadDefine(header, "OPENUSD_SILK_SHADOW_UNSUPPORTED_LIGHT_TYPE"))
            .IsEqualTo((uint)SilkShadowUnsupportedFeatures.LightType);
        await Assert.That(ReadDefine(header, "OPENUSD_SILK_SHADOW_UNSUPPORTED_MAP_BUDGET"))
            .IsEqualTo((uint)SilkShadowUnsupportedFeatures.MapBudget);
        await Assert.That(ReadDefine(header, "OPENUSD_SILK_SHADOW_UNSUPPORTED_NO_CASTERS"))
            .IsEqualTo((uint)SilkShadowUnsupportedFeatures.NoCasters);
        await Assert.That(ReadDefine(header, "OPENUSD_SILK_SHADOW_FLAG_ORTHOGRAPHIC"))
            .IsEqualTo((uint)SilkShadowDescriptorOptions.Orthographic);
        await Assert.That(ReadDefine(header, "OPENUSD_SILK_SHADOW_FLAG_CASTER_LINKED"))
            .IsEqualTo((uint)SilkShadowDescriptorOptions.CasterLinked);
    }

    private static uint ReadDefine(string header, string name)
    {
        Match match = Regex.Match(
            header,
            $@"#define\s+{Regex.Escape(name)}\s+(\d+)u",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        return match.Success
            ? uint.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
            : throw new InvalidOperationException($"{name} is not defined in the header.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ??
            throw new InvalidOperationException("Could not locate repository root.");
    }

    private readonly record struct Descriptor(
        uint LightIndex,
        uint MapIndex,
        uint Resolution,
        SilkShadowDescriptorOptions Flags,
        float DepthBias = 0f,
        float NormalBias = 0f,
        float PcfRadius = 0f);

    private static byte[] CreateShadow(uint lightCount, Descriptor[] descriptors)
    {
        var bytes = new byte[FixedSize + (descriptors.Length * DescriptorSize)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Shadow);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)descriptors.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), lightCount);
        for (int index = 0; index < descriptors.Length; index++)
        {
            Descriptor descriptor = descriptors[index];
            int entry = FixedSize + (index * DescriptorSize);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry), descriptor.LightIndex);
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(entry + 4),
                descriptor.MapIndex);
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(entry + 8),
                descriptor.Resolution);
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(entry + 12),
                (uint)descriptor.Flags);
            for (int element = 0; element < 16; element++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(entry + 16 + (element * 8)),
                    element + 1.0);
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(entry + 144 + (element * 8)),
                    100.0 + element);
            }
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(entry + 272),
                descriptor.DepthBias);
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(entry + 276),
                descriptor.NormalBias);
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(entry + 280),
                descriptor.PcfRadius);
        }
        return bytes;
    }
}
