// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

public sealed class SilkVolumeCameraShaderMathSourceTests
{
    [Test]
    public async Task SharedCameraHelpersAreInlinedForCombinedMetalLibrary()
    {
        string source = (await ReadShaderAsync()).Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (string signature in new[]
        {
            "VolumeLogSum NewVolumeLogSum",
            "float LogPositiveVolumeValue",
            "void AddVolumeLog",
            "float VolumeLogSumValue",
            "void AddTintedVolumeDepth",
            "void AddVolumeSource",
            "float VolumeColorFromLog",
            "float3 ToObjectPoint",
            "float3 ToObjectDirection",
            "float OneMinusExpNegative",
            "float NegativeLogOneMinus",
            "float IsolatedFieldOpticalDepth",
            "uint FieldStepContribution",
            "uint SegmentStepCount",
            "float SegmentSampleDistance",
            "float3 AtlasCoordinate",
            "float DensityAt",
            "bool IntersectUnitBox"
        })
        {
            await Assert.That(source).Contains($"[ForceInline]\n{signature}(");
        }
    }

    [Test]
    public async Task NonfiniteLogAccumulationIsNotTreatedAsAnEmptySum()
    {
        string source = await ReadShaderAsync();
        string result = FunctionBody(source, "VolumeLogSumValue");
        string color = FunctionBody(source, "VolumeColorFromLog");

        await Assert.That(result).Contains("total.sum == 0.0 ? NoLogValue");
        await Assert.That(result).DoesNotContain("total.sum > 0.0 ? ");
        await Assert.That(color).Contains("if (!isfinite(logarithm))");
        await Assert.That(color).Contains("return exp(logarithm);");
    }

    [Test]
    public async Task FullOpacityHighDensityFieldsRetainTheirOpticalDepthRatio()
    {
        string source = await ReadShaderAsync();
        string opticalDepth = FunctionBody(source, "IsolatedFieldOpticalDepth");
        string logHelper = FunctionBody(source, "NegativeLogOneMinus");
        string exponentialHelper = FunctionBody(source, "OneMinusExpNegative");

        await Assert.That(opticalDepth)
            .Contains("if (boundedOpacity >= 1.0)")
            .Because("Opacity one must preserve arbitrarily large field optical depth.");
        await Assert.That(opticalDepth)
            .Contains("return nonnegativeDensityIntegral;");
        await Assert.That(opticalDepth).DoesNotContain("1e-20");
        await Assert.That(logHelper).Contains("return -log(1.0 - value);");
        await Assert.That(logHelper).DoesNotContain("max(");
        await Assert.That(logHelper).Contains("power *= value;");
        await Assert.That(logHelper).Contains("sum += power / float(order);");
        await Assert.That(exponentialHelper).Contains("term *= nonnegative / float(order);");
        await Assert.That(source)
            .Contains("float densityFraction = density / densityScales[field];");
        await Assert.That(source)
            .Contains("(densityFraction * localStepLength) / normalizedDensityIntegrals[field]");
        await Assert.That(source).Contains("fieldDepths[field] = fieldOpticalDepth[field] * stepFraction;");
        await Assert.That(source)
            .Contains("float outputOpacity = OneMinusExpNegative(accumulatedOpticalDepth)");

        const double redDepth = 100;
        const double blueDepth = 200;
        double combinedDepth = redDepth + blueDepth;
        double expectedRed = redDepth / combinedDepth;
        double expectedBlue = blueDepth / combinedDepth;
        await Assert.That(expectedRed).IsEqualTo(1d / 3d);
        await Assert.That(expectedBlue).IsEqualTo(2d / 3d);
        await Assert.That(1 - Math.Exp(-combinedDepth)).IsEqualTo(1d);
    }

    [Test]
    public async Task PartialOpacityUsesTheIsolatedVolumeTransmittanceAtHighAndLowDensity()
    {
        string source = await ReadShaderAsync();
        string opticalDepth = FunctionBody(source, "IsolatedFieldOpticalDepth");

        await Assert.That(opticalDepth)
            .Contains("boundedOpacity * OneMinusExpNegative(nonnegativeDensityIntegral)");
        await Assert.That(opticalDepth).Contains("NegativeLogOneMinus(removedFraction)");
        await Assert.That(opticalDepth).DoesNotContain("max(isolatedTransmittance");

        double highDensityTau = -Math.Log(1 - 0.25 * (1 - Math.Exp(-100)));
        double highDensityLimit = -Math.Log(0.75);
        double lowDensityTau = -Math.Log(1 - 0.4 * (1 - Math.Exp(-0.001)));
        await Assert.That(Math.Abs(highDensityTau - highDensityLimit))
            .IsLessThan(1e-12);
        await Assert.That(Math.Abs(lowDensityTau - 0.0003998800080179))
            .IsLessThan(1e-12);
    }

    [Test]
    public async Task ZeroOpacityFieldsDoNotChangeTheSharedIntegrationLattice()
    {
        string source = await ReadShaderAsync();
        string main = FunctionBody(source, "VOLUME_CAMERA_FRAGMENT_ENTRY");
        string stepCount = FunctionBody(source, "SegmentStepCount");

        await Assert.That(main)
            .Contains("fieldEnabled[field] =")
            .Because("Zero-opacity fields must not contribute events or sampling frequency.");
        await Assert.That(main)
            .Contains("opacity > 0.0 && exits[field] > entries[field]");
        await Assert.That(main)
            .Contains("if (fieldEnabled[field])\n        {\n            events[eventCount++] = entries[field]");
        await Assert.That(stepCount)
            .Contains("entries[field] <= segmentStart");
        await Assert.That(stepCount)
            .Contains("exits[field] >= segmentEnd");
        await Assert.That(Count(main, "SegmentStepCount(")).IsEqualTo(2);
        await Assert.That(Count(main, "SegmentSampleDistance(")).IsEqualTo(2);
        await Assert.That(Count(main, "entries[field] <= segmentStart"))
            .IsEqualTo(1);
        await Assert.That(Count(main, "exits[field] >= segmentEnd"))
            .IsEqualTo(1);
        await Assert.That(Count(main, "entries[field] > segmentStart"))
            .IsEqualTo(1);
        await Assert.That(Count(main, "exits[field] < segmentEnd"))
            .IsEqualTo(1);
        await Assert.That(main).DoesNotContain("entries[field] < segmentEnd");
        await Assert.That(main).DoesNotContain("exits[field] > segmentStart");
        await Assert.That(main)
            .Contains("(densityFraction * localStepLength) / normalizedDensityIntegrals[field]");
        await Assert.That(main).Contains("fieldDepths[field] = fieldOpticalDepth[field] * stepFraction;");
        await Assert.That(main).DoesNotContain("max(normalizedDensityIntegrals[field],");
        await Assert.That(main).DoesNotContain("min(stepCount");
        await Assert.That(source).DoesNotContain("MaximumEvaluationsPerPixel");
    }

    [Test]
    public async Task IntegerSamplingScheduleUsesExtentBoundWithoutFloatRounding()
    {
        string source = await ReadShaderAsync();
        string contribution = FunctionBody(source, "FieldStepContribution");
        string stepCount = FunctionBody(source, "SegmentStepCount");

        await Assert.That(contribution).Contains("return 2u * maxExtent;");
        await Assert.That(contribution).DoesNotContain("float(");
        await Assert.That(stepCount).Contains("stepCount += FieldStepContribution");
        await Assert.That(stepCount).Contains("entries[field] <= segmentStart");
        await Assert.That(stepCount).Contains("exits[field] >= segmentEnd");
        await Assert.That(stepCount).Contains("max(1u, stepCount)");
        await Assert.That(stepCount).DoesNotContain("uint(");
        await Assert.That(stepCount).DoesNotContain("ceil(");
        await Assert.That(source).DoesNotContain("ceil(frequency)");

        int fields = 8;
        int maximumAxisExtent = 512;
        int maximumIntervals = (fields * 2) - 1;
        int totalMaximumExtent = fields * maximumAxisExtent;
        int conservativeStepsPerPhase = 2 * totalMaximumExtent * maximumIntervals;
        int conservativeEvaluationsPerPixel =
            2 * fields * conservativeStepsPerPhase;
        await Assert.That(conservativeStepsPerPhase).IsEqualTo(122880);
        await Assert.That(conservativeEvaluationsPerPixel).IsEqualTo(1966080);
    }

    [Test]
    public async Task AtlasSamplingPreservesClampedNormalizedFieldCoordinatesAtEdgesAndInterior()
    {
        string source = await ReadShaderAsync();
        string coordinate = FunctionBody(source, "AtlasCoordinate");
        string density = FunctionBody(source, "DensityAt");

        await Assert.That(coordinate)
            .Contains("(float3(region.xyz) + (localCoordinate * extent))");
        await Assert.That(coordinate).Contains("float3(extentAndFlags.x, extentAndFlags.y, region.w)");
        await Assert.That(density).Contains("AtlasCoordinate(fieldOffset, localCoordinate)");
        await Assert.That(coordinate).DoesNotContain("extentAndFlags.x - 1u");
        await Assert.That(coordinate).DoesNotContain(" + 0.5");

        double[] sourceRamp = [2, 6, 10, 14];
        double[] gutteredRamp = [2, 2, 6, 10, 14, 14];
        foreach ((double local, double expected) in new[]
        {
            (0d, 2d),
            (0.125d, 2d),
            (0.5d, 8d),
            (0.75d, 12d),
            (1d, 14d)
        })
        {
            double sourceSample = SampleLinearClamp(sourceRamp, local);
            double atlasCoordinate = (1 + local * sourceRamp.Length) / gutteredRamp.Length;
            double atlasSample = SampleLinearClamp(gutteredRamp, atlasCoordinate);
            await Assert.That(sourceSample).IsEqualTo(expected);
            await Assert.That(atlasSample).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task AtlasSamplingPreservesTrilinearSlabValuesWithThreeAxisGutters()
    {
        string source = await ReadShaderAsync();
        string coordinate = FunctionBody(source, "AtlasCoordinate");

        await Assert.That(coordinate)
            .Contains("float3(extentAndFlags.x, extentAndFlags.y, region.w)");
        await Assert.That(coordinate).Contains("float3(region.xyz)");

        double[,,] field = CreateRamp(4, 3, 2);
        double[,,] atlas = AddOneVoxelGutter(field);
        (double LocalX, double LocalY, double LocalZ, double Expected)[] samples =
        [
            (0, 0, 0, 0),
            (0.25, 0.5, 0.75, 9),
            (1, 1, 1, 17)
        ];
        foreach (var sample in samples)
        {
            double sourceSample = SampleTrilinear(
                field, sample.LocalX, sample.LocalY, sample.LocalZ);
            double atlasX = (1 + sample.LocalX * field.GetLength(0)) / atlas.GetLength(0);
            double atlasY = (1 + sample.LocalY * field.GetLength(1)) / atlas.GetLength(1);
            double atlasZ = (1 + sample.LocalZ * field.GetLength(2)) / atlas.GetLength(2);
            double atlasSample = SampleTrilinear(atlas, atlasX, atlasY, atlasZ);
            await Assert.That(sourceSample).IsEqualTo(sample.Expected);
            await Assert.That(atlasSample).IsEqualTo(sample.Expected);
        }
    }

    [Test]
    public async Task TransposedRowMajorEyeToObjectPayloadUsesColumnVectorMultiplication()
    {
        string source = await ReadShaderAsync();
        string point = FunctionBody(source, "ToObjectPoint");
        string direction = FunctionBody(source, "ToObjectDirection");

        await Assert.That(point)
            .Contains("mul(eyeToObject, float4(eyePosition, 1.0))");
        await Assert.That(direction)
            .Contains("mul(eyeToObject, float4(eyeDirection, 0.0))");
    }

    private static double SampleLinearClamp(double[] values, double normalizedCoordinate)
    {
        double position = Math.Clamp(
            normalizedCoordinate * values.Length - 0.5,
            0,
            values.Length - 1);
        int first = (int)Math.Floor(position);
        int second = Math.Min(first + 1, values.Length - 1);
        double fraction = position - first;
        return values[first] + ((values[second] - values[first]) * fraction);
    }

    private static double[,,] CreateRamp(int width, int height, int depth)
    {
        var result = new double[width, height, depth];
        for (int z = 0; z < depth; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    result[x, y, z] = (2 * x) + (3 * y) + (5 * z);
                }
            }
        }
        return result;
    }

    private static double[,,] AddOneVoxelGutter(double[,,] source)
    {
        int width = source.GetLength(0);
        int height = source.GetLength(1);
        int depth = source.GetLength(2);
        var result = new double[width + 2, height + 2, depth + 2];
        for (int z = 0; z < result.GetLength(2); z++)
        {
            for (int y = 0; y < result.GetLength(1); y++)
            {
                for (int x = 0; x < result.GetLength(0); x++)
                {
                    result[x, y, z] = source[
                        Math.Clamp(x - 1, 0, width - 1),
                        Math.Clamp(y - 1, 0, height - 1),
                        Math.Clamp(z - 1, 0, depth - 1)];
                }
            }
        }
        return result;
    }

    private static double SampleTrilinear(
        double[,,] values,
        double normalizedX,
        double normalizedY,
        double normalizedZ)
    {
        static (int First, int Second, double Fraction) axis(double coordinate, int size)
        {
            double position = Math.Clamp(coordinate * size - 0.5, 0, size - 1);
            int first = (int)Math.Floor(position);
            return (first, Math.Min(first + 1, size - 1), position - first);
        }

        (int x0, int x1, double fx) = axis(normalizedX, values.GetLength(0));
        (int y0, int y1, double fy) = axis(normalizedY, values.GetLength(1));
        (int z0, int z1, double fz) = axis(normalizedZ, values.GetLength(2));
        double zLow = Lerp(
            Lerp(values[x0, y0, z0], values[x1, y0, z0], fx),
            Lerp(values[x0, y1, z0], values[x1, y1, z0], fx),
            fy);
        double zHigh = Lerp(
            Lerp(values[x0, y0, z1], values[x1, y0, z1], fx),
            Lerp(values[x0, y1, z1], values[x1, y1, z1], fx),
            fy);
        return Lerp(zLow, zHigh, fz);
    }

    private static double Lerp(double left, double right, double amount) =>
        left + ((right - left) * amount);

    private static int Count(string source, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string FunctionBody(string source, string name)
    {
        int signature = source.IndexOf($"{name}(", StringComparison.Ordinal);
        if (signature < 0)
        {
            throw new InvalidOperationException($"Shader function '{name}' was not found.");
        }
        int openingBrace = source.IndexOf('{', signature);
        if (openingBrace < 0)
        {
            throw new InvalidOperationException($"Shader function '{name}' has no body.");
        }
        int depth = 1;
        for (int index = openingBrace + 1; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[(openingBrace + 1)..index];
            }
        }
        throw new InvalidOperationException($"Shader function '{name}' is not balanced.");
    }

    private static Task<string> ReadShaderAsync() =>
        File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "eng",
            "shaders",
            "sources",
            "volume.camera.fragment.shared.slang"));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ??
            throw new InvalidOperationException("Could not locate repository root.");
    }
}
