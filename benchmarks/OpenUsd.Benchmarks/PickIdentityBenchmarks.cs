// Copyright (c) marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Benchmarks;

[MemoryDiagnoser]
public class PickIdentityBenchmarks
{
    private string _path = string.Empty;
    private SilkPickIdentityTable _table = new();
    private uint _token;

    [Params(8, 128)]
    public int RangeCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        byte[] page = SilkBenchmarkData.CreateMeshPage(
            RangeCount,
            trianglesPerMesh: 2);
        var scene = new SilkSceneState();
        _ = scene.Apply(page, checked((uint)RangeCount), revision: 1);
        _table = scene.PickIdentities;
        _path = SilkBenchmarkData.GetMeshPath(RangeCount / 2);
        if (!_table.TryGetRange(_path, out SilkPickTokenRange range))
        {
            throw new InvalidOperationException("The benchmark token range was not retained.");
        }
        _token = range.LastToken;
    }

    [Benchmark]
    public SilkPickIdentity ResolveToken()
    {
        if (!_table.TryResolve(_token, out SilkPickIdentity identity))
        {
            throw new InvalidOperationException("The benchmark token did not resolve.");
        }
        return identity;
    }

    [Benchmark]
    public SilkPickTokenRange FindRange()
    {
        if (!_table.TryGetRange(_path, out SilkPickTokenRange range))
        {
            throw new InvalidOperationException("The benchmark token range was not found.");
        }
        return range;
    }
}
