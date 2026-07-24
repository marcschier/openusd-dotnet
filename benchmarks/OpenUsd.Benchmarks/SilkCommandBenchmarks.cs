// Copyright (c) marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Benchmarks;

[MemoryDiagnoser]
public class SilkCommandBenchmarks
{
    private byte[] _page = [];
    private SilkSceneState _scene = new();

    [Params(8, 128)]
    public int TriangleCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _page = SilkBenchmarkData.Concat(
            SilkBenchmarkData.CreateFrameCommand(),
            SilkBenchmarkData.CreateMeshCommand(
                "/World/BenchmarkMesh",
                primId: 42,
                TriangleCount));
        _scene = new SilkSceneState();
        _ = _scene.Apply(_page, commandCount: 2, revision: 1);
    }

    [Benchmark]
    [BenchmarkCategory("Smoke")]
    public long ParseCommandPage()
    {
        long checksum = 0;
        using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(_page, 2);
        while (commands.MoveNext())
        {
            switch (commands.Current.Type)
            {
                case SilkCommandType.Frame:
                    SilkFrameCommand frame = commands.Current.AsFrame();
                    checksum += frame.Width + frame.Height;
                    checksum += (long)frame.GetProjectionElement(15);
                    break;
                case SilkCommandType.MeshUpsert:
                    SilkMeshUpsertCommand mesh = commands.Current.AsMeshUpsert();
                    checksum += mesh.Path.Length + mesh.PointCount + mesh.IndexCount;
                    checksum += mesh.GetIndex(mesh.IndexCount - 1);
                    checksum += mesh.GetTriangleSubprim(mesh.TriangleCount - 1);
                    break;
            }
        }
        return checksum;
    }

    [Benchmark]
    public SilkSceneDelta ApplyManagedScenePage() =>
        _scene.Apply(_page, commandCount: 2, revision: 2);
}
