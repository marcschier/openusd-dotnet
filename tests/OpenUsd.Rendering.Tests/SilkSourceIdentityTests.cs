// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkSourceIdentityTests
{
    [Test]
    public async Task SourceBatchPreservesOrderUnicodeAndNestedContext()
    {
        byte[] packet = Packet(
            ("/World/B/Mesh", []),
            ("/World/Prototype", [new("/World/Outer", 3), new("/World/Inner", 7)]),
            ("/World/\u03a9", []));
        IReadOnlyList<SelectionItem> result = OpenUsdSilkRuntime.DecodeSourceIdentities(packet, 3);
        Array.Fill(packet, (byte)0);
        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result[0].PrimPath).IsEqualTo("/World/B/Mesh");
        await Assert.That(result[0].InstancerContext.Count).IsEqualTo(0);
        await Assert.That(result[1].PrimPath).IsEqualTo("/World/Prototype");
        await Assert.That(result[1].InstancerContext.Count).IsEqualTo(2);
        await Assert.That(result[1].InstancerContext[0]).IsEqualTo(new SelectionInstancerEntry("/World/Outer", 3));
        await Assert.That(result[1].InstancerContext[1]).IsEqualTo(new SelectionInstancerEntry("/World/Inner", 7));
        await Assert.That(result[1].ElementKind).IsEqualTo(SelectionElementKind.None);
        await Assert.That(result[2].PrimPath).IsEqualTo("/World/\u03a9");
        await Assert.That(() => ((IList<SelectionItem>)result)[0] = new("/Changed")).Throws<NotSupportedException>();
    }

    [Test]
    public async Task EmptySourceBatchRemainsEmpty()
    {
        await Assert.That(OpenUsdSilkRuntime.DecodeSourceIdentities(Packet(), 0).Count).IsEqualTo(0);
    }

    [Test]
    public async Task ExactPathAndContextLimitsAreAccepted()
    {
        string path = "/" + new string('a', 4095);
        SelectionInstancerEntry[] context = Enumerable.Range(0, 64)
            .Select(static index => new SelectionInstancerEntry("/Instancer", index)).ToArray();
        IReadOnlyList<SelectionItem> result = OpenUsdSilkRuntime.DecodeSourceIdentities(Packet((path, context)), 1);
        await Assert.That(result[0].PrimPath).IsEqualTo(path);
        await Assert.That(result[0].InstancerContext.Count).IsEqualTo(64);
        await Assert.That(result[0].InstancerContext[63]).IsEqualTo(new SelectionInstancerEntry("/Instancer", 63));
    }

    [Test]
    [Arguments("header")]
    [Arguments("version")]
    [Arguments("count")]
    [Arguments("path-empty")]
    [Arguments("path-long")]
    [Arguments("levels")]
    [Arguments("index")]
    [Arguments("utf8")]
    [Arguments("relative")]
    [Arguments("nul")]
    [Arguments("truncated")]
    [Arguments("trailing")]
    public async Task MalformedSourcePacketsAreRefused(string variant)
    {
        byte[] packet = Packet(("/Mesh", [new("/Inst", 7)]));
        switch (variant)
        {
            case "header":
                packet = packet[..7];
                break;
            case "version":
                BinaryPrimitives.WriteUInt32LittleEndian(packet, 2);
                break;
            case "count":
                BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), 2);
                break;
            case "path-empty":
                BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), 0);
                break;
            case "path-long":
                BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), 4097);
                break;
            case "levels":
                BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), 65);
                break;
            case "index":
                BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(21), uint.MaxValue);
                break;
            case "utf8":
                packet[16] = 0xff;
                break;
            case "relative":
                packet[16] = (byte)'M';
                break;
            case "nul":
                packet[17] = 0;
                break;
            case "truncated":
                packet = packet[..^1];
                break;
            case "trailing":
                packet = [.. packet, 0];
                break;
            default:
                throw new ArgumentException("Unknown packet fault.", nameof(variant));
        }
        await Assert.That(() => OpenUsdSilkRuntime.DecodeSourceIdentities(packet, 1)).Throws<InvalidDataException>();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("relative")]
    [Arguments("/Embedded\0Nul")]
    public async Task InvalidQueryPathsAreRejectedBeforeNativeAccess(string? path)
    {
        await Assert.That(() => OpenUsdSilkRuntime.ResolveSourceIdentities(0, 1, [(path!, 0)]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Utf8PathLimitCountsBytesRatherThanCharacters()
    {
        string path = "/" + new string('\u03a9', 2048);
        await Assert.That(() => OpenUsdSilkRuntime.ResolveSourceIdentities(0, 1, [(path, 0)]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task InvalidUtf16IsRejectedRatherThanReplaced()
    {
        await Assert.That(() => OpenUsdSilkRuntime.ResolveSourceIdentities(0, 1, [("/\ud800", 0)]))
            .Throws<EncoderFallbackException>();
    }

    [Test]
    public async Task NegativeInstanceIndexIsRejectedBeforeNativeAccess()
    {
        await Assert.That(() => OpenUsdSilkRuntime.ResolveSourceIdentities(0, 1, [("/Mesh", -1)]))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task OversizedQueryBatchIsRejectedBeforeNativeAccess()
    {
        var queries = new (string Path, int InstanceIndex)[1025];
        await Assert.That(() => OpenUsdSilkRuntime.ResolveSourceIdentities(0, 1, queries))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static byte[] Packet(params (string Path, SelectionInstancerEntry[] Context)[] records)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(1u);
        writer.Write((uint)records.Length);
        foreach ((string path, SelectionInstancerEntry[] context) in records)
        {
            byte[] encoded = Encoding.UTF8.GetBytes(path);
            writer.Write((uint)encoded.Length);
            writer.Write((uint)context.Length);
            writer.Write(encoded);
            foreach (SelectionInstancerEntry entry in context)
            {
                byte[] level = Encoding.UTF8.GetBytes(entry.InstancerPath);
                writer.Write(entry.InstanceIndex);
                writer.Write((uint)level.Length);
                writer.Write(level);
            }
        }
        writer.Flush();
        return stream.ToArray();
    }
}
