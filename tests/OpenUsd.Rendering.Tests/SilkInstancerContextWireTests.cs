// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Gates the ABI v23 ordered instancer context end to end: decode, validation,
/// retention through updates and retirement, and the resolved pick identity.
/// </summary>
/// <remarks>
/// Nested instancing has no single "the" instancer. A prototype instanced by an
/// inner instancer that is itself instanced by an outer one has one index per
/// level, and the record's own instance index is an hdSilk composite that counts
/// in a private space. Reporting that composite beside the innermost instancer
/// path describes an instance that does not exist, which is why the chain is
/// carried in full and why every level of it has to survive the wire in order.
/// </remarks>
public sealed class SilkInstancerContextWireTests
{
    private const string LeafPath = "/World/Protos/Leaf";
    private const string OuterPath = "/World/Outer";
    private const string InnerPath = "/World/Outer/Inner";

    [Test]
    public async Task ATwoLevelChainSurvivesTheWireInOrder()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            MeshUpsert(
                LeafPath,
                primId: 3,
                instanceIndex: 17,
                carriesGeometry: true,
                context: [(OuterPath, 2), (InnerPath, 5)]),
            1,
            1);

        SilkMeshData mesh = scene.MeshesByPath[(LeafPath, 17)];

        // The composite ordinal keys the retained table, and the chain is the
        // only description that names a scene instance.
        await Assert.That(mesh.InstanceIndex).IsEqualTo(17);
        await Assert.That(mesh.InstancerPath).IsEqualTo(InnerPath);
        await Assert.That(mesh.InstancerContext.Count).IsEqualTo(2);
        await Assert.That(mesh.InstancerContext[0].InstancerPath).IsEqualTo(OuterPath);
        await Assert.That(mesh.InstancerContext[0].InstanceIndex).IsEqualTo(2);
        await Assert.That(mesh.InstancerContext[1].InstancerPath).IsEqualTo(InnerPath);
        await Assert.That(mesh.InstancerContext[1].InstanceIndex).IsEqualTo(5);
    }

    [Test]
    public async Task AThreeLevelChainKeepsEveryLevelOutermostFirst()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            MeshUpsert(
                LeafPath,
                primId: 4,
                instanceIndex: 41,
                carriesGeometry: true,
                context: [("/A", 1), ("/A/B", 0), ("/A/B/C", 3)]),
            1,
            1);

        SilkMeshData mesh = scene.MeshesByPath[(LeafPath, 41)];

        await Assert.That(mesh.InstancerContext.Count).IsEqualTo(3);
        await Assert.That(mesh.InstancerContext[0].InstancerPath).IsEqualTo("/A");
        await Assert.That(mesh.InstancerContext[1].InstancerPath).IsEqualTo("/A/B");
        await Assert.That(mesh.InstancerContext[2].InstancerPath).IsEqualTo("/A/B/C");
        await Assert.That(mesh.InstancerContext[2].InstanceIndex).IsEqualTo(3);
        await Assert.That(mesh.InstancerPath).IsEqualTo("/A/B/C");
    }

    [Test]
    public async Task ANonInstancedRecordPublishesNoChain()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            MeshUpsert(
                LeafPath,
                primId: 5,
                instanceIndex: 0,
                carriesGeometry: true,
                context: []),
            1,
            1);

        SilkMeshData mesh = scene.MeshesByPath[(LeafPath, 0)];
        await Assert.That(mesh.InstancerPath).IsEqualTo(string.Empty);
        await Assert.That(mesh.InstancerContext.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TwoPrototypesUnderOneNestedChainKeepDistinctIdentities()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            Page(
                MeshUpsert(
                    "/World/Protos/Alpha",
                    primId: 11,
                    instanceIndex: 0,
                    carriesGeometry: true,
                    context: [(OuterPath, 0), (InnerPath, 0)]),
                MeshUpsert(
                    "/World/Protos/Alpha",
                    primId: 11,
                    instanceIndex: 2,
                    carriesGeometry: false,
                    context: [(OuterPath, 1), (InnerPath, 0)]),
                MeshUpsert(
                    "/World/Protos/Beta",
                    primId: 12,
                    instanceIndex: 1,
                    carriesGeometry: true,
                    context: [(OuterPath, 0), (InnerPath, 1)])),
            3,
            1);

        // The two prototypes share both instancing levels, so identity is only
        // unique because the path participates in the key and because each
        // record carries its own chain.
        await Assert.That(
                scene.MeshesByPath[("/World/Protos/Alpha", 2)]
                    .InstancerContext[0].InstanceIndex)
            .IsEqualTo(1);
        await Assert.That(
                scene.MeshesByPath[("/World/Protos/Beta", 1)]
                    .InstancerContext[1].InstanceIndex)
            .IsEqualTo(1);

        // A lightweight instance reference reuses the prototype's geometry and
        // keeps its own chain, which is the one thing about it that is not the
        // prototype's.
        SilkMeshData reference = scene.MeshesByPath[("/World/Protos/Alpha", 2)];
        await Assert.That(reference.Points.Length).IsEqualTo(9);
        await Assert.That(reference.InstancerContext.Count).IsEqualTo(2);
    }

    [Test]
    public async Task AnUpdatedChainReplacesTheRetainedOneAndRetirementDropsIt()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            MeshUpsert(
                LeafPath,
                primId: 7,
                instanceIndex: 4,
                carriesGeometry: true,
                context: [(OuterPath, 0), (InnerPath, 4)]),
            1,
            1);
        await Assert.That(
                scene.MeshesByPath[(LeafPath, 4)].InstancerContext[0].InstanceIndex)
            .IsEqualTo(0);

        // The outer instance moved. The retained chain must follow it rather
        // than keeping a level the scene no longer publishes.
        _ = scene.Apply(
            MeshUpsert(
                LeafPath,
                primId: 7,
                instanceIndex: 4,
                carriesGeometry: true,
                context: [(OuterPath, 3), (InnerPath, 4)]),
            1,
            2);
        await Assert.That(
                scene.MeshesByPath[(LeafPath, 4)].InstancerContext[0].InstanceIndex)
            .IsEqualTo(3);

        _ = scene.Apply(Removal(LeafPath, 4), 1, 3);
        await Assert.That(scene.MeshesByPath.ContainsKey((LeafPath, 4))).IsFalse();
    }

    [Test]
    public async Task AMalformedOrOverBudgetChainIsRefusedBeforeAnythingIsRetained()
    {
        // A chain with no instancer, an instancer with no chain, a chain that
        // ends somewhere other than the instancer the record names, a negative
        // level index, and a chain past the ABI level budget are all refused,
        // and none of them leaves a retained record behind.
        await Refuses(MeshUpsert(
            LeafPath,
            primId: 8,
            instanceIndex: 0,
            carriesGeometry: true,
            context: [(InnerPath, 0)],
            instancerPathOverride: string.Empty));
        await Refuses(MeshUpsert(
            LeafPath,
            primId: 8,
            instanceIndex: 1,
            carriesGeometry: true,
            context: [],
            instancerPathOverride: InnerPath));
        await Refuses(MeshUpsert(
            LeafPath,
            primId: 8,
            instanceIndex: 1,
            carriesGeometry: true,
            context: [(OuterPath, 0)],
            instancerPathOverride: InnerPath));
        await Refuses(MeshUpsert(
            LeafPath,
            primId: 8,
            instanceIndex: 1,
            carriesGeometry: true,
            context: [(InnerPath, -1)]));

        (string, int)[] overBudget = new (string, int)[65];
        for (int level = 0; level < overBudget.Length; level++)
        {
            overBudget[level] = (InnerPath, level);
        }
        await Refuses(MeshUpsert(
            LeafPath,
            primId: 8,
            instanceIndex: 1,
            carriesGeometry: true,
            context: overBudget));
    }

    [Test]
    public async Task AChainExactlyAtTheLevelBudgetIsAdmitted()
    {
        (string, int)[] atBudget = new (string, int)[64];
        for (int level = 0; level < atBudget.Length; level++)
        {
            atBudget[level] = (InnerPath, level);
        }

        var scene = new SilkSceneState();
        _ = scene.Apply(
            MeshUpsert(
                LeafPath,
                primId: 9,
                instanceIndex: 1,
                carriesGeometry: true,
                context: atBudget),
            1,
            1);

        await Assert.That(scene.MeshesByPath[(LeafPath, 1)].InstancerContext.Count)
            .IsEqualTo(64);
    }

    private static async Task Refuses(byte[] command)
    {
        var scene = new SilkSceneState();
        await Assert.That(() => scene.Apply(command, 1, 1))
            .Throws<InvalidDataException>();
        await Assert.That(scene.MeshesByPath.Count).IsEqualTo(0);
    }

    private static byte[] Page(params byte[][] commands)
    {
        int size = 0;
        foreach (byte[] command in commands)
        {
            size += command.Length;
        }
        var page = new byte[size];
        int cursor = 0;
        foreach (byte[] command in commands)
        {
            command.CopyTo(page, cursor);
            cursor += command.Length;
        }
        return page;
    }

    private static byte[] Removal(string path, int instanceIndex)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        var bytes = new byte[24 + pathBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshRemove);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(path));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), instanceIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(20),
            (uint)pathBytes.Length);
        pathBytes.CopyTo(bytes, 24);
        return bytes;
    }

    private static byte[] MeshUpsert(
        string path,
        int primId,
        int instanceIndex,
        bool carriesGeometry,
        (string Path, int Index)[] context,
        string? instancerPathOverride = null)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        string instancerPathValue = instancerPathOverride ??
            (context.Length == 0 ? string.Empty : context[^1].Path);
        byte[] instancerPathBytes = Encoding.UTF8.GetBytes(instancerPathValue);
        byte[][] contextPaths =
            [.. context.Select(entry => Encoding.UTF8.GetBytes(entry.Path))];
        float[] points = carriesGeometry
            ? [0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f]
            : [];
        uint[] indices = carriesGeometry ? [0u, 1u, 2u] : [];
        uint[] subprims = carriesGeometry ? [0u] : [];

        int chainBytes = 0;
        foreach (byte[] entry in contextPaths)
        {
            chainBytes += 8 + entry.Length;
        }
        int size = 268 +
            pathBytes.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            (subprims.Length * sizeof(uint)) +
            instancerPathBytes.Length +
            chainBytes;
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(path));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), primId);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(20),
            instancerPathBytes.Length == 0 ? 0 : 735836358);
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
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(52),
            (uint)(points.Length / 3));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), (uint)indices.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), (uint)subprims.Length);
        for (int component = 0; component < 4; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64 + (component * 4)), 1);
        }
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (element * 8)),
                element % 5 == 0 ? 1 : 0);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(260),
            (uint)instancerPathBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(264),
            (uint)context.Length);

        int cursor = 268;
        pathBytes.CopyTo(bytes.AsSpan(cursor));
        cursor += pathBytes.Length;
        foreach (float value in points)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor), value);
            cursor += sizeof(float);
        }
        foreach (uint value in indices)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), value);
            cursor += sizeof(uint);
        }
        foreach (uint value in subprims)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), value);
            cursor += sizeof(uint);
        }
        instancerPathBytes.CopyTo(bytes.AsSpan(cursor));
        cursor += instancerPathBytes.Length;
        for (int level = 0; level < context.Length; level++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(cursor),
                (uint)contextPaths[level].Length);
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(cursor + 4),
                context[level].Index);
            contextPaths[level].CopyTo(bytes.AsSpan(cursor + 8));
            cursor += 8 + contextPaths[level].Length;
        }
        return bytes;
    }
}
