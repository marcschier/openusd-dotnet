// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkVolumeCameraValidationTests
{
    [Test]
    public async Task GetTint_UsesTheSameClampingAndDefaultsAsVolumeMetadata()
    {
        await Assert.That(SilkVolumeCameraValidation.GetTint([]))
            .IsEqualTo(new Vector3(0.18f, 0.18f, 0.18f));
        await Assert.That(SilkVolumeCameraValidation.GetTint([0.5f]))
            .IsEqualTo(new Vector3(0.5f, 0.5f, 0.5f));
        await Assert.That(SilkVolumeCameraValidation.GetTint([0.5f, -1]))
            .IsEqualTo(new Vector3(0.5f, 0, 0.5f));
        await Assert.That(SilkVolumeCameraValidation.GetTint([0.5f, -1, 2]))
            .IsEqualTo(new Vector3(0.5f, 0, 2));
    }

    [Test]
    public async Task ValidateTintExposure_UsesTheFloatProductBoundaryAndClampedTint()
    {
        float boundary = MathF.BitDecrement(2f);
        await Assert.That(() => SilkVolumeCameraValidation.ValidateTintExposure(
            [boundary, 0, 0], 127, false, "/boundary")).ThrowsNothing();
        await Assert.That(() => SilkVolumeCameraValidation.ValidateTintExposure(
                [2, 0, 0], 127, false, "/boundary"))
            .Throws<InvalidDataException>().WithMessageContaining("tint and exposure");
        await Assert.That(() => SilkVolumeCameraValidation.ValidateTintExposure(
            [3, 0, 0], 126, false, "/control")).ThrowsNothing();
        await Assert.That(() => SilkVolumeCameraValidation.ValidateTintExposure(
            [-float.MaxValue, 1, 0], 127, false, "/negative")).ThrowsNothing();
        await Assert.That(() => SilkVolumeCameraValidation.ValidateTintExposure(
                [-float.MaxValue, 3, 0], 127, false, "/green"))
            .Throws<InvalidDataException>().WithMessageContaining("/green");
        await Assert.That(() => SilkVolumeCameraValidation.ValidateTintExposure(
                [3, 0, 0], 127, true, "/display-fallback"))
            .Throws<InvalidDataException>().WithMessageContaining("/display-fallback");
        await Assert.That(() => SilkVolumeCameraValidation.ValidateTintExposure(
            [], 127, false, "/default")).ThrowsNothing();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ValidateTintExposure_RefusesNonfiniteExposureScaleForBlackTint(
        bool displayTransformRequested)
    {
        await Assert.That(() => SilkVolumeCameraValidation.ValidateTintExposure(
                [0, 0, 0], 128, displayTransformRequested, "/black"))
            .Throws<InvalidDataException>().WithMessageContaining("tint and exposure");
        await Assert.That(() => SilkVolumeCameraValidation.ValidateTintExposure(
            [0, 0, 0], 127, displayTransformRequested, "/black")).ThrowsNothing();
    }

    [Test]
    [Arguments(float.NaN)]
    [Arguments(float.PositiveInfinity)]
    [Arguments(float.NegativeInfinity)]
    public async Task ValidateTintExposure_RefusesNonfiniteInputs(float value)
    {
        await Assert.That(() => SilkVolumeCameraValidation.ValidateTintExposure(
                [value, 0, 0], 0, false, "/tint"))
            .Throws<InvalidDataException>().WithMessageContaining("/tint");
        await Assert.That(() => SilkVolumeCameraValidation.ValidateTintExposure(
                [0, 0, 0], value, false, "/exposure"))
            .Throws<InvalidDataException>().WithMessageContaining("/exposure");
    }

    [Test]
    public async Task ValidateDensityProduct_RefusesFiniteProductOverflowAndAcceptsCorrectedRetry()
    {
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensityProduct(
                1e30f, 1e10f, "large.raw"))
            .Throws<InvalidDataException>().WithMessageContaining("density-scale arithmetic");
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensityProduct(
            1e20f, 100, "large.raw")).ThrowsNothing();
    }

    [Test]
    public async Task ValidateDensityProduct_UsesExactArithmeticHeadroomBoundary()
    {
        float boundary = (float)(float.MaxValue / 32d);
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensityProduct(
            boundary, 1, "boundary.raw")).ThrowsNothing();
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensityProduct(
                MathF.BitIncrement(boundary), 1, "boundary.raw"))
            .Throws<InvalidDataException>();
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensityProduct(
            boundary / 2, 2, "boundary.raw")).ThrowsNothing();
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensityProduct(
                boundary / 2, MathF.BitIncrement(2), "boundary.raw"))
            .Throws<InvalidDataException>();
    }

    [Test]
    [Arguments(-1f)]
    [Arguments(float.NaN)]
    [Arguments(float.PositiveInfinity)]
    [Arguments(float.NegativeInfinity)]
    public async Task ValidateDensityProduct_RefusesInvalidMaximumMetadata(float maximum)
    {
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensityProduct(
                maximum, 1, "invalid.raw"))
            .Throws<InvalidDataException>().WithMessageContaining("invalid.raw");
    }

    [Test]
    public async Task ValidateDensitySamples_ReturnsAbsoluteMaximumWithoutChangingSignedSamples()
    {
        byte[] pixels = new byte[3 * sizeof(float)];
        BinaryPrimitives.WriteSingleLittleEndian(pixels, -200);
        BinaryPrimitives.WriteSingleLittleEndian(pixels.AsSpan(sizeof(float)), 100);
        byte[] before = (byte[])pixels.Clone();
        float maximum = SilkVolumeCameraValidation.ValidateDensitySamples(pixels, "signed.raw");
        await Assert.That(maximum).IsEqualTo(200f);
        await Assert.That(pixels.AsSpan().SequenceEqual(before)).IsTrue();
    }

    [Test]
    public async Task InspectDensitySamples_RetainsPositiveRangeAndSubnormalStatus()
    {
        byte[] pixels = new byte[3 * sizeof(float)];
        BinaryPrimitives.WriteSingleLittleEndian(pixels, -float.Epsilon);
        BinaryPrimitives.WriteSingleLittleEndian(pixels.AsSpan(sizeof(float)), -200);
        BinaryPrimitives.WriteSingleLittleEndian(pixels.AsSpan(2 * sizeof(float)), 100);
        byte[] before = (byte[])pixels.Clone();
        SilkVolumeDensitySamples samples = SilkVolumeCameraValidation.InspectDensitySamples(pixels, "signed.raw");
        await Assert.That(samples.MaximumMagnitude).IsEqualTo(200f);
        await Assert.That(samples.MaximumPositive).IsEqualTo(100f);
        await Assert.That(samples.HasSubnormal).IsTrue();
        await Assert.That(pixels.AsSpan().SequenceEqual(before)).IsTrue();
    }

    [Test]
    public async Task ValidateDensitySignal_UsesTheExactPerSegmentBoundary()
    {
        var samples = new SilkVolumeDensitySamples(1, 1, false);
        float boundary = SilkVolumeCameraValidation.MinimumNormalFloat * 4;
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensitySignal(
            samples, boundary, 1, 4, "/boundary")).ThrowsNothing();
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensitySignal(
                samples, MathF.BitDecrement(boundary), 1, 4, "/boundary"))
            .Throws<InvalidDataException>().WithMessageContaining("/boundary");
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensitySignal(
                samples, boundary, 0.5f, 4, "/opacity"))
            .Throws<InvalidDataException>().WithMessageContaining("too small");
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensitySignal(
            samples, boundary, 0.5f, 2, "/opacity")).ThrowsNothing();
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensitySignal(
                samples, 1, SilkVolumeCameraValidation.MinimumNormalFloat * 3, 2, "/attenuated"))
            .Throws<InvalidDataException>().WithMessageContaining("too small");
    }

    [Test]
    public async Task ValidateDensitySignal_RejectsSubnormalInputsAndProductsButPreservesZero()
    {
        var uniform = new SilkVolumeDensitySamples(1, 1, false);
        var mixed = new SilkVolumeDensitySamples(1, 1, true);
        var large = new SilkVolumeDensitySamples(1e30f, 1e30f, false);
        var negative = new SilkVolumeDensitySamples(1e-40f, 0, true);
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensitySignal(
                large, 1e-40f, 1, 2, "/subnormal-scale"))
            .Throws<InvalidDataException>().WithMessageContaining("too small");
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensitySignal(
                mixed, 1, 1, 2, "/subnormal-sample"))
            .Throws<InvalidDataException>().WithMessageContaining("too small");
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensitySignal(
                uniform, 1e-10f, 1e-30f, 2, "/product"))
            .Throws<InvalidDataException>().WithMessageContaining("too small");
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensitySignal(
            uniform, 0, 1, 2, "/zero-density")).ThrowsNothing();
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensitySignal(
            mixed, 1, 0, 2, "/zero-opacity")).ThrowsNothing();
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensitySignal(
            negative, 1, 1, 2, "/negative-only")).ThrowsNothing();
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensitySignal(
            uniform, 1e-20f, 1, 2, "/corrected")).ThrowsNothing();
    }

    [Test]
    public async Task ValidateDensitySignal_RejectsInvalidMetadataAndStepCounts()
    {
        var invalid = new SilkVolumeDensitySamples(1, float.NaN, false);
        var uniform = new SilkVolumeDensitySamples(1, 1, false);
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensitySignal(
                invalid, 1, 1, 2, "/invalid"))
            .Throws<InvalidDataException>().WithMessageContaining("/invalid");
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensitySignal(
                uniform, 1, float.NaN, 2, "/opacity"))
            .Throws<InvalidDataException>().WithMessageContaining("/opacity");
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensitySignal(
                uniform, 1, 1, 0, "/steps"))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(float.NaN)]
    [Arguments(float.PositiveInfinity)]
    [Arguments(float.NegativeInfinity)]
    public async Task ValidateDensityScalar_RefusesNonfiniteValuesAndAcceptsCorrectedRetry(float density)
    {
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
        {
            SilkVolumeCameraValidation.ValidateDensityScalar(density, "/density");
            return Task.CompletedTask;
        }) ?? throw new InvalidOperationException("Expected an invalid density scalar exception.");
        await Assert.That(exception.Message).Contains("'/density'");
        await Assert.That(exception.Message).Contains("nonfinite density scalar");
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensityScalar(1, "/density"))
            .ThrowsNothing();
    }

    [Test]
    [Arguments(float.NaN)]
    [Arguments(float.PositiveInfinity)]
    [Arguments(float.NegativeInfinity)]
    public async Task ValidateDensitySamples_ReportsAssetAndVoxelAndAcceptsCorrectedRetry(float sample)
    {
        byte[] pixels = new byte[3 * sizeof(float)];
        BinaryPrimitives.WriteSingleLittleEndian(pixels.AsSpan(sizeof(float)), sample);
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
        {
            SilkVolumeCameraValidation.ValidateDensitySamples(pixels, "fixture.raw");
            return Task.CompletedTask;
        }) ?? throw new InvalidOperationException("Expected an invalid density sample exception.");
        await Assert.That(exception.Message).Contains("'fixture.raw'");
        await Assert.That(exception.Message).Contains("voxel 1");
        BinaryPrimitives.WriteSingleLittleEndian(pixels.AsSpan(sizeof(float)), 200);
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensitySamples(pixels, "fixture.raw"))
            .ThrowsNothing();
    }

    [Test]
    public async Task ValidateDensitySamples_RefusesTruncatedSample()
    {
        byte[] pixels = new byte[sizeof(float) + 1];
        await Assert.That(() => SilkVolumeCameraValidation.ValidateDensitySamples(pixels, "truncated.raw"))
            .Throws<InvalidDataException>().WithMessageContaining("truncated R32 sample");
    }

    [Test]
    public async Task ValidateWorkload_ReportsExplicitEightFieldAndPerPixelBounds()
    {
        SilkVolumeCameraWorkload workload =
            SilkVolumeCameraValidation.ValidateWorkload(8, 1920, 1080);

        await Assert.That(workload.FieldCount).IsEqualTo(8);
        await Assert.That(workload.MaximumIntervalCount).IsEqualTo(17);
        await Assert.That(workload.MaximumEvaluationsPerPixel).IsEqualTo(64_040);
        await Assert.That(workload.MaximumFrameEvaluations)
            .IsEqualTo(1920UL * 1080UL * 64_040UL);
    }

    [Test]
    public async Task ValidateWorkload_RefusesNinthFieldBeforePublication()
    {
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Task.FromResult(SilkVolumeCameraValidation.ValidateWorkload(9, 1920, 1080)))
            ?? throw new InvalidOperationException("Expected an invalid volume workload exception.");
        await Assert.That(exception.Message).Contains("supports 1 to 8 fields");
        await Assert.That(exception.Message).Contains("Reduce the active density volumes");
    }

    [Test]
    public async Task ValidateWorkload_RefusesFrameEvaluationBoundOverflow()
    {
        await Assert.That(() =>
                SilkVolumeCameraValidation.ValidateWorkload(1, uint.MaxValue, uint.MaxValue))
            .Throws<OverflowException>();
    }

    [Test]
    [Arguments(64_040)]
    public async Task ValidateEvaluationBound_AcceptsExactCeiling(int evaluations)
    {
        await Assert.That(() => SilkVolumeCameraValidation.ValidateEvaluationBound(evaluations))
            .ThrowsNothing();
    }

    [Test]
    [Arguments(64_041)]
    public async Task ValidateEvaluationBound_RefusesExcessAndAcceptsCorrectedRetry(int evaluations)
    {
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
        {
            SilkVolumeCameraValidation.ValidateEvaluationBound(evaluations);
            return Task.CompletedTask;
        }) ?? throw new InvalidOperationException("Expected an invalid volume evaluation bound exception.");
        await Assert.That(exception.Message).Contains($"{evaluations} evaluations per pixel");
        await Assert.That(exception.Message).Contains("64040 ceiling");
        await Assert.That(() => SilkVolumeCameraValidation.ValidateEvaluationBound(evaluations - 1))
            .ThrowsNothing();
    }

    [Test]
    public async Task ValidateWorkload_UsesActualAxesAndBothPassesAtSceneAdmissionBoundary()
    {
        uint[] axes = Enumerable.Repeat(16u, 8).ToArray();
        axes[0] = 21;
        SilkVolumeCameraWorkload admitted =
            SilkVolumeCameraValidation.ValidateWorkload(axes, 1920, 1080);
        await Assert.That(admitted.MaximumEvaluationsPerPixel).IsEqualTo(63_840);
        await Assert.That(admitted.MaximumFrameEvaluations)
            .IsEqualTo(1920UL * 1080UL * 63_840UL);

        axes[0] = 22;
        await Assert.That(() => SilkVolumeCameraValidation.ValidateWorkload(axes, 1920, 1080))
            .Throws<InvalidDataException>().WithMessageContaining("64320 evaluations per pixel");
        axes[0] = 21;
        await Assert.That(SilkVolumeCameraValidation.ValidateWorkload(axes, 1920, 1080))
            .IsEqualTo(admitted);
    }

    [Test]
    public async Task ValidateWorkload_RefusesEightDeepFieldsUsingGlobalStepBackstop()
    {
        uint[] axes = Enumerable.Repeat(512u, 8).ToArray();
        await Assert.That(() => SilkVolumeCameraValidation.ValidateWorkload(axes, 1, 1))
            .Throws<InvalidDataException>().WithMessageContaining("1966080 evaluations per pixel");
        axes.AsSpan().Fill(1);
        SilkVolumeCameraWorkload corrected =
            SilkVolumeCameraValidation.ValidateWorkload(axes, 1, 1);
        await Assert.That(corrected.MaximumEvaluationsPerPixel).IsEqualTo(3840);
    }

    [Test]
    [Arguments(7117)]
    public async Task ValidatePositionBound_AcceptsExactCeiling(int positions)
    {
        await Assert.That(() => SilkVolumeCameraValidation.ValidatePositionBound(positions))
            .ThrowsNothing();
        await Assert.That(() => SilkVolumeCameraValidation.ValidatePositionBound(positions + 1))
            .Throws<InvalidDataException>().WithMessageContaining("7118 positions per phase");
    }

    [Test]
    public async Task ValidateWorkload_RefusesPositionExcessEvenBelowEvaluationCeiling()
    {
        uint[] axes = Enumerable.Repeat(127u, 4).ToArray();
        SilkVolumeCameraWorkload admitted = SilkVolumeCameraValidation.ValidateWorkload(axes, 1, 1);
        await Assert.That(admitted.MaximumEvaluationsPerPixel).IsEqualTo(56_896);
        axes[0]++;
        await Assert.That(() => SilkVolumeCameraValidation.ValidateWorkload(axes, 1, 1))
            .Throws<InvalidDataException>().WithMessageContaining("7126 positions per phase");
        axes[0]--;
        await Assert.That(SilkVolumeCameraValidation.ValidateWorkload(axes, 1, 1)).IsEqualTo(admitted);
    }

    [Test]
    public async Task ValidateRayOrigin_RefusesJointProductOverflowAndAcceptsCorrectedRetry()
    {
        double[] transform =
        [
            1.2e-18, 0, 0, 0,
            4, 1, 0, 0,
            0, 0, 10, 0,
            0, 0, 0, 1
        ];
        double[] inverse = SilkVolumeCameraValidation.InvertObjectToEye(
            transform, Identity(), "joint-product fixture");
        await Assert.That(() => SilkVolumeCameraValidation.ValidateRayOrigin(
                inverse, 1e25, "joint-product fixture"))
            .Throws<InvalidDataException>().WithMessageContaining("composed camera ray origin");
        await Assert.That(() => SilkVolumeCameraValidation.ValidateRayOrigin(
            inverse, 1e10, "corrected joint-product fixture")).ThrowsNothing();
    }

    [Test]
    public async Task ValidateProjection_RefusesFiniteEndpointsWhoseRayLengthArithmeticCanOverflow()
    {
        double[] projection = Identity();
        projection[10] = -1e-25;
        await Assert.That(() => SilkVolumeCameraValidation.ValidateProjection(
                projection, false, "finite endpoints with overflowing ray length"))
            .Throws<InvalidDataException>().WithMessageContaining("ray-length arithmetic");
        projection[10] = -1e-10;
        await Assert.That(() => SilkVolumeCameraValidation.ValidateProjection(
            projection, false, "corrected ray range")).ThrowsNothing();
    }

    [Test]
    [Arguments(0u)]
    [Arguments(513u)]
    public async Task ValidateWorkload_RefusesInvalidSamplingAxis(uint axis)
    {
        await Assert.That(() =>
                SilkVolumeCameraValidation.ValidateWorkload(new[] { axis }, 1, 1))
            .Throws<InvalidDataException>().WithMessageContaining("between 1 and 512");
    }

    [Test]
    public async Task VolumeCameraLayouts_UniformFallbackDoesNotDeclareThreeDimensionalResources()
    {
        SilkBindingLayoutDescriptor uniform =
            SilkBindingLayoutDescriptor.VolumeCameraParameters(densityAtlas: false);
        SilkBindingLayoutDescriptor atlas =
            SilkBindingLayoutDescriptor.VolumeCameraParameters(densityAtlas: true);

        await Assert.That(uniform.Validate).ThrowsNothing();
        await Assert.That(atlas.Validate).ThrowsNothing();
        await Assert.That(SilkBindingLayoutDescriptor.SceneParameters.Validate).ThrowsNothing();
        await Assert.That(uniform.UniformByteSize).IsEqualTo(32u);
        await Assert.That(uniform.MaterialSlots.Any(
            static slot => slot.Binding == SilkBindingLayoutDescriptor.VolumeCameraFieldBufferBinding &&
                slot.Kind == SilkBindingKind.StorageBuffer)).IsTrue();
        await Assert.That(uniform.MaterialSlots.Any(
            static slot => slot.Binding == SilkBindingLayoutDescriptor.FrameParametersBinding &&
                slot.Kind == SilkBindingKind.StorageBuffer)).IsTrue();
        await Assert.That(uniform.MaterialSlots.Any(
            static slot => slot.Binding == SilkBindingLayoutDescriptor.VolumeOpaqueDepthTextureBinding))
            .IsTrue();
        await Assert.That(uniform.MaterialSlots.Any(
            static slot => slot.Binding == SilkBindingLayoutDescriptor.VolumeDensityTextureBinding ||
                slot.Binding == SilkBindingLayoutDescriptor.VolumeSamplerBinding)).IsFalse();
        await Assert.That(atlas.MaterialSlots.Any(
            static slot => slot.Binding == SilkBindingLayoutDescriptor.VolumeDensityTextureBinding &&
                slot.Kind == SilkBindingKind.SampledTexture)).IsTrue();
        await Assert.That(atlas.MaterialSlots.Any(
            static slot => slot.Binding == SilkBindingLayoutDescriptor.VolumeSamplerBinding &&
                slot.Kind == SilkBindingKind.Sampler)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task VolumeCameraLayouts_RejectAlteredUniformContracts(bool densityAtlas)
    {
        SilkBindingLayoutDescriptor layout =
            SilkBindingLayoutDescriptor.VolumeCameraParameters(densityAtlas);
        SilkBindingSlot[] reordered = layout.MaterialSlots.Reverse().ToArray();
        SilkBindingSlot[] duplicate = layout.MaterialSlots.ToArray();
        duplicate[^1] = duplicate[0];

        await Assert.That(() => (layout with { MaterialSlots = reordered }).Validate())
            .ThrowsNothing();
        await Assert.That(() => (layout with { UniformByteSize = 16 }).Validate())
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => (layout with { Visibility = SilkShaderStageVisibility.Vertex }).Validate())
            .Throws<ArgumentException>();
        await Assert.That(() => (layout with
        {
            Visibility = SilkShaderStageVisibility.Vertex | SilkShaderStageVisibility.Fragment
        }).Validate()).Throws<ArgumentException>();
        await Assert.That(() => (layout with { MaterialSlots = [] }).Validate())
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => (layout with { MaterialSlots = duplicate }).Validate())
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => (SilkBindingLayoutDescriptor.SceneParameters with
        {
            UniformByteSize = 32,
            Visibility = SilkShaderStageVisibility.Fragment
        }).Validate()).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task CameraInverseUploadMatchesColumnShaderMultiplicationForTranslatedRotatedShearedField()
    {
        // Row-vector local->eye maps (x,y,z) to (2*y+5,-x+y-7,3*z+11).
        double[] transform =
        [
            0, -1, 0, 0,
            2, 1, 0, 0,
            0, 0, 3, 0,
            5, -7, 11, 1
        ];
        double[] inverse = SilkVolumeCameraValidation.InvertObjectToEye(
            transform, Identity(), "translated rotated sheared field");
        var bytes = new byte[64];
        SilkVolumeCameraValidation.WriteEyeToObject(bytes, inverse);
        float[] point = [13, -5, 5, 1]; // Independent forward mapping of (2,4,-2).
        float[] direction = [8, 2, -6, 0];
        float[] expectedPoint = [2, 4, -2, 1];
        float[] expectedDirection = [2, 4, -2, 0];
        for (int row = 0; row < 4; row++)
        {
            float actualPoint = 0;
            float actualDirection = 0;
            for (int column = 0; column < 4; column++)
            {
                float component = BinaryPrimitives.ReadSingleLittleEndian(
                    bytes.AsSpan((row * 4 + column) * sizeof(float), sizeof(float)));
                actualPoint += component * point[column];
                actualDirection += component * direction[column];
            }
            await Assert.That(Math.Abs(actualPoint - expectedPoint[row])).IsLessThanOrEqualTo(1e-5f);
            await Assert.That(Math.Abs(actualDirection - expectedDirection[row])).IsLessThanOrEqualTo(1e-5f);
        }
        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(48))).IsEqualTo(0f);
        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(60))).IsEqualTo(1f);
    }

    [Test]
    public async Task InvertObjectToEye_ComposesViewAndWorldTransformBeforeNarrowing()
    {
        double[] worldTransform = Identity();
        worldTransform[12] = 100_000_000;
        double[] worldToEye = Identity();
        worldToEye[0] = 0.001;
        worldToEye[5] = 0.001;
        worldToEye[10] = 0.001;
        worldToEye[12] = -50_000;

        double[] inverse = SilkVolumeCameraValidation.InvertObjectToEye(
            worldTransform,
            worldToEye,
            "fixture volume");

        await Assert.That(inverse.Length).IsEqualTo(16);
        await Assert.That(inverse.All(double.IsFinite)).IsTrue();
        await Assert.That(Math.Abs(inverse[12] - -50_000_000d)).IsLessThanOrEqualTo(1e-6);
    }

    [Test]
    public async Task InvertObjectToEye_RefusesAFloatBasisThatFailsShaderRelativeDeterminantRule()
    {
        double[] view = Identity();
        view[1] = 100_000_000;

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Task.FromResult(SilkVolumeCameraValidation.InvertObjectToEye(
                    Identity(),
                    view,
                    "'/volume' instance 0 bound to material '/mat'")))
            ?? throw new InvalidOperationException("Expected an invalid volume transform exception.");
        await Assert.That(exception.Message).Contains("relative determinant tolerance");
        await Assert.That(exception.Message).Contains("bake the transform");
    }

    [Test]
    public async Task InvertObjectToEye_RefusesInverseThatOverflowsFloatEvenWhenWorldTransformIsFinite()
    {
        double[] view = Identity();
        view[0] = 1e-40;
        view[5] = 1e-40;
        view[10] = 1e-40;

        await Assert.That(() =>
                SilkVolumeCameraValidation.InvertObjectToEye(
                    Identity(),
                    view,
                    "fixture volume"))
            .Throws<InvalidDataException>()
            .WithMessageContaining("inverse object-to-eye matrix exceeds the finite shader range");
    }

    [Test]
    public async Task InvertObjectToEye_RefusesFloatBasisWhoseShaderReciprocalOverflows()
    {
        double[] view = Identity();
        view[0] = 1e-15;
        view[5] = 1e-15;
        view[10] = 1e-15;

        await Assert.That(() =>
                SilkVolumeCameraValidation.InvertObjectToEye(
                    Identity(),
                    view,
                    "small-scale camera"))
            .Throws<InvalidDataException>()
            .WithMessageContaining("underflows the shader inverse-basis arithmetic");
    }

    [Test]
    public async Task InvertObjectToEye_RefusesFiniteInverseWhoseObliqueRayLengthCanOverflow()
    {
        double[] transform =
        [
            1.2e-19, 0, 0, 0,
            4, 1, 0, 0,
            0, 0, 10, 0,
            0, 0, 0, 1
        ];
        await Assert.That(() => SilkVolumeCameraValidation.InvertObjectToEye(
                transform, Identity(), "overflowing oblique ray"))
            .Throws<InvalidDataException>()
            .WithMessageContaining("ray-length arithmetic");

        transform[0] = 1.2e-9;
        double[] corrected = SilkVolumeCameraValidation.InvertObjectToEye(
            transform, Identity(), "corrected oblique ray");
        await Assert.That(corrected[0]).IsEqualTo(1 / transform[0]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ValidateProjection_RefusesInteriorHomogeneousHorizon(bool flipY)
    {
        // After GL-depth conversion and inversion, eye z=1/(4-8*depth).
        // Both endpoints are finite, but the interior crosses infinity.
        double[] projection =
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 0, 1,
            0, 0, -0.25, 0
        ];
        await Assert.That(() => SilkVolumeCameraValidation.ValidateProjection(
                projection, flipY, "interior horizon"))
            .Throws<InvalidDataException>()
            .WithMessageContaining("homogeneous horizon");
        SilkVolumeCameraValidation.ValidateProjection(
            Perspective(1, double.PositiveInfinity), flipY, "corrected infinite far");
    }

    [Test]
    [Arguments(2.0, 0.18, -0.11)]
    [Arguments(4000.0, 0.00005, -0.00004)]
    public async Task ValidateProjection_AcceptsOffcenterObliqueConventionalProjection(
        double far, double xDepth, double yDepth)
    {
        double[] projection = Perspective(0.5, far);
        projection[1] = 0.07;
        projection[2] = xDepth;
        projection[4] = -0.04;
        projection[6] = yDepth;
        projection[8] = 0.03;
        projection[9] = -0.02;

        await Assert.That(() => SilkVolumeCameraValidation.ValidateProjection(
            projection,
            flipClipSpaceY: false,
            "oblique camera")).ThrowsNothing();
    }

    [Test]
    public async Task ValidateProjection_RefusesObliqueFarFaceThatCrossesHomogeneousHorizon()
    {
        double[] projection = Perspective(0.5, 4000);
        projection[2] = 0.18;

        // For this projection, a far NDC corner has
        // w = (1 + projection[2] * x + projection[10]) / projection[14].
        double leftW = (1 - projection[2] + projection[10]) / projection[14];
        double rightW = (1 + projection[2] + projection[10]) / projection[14];
        await Assert.That(leftW).IsGreaterThan(0d);
        await Assert.That(rightW).IsLessThan(0d);
        await Assert.That(() => SilkVolumeCameraValidation.ValidateProjection(
                projection, flipClipSpaceY: false, "crossing oblique camera"))
            .Throws<InvalidDataException>()
            .WithMessageContaining("homogeneous horizon");

        projection[2] = 0.00005;
        await Assert.That(() => SilkVolumeCameraValidation.ValidateProjection(
                projection, flipClipSpaceY: false, "corrected oblique camera"))
            .ThrowsNothing();
    }

    [Test]
    public async Task ValidateProjection_AcceptsConventionalNearOneMillionFarTwentyMillionAndInfiniteFar()
    {
        await Assert.That(() => SilkVolumeCameraValidation.ValidateProjection(
            Perspective(1_000_000, 20_000_000),
            flipClipSpaceY: false,
            "finite large-range camera")).ThrowsNothing();
        await Assert.That(() => SilkVolumeCameraValidation.ValidateProjection(
            Perspective(1_000_000, double.PositiveInfinity),
            flipClipSpaceY: true,
            "infinite-far camera")).ThrowsNothing();
    }

    [Test]
    public async Task ValidateProjection_RefusesFiniteFarBoundaryLostAfterFloatProjectionNarrowing()
    {
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
        {
            SilkVolumeCameraValidation.ValidateProjection(
                    Perspective(0.1, 20_000_000),
                    flipClipSpaceY: false,
                    "lossy finite-far camera");
            return Task.CompletedTask;
        }) ?? throw new InvalidOperationException("Expected an invalid volume projection exception.");
        await Assert.That(exception.Message).Contains("finite clip boundary becomes infinite");
        await Assert.That(exception.Message).Contains("update was refused");
    }

    [Test]
    public async Task ValidateProjection_RefusesReversedDepthWithActionableCameraDiagnostic()
    {
        double[] reversed = Perspective(0.1, 100);
        for (int row = 0; row < 4; row++)
        {
            reversed[row * 4 + 2] *= -1;
        }

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
        {
            SilkVolumeCameraValidation.ValidateProjection(
                    reversed,
                    flipClipSpaceY: false,
                    "camera");
            return Task.CompletedTask;
        }) ?? throw new InvalidOperationException("Expected an invalid volume projection exception.");
        await Assert.That(exception.Message).Contains("reversed depth");
        await Assert.That(exception.Message).Contains("use conventional near-to-far depth");
    }

    private static double[] Identity() =>
    [
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1
    ];

    private static double[] Perspective(double near, double far)
    {
        double depthScale = double.IsPositiveInfinity(far)
            ? -1
            : -(far + near) / (far - near);
        double depthOffset = double.IsPositiveInfinity(far)
            ? -2 * near
            : -(2 * far * near) / (far - near);
        return
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, depthScale, -1,
            0, 0, depthOffset, 0
        ];
    }
}
