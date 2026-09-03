// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerDebugModelsTests
{
    [Test]
    public async Task PickModeResolverClimbsModelsInstancesAndPrototypesIndependently()
    {
        var existing = new HashSet<string>(StringComparer.Ordinal)
        {
            "/World",
            "/World/Model",
            "/World/Model/Mesh",
            "/World/Instance",
            "/__Prototype_1"
        };

        string model = ViewerPickModeResolver.ResolvePath(
            "/World/Model/Mesh",
            ViewerPickMode.Models,
            existing.Contains,
            path => path == "/World/Model",
            path => path == "/World/Instance",
            path => path == "/__Prototype_1",
            _ => string.Empty);
        string instance = ViewerPickModeResolver.ResolvePath(
            "/World/Instance",
            ViewerPickMode.Instances,
            existing.Contains,
            path => path == "/World/Model",
            path => path == "/World/Instance",
            path => path == "/__Prototype_1",
            _ => string.Empty);
        string prototype = ViewerPickModeResolver.ResolvePath(
            "/World/Instance",
            ViewerPickMode.Prototypes,
            existing.Contains,
            path => path == "/World/Model",
            path => path == "/World/Instance",
            path => path == "/__Prototype_1",
            _ => "/__Prototype_1");

        await Assert.That(model).IsEqualTo("/World/Model");
        await Assert.That(instance).IsEqualTo("/World/Instance");
        await Assert.That(prototype).IsEqualTo("/__Prototype_1");
    }

    [Test]
    public async Task HydraSceneBrowserReportsCommandPageContentsAndChanges()
    {
        byte[] firstPage = BuildPage(
            CreateMeshCommand("/World/Cube", primId: 7, pointCount: 4, primitiveCount: 2, "/World/Looks/Blue"));
        byte[] secondPage = BuildPage(
            CreateMeshCommand("/World/Cube", primId: 7, pointCount: 4, primitiveCount: 2, "/World/Looks/Blue"),
            CreateMeshCommand("/World/Sphere", primId: 8, pointCount: 9, primitiveCount: 6, "/World/Looks/Red"));

        ViewerHydraSceneSnapshot first = ViewerHydraSceneSnapshot.FromCommands(
            firstPage,
            commandCount: 1,
            pageRevision: 11);
        ViewerHydraSceneSnapshot second = ViewerHydraSceneSnapshot.FromCommands(
            secondPage,
            commandCount: 2,
            pageRevision: 12);

        string firstBrowser = first.Format();
        string secondBrowser = second.Format();

        await Assert.That(firstBrowser).Contains("Hydra scene revision 11; commands 1; mesh records 1");
        await Assert.That(firstBrowser).Contains("Mesh: /World/Cube primId=7");
        await Assert.That(firstBrowser).Contains("topology=TriangleList points=4 indices=6 primitives=2");
        await Assert.That(firstBrowser).Contains("material=/World/Looks/Blue");
        await Assert.That(firstBrowser).DoesNotContain("/World/Sphere");
        await Assert.That(secondBrowser).Contains("Hydra scene revision 12; commands 2; mesh records 2");
        await Assert.That(secondBrowser).Contains("Mesh: /World/Sphere primId=8");
        await Assert.That(secondBrowser).Contains("topology=TriangleList points=9 indices=18 primitives=6");
    }

    [Test]
    public async Task BitmapWriterEmitsBottomUpTwentyFourBitBmpWithPaddedRows()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "viewer-debug-bmp-tests",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "capture.bmp");
        try
        {
            byte[] rgba =
            [
                255, 0, 0, 255,      0, 255, 0, 255,      0, 0, 255, 255,
                0, 255, 255, 255,    255, 0, 255, 255,    255, 255, 0, 255
            ];

            ViewerFrameBitmapWriter.WriteBmp(path, width: 3, height: 2, rgba);
            byte[] bmp = File.ReadAllBytes(path);

            await Assert.That(Encoding.ASCII.GetString(bmp, 0, 2)).IsEqualTo("BM");
            await Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(2, 4))).IsEqualTo(78);
            await Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(10, 4))).IsEqualTo(54);
            await Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(18, 4))).IsEqualTo(3);
            await Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(22, 4))).IsEqualTo(2);
            await Assert.That(BinaryPrimitives.ReadInt16LittleEndian(bmp.AsSpan(28, 2))).IsEqualTo((short)24);
            await Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(34, 4))).IsEqualTo(24);

            byte[] expectedPixels =
            [
                255, 255, 0,      255, 0, 255,      0, 255, 255,      0, 0, 0,
                0, 0, 255,        0, 255, 0,        255, 0, 0,        0, 0, 0
            ];
            await Assert.That(bmp.Skip(54).SequenceEqual(expectedPixels)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static byte[] BuildPage(params byte[][] commands)
    {
        byte[] page = new byte[commands.Sum(command => command.Length)];
        int offset = 0;
        foreach (byte[] command in commands)
        {
            command.CopyTo(page, offset);
            offset += command.Length;
        }
        return page;
    }

    private static byte[] CreateMeshCommand(
        string pathValue,
        int primId,
        int pointCount,
        int primitiveCount,
        string materialPath)
    {
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        byte[] material = Encoding.UTF8.GetBytes(materialPath);
        int indexCount = checked(primitiveCount * 3);
        int size = 268 +
            path.Length +
            (pointCount * 12) +
            (indexCount * sizeof(uint)) +
            (primitiveCount * sizeof(uint)) +
            material.Length;
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8, 8), 0xA11CE);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), primId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28, 4), (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32, 8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44, 4), (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48, 4), (uint)path.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52, 4), (uint)pointCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56, 4), (uint)indexCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60, 4), (uint)primitiveCount);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(76, 4), 1);
        for (int index = 0; index < 16; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (index * sizeof(double)), sizeof(double)),
                index % 5 == 0 ? 1 : 0);
        }
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(208, 8), 0xB10CE);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(216, 4), (uint)material.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(220, 4), 0);
        path.CopyTo(bytes, 268);
        int pointsOffset = 268 + path.Length;
        for (int point = 0; point < pointCount * 3; point++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(pointsOffset + (point * sizeof(float)), sizeof(float)),
                point);
        }
        int indicesOffset = pointsOffset + (pointCount * 12);
        for (int index = 0; index < indexCount; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(indicesOffset + (index * sizeof(uint)), sizeof(uint)),
                (uint)(index % pointCount));
        }
        int subprimsOffset = indicesOffset + (indexCount * sizeof(uint));
        for (int primitive = 0; primitive < primitiveCount; primitive++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(subprimsOffset + (primitive * sizeof(uint)), sizeof(uint)),
                (uint)primitive);
        }
        material.CopyTo(bytes, subprimsOffset + (primitiveCount * sizeof(uint)));
        return bytes;
    }
}
