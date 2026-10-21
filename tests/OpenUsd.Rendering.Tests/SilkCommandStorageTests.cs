// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

[NotInParallel]
public sealed class SilkCommandStorageTests
{
    private static readonly string[] ExpectedPaths = ["/First", "/Second"];

    [Test]
    [Arguments(1)]
    [Arguments(7)]
    [Arguments(64)]
    [Arguments(4096)]
    public async Task StoredPagesApplyIdenticallyAcrossTransferBoundaries(int transfer)
    {
        byte[] bytes = [.. SilkTransactionalApplyTests.Mesh("/First", 1), .. SilkTransactionalApplyTests.Mesh(
            "/Second", 2)];
        using OpenUsdSilkPage page = Stored(bytes, 2, transfer, out MemoryStorage storage);
        var expected = new SilkSceneState();
        var actual = new SilkSceneState();
        expected.Apply(bytes, 2, 7);
        actual.Apply(page);
        await Assert.That(actual.Revision).IsEqualTo(expected.Revision);
        await Assert.That(actual.Meshes.Count).IsEqualTo(2);
        foreach ((ulong id, SilkMeshData mesh) in actual.Meshes)
        {
            await Assert.That(mesh.Path).IsEqualTo(expected.Meshes[id].Path);
            await Assert.That(mesh.Points.ToArray()).IsEquivalentTo(expected.Meshes[id].Points.ToArray());
        }
        await Assert.That(storage.Reads).IsEqualTo(2 * ((bytes.Length + transfer - 1) / transfer));
        await Assert.That(storage.MaximumRead).IsLessThanOrEqualTo(transfer);
        page.Dispose();
        await Assert.That(storage.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task FailedSecondPassRollsBackAndTheSameStorageRetriesExactly()
    {
        byte[] first = SilkTransactionalApplyTests.Mesh("/First", 1);
        byte[] bytes = [.. first, .. SilkTransactionalApplyTests.Mesh("/Second", 2)];
        using OpenUsdSilkPage page = Stored(bytes, 2, 31, out MemoryStorage storage);
        storage.FailPass = 2;
        storage.FailOffset = (ulong)first.Length;
        var scene = new SilkSceneState();
        await Assert.That(() => scene.Apply(page)).Throws<IOException>();
        await Assert.That(scene.Meshes.Count).IsEqualTo(0);
        await Assert.That(scene.Revision).IsEqualTo(0ul);
        await Assert.That(storage.Disposals).IsEqualTo(0);
        storage.FailPass = 0;
        scene.Apply(page);
        await Assert.That(scene.Meshes.Count).IsEqualTo(2);
        await Assert.That(scene.Meshes.Values.Select(static mesh => mesh.Path).Order().ToArray())
            .IsEquivalentTo(ExpectedPaths);
        page.Dispose();
        await Assert.That(storage.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task ReplayOwnsStorageAfterTheOriginalPageIsDisposed()
    {
        using OpenUsdSilkPage page = Stored(SilkTransactionalApplyTests.Mesh("/Retained", 1), 1, 29,
            out MemoryStorage storage);
        OpenUsdSilkPage.ReplaySnapshot snapshot = page.CaptureReplay();
        page.Dispose();
        await Assert.That(storage.Disposals).IsEqualTo(0);
        using OpenUsdSilkPage replay = snapshot.CreatePage();
        snapshot.Dispose();
        var scene = new SilkSceneState();
        scene.Apply(replay);
        await Assert.That(scene.Meshes.Values.Single().Path).IsEqualTo("/Retained");
        replay.Dispose();
        await Assert.That(storage.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task PublicViewsAndEnumeratorCopiesSurviveAdvanceAndPageDisposal()
    {
        MemoryStorage storage = ReadPublicViews();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        await Assert.That(storage.Disposals).IsEqualTo(1);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static MemoryStorage ReadPublicViews()
    {
        using OpenUsdSilkPage page = Stored(
            [.. SilkTransactionalApplyTests.Mesh("/First", 1), .. SilkTransactionalApplyTests.Mesh("/Second", 2)],
            2, 31, out MemoryStorage storage);
        SilkCommandEnumerator original = page.GetEnumerator();
        if (!original.MoveNext())
        {
            throw new InvalidDataException("The first command was missing.");
        }
        SilkMeshUpsertCommand first = original.Current.AsMeshUpsert();
        SilkCommandEnumerator copy = original;
        original.Dispose();
        page.Dispose();
        if (!copy.MoveNext() || first.Path != "/First" || copy.Current.AsMeshUpsert().Path != "/Second" ||
            copy.MoveNext())
        {
            throw new InvalidDataException("A public view or independent enumerator cursor was invalidated.");
        }
        copy.Dispose();
        return storage;
    }

    [Test]
    [Arguments(0u, 8u, 8)]
    [Arguments(1u, 7u, 8)]
    [Arguments(1u, 9u, 8)]
    [Arguments(1u, 8u, 9)]
    [Arguments(2u, 8u, 8)]
    public async Task MalformedExtentOrCountIsRefusedWithoutLeakingTheReader(uint commands, uint encodedSize,
        int length)
    {
        var bytes = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), encodedSize);
        using OpenUsdSilkPage page = Stored(bytes, commands, 3, out MemoryStorage storage);
        await Assert.That(() => Consume(page)).Throws<InvalidDataException>();
        page.Dispose();
        await Assert.That(storage.Disposals).IsEqualTo(1);
    }

    private static void Consume(OpenUsdSilkPage page)
    {
        using SilkCommandEnumerator commands = page.GetTransientEnumerator();
        while (commands.MoveNext())
        {
        }
    }

    [Test]
    public async Task ReaderAndLengthUseOffsetsBeyondFourGiBWithoutAnAggregateArray()
    {
        const ulong start = 1ul << 32;
        var storage = new OffsetStorage(start);
        using var page = new OpenUsdSilkPage(27, 1, storage, 1, null, 12);
        using (var reader = new SilkCommandStorageReader(storage, preserveViews: false))
        {
            ulong offset = start;
            uint count = 1;
            bool moved = reader.MoveNext(ref offset, ref count, out SilkCommand command);
            bool correct = moved && command.Type == SilkCommandType.MeshRemove && offset == start + 12 && count == 0;
            await Assert.That(correct).IsTrue();
        }
        await Assert.That(page.SerializedByteLength).IsEqualTo(start + 12);
        await Assert.That(() => page.ByteLength).Throws<OverflowException>();
        page.Dispose();
        storage.Dispose();
        await Assert.That(storage.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task ConfigurationRequiresCompleteOrderedCeilings()
    {
        await Assert.That(SilkCommandStorageLimits.FromConfiguration(null, null, null, null)).IsNull();
        await Assert.That(() => SilkCommandStorageLimits.FromConfiguration("100", null, "8", "4"))
            .Throws<ArgumentException>();
        await Assert.That(() => new SilkCommandStorageLimits(31, 32, 16, 8)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new SilkCommandStorageLimits(64, 32, 7, 4)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new SilkCommandStorageLimits(64, 32, 16, 17)).Throws<ArgumentOutOfRangeException>();
        var storage = new SilkCommandStorageLimits(64, 32, 16, 8);
        await Assert.That(() => new SilkPreparationLimits(128, 15, storage)).Throws<ArgumentException>();
        var limits = new SilkPreparationLimits(128, 16, storage);
        await Assert.That(limits.CommandStorage).IsSameReferenceAs(storage);
        await Assert.That(storage.MaximumLiveBytes).IsEqualTo(64ul);
        await Assert.That(storage.MaximumUpdateBytes).IsEqualTo(32ul);
        await Assert.That(storage.MaximumCommandBytes).IsEqualTo(16);
        await Assert.That(storage.TransferBufferBytes).IsEqualTo(8);
    }

    internal static OpenUsdSilkPage Stored(byte[] bytes, uint commands, int transfer, out MemoryStorage storage)
    {
        storage = new MemoryStorage(bytes, transfer);
        try
        {
            return new OpenUsdSilkPage(27, 7, storage, commands, null, Math.Max(8, bytes.Length));
        }
        finally
        {
            storage.Dispose();
        }
    }

    internal sealed class MemoryStorage(byte[] bytes, int transfer)
        : SilkCommandStorage((ulong)bytes.Length, Math.Max(8, bytes.Length), transfer)
    {
        private int _pass;
        internal int Reads { get; private set; }
        internal int MaximumRead { get; private set; }
        internal int Disposals { get; private set; }
        internal int FailPass { get; set; }
        internal ulong FailOffset { get; set; }

        protected override void ReadCore(ulong offset, Span<byte> destination)
        {
            if (offset == 0)
            {
                _pass++;
            }
            if (_pass == FailPass && offset >= FailOffset)
            {
                throw new IOException("Injected stored-command read refusal.");
            }
            Reads++;
            MaximumRead = Math.Max(MaximumRead, destination.Length);
            bytes.AsSpan(checked((int)offset), destination.Length).CopyTo(destination);
        }

        protected override void DisposeCore() => Disposals++;
    }

    private sealed class OffsetStorage(ulong start) : SilkCommandStorage(start + 12, 12, 5)
    {
        internal int Disposals { get; private set; }

        protected override void ReadCore(ulong offset, Span<byte> destination)
        {
            ReadOnlySpan<byte> command = [(byte)SilkCommandType.MeshRemove, 0, 0, 0, 12, 0, 0, 0, 42, 0, 0, 0];
            command.Slice(checked((int)(offset - start)), destination.Length).CopyTo(destination);
        }

        protected override void DisposeCore() => Disposals++;
    }
}
