// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;

namespace OpenUsd.Rendering.Silk;

internal readonly record struct SilkVolumeCameraWorkload(
    int FieldCount,
    int MaximumIntervalCount,
    int MaximumEvaluationsPerPixel,
    ulong MaximumFrameEvaluations,
    int MaximumStepsPerSegment);

internal readonly record struct SilkVolumeDensitySamples(
    float MaximumMagnitude,
    float MaximumPositive,
    bool HasSubnormal);

internal static class SilkVolumeCameraValidation
{
    internal const int MaximumFieldCount = SilkVolumeAtlas.MaximumFieldCount;
    internal const int MaximumIntervalCount = (MaximumFieldCount * 2) + 1;
    private const int MaximumCompositorStepsPerPixel = 7_100 + MaximumIntervalCount;
    internal const int MaximumEvaluationsPerPixel = 64_040;
    internal const float MinimumNormalFloat = 1.17549435e-38f;

    internal static void ValidateMaterialChannels(SilkMaterialData material)
    {
        ValidateMaterialChannel(material, SilkMaterialParameter.DiffuseColor);
        ValidateMaterialChannel(material, SilkMaterialParameter.Opacity);
    }

    private static void ValidateMaterialChannel(
        SilkMaterialData material,
        SilkMaterialParameter parameter)
    {
        ReadOnlySpan<float> values = material.GetScalar(parameter);
        for (int component = 0; component < values.Length; component++)
        {
            if (!float.IsFinite(values[component]))
            {
                throw new InvalidDataException(
                    $"Density-volume material '{material.Path}' has a nonfinite {parameter} component {component}. " +
                    "Author finite material channels and retry; the volume update was refused.");
            }
        }
    }

    internal static Vector3 GetTint(ReadOnlySpan<float> diffuse)
    {
        float red = diffuse.Length > 0 ? diffuse[0] : 0.18f;
        float green = diffuse.Length > 1 ? diffuse[1] : red;
        float blue = diffuse.Length > 2 ? diffuse[2] : red;
        return new Vector3(Math.Max(0, red), Math.Max(0, green), Math.Max(0, blue));
    }

    internal static void ValidateTintExposure(
        ReadOnlySpan<float> diffuse,
        float exposure,
        bool displayTransformRequested,
        string materialPath)
    {
        bool finiteTint = true;
        foreach (float component in diffuse)
        {
            finiteTint &= float.IsFinite(component);
        }
        Vector3 tint = GetTint(diffuse);
        double scale = Math.Pow(2, exposure);
        // A requested display transform can fall back to rendering directly.
        if (displayTransformRequested)
        {
            scale = Math.Max(1, scale);
        }
        double maximumTint = Math.Max(tint.X, Math.Max(tint.Y, tint.Z));
        if (!float.IsFinite(exposure) || !finiteTint ||
            !float.IsFinite(tint.X) || !float.IsFinite(tint.Y) || !float.IsFinite(tint.Z) ||
            !double.IsFinite(scale) || scale > float.MaxValue ||
            maximumTint * scale > float.MaxValue)
        {
            throw new InvalidDataException(
                $"Density-volume material '{materialPath}' has tint and exposure outside " +
                "finite shader color arithmetic. Reduce tint or exposure and retry; " +
                "the volume update was refused.");
        }
    }

    internal static void ValidateDensityScalar(float density, string materialPath)
    {
        if (!float.IsFinite(density))
        {
            throw new InvalidDataException(
                $"Density-volume material '{materialPath}' has a nonfinite density scalar. " +
                "Author a finite density and retry; the volume update was refused.");
        }
    }

    internal static float ValidateDensitySamples(ReadOnlySpan<byte> pixels, string asset) =>
        InspectDensitySamples(pixels, asset).MaximumMagnitude;

    internal static SilkVolumeDensitySamples InspectDensitySamples(ReadOnlySpan<byte> pixels, string asset)
    {
        if (pixels.Length % sizeof(float) != 0)
        {
            throw new InvalidDataException($"Density field '{asset}' has a truncated R32 sample.");
        }
        float maximumMagnitude = 0;
        float maximumPositive = 0;
        bool hasSubnormal = false;
        for (int offset = 0; offset < pixels.Length; offset += sizeof(float))
        {
            float sample = BinaryPrimitives.ReadSingleLittleEndian(pixels[offset..]);
            if (!float.IsFinite(sample))
            {
                throw new InvalidDataException(
                    $"Density field '{asset}' has a nonfinite R32 sample at voxel {offset / sizeof(float)}. " +
                    "Correct the field and retry; the volume update was refused.");
            }
            maximumMagnitude = Math.Max(maximumMagnitude, Math.Abs(sample));
            maximumPositive = Math.Max(maximumPositive, sample);
            uint magnitudeBits = (uint)BitConverter.SingleToInt32Bits(sample) & 0x7fff_ffffu;
            hasSubnormal |= magnitudeBits is > 0 and < 0x0080_0000u;
        }
        return new SilkVolumeDensitySamples(maximumMagnitude, maximumPositive, hasSubnormal);
    }

    internal static void ValidateDensityProduct(float maximumSampleMagnitude, float density, string description)
    {
        ValidateDensityScalar(density, description);
        // Headroom covers trilinear interpolation intermediates and density scaling.
        double limit = float.MaxValue / 32d;
        double nonnegativeDensity = Math.Max(0d, density);
        if (!float.IsFinite(maximumSampleMagnitude) || maximumSampleMagnitude < 0 ||
            maximumSampleMagnitude > limit ||
            nonnegativeDensity > limit ||
            maximumSampleMagnitude * nonnegativeDensity > limit)
        {
            throw new InvalidDataException(
                $"Density field '{description}' exceeds finite shader interpolation or density-scale arithmetic. " +
                "Reduce sample magnitudes or density and retry; the volume update was refused.");
        }
    }

    internal static void ValidateDensitySignal(
        SilkVolumeDensitySamples samples,
        float density,
        float opacity,
        int maximumStepsPerSegment,
        string description)
    {
        ValidateDensityProduct(samples.MaximumMagnitude, density, description);
        if (!float.IsFinite(samples.MaximumPositive) || samples.MaximumPositive < 0 ||
            samples.MaximumPositive > samples.MaximumMagnitude || !float.IsFinite(opacity))
        {
            throw new InvalidDataException(
                $"Density field '{description}' has invalid density-sample or opacity admission metadata.");
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStepsPerSegment);
        double positiveDensity = Math.Max(0d, density);
        double positiveOpacity = Math.Clamp((double)opacity, 0, 1);
        if (positiveDensity == 0 || positiveOpacity == 0 || samples.MaximumPositive == 0)
        {
            return;
        }

        double maximumDensity = samples.MaximumPositive * positiveDensity;
        double maximumSignal = GetUnitRayOpticalDepth(maximumDensity, positiveOpacity);
        if (positiveDensity < MinimumNormalFloat || positiveOpacity < MinimumNormalFloat ||
            samples.HasSubnormal ||
            maximumSignal < (double)MinimumNormalFloat * maximumStepsPerSegment)
        {
            throw new InvalidDataException(
                $"Density field '{description}' has a density or opacity too small for " +
                "reliable volume camera accumulation. Increase the nonzero density or opacity, " +
                "or remove subnormal field samples, and retry; the volume update was refused.");
        }
    }

    private static double GetUnitRayOpticalDepth(double density, double opacity)
    {
        if (opacity >= 1)
        {
            return density;
        }

        double absorbed = density < 1e-4
            ? density * (1 - (density / 2) + (density * density / 6))
            : 1 - Math.Exp(-density);
        double removed = opacity * absorbed;
        return removed < 1e-4
            ? removed * (1 + (removed / 2) + (removed * removed / 3))
            : -Math.Log(1 - removed);
    }

    internal static SilkVolumeCameraWorkload ValidateWorkload(
        int fieldCount,
        uint width,
        uint height)
    {
        if (fieldCount is < 1 or > MaximumFieldCount)
        {
            throw new InvalidDataException(
                $"A joint density-volume pass supports 1 to {MaximumFieldCount} fields; " +
                $"received {fieldCount}. Reduce the active density volumes and retry.");
        }
        ulong maximumFrameEvaluations = checked(
            (ulong)width * height * MaximumEvaluationsPerPixel);
        return new SilkVolumeCameraWorkload(
            fieldCount,
            MaximumIntervalCount,
            MaximumEvaluationsPerPixel,
            maximumFrameEvaluations,
            checked(fieldCount * (int)SilkVolumeTextureExtent.MaximumAxisCount * 2));
    }

    internal static SilkVolumeCameraWorkload ValidateWorkload(
        ReadOnlySpan<uint> longestAxes,
        uint width,
        uint height)
    {
        int fieldCount = longestAxes.Length;
        if (fieldCount is < 1 or > MaximumFieldCount)
        {
            throw new InvalidDataException(
                $"A joint density-volume pass supports 1 to {MaximumFieldCount} fields; " +
                $"received {fieldCount}. Reduce the active density volumes and retry.");
        }

        uint summedAxes = 0;
        foreach (uint axis in longestAxes)
        {
            if (axis is < 1 or > SilkVolumeTextureExtent.MaximumAxisCount)
            {
                throw new InvalidDataException("A camera sampling axis must be between 1 and 512.");
            }
            summedAxes = checked(summedAxes + axis);
        }
        // Each nonempty event segment requests exactly two positions per active
        // field axis. There are at most 2*N-1 segments between the 2*N endpoints.
        // Integer arithmetic avoids a float-to-uint conversion or sample clamp.
        int intervals = checked((2 * fieldCount) - 1);
        int stepsPerSegment = checked((int)(2 * summedAxes));
        int steps = checked(stepsPerSegment * intervals);
        int evaluations = checked(2 * fieldCount * steps);
        ValidateEvaluationBound(evaluations);
        ValidatePositionBound(steps);
        ulong pixelCount = checked((ulong)width * height);
        ulong maximumFrameEvaluations = checked(
            pixelCount * (uint)evaluations);
        return new SilkVolumeCameraWorkload(
            fieldCount,
            MaximumIntervalCount,
            evaluations,
            maximumFrameEvaluations,
            stepsPerSegment);
    }

    internal static void ValidateEvaluationBound(int evaluations)
    {
        if (evaluations < 0 || evaluations > MaximumEvaluationsPerPixel)
        {
            throw new InvalidDataException(
                $"The two-pass density camera workload requires {evaluations} evaluations per pixel, " +
                $"exceeding the {MaximumEvaluationsPerPixel} ceiling. Reduce active fields or grid axes and retry.");
        }
    }

    internal static void ValidatePositionBound(int positions)
    {
        if (positions < 0 || positions > MaximumCompositorStepsPerPixel)
        {
            throw new InvalidDataException(
                $"The two-pass density camera workload requires {positions} positions per phase, " +
                $"exceeding the {MaximumCompositorStepsPerPixel} ceiling. Reduce active fields or grid axes and retry.");
        }
    }

    internal static void ValidateRayOrigin(
        ReadOnlySpan<double> eyeToObject,
        double maximumEyeCoordinate,
        string description)
    {
        // Bound each row of the column-vector shader multiply, using the
        // row-vector inverse before upload transposition.
        for (int component = 0; component < 3; component++)
        {
            double bound = Math.Abs((float)eyeToObject[12 + component]);
            for (int axis = 0; axis < 3; axis++)
            {
                bound += Math.Abs((float)eyeToObject[(axis * 4) + component]) * maximumEyeCoordinate;
            }
            if (!double.IsFinite(bound) || bound > float.MaxValue / 16d)
            {
                throw Refusal(description, "the composed camera ray origin exceeds finite shader point arithmetic");
            }
        }
    }

    internal static double[] InvertObjectToEye(
        ReadOnlySpan<double> objectToWorld,
        ReadOnlySpan<double> worldToEye,
        string volumeDescription)
    {
        ArgumentNullException.ThrowIfNull(volumeDescription);
        if (objectToWorld.Length != 16 || worldToEye.Length != 16)
        {
            throw new ArgumentException("Volume and view transforms must each contain 16 elements.");
        }

        double[] objectToEye = Multiply(objectToWorld, worldToEye);
        if (!TryInvert(objectToEye, out double[] inverse))
        {
            throw Refusal(volumeDescription, "the composed object-to-eye matrix is singular");
        }

        for (int index = 0; index < objectToEye.Length; index++)
        {
            if (!float.IsFinite((float)objectToEye[index]))
            {
                throw Refusal(
                    volumeDescription,
                    "the composed object-to-eye matrix exceeds the finite shader range");
            }
        }
        for (int index = 0; index < inverse.Length; index++)
        {
            if (!float.IsFinite((float)inverse[index]))
            {
                throw Refusal(
                    volumeDescription,
                    "the inverse object-to-eye matrix exceeds the finite shader range");
            }
        }
        ValidateShaderBasis(objectToEye, volumeDescription);
        double squaredDirectionBound = 0;
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                double component = (float)inverse[(row * 4) + column];
                squaredDirectionBound += component * component;
            }
        }
        // The Frobenius norm bounds every transformed unit eye direction, including
        // oblique rays. Leave headroom for the shader's float dot-product rounding.
        if (squaredDirectionBound > float.MaxValue / 4.0)
        {
            throw Refusal(
                volumeDescription,
                "the inverse camera basis can overflow the shader ray-length arithmetic");
        }
        return inverse;
    }

    internal static void WriteEyeToObject(Span<byte> destination, ReadOnlySpan<double> inverse)
    {
        if (destination.Length != 64 || inverse.Length != 16)
        {
            throw new ArgumentException("A camera inverse requires 16 doubles and exactly 64 output bytes.");
        }
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                float value = (float)inverse[column * 4 + row];
                if (!float.IsFinite(value))
                {
                    throw new InvalidDataException("A density-volume camera inverse exceeds the finite shader range.");
                }
                BinaryPrimitives.WriteSingleLittleEndian(
                    destination.Slice((row * 4 + column) * sizeof(float), sizeof(float)), value);
            }
        }
    }

    internal static double ValidateProjection(
        ReadOnlySpan<double> projection,
        bool flipClipSpaceY,
        string volumeDescription)
    {
        ArgumentNullException.ThrowIfNull(volumeDescription);
        if (projection.Length != 16)
        {
            throw new ArgumentException("A projection matrix must contain 16 elements.", nameof(projection));
        }

        var clipToEyeInput = new double[16];
        for (int row = 0; row < 4; row++)
        {
            int offset = row * 4;
            clipToEyeInput[offset] = projection[offset];
            clipToEyeInput[offset + 1] = flipClipSpaceY
                ? -projection[offset + 1]
                : projection[offset + 1];
            clipToEyeInput[offset + 2] =
                (projection[offset + 2] + projection[offset + 3]) * 0.5;
            clipToEyeInput[offset + 3] = projection[offset + 3];
        }

        if (!TryInvert(clipToEyeInput, out double[] doubleClipToEyeRow))
        {
            throw Refusal(volumeDescription, "the converted projection matrix is singular");
        }
        double[] doubleClipToEye = Transpose(doubleClipToEyeRow);

        var narrowedProjection = new float[16];
        for (int index = 0; index < narrowedProjection.Length; index++)
        {
            narrowedProjection[index] = (float)clipToEyeInput[index];
            if (!float.IsFinite(narrowedProjection[index]))
            {
                throw Refusal(
                    volumeDescription,
                    "the converted projection exceeds the finite shader range");
            }
        }

        if (!Matrix4x4.Invert(ToMatrix4x4(narrowedProjection), out Matrix4x4 floatInverse))
        {
            throw Refusal(volumeDescription, "the float projection matrix is singular");
        }
        float[] floatClipToEye = Transpose(ToArray(floatInverse));
        for (int index = 0; index < floatClipToEye.Length; index++)
        {
            if (!float.IsFinite(floatClipToEye[index]))
            {
                throw Refusal(
                    volumeDescription,
                    "the inverse float projection exceeds the finite shader range");
            }
        }

        Span<double> homogeneous = stackalloc double[4];
        Span<float> floatHomogeneous = stackalloc float[4];
        Span<float> floatNearEndpoint = stackalloc float[4];
        Span<float> floatFarEndpoint = stackalloc float[4];
        double nearSign = 0;
        bool anyFiniteFar = false;
        bool anyInfiniteFar = false;
        for (int xIndex = 0; xIndex < 2; xIndex++)
        {
            float x = xIndex == 0 ? -1 : 1;
            for (int yIndex = 0; yIndex < 2; yIndex++)
            {
                float y = yIndex == 0 ? -1 : 1;
                for (int depthIndex = 0; depthIndex < 2; depthIndex++)
                {
                    Multiply(doubleClipToEye, x, y, depthIndex, 1, homogeneous);
                    Multiply(floatClipToEye, x, y, depthIndex, 1, floatHomogeneous);
                    for (int component = 0; component < 4; component++)
                    {
                        if (!double.IsFinite(homogeneous[component]) ||
                            !float.IsFinite(floatHomogeneous[component]))
                        {
                            throw Refusal(
                                volumeDescription,
                                "a corner near/far homogeneous endpoint exceeds the finite shader range");
                        }
                    }

                    if (homogeneous[3] != 0)
                    {
                        if (floatHomogeneous[3] == 0)
                        {
                            throw Refusal(
                                volumeDescription,
                                "a finite clip boundary becomes infinite when the projection is narrowed to shader float");
                        }
                        for (int component = 0; component < 3; component++)
                        {
                            float eyeCoordinate =
                                floatHomogeneous[component] / floatHomogeneous[3];
                            if (!float.IsFinite(eyeCoordinate))
                            {
                                throw Refusal(
                                    volumeDescription,
                                    "a corner near/far eye-space endpoint exceeds the finite shader range");
                            }
                        }
                    }
                    else if (floatHomogeneous[3] != 0)
                    {
                        throw Refusal(
                            volumeDescription,
                            "an infinite clip boundary becomes finite when the projection is narrowed to shader float");
                    }
                }

                Multiply(floatClipToEye, x, y, 0, 1, floatNearEndpoint);
                Multiply(floatClipToEye, x, y, 1, 1, floatFarEndpoint);
                Multiply(doubleClipToEye, x, y, 0, 1, homogeneous);
                double nearW = homogeneous[3];
                Multiply(doubleClipToEye, x, y, 1, 1, homogeneous);
                double farW = homogeneous[3];
                if (nearSign == 0)
                {
                    nearSign = Math.Sign(nearW);
                }
                if (nearW == 0 || Math.Sign(nearW) != nearSign ||
                    Math.Sign(floatNearEndpoint[3]) != nearSign ||
                    (farW != 0 && Math.Sign(farW) != nearSign) ||
                    (floatFarEndpoint[3] != 0 && Math.Sign(floatFarEndpoint[3]) != nearSign))
                {
                    throw Refusal(
                        volumeDescription,
                        "the clip volume crosses a homogeneous horizon; use a projection with finite near rays");
                }
                anyFiniteFar |= farW != 0;
                anyInfiniteFar |= farW == 0;
                if (floatNearEndpoint[3] == 0 ||
                    (floatFarEndpoint[3] != 0 &&
                        (floatNearEndpoint[2] / floatNearEndpoint[3]) <=
                        (floatFarEndpoint[2] / floatFarEndpoint[3])))
                {
                    throw Refusal(
                        volumeDescription,
                        "the projection uses reversed depth; use conventional near-to-far depth before retrying");
                }
            }
        }
        if (anyFiniteFar && anyInfiniteFar)
        {
            throw Refusal(
                volumeDescription,
                "only part of the far boundary is infinite; use a finite boundary or a conventional infinite far plane");
        }
        double maximumEyeCoordinate = Math.Max(
            BoundEndpointCoordinates(floatClipToEye, 0, homogeneousDirection: false, volumeDescription),
            BoundEndpointCoordinates(floatClipToEye, 1, anyInfiniteFar, volumeDescription));
        if (maximumEyeCoordinate > Math.Sqrt(float.MaxValue) / 16)
        {
            throw Refusal(volumeDescription,
                "the camera endpoint range exceeds finite shader ray-length arithmetic");
        }
        return maximumEyeCoordinate;
    }

    private static double BoundEndpointCoordinates(
        float[] clipToEye,
        int depth,
        bool homogeneousDirection,
        string description)
    {
        const double roundingMargin = 0.000001;
        double minimumW = double.PositiveInfinity;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                double w = (double)clipToEye[12] * x + (double)clipToEye[13] * y +
                    (double)clipToEye[14] * depth + clipToEye[15];
                minimumW = Math.Min(minimumW, Math.Abs(w));
            }
        }
        double wMagnitude = Math.Abs((double)clipToEye[12]) + Math.Abs((double)clipToEye[13]) +
            Math.Abs((double)clipToEye[14]) * depth + Math.Abs((double)clipToEye[15]);
        // W has one sign across each face. Subtract an eight-operation float
        // rounding allowance before bounding the division at every pixel.
        double denominator = homogeneousDirection ? 1 : minimumW - roundingMargin * wMagnitude;
        if (denominator <= 0)
        {
            throw Refusal(description, "the camera homogeneous division lacks finite shader rounding headroom");
        }
        double maximum = 0;
        for (int component = 0; component < 3; component++)
        {
            int row = component * 4;
            double numerator = Math.Abs((double)clipToEye[row]) + Math.Abs((double)clipToEye[row + 1]) +
                Math.Abs((double)clipToEye[row + 2]) * depth + Math.Abs((double)clipToEye[row + 3]);
            maximum = Math.Max(maximum, numerator * (1 + roundingMargin) / denominator);
        }
        return maximum;
    }

    private static void ValidateShaderBasis(double[] objectToEye, string description)
    {
        float[] x = [0, 0, 0];
        float[] y = [0, 0, 0];
        float[] z = [0, 0, 0];
        for (int index = 0; index < 3; index++)
        {
            x[index] = (float)objectToEye[index];
            y[index] = (float)objectToEye[4 + index];
            z[index] = (float)objectToEye[8 + index];
        }

        float yCrossZx = (y[1] * z[2]) - (y[2] * z[1]);
        float yCrossZy = (y[2] * z[0]) - (y[0] * z[2]);
        float yCrossZz = (y[0] * z[1]) - (y[1] * z[0]);
        float zCrossXx = (z[1] * x[2]) - (z[2] * x[1]);
        float zCrossXy = (z[2] * x[0]) - (z[0] * x[2]);
        float zCrossXz = (z[0] * x[1]) - (z[1] * x[0]);
        float xCrossYx = (x[1] * y[2]) - (x[2] * y[1]);
        float xCrossYy = (x[2] * y[0]) - (x[0] * y[2]);
        float xCrossYz = (x[0] * y[1]) - (x[1] * y[0]);
        float determinant = (x[0] * yCrossZx) + (x[1] * yCrossZy) + (x[2] * yCrossZz);
        float basisScale = length(x) * length(y) * length(z);
        float inverseDeterminant = 1.0f / determinant;
        if (!float.IsFinite(basisScale) || basisScale <= 0 ||
            !float.IsFinite(yCrossZx) || !float.IsFinite(yCrossZy) || !float.IsFinite(yCrossZz) ||
            !float.IsFinite(zCrossXx) || !float.IsFinite(zCrossXy) || !float.IsFinite(zCrossXz) ||
            !float.IsFinite(xCrossYx) || !float.IsFinite(xCrossYy) || !float.IsFinite(xCrossYz) ||
            !float.IsFinite(determinant) || !float.IsFinite(inverseDeterminant) ||
            !float.IsFinite(yCrossZx * inverseDeterminant) ||
            !float.IsFinite(yCrossZy * inverseDeterminant) ||
            !float.IsFinite(yCrossZz * inverseDeterminant) ||
            !float.IsFinite(zCrossXx * inverseDeterminant) ||
            !float.IsFinite(zCrossXy * inverseDeterminant) ||
            !float.IsFinite(zCrossXz * inverseDeterminant) ||
            !float.IsFinite(xCrossYx * inverseDeterminant) ||
            !float.IsFinite(xCrossYy * inverseDeterminant) ||
            !float.IsFinite(xCrossYz * inverseDeterminant))
        {
            throw Refusal(
                description,
                "the float object-to-eye basis overflows or underflows the shader inverse-basis arithmetic");
        }
        if (Math.Abs(determinant) <= basisScale * 1e-6f)
        {
            throw Refusal(
                description,
                "the float object-to-eye basis is singular, poorly conditioned, or outside " +
                "the shader's relative determinant tolerance");
        }

        static float length(float[] value) =>
            MathF.Sqrt((value[0] * value[0]) + (value[1] * value[1]) + (value[2] * value[2]));
    }

    private static double[] Multiply(ReadOnlySpan<double> left, ReadOnlySpan<double> right)
    {
        var result = new double[16];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                double sum = 0;
                for (int inner = 0; inner < 4; inner++)
                {
                    sum += left[(row * 4) + inner] * right[(inner * 4) + column];
                }
                if (!double.IsFinite(sum))
                {
                    throw new InvalidDataException("A composed volume object-to-eye matrix is not finite.");
                }
                result[(row * 4) + column] = sum;
            }
        }
        return result;
    }

    private static void Multiply(
        ReadOnlySpan<double> matrix,
        double x,
        double y,
        double z,
        double w,
        Span<double> destination)
    {
        for (int row = 0; row < 4; row++)
        {
            int offset = row * 4;
            destination[row] =
                (matrix[offset] * x) +
                (matrix[offset + 1] * y) +
                (matrix[offset + 2] * z) +
                (matrix[offset + 3] * w);
        }
    }

    private static void Multiply(
        ReadOnlySpan<float> matrix,
        float x,
        float y,
        float z,
        float w,
        Span<float> destination)
    {
        for (int row = 0; row < 4; row++)
        {
            int offset = row * 4;
            destination[row] =
                (matrix[offset] * x) +
                (matrix[offset + 1] * y) +
                (matrix[offset + 2] * z) +
                (matrix[offset + 3] * w);
        }
    }

    private static Matrix4x4 ToMatrix4x4(ReadOnlySpan<float> values) =>
        new(
            values[0], values[1], values[2], values[3],
            values[4], values[5], values[6], values[7],
            values[8], values[9], values[10], values[11],
            values[12], values[13], values[14], values[15]);

    private static float[] ToArray(Matrix4x4 value) =>
    [
        value.M11, value.M12, value.M13, value.M14,
        value.M21, value.M22, value.M23, value.M24,
        value.M31, value.M32, value.M33, value.M34,
        value.M41, value.M42, value.M43, value.M44
    ];

    private static double[] Transpose(ReadOnlySpan<double> matrix)
    {
        var result = new double[16];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                result[(row * 4) + column] = matrix[(column * 4) + row];
            }
        }
        return result;
    }

    private static float[] Transpose(ReadOnlySpan<float> matrix)
    {
        var result = new float[16];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                result[(row * 4) + column] = matrix[(column * 4) + row];
            }
        }
        return result;
    }

    private static bool TryInvert(double[] matrix, out double[] inverse)
    {
        var augmented = new double[4, 8];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                double value = matrix[(row * 4) + column];
                if (!double.IsFinite(value))
                {
                    inverse = [];
                    return false;
                }
                augmented[row, column] = value;
            }
            augmented[row, row + 4] = 1;
        }

        for (int column = 0; column < 4; column++)
        {
            int pivotRow = column;
            for (int row = column + 1; row < 4; row++)
            {
                if (Math.Abs(augmented[row, column]) > Math.Abs(augmented[pivotRow, column]))
                {
                    pivotRow = row;
                }
            }

            double pivot = augmented[pivotRow, column];
            if (pivot == 0 || !double.IsFinite(pivot))
            {
                inverse = [];
                return false;
            }
            if (pivotRow != column)
            {
                for (int index = 0; index < 8; index++)
                {
                    (augmented[column, index], augmented[pivotRow, index]) =
                        (augmented[pivotRow, index], augmented[column, index]);
                }
            }

            for (int index = 0; index < 8; index++)
            {
                augmented[column, index] /= pivot;
            }
            for (int row = 0; row < 4; row++)
            {
                if (row == column)
                {
                    continue;
                }
                double factor = augmented[row, column];
                for (int index = 0; index < 8; index++)
                {
                    augmented[row, index] -= factor * augmented[column, index];
                }
            }
        }

        inverse = new double[16];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                double value = augmented[row, column + 4];
                if (!double.IsFinite(value))
                {
                    inverse = [];
                    return false;
                }
                inverse[(row * 4) + column] = value;
            }
        }
        return true;
    }

    private static InvalidDataException Refusal(string description, string reason) =>
        new(
            $"Density volume {description} cannot be represented safely by the camera pass because {reason}. " +
            "Reduce the camera/volume scale or shear, move the scene closer to the camera origin, or bake " +
            "the transform into the density grid before retrying. The update was refused.");
}
