// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Proves the committed dome-light fixtures carry the exact radiance their
/// documentation claims, and that the renderer reduces them to the documented
/// ambient value.
/// </summary>
/// <remarks>
/// <para>
/// The fixtures are consumed for real through OpenUSD's Hio image plugins, which
/// need the native runtime. That path is exercised by the render workflows, not
/// by an ordinary test pass, and a fixture that is silently wrong there is
/// expensive to notice: the render simply comes out at a different exposure and
/// nothing fails.
/// </para>
/// <para>
/// So this file decodes the committed Radiance images with a small reader of its
/// own and pushes the result through the same
/// <see cref="SilkEnvironmentMeanRadiance"/> the renderer uses. That does not
/// prove Hio reads them -- nothing here claims it does -- but it does prove the
/// bytes on disk are a well-formed equirectangular image carrying exactly the
/// radiance the fixture documents, and that the mean the renderer will compute
/// from it is the one the fixture's prose and the support claim both state.
/// </para>
/// <para>
/// Every authored radiance is a power of two, which Radiance RGBE stores
/// exactly, so these are equality assertions rather than tolerances wide enough
/// to hide a real error.
/// </para>
/// </remarks>
public sealed class OmniverseDomeFixtureTests
{
    private const float UnitDomeAmbient = 0.96f;

    [Test]
    public async Task TheWhiteFixtureIsExactlyUnitRadianceAndIsTheParityCase()
    {
        // The parity fixture. A constant 1.0 environment must resolve to the
        // ambient the untextured unit dome it replaces produced, which is what
        // makes the textured path a replacement for the old approximation
        // rather than a second light stacked on top of it.
        Vector3 mean = MeanOf("dome-white-latlong.hdr");

        await Assert.That(mean.X).IsEqualTo(1f);
        await Assert.That(mean.Y).IsEqualTo(1f);
        await Assert.That(mean.Z).IsEqualTo(1f);
        await Assert.That(mean.X * UnitDomeAmbient).IsEqualTo(UnitDomeAmbient);
    }

    [Test]
    public async Task TheSkyGroundFixtureResolvesToItsDocumentedPerChannelMean()
    {
        // Documented in dome-hdr-mean-ambient.usda as (1.25, 0.625, 0.3125): the two
        // hemispheres carry equal solid angle, so the mean is the plain average
        // of (2, 1, 0.5) and (0.5, 0.25, 0.125), accumulated per channel.
        Vector3 mean = MeanOf("dome-sky-ground-latlong.hdr");

        await Assert.That(mean.X).IsEqualTo(1.25f);
        await Assert.That(mean.Y).IsEqualTo(0.625f);
        await Assert.That(mean.Z).IsEqualTo(0.3125f);
    }

    [Test]
    public async Task TheSkyGroundFixtureResolvesToItsDocumentedAmbient()
    {
        // dome-hdr-mean-ambient.usda states the resulting ambient is (1.2, 0.6, 0.3) for
        // a unit-emission dome. Recomputing it here keeps the fixture's prose
        // and the renderer's arithmetic from drifting apart.
        Vector3 ambient = MeanOf("dome-sky-ground-latlong.hdr") * UnitDomeAmbient;

        await Assert.That(ambient.X).IsEqualTo(1.2f).Within(1e-6f);
        await Assert.That(ambient.Y).IsEqualTo(0.6f).Within(1e-6f);
        await Assert.That(ambient.Z).IsEqualTo(0.3f).Within(1e-6f);
    }

    [Test]
    public async Task ThePolarCapFixtureIsFarDimmerThanAnUnweightedAverage()
    {
        // One lit row of eight. An implementation that averaged texels rather
        // than solid angle would report 0.125 for this fixture, which is the
        // whole reason it is committed.
        Vector3 mean = MeanOf("dome-polar-cap-latlong.hdr");
        double weight = Math.Sin(Math.PI * 0.5 / 8);
        double total = 0;
        for (int row = 0; row < 8; row++)
        {
            total += Math.Sin(Math.PI * (row + 0.5) / 8);
        }

        await Assert.That(mean.X).IsEqualTo((float)(weight / total)).Within(1e-6f);
        await Assert.That(mean.X).IsLessThan(0.125f);
    }

    [Test]
    public async Task EveryCommittedFixtureImageIsReferencedByAFixtureStage()
    {
        // A fixture image nothing references is dead weight that still has to be
        // reviewed and redistributed, and a stage that references an image which
        // is not committed is worse: it fails only where the render actually
        // runs. The deliberately absent asset in the diagnostics fixture is the
        // one exception, and it is named here so it stays deliberate.
        DirectoryInfo directory = new(FixtureDirectory);
        string stages = string.Concat(
            directory.GetFiles("*.usda").Select(file => File.ReadAllText(file.FullName)));

        List<string> unreferenced = [];
        foreach (FileInfo image in directory.GetFiles("*.hdr"))
        {
            if (!stages.Contains(image.Name, StringComparison.Ordinal))
            {
                unreferenced.Add(image.Name);
            }
        }

        await Assert.That(unreferenced).IsEmpty();
        await Assert.That(stages).Contains("dome-absent-latlong.hdr");
        await Assert.That(File.Exists(
                Path.Combine(FixtureDirectory, "dome-absent-latlong.hdr")))
            .IsFalse();
    }

    [Test]
    public async Task TheParityFixtureAuthorsTheSameEmissionOnBothDomes()
    {
        // The parity claim only means anything if the two domes differ in
        // exactly one thing: whether a texture is authored.
        string text = File.ReadAllText(
            Path.Combine(FixtureDirectory, "dome-untextured-parity.usda"));
        int textured = text.IndexOf(
            "def DomeLight \"TexturedDome\"",
            StringComparison.Ordinal);
        int untextured = text.IndexOf(
            "def DomeLight \"UntexturedDome\"",
            StringComparison.Ordinal);
        await Assert.That(textured).IsGreaterThan(0);
        await Assert.That(untextured).IsGreaterThan(textured);

        string texturedBlock = text[textured..untextured];
        string untexturedBlock = text[untextured..];
        foreach (string input in new[]
        {
            "color3f inputs:color = (1, 1, 1)",
            "float inputs:intensity = 1",
            "float inputs:exposure = 0",
            "float inputs:diffuse = 1",
            "float inputs:specular = 0",
        })
        {
            await Assert.That(texturedBlock).Contains(input);
            await Assert.That(untexturedBlock).Contains(input);
        }

        await Assert.That(texturedBlock).Contains("inputs:texture:file");
        await Assert.That(untexturedBlock).DoesNotContain("inputs:texture:file");
    }

    private static Vector3 MeanOf(string fileName) =>
        SilkEnvironmentMeanRadiance.ComputeMeanRadiance(
            ReadRadiance(Path.Combine(FixtureDirectory, fileName)),
            SilkColorSpace.Raw);

    private static string FixtureDirectory =>
        Path.Combine(FindRepositoryRoot(), "test-assets", "omniverse", "lighting");

    /// <summary>
    /// Reads one uncompressed Radiance RGBE image into the decoded float form
    /// the renderer's environment resolver consumes.
    /// </summary>
    /// <remarks>
    /// Deliberately minimal: it accepts only what
    /// <c>generate-dome-fixtures.py</c> writes, and rejects anything else rather
    /// than guessing. A run-length-encoded scanline, which starts 0x02 0x02, is
    /// refused outright so a future fixture written by a different tool cannot
    /// be silently misread as flat data.
    /// </remarks>
    private static SilkDecodedImage ReadRadiance(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int offset = 0;
        string readLine()
        {
            int start = offset;
            while (offset < bytes.Length && bytes[offset] != (byte)'\n')
            {
                offset++;
            }
            string line = Encoding.ASCII.GetString(bytes, start, offset - start);
            offset++;
            return line;
        }

        if (readLine() != "#?RADIANCE")
        {
            throw new InvalidDataException($"'{path}' is not a Radiance image.");
        }

        string resolution = string.Empty;
        while (offset < bytes.Length)
        {
            string line = readLine();
            if (line.Length == 0)
            {
                resolution = readLine();
                break;
            }
        }

        string[] parts = resolution.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || parts[0] != "-Y" || parts[2] != "+X")
        {
            throw new InvalidDataException(
                $"'{path}' does not declare a top-down latitude/longitude resolution.");
        }

        int height = int.Parse(parts[1], CultureInfo.InvariantCulture);
        int width = int.Parse(parts[3], CultureInfo.InvariantCulture);
        if ((long)width * height * 4 != bytes.Length - offset)
        {
            throw new InvalidDataException(
                $"'{path}' is not stored as uncompressed RGBE scanlines.");
        }
        if (width >= 8 && bytes[offset] == 2 && bytes[offset + 1] == 2)
        {
            throw new InvalidDataException(
                $"'{path}' uses run-length encoding, which this reader refuses.");
        }

        float[] values = new float[width * height * 4];
        for (int texel = 0; texel < width * height; texel++)
        {
            int source = offset + (texel * 4);
            byte exponent = bytes[source + 3];
            float scale = exponent == 0
                ? 0f
                : MathF.Pow(2f, exponent - 136);
            values[(texel * 4) + 0] = bytes[source] * scale;
            values[(texel * 4) + 1] = bytes[source + 1] * scale;
            values[(texel * 4) + 2] = bytes[source + 2] * scale;
            values[(texel * 4) + 3] = 1f;
        }

        return new SilkDecodedImage(
            (uint)width,
            (uint)height,
            MemoryMarshal.AsBytes(values.AsSpan()).ToArray(),
            SilkTextureFormat.Rgba32Float);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ??
            throw new InvalidOperationException("The repository root was not found.");
    }
}
