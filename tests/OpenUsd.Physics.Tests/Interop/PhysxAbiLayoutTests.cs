// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Interop;

public sealed class PhysxAbiLayoutTests
{
    [Test]
    public async Task PageRecordSizesMatchTheNativeAbi()
    {
        await Assert.That(Unsafe.SizeOf<PhysxTransform>()).IsEqualTo(PhysxAbi.RecordSizes.Transform);
        await Assert.That(Unsafe.SizeOf<PhysxPageSpan>()).IsEqualTo(PhysxAbi.RecordSizes.PageSpan);
        await Assert.That(Unsafe.SizeOf<PhysxVec3f>()).IsEqualTo(PhysxAbi.RecordSizes.Vec3f);
        await Assert.That(Unsafe.SizeOf<PhysxResultCapacities>()).IsEqualTo(PhysxAbi.RecordSizes.ResultCapacities);
        await Assert.That(Unsafe.SizeOf<PhysxBuildPageHeader>()).IsEqualTo(PhysxAbi.RecordSizes.BuildPageHeader);
        await Assert.That(Unsafe.SizeOf<PhysxIdentityRecord>()).IsEqualTo(PhysxAbi.RecordSizes.Identity);
        await Assert.That(Unsafe.SizeOf<PhysxSceneDesc>()).IsEqualTo(PhysxAbi.RecordSizes.SceneDesc);
        await Assert.That(Unsafe.SizeOf<PhysxMaterialDesc>()).IsEqualTo(PhysxAbi.RecordSizes.MaterialDesc);
        await Assert.That(Unsafe.SizeOf<PhysxShapeDesc>()).IsEqualTo(PhysxAbi.RecordSizes.ShapeDesc);
        await Assert.That(Unsafe.SizeOf<PhysxActorDesc>()).IsEqualTo(PhysxAbi.RecordSizes.ActorDesc);
        await Assert.That(Unsafe.SizeOf<PhysxActorShapeRef>()).IsEqualTo(PhysxAbi.RecordSizes.ActorShapeRef);
        await Assert.That(Unsafe.SizeOf<PhysxJointDesc>()).IsEqualTo(PhysxAbi.RecordSizes.JointDesc);
        await Assert.That(Unsafe.SizeOf<PhysxFilterPair>()).IsEqualTo(PhysxAbi.RecordSizes.FilterPair);
    }

    [Test]
    public async Task RuntimeRecordSizesMatchTheNativeAbi()
    {
        await Assert.That(Unsafe.SizeOf<PhysxCommand>()).IsEqualTo(PhysxAbi.RecordSizes.Command);
        await Assert.That(Unsafe.SizeOf<PhysxBodyState>()).IsEqualTo(PhysxAbi.RecordSizes.BodyState);
        await Assert.That(Unsafe.SizeOf<PhysxEventRecord>()).IsEqualTo(PhysxAbi.RecordSizes.Event);
        await Assert.That(Unsafe.SizeOf<PhysxDiagnosticMessage>()).IsEqualTo(PhysxAbi.DiagnosticMessageBytes);
        await Assert.That(Unsafe.SizeOf<PhysxDiagnosticRecord>()).IsEqualTo(PhysxAbi.RecordSizes.Diagnostic);
        await Assert.That(Unsafe.SizeOf<PhysxDebugLine>()).IsEqualTo(PhysxAbi.RecordSizes.DebugLine);
        await Assert.That(Unsafe.SizeOf<PhysxResultHeader>()).IsEqualTo(PhysxAbi.RecordSizes.ResultHeader);
        await Assert.That(Unsafe.SizeOf<PhysxQueryRequest>()).IsEqualTo(PhysxAbi.RecordSizes.QueryRequest);
        await Assert.That(Unsafe.SizeOf<PhysxQueryHit>()).IsEqualTo(PhysxAbi.RecordSizes.QueryHit);
    }

    [Test]
    public async Task CpuDomainRecordSizesMatchTheNativeAbi()
    {
        await Assert.That(Unsafe.SizeOf<PhysxHeightfieldSample>()).IsEqualTo(PhysxAbi.RecordSizes.HeightfieldSample);
        await Assert.That(Unsafe.SizeOf<PhysxArticulationDesc>()).IsEqualTo(PhysxAbi.RecordSizes.ArticulationDesc);
        await Assert.That(Unsafe.SizeOf<PhysxArticulationLinkDesc>())
            .IsEqualTo(PhysxAbi.RecordSizes.ArticulationLinkDesc);
        await Assert.That(Unsafe.SizeOf<PhysxControllerDesc>()).IsEqualTo(PhysxAbi.RecordSizes.ControllerDesc);
        await Assert.That(Unsafe.SizeOf<PhysxTendonDesc>()).IsEqualTo(PhysxAbi.RecordSizes.TendonDesc);
        await Assert.That(Unsafe.SizeOf<PhysxTendonNodeDesc>()).IsEqualTo(PhysxAbi.RecordSizes.TendonNodeDesc);
        await Assert.That(Unsafe.SizeOf<PhysxMimicJointDesc>()).IsEqualTo(PhysxAbi.RecordSizes.MimicJointDesc);
        await Assert.That(Unsafe.SizeOf<PhysxVehicleDesc>()).IsEqualTo(PhysxAbi.RecordSizes.VehicleDesc);
        await Assert.That(Unsafe.SizeOf<PhysxVehicleWheelDesc>()).IsEqualTo(PhysxAbi.RecordSizes.VehicleWheelDesc);
    }

    [Test]
    public async Task GpuDomainRecordSizesMatchTheNativeAbi()
    {
        await Assert.That(Unsafe.SizeOf<PhysxParticleMaterialDesc>())
            .IsEqualTo(PhysxAbi.RecordSizes.ParticleMaterialDesc);
        await Assert.That(Unsafe.SizeOf<PhysxParticleSystemDesc>())
            .IsEqualTo(PhysxAbi.RecordSizes.ParticleSystemDesc);
        await Assert.That(Unsafe.SizeOf<PhysxParticleBodyDesc>())
            .IsEqualTo(PhysxAbi.RecordSizes.ParticleBodyDesc);
        await Assert.That(Unsafe.SizeOf<PhysxDeformableMaterialDesc>())
            .IsEqualTo(PhysxAbi.RecordSizes.DeformableMaterialDesc);
        await Assert.That(Unsafe.SizeOf<PhysxDeformableDesc>()).IsEqualTo(PhysxAbi.RecordSizes.DeformableDesc);
        await Assert.That(Unsafe.SizeOf<PhysxDeformationState>())
            .IsEqualTo(PhysxAbi.RecordSizes.DeformationState);
    }

    [Test]
    public async Task EveryPageRecordSizeIsEightByteAligned()
    {
        int[] sizes =
        [
            PhysxAbi.RecordSizes.PageSpan,
            PhysxAbi.RecordSizes.ResultCapacities,
            PhysxAbi.RecordSizes.BuildPageHeader,
            PhysxAbi.RecordSizes.Identity,
            PhysxAbi.RecordSizes.SceneDesc,
            PhysxAbi.RecordSizes.MaterialDesc,
            PhysxAbi.RecordSizes.ShapeDesc,
            PhysxAbi.RecordSizes.ActorDesc,
            PhysxAbi.RecordSizes.ActorShapeRef,
            PhysxAbi.RecordSizes.JointDesc,
            PhysxAbi.RecordSizes.FilterPair,
            PhysxAbi.RecordSizes.ArticulationDesc,
            PhysxAbi.RecordSizes.ArticulationLinkDesc,
            PhysxAbi.RecordSizes.ControllerDesc,
            PhysxAbi.RecordSizes.TendonDesc,
            PhysxAbi.RecordSizes.TendonNodeDesc,
            PhysxAbi.RecordSizes.MimicJointDesc,
            PhysxAbi.RecordSizes.VehicleDesc,
            PhysxAbi.RecordSizes.VehicleWheelDesc,
            PhysxAbi.RecordSizes.ParticleMaterialDesc,
            PhysxAbi.RecordSizes.ParticleSystemDesc,
            PhysxAbi.RecordSizes.ParticleBodyDesc,
            PhysxAbi.RecordSizes.DeformableMaterialDesc,
            PhysxAbi.RecordSizes.DeformableDesc,
            PhysxAbi.RecordSizes.DeformationState
        ];

        foreach (int size in sizes)
        {
            await Assert.That(size % (int)PhysxAbi.PageAlignment).IsEqualTo(0);
        }
    }

    [Test]
    public async Task TheHeaderCarriesOneSpanPerSectionAndNothingElse()
    {
        // The section enumeration counts the header and the capacities pseudo
        // sections as well; the capacities are the one section the header carries
        // by value rather than as a span. A header that gained a span without
        // gaining a section, or the other way around, fails here rather than in a
        // misaligned read.
        int sections = (int)PhysxPageSection.Count;
        await Assert.That(sections).IsEqualTo(PhysxAbi.PageSectionSpanCount + 2);

        int spanBytes = PhysxAbi.PageSectionSpanCount * PhysxAbi.RecordSizes.PageSpan;
        int fixedBytes = Unsafe.SizeOf<PhysxBuildPageHeader>() - spanBytes -
            Unsafe.SizeOf<PhysxResultCapacities>();
        await Assert.That(fixedBytes).IsEqualTo(120);
    }

    [Test]
    public async Task HeightfieldSampleDividesThePageAlignment()
    {
        // A heightfield sample is deliberately smaller than the page alignment,
        // so it only has to tile it exactly for the section to stay aligned.
        int size = Unsafe.SizeOf<PhysxHeightfieldSample>();
        await Assert.That(size).IsGreaterThan(0);
        await Assert.That((int)PhysxAbi.PageAlignment % size).IsEqualTo(0);
    }

    [Test]
    public async Task ManagedLayoutValidationReportsNoMismatch()
    {
        await Assert.That(PhysxRuntime.ValidateManagedLayout().Length).IsEqualTo(0);
    }
}
