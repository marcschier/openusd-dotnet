// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkPointShadowDepthEncodingTests
{
    [Test]
    public async Task DistantCubeDepthSeparatesCastersWithoutFittingTheNearPlane()
    {
        SilkShadowDescriptor[] descriptors = CreateCube(0.0000220594, 49.89036);
        SilkShadowAtlasLayout layout = SilkShadowAtlasLayout.Create(descriptors)!;
        SilkShadowFrameBinding binding = SilkShadowFrameBinding.Create(descriptors, layout);
        float scale = binding.GetPointDepthScaleForLight(0);
        Matrix4x4 matrix = binding.GetWorldToLightClip(5);
        Vector4 caster = Vector4.Transform(new Vector4(0, 0, 0.8f, 1), matrix);
        Vector4 receiver = Vector4.Transform(new Vector4(0, 0, 0.2f, 1), matrix);
        Vector4 movedCaster = Vector4.Transform(new Vector4(0, 0, 40, 1), matrix);

        await Assert.That(scale).IsGreaterThan(0f);
        await Assert.That(caster.W * scale).IsLessThan(receiver.W * scale);
        await Assert.That((receiver.W - caster.W) * scale).IsGreaterThan(0.01f);
        await Assert.That(movedCaster.W).IsEqualTo(10f);
        await Assert.That(movedCaster.Z / movedCaster.W).IsGreaterThanOrEqualTo(0f);
        await Assert.That(movedCaster.Z / movedCaster.W).IsLessThanOrEqualTo(1f);
        await Assert.That(movedCaster.W * scale).IsLessThan(caster.W * scale);
    }

    [Test]
    [Arguments(0.001, 4.0, 1e-6f)]
    [Arguments(0.0000220594, 49.89036, 6.5e-10f)]
    [Arguments(0.05, 100.0, 2e-5f)]
    public async Task CubeBiasPreservesThePublishedPerspectiveBias(
        double near, double far, float publishedBias)
    {
        SilkShadowDescriptor[] descriptors = CreateCube(near, far, publishedBias);
        SilkShadowAtlasLayout layout = SilkShadowAtlasLayout.Create(descriptors)!;
        SilkShadowFrameBinding binding = SilkShadowFrameBinding.Create(descriptors, layout);
        float scale = binding.GetPointDepthScaleForLight(0);
        float linearBias = binding.GetControls(0).X;
        double coefficient = far / (far - near);
        double translation = near * coefficient;

        foreach (double fraction in (double[])[0.01, 0.1, 0.5, 0.99])
        {
            double distance = near + ((far - near) * fraction);
            foreach (double slopeFactor in (double[])[1, 2, 3])
            {
                double linear = distance * scale;
                double biasedDistance = (linear / (1 + (linearBias * slopeFactor * linear))) / scale;
                double mapped = coefficient - (translation / biasedDistance);
                double unadjusted = coefficient - (translation / distance);
                double expectedBias = publishedBias * slopeFactor;
                double tolerance = Math.Max(1e-14, expectedBias * 5e-6);
                await Assert.That(Math.Abs((unadjusted - mapped) - expectedBias)).IsLessThan(tolerance);
            }
        }
    }

    [Test]
    public async Task CubeFacesUseOneScaleWhenTheirFarPlanesDiffer()
    {
        SilkShadowDescriptor[] descriptors = CreateCube(0.001, 4);
        descriptors[4] = CreateDescriptor(4, 0.01, 12, 0.0001f);
        SilkShadowAtlasLayout layout = SilkShadowAtlasLayout.Create(descriptors)!;
        SilkShadowFrameBinding binding = SilkShadowFrameBinding.Create(descriptors, layout);
        float scale = binding.GetPointDepthScaleForLight(0);

        await Assert.That(Math.Abs(scale - (1f / 12f))).IsLessThan(1e-7f);
        await Assert.That(MathF.BitIncrement(12f) * scale).IsLessThanOrEqualTo(1f);
        await Assert.That(binding.GetMapCountForLight(0)).IsEqualTo(6);
        await Assert.That(binding.GetControls(4).X).IsGreaterThan(0f);
        await Assert.That(binding.GetControls(0).X).IsEqualTo(0f);
    }

    [Test]
    public async Task CubeGroupRejectsFaceWhoseLinearNearDepthWouldUnderflow()
    {
        SilkShadowDescriptor[] descriptors = CreateCube(1e-30, 1e-29);
        descriptors[0] = CreateDescriptor(0, 1e20, 1e21, 0);

        await Assert.That(() =>
            SilkPointShadowDepthEncoding.Prepare(descriptors, new float[SilkFrameState.MaximumLights]))
            .Throws<InvalidDataException>()
            .WithMessageContaining("depth range");
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task InvalidCubeDepthProjectionIsRejected(int invalidField)
    {
        SilkShadowDescriptor[] descriptors = CreateCube(0.001, 4);
        if (invalidField == 0)
        {
            descriptors[3].Projection[10] = -1;
        }
        else if (invalidField == 1)
        {
            descriptors[3].View[3] = 1;
        }
        else if (invalidField == 2)
        {
            descriptors[3].Projection[2] = 1;
        }
        else
        {
            descriptors[3] = descriptors[3] with { DepthBias = float.MaxValue };
        }
        SilkShadowAtlasLayout layout = SilkShadowAtlasLayout.Create(descriptors)!;

        await Assert.That(() => SilkShadowFrameBinding.Create(descriptors, layout))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task OrthographicMapsRetainTheirPublishedDepthBias()
    {
        SilkShadowDescriptor descriptor = CreateDescriptor(0, 0.001, 4, 0.125f) with
        {
            Flags = SilkShadowDescriptorOptions.Orthographic,
        };
        SilkShadowAtlasLayout layout = SilkShadowAtlasLayout.Create([descriptor])!;
        SilkShadowFrameBinding binding = SilkShadowFrameBinding.Create([descriptor], layout);

        await Assert.That(binding.GetPointDepthScaleForLight(0)).IsEqualTo(0f);
        await Assert.That(binding.GetControls(0).X).IsEqualTo(0.125f);
    }

    [Test]
    public async Task CasterInstanceWritesTheDepthScaleAndClearsOtherReservedValues()
    {
        byte[] destination = Enumerable.Repeat((byte)0xff, 80).ToArray();
        SilkShadowInstanceWriter.Write(Matrix4x4.Identity, destination, 0.125f);

        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(destination.AsSpan(64, 4)))
            .IsEqualTo(0.125f);
        await Assert.That(destination.AsSpan(68, 12).ToArray()).IsEquivalentTo(new byte[12]);
    }

    [Test]
    [Arguments(-1f)]
    [Arguments(float.NaN)]
    [Arguments(float.PositiveInfinity)]
    public async Task CasterInstanceRejectsInvalidDepthScale(float scale)
    {
        await Assert.That(() => SilkShadowInstanceWriter.Write(Matrix4x4.Identity, new byte[80], scale))
            .Throws<InvalidDataException>();
    }

    private static SilkShadowDescriptor[] CreateCube(double near, double far, float bias = 0) =>
        [.. Enumerable.Range(0, 6).Select(face => CreateDescriptor(face, near, far, bias))];

    private static SilkShadowDescriptor CreateDescriptor(int face, double near, double far, float bias)
    {
        double[] view =
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, -50, 1,
        ];
        double[] projection =
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, -(far + near) / (far - near), -1,
            0, 0, -(2 * far * near) / (far - near), 0,
        ];
        return new SilkShadowDescriptor(
            0, (uint)face, 256, SilkShadowDescriptorOptions.CubeFace, bias, 0, 1)
        {
            View = view,
            Projection = projection,
        };
    }
}
