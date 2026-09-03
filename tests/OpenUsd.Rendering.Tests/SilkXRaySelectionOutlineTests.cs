// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Covers the renderer-neutral x-ray selection policy: what the occluded
/// composite writes into its constant buffer, what it costs, and how an
/// unsupported backend is reported.
/// </summary>
public sealed class SilkXRaySelectionOutlineTests
{
    /// <summary>
    /// One constant buffer carries both styles, because both silhouettes are
    /// composited in one pass: the visible colour with the tight depth tolerance
    /// that suppresses outlines over nearer occluders, and the distinct occluded
    /// colour beside it.
    /// </summary>
    /// <remarks>
    /// Compositing the two one after the other blended them wherever they
    /// overlapped, so a visible edge came out as a mixture instead of exactly
    /// what the visible-only mode draws. The single composite is why both
    /// colours have to be in the same buffer.
    /// </remarks>
    [Test]
    public async Task OneCompositeBufferCarriesTheVisibleAndOccludedStyles()
    {
        var settings = new SilkSelectionOutlineSettings(
            enabled: true,
            new SilkColor(1, 0.55f, 0, 0.9f),
            width: 2,
            SilkSelectionOutlineMode.XRay,
            SilkSelectionOutlineSettings.DefaultOccludedColor);

        var parameters = new byte[SilkSelectionOutlineUniformWriter.ByteSize];
        SilkSelectionOutlineUniformWriter.Write(settings, 64, 32, parameters);

        await Assert.That(parameters.Length).IsEqualTo(48);
        await Assert.That(ReadSingle(parameters, 0)).IsEqualTo(1f);
        await Assert.That(ReadSingle(parameters, 12)).IsEqualTo(0.9f);
        await Assert.That(ReadSingle(parameters, 28))
            .IsEqualTo(SilkSelectionOutlineUniformWriter.DepthEpsilon);

        await Assert.That(ReadSingle(parameters, 32))
            .IsEqualTo(SilkSelectionOutlineSettings.DefaultOccludedColor.Red);
        await Assert.That(ReadSingle(parameters, 36))
            .IsEqualTo(SilkSelectionOutlineSettings.DefaultOccludedColor.Green);
        await Assert.That(ReadSingle(parameters, 40))
            .IsEqualTo(SilkSelectionOutlineSettings.DefaultOccludedColor.Blue);
        await Assert.That(ReadSingle(parameters, 44))
            .IsEqualTo(SilkSelectionOutlineSettings.DefaultOccludedColor.Alpha);
    }

    /// <summary>
    /// The visible-only mode writes a zero-alpha occluded colour, so the shared
    /// composite's occluded branch contributes nothing at all under
    /// straight-alpha-over blending.
    /// </summary>
    /// <remarks>
    /// That is what makes the one-pass composite safe for the mode that must
    /// never show an occluded outline: the branch still runs, and it still
    /// leaves the target byte-for-byte as it found it.
    /// </remarks>
    [Test]
    public async Task VisibleOnlyWritesATransparentOccludedStyle()
    {
        var settings = new SilkSelectionOutlineSettings(
            enabled: true,
            new SilkColor(1, 0.55f, 0, 0.9f),
            width: 2,
            SilkSelectionOutlineMode.VisibleOnly,
            SilkSelectionOutlineSettings.DefaultOccludedColor);

        var parameters = new byte[SilkSelectionOutlineUniformWriter.ByteSize];
        SilkSelectionOutlineUniformWriter.Write(settings, 64, 32, parameters);

        await Assert.That(ReadSingle(parameters, 12)).IsEqualTo(0.9f);
        await Assert.That(ReadSingle(parameters, 44)).IsEqualTo(0f);
    }

    /// <summary>
    /// The occluded mask pipeline uses the checked occluded fragment stage, and
    /// the visible one keeps the ordinary stage.
    /// </summary>
    /// <remarks>
    /// The two silhouettes share one mask texture and differ by channel, so the
    /// depth policy is what selects the fragment stage. Using one stage for both
    /// would have the visible pass erase the whole silhouette it is drawn over.
    /// </remarks>
    [Test]
    public async Task TheMaskStageFollowsTheDepthPolicy()
    {
        SilkSelectionMaskPipelineDescriptor visible =
            SilkSelectionMaskPipelineDescriptor.CreateChecked(
                SilkShaderBinaryFormat.Dxil,
                depthTested: true);
        SilkSelectionMaskPipelineDescriptor occluded =
            SilkSelectionMaskPipelineDescriptor.CreateChecked(
                SilkShaderBinaryFormat.Dxil,
                depthTested: false);

        visible.Validate();
        occluded.Validate();
        await Assert.That(visible.FragmentShader.EntryPoint)
            .IsEqualTo("selectionMaskFragmentMain");
        await Assert.That(occluded.FragmentShader.EntryPoint)
            .IsEqualTo("selectionMaskOccludedFragmentMain");
        await Assert.That(occluded.FragmentShader.Format)
            .IsEqualTo(visible.FragmentShader.Format);
        await Assert.That(visible.VertexShader.EntryPoint)
            .IsEqualTo(occluded.VertexShader.EntryPoint);
    }

    /// <summary>
    /// The occluded style must be distinguishable from the visible one by hue,
    /// not by brightness alone, so a viewer who cannot separate the two by
    /// colour still separates them by contrast.
    /// </summary>
    [Test]
    public async Task TheOccludedStyleIsDistinctFromTheVisibleStyle()
    {
        SilkColor visible = SilkSelectionOutlineSettings.Default.Color;
        SilkColor occluded = SilkSelectionOutlineSettings.DefaultOccludedColor;

        await Assert.That(occluded).IsNotEqualTo(visible);

        // Hue separation: the visible style is warm (red dominant) and the
        // occluded style is cool (blue dominant), which survives the common
        // forms of colour vision deficiency.
        await Assert.That(visible.Red).IsGreaterThan(visible.Blue);
        await Assert.That(occluded.Blue).IsGreaterThan(occluded.Red);

        // Luminance separation, so the two are also told apart in greyscale.
        float visibleLuminance = Luminance(visible);
        float occludedLuminance = Luminance(occluded);
        await Assert.That(Math.Abs(visibleLuminance - occludedLuminance))
            .IsGreaterThan(0.05f);

        // The occluded outline never competes with the visible one.
        await Assert.That(occluded.Alpha).IsLessThan(visible.Alpha);
    }

    /// <summary>
    /// The legacy boolean constructor still selects the two modes, so every
    /// existing caller keeps its meaning.
    /// </summary>
    [Test]
    [Arguments(true, SilkSelectionOutlineMode.VisibleOnly)]
    [Arguments(false, SilkSelectionOutlineMode.XRay)]
    public async Task TheBooleanPolicyStillSelectsTheMode(
        bool visibleOnly,
        SilkSelectionOutlineMode expected)
    {
        var settings = new SilkSelectionOutlineSettings(
            enabled: true,
            new SilkColor(1, 1, 1, 1),
            width: 2,
            visibleOnly);

        await Assert.That(settings.Mode).IsEqualTo(expected);
        await Assert.That(settings.VisibleOnly).IsEqualTo(visibleOnly);
    }

    /// <summary>
    /// A capability set that claims x-ray without visible-only is incoherent,
    /// because the x-ray mode composites the visible outline as well.
    /// </summary>
    [Test]
    public async Task XRayWithoutVisibleOnlyIsRefused()
    {
        var incoherent = new SilkSelectionOutlineCapabilities(
            SupportsVisibleOnly: false,
            SupportsXRay: true);

        await Assert.That(incoherent.Validate).Throws<ArgumentException>();
        await Assert.That(SilkSelectionOutlineCapabilities.Full.SupportsVisibleOnly)
            .IsTrue();
        await Assert.That(SilkSelectionOutlineCapabilities.Full.SupportsXRay).IsTrue();
        await Assert.That(SilkSelectionOutlineCapabilities.VisibleOnly.SupportsXRay)
            .IsFalse();
    }

    /// <summary>An occluded colour outside the valid range is refused.</summary>
    [Test]
    public async Task AnInvalidOccludedColourIsRefused()
    {
        await Assert.That(() => new SilkSelectionOutlineSettings(
                enabled: true,
                new SilkColor(1, 1, 1, 1),
                width: 2,
                SilkSelectionOutlineMode.XRay,
                new SilkColor(float.NaN, 0, 0, 1)))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static float Luminance(SilkColor color) =>
        (0.2126f * color.Red) + (0.7152f * color.Green) + (0.0722f * color.Blue);

    private static float ReadSingle(ReadOnlySpan<byte> bytes, int offset) =>
        System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(
            bytes.Slice(offset, sizeof(float)));
}
