// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Text;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Interop;

public sealed class PhysxRuntimeTests
{
    [Test]
    public async Task NegotiationEitherMatchesExactlyOrReportsAnHonestDiagnostic()
    {
        PhysxRuntimeInfo info = PhysxRuntime.Info;

        if (!info.IsAvailable)
        {
            await Assert.That(info.ManagedCapabilities.Supports(UsdPhysicsCapability.RigidBodies)).IsFalse();
            await Assert.That(info.Diagnostics.Entries.Count).IsEqualTo(1);
            string code = info.Diagnostics.Entries[0].Code;
            await Assert.That(code == PhysxRuntime.UnavailableCode || code == PhysxRuntime.MismatchCode).IsTrue();
            await Assert.That(info.Diagnostics.Entries[0].Category)
                .IsEqualTo(UsdPhysicsDiagnosticCategory.Capability);
            return;
        }

        await Assert.That(info.Abi.AbiVersion).IsEqualTo(PhysxAbi.Version);
        await Assert.That(info.Abi.PageMagic).IsEqualTo(PhysxAbi.PageMagic);
        await Assert.That(PhysxRuntime.CompareWithNative(info.Abi).Length).IsEqualTo(0);
        await Assert.That(PhysxRuntime.CompareLimits(info.Capabilities).Length).IsEqualTo(0);
        await Assert.That(info.Diagnostics.Entries.Count).IsEqualTo(0);
    }

    [Test]
    public async Task MatchingAbiInfoProducesNoMismatch()
    {
        // Joining the mismatches into the asserted value keeps a future ABI change from failing
        // here with a bare count that says nothing about which record moved.
        await Assert.That(string.Join("; ", PhysxRuntime.CompareWithNative(CreateMatchingAbi())))
            .IsEqualTo(string.Empty);
    }

    [Test]
    public async Task EveryAbiDifferenceIsReported()
    {
        PhysxAbiInfo abi = CreateMatchingAbi();
        abi.AbiVersion = PhysxAbi.Version + 1;
        await Assert.That(PhysxRuntime.CompareWithNative(abi).Length).IsEqualTo(1);

        abi = CreateMatchingAbi();
        abi.PageMagic = 0;
        await Assert.That(PhysxRuntime.CompareWithNative(abi).Length).IsEqualTo(1);

        abi = CreateMatchingAbi();
        abi.ActorDescSize = 120;
        abi.JointDescSize = 8;
        await Assert.That(PhysxRuntime.CompareWithNative(abi).Length).IsEqualTo(2);

        abi = CreateMatchingAbi();
        abi.PageAlignment = 16;
        await Assert.That(PhysxRuntime.CompareWithNative(abi).Length).IsEqualTo(1);
    }

    [Test]
    public async Task MatchingCapabilityLimitsProduceNoMismatch()
    {
        await Assert.That(PhysxRuntime.CompareLimits(CreateMatchingCapabilities()).Length).IsEqualTo(0);
    }

    [Test]
    public async Task DifferentCapabilityLimitsAreReported()
    {
        PhysxCapabilitiesInfo capabilities = CreateMatchingCapabilities();
        capabilities.MaxScenes = 8;
        await Assert.That(PhysxRuntime.CompareLimits(capabilities).Length).IsEqualTo(1);

        capabilities = CreateMatchingCapabilities();
        capabilities.MinSimulationRateHz = 1;
        capabilities.MaxSimulationRateHz = 1000;
        await Assert.That(PhysxRuntime.CompareLimits(capabilities).Length).IsEqualTo(2);
    }

    [Test]
    public async Task CapabilityFlagsMapOntoThePublicContract()
    {
        UsdPhysicsCapabilities none = PhysxRuntime.MapCapabilities(PhysxCapabilityFlags.None);
        await Assert.That(none.Supports(UsdPhysicsCapability.RigidBodies)).IsFalse();

        UsdPhysicsCapabilities rigid = PhysxRuntime.MapCapabilities(PhysxCapabilityFlags.CpuRigidBodies);
        await Assert.That(rigid.Supports(UsdPhysicsCapability.RigidBodies)).IsTrue();
        await Assert.That(rigid.Supports(UsdPhysicsCapability.Commands)).IsTrue();
        await Assert.That(rigid.Supports(UsdPhysicsCapability.SceneQueries)).IsFalse();

        UsdPhysicsCapabilities full = PhysxRuntime.MapCapabilities(
            PhysxCapabilityFlags.CpuRigidBodies |
            PhysxCapabilityFlags.SceneQueries |
            PhysxCapabilityFlags.GpuDomains);
        await Assert.That(full.Supports(UsdPhysicsCapability.SceneQueries)).IsTrue();
        await Assert.That(full.Supports(UsdPhysicsCapability.Cuda)).IsTrue();
    }

    [Test]
    public async Task ErrorScopeDescribesEveryStatusWithoutARuntimeMessage()
    {
        var empty = default(PhysxErrorBuffer);

        foreach (PhysxStatus status in Enum.GetValues<PhysxStatus>())
        {
            await Assert.That(PhysxErrorScope.Describe(status, in empty)).IsEqualTo(
                PhysxErrorScope.Describe(status));
            await Assert.That(string.IsNullOrWhiteSpace(PhysxErrorScope.Describe(status))).IsFalse();
        }
    }

    [Test]
    public async Task ErrorScopeReadsTheCallerOwnedBuffer()
    {
        byte[] storage = new byte[PhysxErrorScope.DefaultCapacity];
        byte[] message = Encoding.UTF8.GetBytes("Aktor „/World/Böx“ wurde abgelehnt");
        message.CopyTo(storage, 0);

        string described = describe(storage, message.Length, PhysxStatus.NativeError, truncated: false);
        string truncated = describe(storage, message.Length, PhysxStatus.NativeError, truncated: true);

        await Assert.That(described).IsEqualTo("Aktor „/World/Böx“ wurde abgelehnt");
        await Assert.That(truncated.EndsWith("(truncated)", StringComparison.Ordinal)).IsTrue();

        static unsafe string describe(byte[] storage, int length, PhysxStatus status, bool truncated)
        {
            fixed (byte* data = storage)
            {
                var error = new PhysxErrorBuffer(data, (nuint)storage.Length)
                {
                    Required = truncated ? (nuint)(storage.Length + 1) : (nuint)(length + 1)
                };
                return PhysxErrorScope.Describe(status, in error);
            }
        }
    }

    [Test]
    public async Task ErrorScopeBuildsDiagnosticsFromFailedCalls()
    {
        var empty = default(PhysxErrorBuffer);

        UsdPhysicsDiagnostic failure = PhysxErrorScope.ToDiagnostic(
            PhysxStatus.InvalidPage,
            UsdPhysicsDiagnosticCategory.Build,
            "OPENUSD_PHYSICS_BUILD_REJECTED",
            in empty,
            new UsdPhysicsObjectId(7));

        await Assert.That(failure.Severity).IsEqualTo(UsdPhysicsDiagnosticSeverity.Error);
        await Assert.That(failure.Category).IsEqualTo(UsdPhysicsDiagnosticCategory.Build);
        await Assert.That(failure.Code).IsEqualTo("OPENUSD_PHYSICS_BUILD_REJECTED");
        await Assert.That(failure.ObjectId!.Value.Value).IsEqualTo(7UL);

        UsdPhysicsDiagnostic success = PhysxErrorScope.ToDiagnostic(
            PhysxStatus.Ok,
            UsdPhysicsDiagnosticCategory.General,
            "OPENUSD_PHYSICS_OK",
            in empty);

        await Assert.That(success.Severity).IsEqualTo(UsdPhysicsDiagnosticSeverity.Information);
    }

    private static PhysxAbiInfo CreateMatchingAbi() => new()
    {
        StructSize = (uint)Unsafe.SizeOf<PhysxAbiInfo>(),
        AbiVersion = PhysxAbi.Version,
        PageMagic = PhysxAbi.PageMagic,
        BuildPageHeaderSize = PhysxAbi.RecordSizes.BuildPageHeader,
        PageSpanSize = PhysxAbi.RecordSizes.PageSpan,
        CapacitiesSize = PhysxAbi.RecordSizes.ResultCapacities,
        IdentitySize = PhysxAbi.RecordSizes.Identity,
        SceneDescSize = PhysxAbi.RecordSizes.SceneDesc,
        MaterialDescSize = PhysxAbi.RecordSizes.MaterialDesc,
        ShapeDescSize = PhysxAbi.RecordSizes.ShapeDesc,
        ActorDescSize = PhysxAbi.RecordSizes.ActorDesc,
        ActorShapeRefSize = PhysxAbi.RecordSizes.ActorShapeRef,
        JointDescSize = PhysxAbi.RecordSizes.JointDesc,
        FilterPairSize = PhysxAbi.RecordSizes.FilterPair,
        CommandSize = PhysxAbi.RecordSizes.Command,
        BodyStateSize = PhysxAbi.RecordSizes.BodyState,
        EventSize = PhysxAbi.RecordSizes.Event,
        DiagnosticSize = PhysxAbi.RecordSizes.Diagnostic,
        DebugLineSize = PhysxAbi.RecordSizes.DebugLine,
        ResultHeaderSize = PhysxAbi.RecordSizes.ResultHeader,
        QueryRequestSize = PhysxAbi.RecordSizes.QueryRequest,
        QueryHitSize = PhysxAbi.RecordSizes.QueryHit,
        HeightfieldSampleSize = PhysxAbi.RecordSizes.HeightfieldSample,
        ArticulationDescSize = PhysxAbi.RecordSizes.ArticulationDesc,
        ArticulationLinkDescSize = PhysxAbi.RecordSizes.ArticulationLinkDesc,
        ControllerDescSize = PhysxAbi.RecordSizes.ControllerDesc,
        TendonDescSize = PhysxAbi.RecordSizes.TendonDesc,
        TendonNodeDescSize = PhysxAbi.RecordSizes.TendonNodeDesc,
        MimicJointDescSize = PhysxAbi.RecordSizes.MimicJointDesc,
        VehicleDescSize = PhysxAbi.RecordSizes.VehicleDesc,
        VehicleWheelDescSize = PhysxAbi.RecordSizes.VehicleWheelDesc,
        ParticleMaterialDescSize = PhysxAbi.RecordSizes.ParticleMaterialDesc,
        ParticleSystemDescSize = PhysxAbi.RecordSizes.ParticleSystemDesc,
        ParticleBodyDescSize = PhysxAbi.RecordSizes.ParticleBodyDesc,
        DeformableMaterialDescSize = PhysxAbi.RecordSizes.DeformableMaterialDesc,
        DeformableDescSize = PhysxAbi.RecordSizes.DeformableDesc,
        DeformationStateSize = PhysxAbi.RecordSizes.DeformationState,
        PageAlignment = PhysxAbi.PageAlignment
    };

    private static PhysxCapabilitiesInfo CreateMatchingCapabilities() => new()
    {
        StructSize = (uint)Unsafe.SizeOf<PhysxCapabilitiesInfo>(),
        AbiVersion = PhysxAbi.Version,
        Flags = (uint)PhysxCapabilityFlags.CpuRigidBodies,
        MaxScenes = PhysxAbi.MaxScenes,
        MaxCollisionGroups = PhysxAbi.MaxCollisionGroups,
        MinSimulationRateHz = PhysxAbi.MinSimulationRateHz,
        MaxSimulationRateHz = PhysxAbi.MaxSimulationRateHz,
        MaxSubsteps = PhysxAbi.MaxSubsteps,
        MaxResultCapacity = PhysxAbi.MaxResultCapacity
    };
}
