// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Extraction;

namespace OpenUsd.Physics.Tests.Extraction;

public sealed class UsdPhysicsExtractionOptionsTests
{
    [Test]
    public async Task DefaultOptionsRecordMeshDataAndSkipGuidePrims()
    {
        UsdPhysicsExtractionOptions options = UsdPhysicsExtractionOptions.Default;

        await Assert.That(options.IncludeMeshData).IsTrue();
        await Assert.That(options.SkipGuide).IsTrue();
        await Assert.That(options.IncludeUnmapped).IsFalse();
        await Assert.That(options.SkipInvisible).IsFalse();
        await Assert.That(double.IsNaN(options.TimeCode)).IsTrue();

        PhysicsExtractNativeOptions native = options.ToNative();
        await Assert.That(native.StructSize).IsEqualTo(PhysicsExtractNativeMethods.OptionsBytes);
        await Assert.That(native.Version).IsEqualTo(PhysicsExtractAbi.OptionsVersion);
        await Assert.That(native.Flags).IsEqualTo(
            PhysicsExtractAbi.OptionIncludeMeshData | PhysicsExtractAbi.OptionSkipGuide);
        await Assert.That(native.MaxObjects).IsEqualTo(0u);
        await Assert.That(native.Reserved0).IsEqualTo(0u);
    }

    [Test]
    public async Task SwitchesMapOntoTheNativeFlagWord()
    {
        PhysicsExtractNativeOptions native = (UsdPhysicsExtractionOptions.Default with
        {
            IncludeMeshData = false,
            IncludeUnmapped = true,
            SkipInvisible = true,
            SkipGuide = false,
        }).ToNative();

        await Assert.That(native.Flags).IsEqualTo(
            PhysicsExtractAbi.OptionIncludeUnmapped | PhysicsExtractAbi.OptionSkipInvisible);
    }

    [Test]
    public async Task CapacitiesAreClampedToTheAbiBounds()
    {
        PhysicsExtractNativeOptions native = (UsdPhysicsExtractionOptions.Default with
        {
            MaxObjects = 16,
            MaxProperties = int.MaxValue,
            MaxRelationships = 4,
            MaxTargets = 5,
            MaxNumbers = 6,
            MaxTexts = 7,
            MaxPoints = 8,
            MaxIndices = 9,
            MaxDiagnostics = 10,
            MaxStringBytes = 2048,
        }).ToNative();

        await Assert.That(native.MaxObjects).IsEqualTo(16u);
        await Assert.That(native.MaxProperties)
            .IsEqualTo((uint)PhysicsExtractAbi.MaxProperties);
        await Assert.That(native.MaxRelationships).IsEqualTo(4u);
        await Assert.That(native.MaxTargets).IsEqualTo(5u);
        await Assert.That(native.MaxNumbers).IsEqualTo(6u);
        await Assert.That(native.MaxTexts).IsEqualTo(7u);
        await Assert.That(native.MaxPoints).IsEqualTo(8u);
        await Assert.That(native.MaxIndices).IsEqualTo(9u);
        await Assert.That(native.MaxDiagnostics).IsEqualTo(10u);
        await Assert.That(native.MaxStringBytes).IsEqualTo(2048u);
    }

    [Test]
    public async Task NegativeCapacitiesAreRejected() =>
        await Assert.That(() => (UsdPhysicsExtractionOptions.Default with { MaxObjects = -1 })
                .ToNative())
            .Throws<ArgumentOutOfRangeException>();

    [Test]
    public async Task TimeCodeIsCarriedThrough()
    {
        PhysicsExtractNativeOptions native =
            (UsdPhysicsExtractionOptions.Default with { TimeCode = 12.5 }).ToNative();

        await Assert.That(native.TimeCode).IsEqualTo(12.5).Within(1e-12);
    }

    [Test]
    public async Task NullStageIsRejected() =>
        await Assert.That(
                () => UsdPhysicsStageExtractor.Extract(null!, UsdPhysicsExtractionOptions.Default))
            .Throws<ArgumentNullException>();
}
