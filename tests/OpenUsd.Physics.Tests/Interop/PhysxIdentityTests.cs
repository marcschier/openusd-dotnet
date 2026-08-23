// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Interop;

public sealed class PhysxIdentityTests
{
    [Test]
    public async Task IdentityMatchesTheNativeHashVectors()
    {
        await Assert.That(PhysxIdentity.Compute("/World/Box", PhysxInstanceDomain.Prim, 0))
            .IsEqualTo(6420092752705120442UL);
        await Assert.That(PhysxIdentity.Compute("/World/Böx", PhysxInstanceDomain.Prim, 0))
            .IsEqualTo(660549988999612420UL);
        await Assert.That(PhysxIdentity.Compute("/World/Instancer", PhysxInstanceDomain.PointInstancer, 7))
            .IsEqualTo(11055070733379733229UL);
    }

    [Test]
    public async Task IdentityIsStableAcrossEncodings()
    {
        ulong fromString = PhysxIdentity.Compute("/World/Böx", PhysxInstanceDomain.Prim, 0);
        ulong fromBytes = PhysxIdentity.Compute(
            Encoding.UTF8.GetBytes("/World/Böx"),
            PhysxInstanceDomain.Prim,
            0);

        await Assert.That(fromBytes).IsEqualTo(fromString);
    }

    [Test]
    public async Task IdentityDependsOnDomainAndInstanceIndex()
    {
        ulong prim = PhysxIdentity.Compute("/World/Instancer", PhysxInstanceDomain.Prim, 0);
        ulong instanced = PhysxIdentity.Compute("/World/Instancer", PhysxInstanceDomain.PointInstancer, 0);
        ulong element = PhysxIdentity.Compute("/World/Instancer", PhysxInstanceDomain.PointInstancer, 1);

        await Assert.That(instanced).IsNotEqualTo(prim);
        await Assert.That(element).IsNotEqualTo(instanced);
    }

    [Test]
    public async Task IdentityIsNeverTheReservedZeroValue()
    {
        for (uint index = 0; index < 4096; index++)
        {
            ulong id = PhysxIdentity.Compute("/World/Instancer", PhysxInstanceDomain.PointInstancer, index);
            await Assert.That(id).IsNotEqualTo(PhysxAbi.InvalidId);
        }
    }

    [Test]
    public async Task TryComputeRejectsRelativeEmptyAndUnknownAddresses()
    {
        await Assert.That(PhysxIdentity.TryCompute([], PhysxInstanceDomain.Prim, 0, out _, out _)).IsFalse();
        await Assert.That(PhysxIdentity.TryCompute(
            Encoding.UTF8.GetBytes("World/Box"),
            PhysxInstanceDomain.Prim,
            0,
            out _,
            out _)).IsFalse();
        await Assert.That(PhysxIdentity.TryCompute(
            Encoding.UTF8.GetBytes("/World/Box"),
            PhysxInstanceDomain.Count,
            0,
            out _,
            out _)).IsFalse();
        await Assert.That(PhysxIdentity.TryCompute(
            [(byte)'/', 0x41, 0xC3, 0x28],
            PhysxInstanceDomain.Prim,
            0,
            out _,
            out _)).IsFalse();
    }

    [Test]
    public async Task EncodeRejectsUnpairedSurrogates()
    {
        await Assert.That(() => PhysxIdentity.Encode("/World/\uD800")).Throws<ArgumentException>();
    }

    [Test]
    public async Task TableReusesTheIdentityOfAnAlreadyKnownAddress()
    {
        using var table = new PhysxIdentityTable();

        ulong first = table.Add("/World/Böx");
        ulong second = table.Add("/World/Böx");

        await Assert.That(second).IsEqualTo(first);
        await Assert.That(table.Count).IsEqualTo(1);
        await Assert.That(table.StringBytes.Length).IsEqualTo(Encoding.UTF8.GetByteCount("/World/Böx"));
    }

    [Test]
    public async Task TableRecordsPointAtTheirOwnPathBytes()
    {
        using var table = new PhysxIdentityTable();
        table.Add("/World/Ground");
        table.Add("/World/Böx", PhysxInstanceDomain.PointInstancer, 3);

        PhysxIdentityRecord[] records = table.ToRecords();
        byte[] strings = table.StringBytes.ToArray();

        await Assert.That(records.Length).IsEqualTo(2);
        for (int index = 0; index < records.Length; index++)
        {
            PhysxIdentityRecord record = records[index];
            PhysxIdentityEntry entry = table.Entries[index];
            string path = Encoding.UTF8.GetString(
                strings,
                (int)record.PathOffset,
                (int)record.PathLength);

            await Assert.That(path).IsEqualTo(entry.Path);
            await Assert.That(record.Id).IsEqualTo(
                PhysxIdentity.Compute(entry.Path, entry.Domain, entry.InstanceIndex));
        }

        await Assert.That(table.TryGet(records[1].Id, out PhysxIdentityEntry? found)).IsTrue();
        await Assert.That(found!.InstanceIndex).IsEqualTo(3u);
        await Assert.That(found.Domain).IsEqualTo(PhysxInstanceDomain.PointInstancer);
    }

    [Test]
    public async Task TableRejectsRelativePaths()
    {
        using var table = new PhysxIdentityTable();

        await Assert.That(table.TryAdd("World/Box", PhysxInstanceDomain.Prim, 0, out _, out string? error))
            .IsFalse();
        await Assert.That(error).IsNotNull();
        await Assert.That(() => table.Add("World/Box")).Throws<InvalidOperationException>();
    }
}
