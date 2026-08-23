// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Events;

/// <summary>
/// Locks the parts of the retained world ABI that batched events, commands, and queries widened.
/// </summary>
/// <remarks>
/// The event record grew two identity slots so contact, trigger, and controller hit events can name
/// the collider as well as the body, which is what makes the deterministic order collision free, and
/// the actor record later grew the principal axis rotation of its inertia. Each widening is an exact
/// ABI version bump: a runtime that reports anything else is rejected rather than downgraded, so
/// these assertions must be updated together with the native header.
/// The assertions read the values the way the runtime does, through a staged descriptor or through
/// a runtime size, so the test cannot be satisfied by a constant that native code never sees.
/// </remarks>
public sealed class PhysxEventAbiTests
{
    [Test]
    public async Task EveryStagedDescriptorDeclaresTheSameAbiVersion()
    {
        using var results = new PhysxResultBuffers(default);
        using var queries = new PhysxQueryBuffers(1, 1);

        uint declared = results.CreatePage().AbiVersion;

        await Assert.That(declared).IsEqualTo(PhysxAbi.Version);
        await Assert.That(queries.CreateDesc().AbiVersion).IsEqualTo(declared);
    }

    [Test]
    public async Task TheEventRecordCarriesTwoDetailIdentities()
    {
        int size = Unsafe.SizeOf<PhysxEventRecord>();

        await Assert.That(size).IsEqualTo(80);
        await Assert.That(size).IsEqualTo(PhysxAbi.RecordSizes.Event);
        await Assert.That(size % 8).IsEqualTo(0);
    }

    [Test]
    public async Task EveryEventTypeHasAPublicKind()
    {
        for (uint type = 0; type < (uint)PhysxEventType.Count; type++)
        {
            UsdPhysicsEventKind kind = PhysxEventAdapter.MapKind((PhysxEventType)type);
            await Assert.That(Enum.IsDefined(kind)).IsTrue();
        }
    }

    [Test]
    public async Task EveryFlagStaysInsideItsDeclaredMask()
    {
        uint[] masks =
        [
            (uint)PhysxCommandFlags.All,
            (uint)PhysxEventFlags.All,
            (uint)PhysxQueryFlags.All,
            (uint)PhysxQueryHitFlags.All
        ];

        await Assert.That(masks[0]).IsEqualTo(0x3Fu);
        await Assert.That(masks[1]).IsEqualTo(0xFu);
        await Assert.That(masks[2]).IsEqualTo(0x1Fu);
        await Assert.That(masks[3]).IsEqualTo(0x3Fu);

        foreach (PhysxCommandFlags flag in Enum.GetValues<PhysxCommandFlags>())
        {
            await Assert.That((uint)flag & ~masks[0]).IsEqualTo(0u);
        }
        foreach (PhysxEventFlags flag in Enum.GetValues<PhysxEventFlags>())
        {
            await Assert.That((uint)flag & ~masks[1]).IsEqualTo(0u);
        }
        foreach (PhysxQueryFlags flag in Enum.GetValues<PhysxQueryFlags>())
        {
            await Assert.That((uint)flag & ~masks[2]).IsEqualTo(0u);
        }
        foreach (PhysxQueryHitFlags flag in Enum.GetValues<PhysxQueryHitFlags>())
        {
            await Assert.That((uint)flag & ~masks[3]).IsEqualTo(0u);
        }
    }

    [Test]
    public async Task TheEventQueryAndTriggerCapabilitiesAreDeclared()
    {
        PhysxCapabilityFlags[] added =
        [
            PhysxCapabilityFlags.TriggerEvents,
            PhysxCapabilityFlags.ControllerHitEvents,
            PhysxCapabilityFlags.BatchedQueries
        ];

        await Assert.That((uint)added[0]).IsEqualTo(1u << 9);
        await Assert.That((uint)added[1]).IsEqualTo(1u << 10);
        await Assert.That((uint)added[2]).IsEqualTo(1u << 11);
    }

    [Test]
    public async Task ManagedLayoutValidationStillReportsNoMismatch()
    {
        await Assert.That(PhysxRuntime.ValidateManagedLayout().Length).IsEqualTo(0);
    }
}
