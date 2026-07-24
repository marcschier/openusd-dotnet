// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkGraphicsBufferTests
{
    [Test]
    public async Task UploadWriteAcceptsExactBufferRange()
    {
        using var buffer = new TestBuffer(4, SilkBufferUsage.Upload);
        byte[] data = [1, 2, 3, 4];

        buffer.Write(data);

        await Assert.That(buffer.LastWriteLength).IsEqualTo((nuint)4);
    }

    [Test]
    public async Task WriteRejectsNonUploadBuffer()
    {
        using var buffer = new TestBuffer(4, SilkBufferUsage.Vertex);
        byte[] data = [1];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => buffer.Write(data));

        await Assert.That(exception.Message).Contains("SilkBufferUsage.Upload");
    }

    [Test]
    public async Task WriteRejectsRangePastAllocation()
    {
        using var buffer = new TestBuffer(4, SilkBufferUsage.Upload);
        byte[] data = [1, 2];

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => buffer.Write(data, 3));

        await Assert.That(exception.ParamName).IsEqualTo("offset");
    }

    private sealed class TestBuffer(nuint size, SilkBufferUsage usage)
        : SilkGraphicsBufferBase(size, usage)
    {
        internal nuint LastWriteLength { get; private set; }

        public override void Write(ReadOnlySpan<byte> data, nuint offset = 0)
        {
            LastWriteLength = ValidateWrite(data.Length, offset);
        }

        public override void ReadbackForTesting(Span<byte> destination)
        {
            _ = ValidateReadback(destination.Length);
        }

        protected override void ReleaseNative()
        {
        }
    }
}
