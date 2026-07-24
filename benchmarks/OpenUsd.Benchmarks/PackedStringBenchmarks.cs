// Copyright (c) marcschier. Licensed under the MIT License.

using System.Reflection;
using BenchmarkDotNet.Attributes;
using OpenUsd.Interop;

namespace OpenUsd.Benchmarks;

[MemoryDiagnoser]
public class PackedStringBenchmarks
{
    private delegate (byte[] Data, nuint[] Offsets) PackDelegate(
        ReadOnlySpan<string> values);

    private delegate string[] DecodeDelegate(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<nuint> offsets,
        string description);

    private static readonly PackDelegate PackStrings =
        Bind<PackDelegate>("OpenUsd.Interop.NativeStringListPacking", "Pack");
    private static readonly DecodeDelegate DecodeStrings =
        Bind<DecodeDelegate>("OpenUsd.Interop.NativePackedStringListDecoder", "Decode");

    private byte[] _data = [];
    private nuint[] _offsets = [];
    private string[] _values = [];

    [Params(8, 128)]
    public int ValueCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _values = new string[ValueCount];
        for (int index = 0; index < _values.Length; index++)
        {
            _values[index] = $"/World/Group_{index:D4}/Mësh_{index % 17:D2}";
        }
        (_data, _offsets) = PackStrings(_values);
    }

    [Benchmark]
    [BenchmarkCategory("Smoke")]
    public (byte[] Data, nuint[] Offsets) PackUtf8Strings() =>
        PackStrings(_values);

    [Benchmark]
    public string[] DecodeUtf8Strings() =>
        DecodeStrings(_data, _offsets, "benchmark string list");

    private static TDelegate Bind<TDelegate>(string typeName, string methodName)
        where TDelegate : Delegate
    {
        Type type = typeof(OpenUsdNativeStatus).Assembly.GetType(
            typeName,
            throwOnError: true) ??
            throw new InvalidOperationException($"Could not load {typeName}.");
        MethodInfo method = type.GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static) ??
            throw new InvalidOperationException($"Could not bind {typeName}.{methodName}.");
        return method.CreateDelegate<TDelegate>();
    }
}
