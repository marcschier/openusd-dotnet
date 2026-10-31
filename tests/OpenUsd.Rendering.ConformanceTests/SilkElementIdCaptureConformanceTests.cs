// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkElementIdCaptureConformanceTests
{
    private const int Size = 32;
    private const float Margin = 0.08f;

    // Emission order differs from authored face order, so a capture that reported the
    // emitted triangle or quad index instead of the authored face would disagree.
    private static readonly (float Left, float Bottom, float Right, float Top, int Face)[] FaceQuads =
    [
        (-0.9f, 0.1f, -0.4f, 0.9f, 2),
        (-0.25f, 0.1f, 0.25f, 0.9f, 0),
        (0.4f, 0.1f, 0.9f, 0.9f, 3),
        (-0.9f, -0.9f, -0.1f, -0.1f, 1),
    ];

    private static readonly (float Left, float Bottom, float Right, float Top) UnsupportedQuad =
        (0.1f, -0.9f, 0.9f, -0.1f);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task RetainedCaptureResolvesVisibleAuthoredFaceIndicesFromThePickRaster(
        SilkGraphicsBackend backend, bool withPrimId)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var renderer = new SilkMeshRenderer(device);
        SilkMeshRendererConformance.Apply(
            renderer, 1,
            SilkMeshRendererConformance.CreateFrameCommand(Size, Size, SilkMeshRendererConformance.Identity()),
            CreateFacesCommand(),
            CreateUnsupportedCommand());

        SilkFrameCaptureResult capture = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, Size, Size, RenderSettings.Default,
            new SilkDepthCaptureOptions { IncludePrimId = withPrimId, IncludeElementId = true }, pageRevision: 1);
        SilkElementIdCaptureResult elementId = capture.ElementId
            ?? throw new InvalidOperationException("An explicitly requested element identity capture is missing.");
        int[] original = elementId.Values.ToArray();
        SilkFrameCaptureResult primOnly = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, Size, Size, RenderSettings.Default,
            new SilkDepthCaptureOptions { IncludePrimId = true }, pageRevision: 2);
        renderer.Dispose();
        device.Dispose();

        await Assert.That(elementId.Width).IsEqualTo(Size);
        await Assert.That(elementId.Height).IsEqualTo(Size);
        await Assert.That(elementId.Values.Length).IsEqualTo(Size * Size);
        await Assert.That(original[(8 * Size) + 5]).IsEqualTo(2);
        await Assert.That(original[(8 * Size) + 16]).IsEqualTo(0);
        await Assert.That(original[(8 * Size) + 26]).IsEqualTo(3);
        await Assert.That(original[(24 * Size) + 8]).IsEqualTo(1);
        await Assert.That(original[(24 * Size) + 24]).IsEqualTo(-1)
            .Because("a prim that refuses authored face identity reports no element index");
        await Assert.That(original[(16 * Size) + 16]).IsEqualTo(-1).Because("background has no element index");
        await Assert.That(original.All(static value => value is >= -1 and <= 3)).IsTrue();
        int interior = 0;
        for (int pixel = 0; pixel < original.Length; pixel++)
        {
            float x = ((pixel % Size) + 0.5f) / (Size / 2f) - 1;
            float y = 1 - (((pixel / Size) + 0.5f) / (Size / 2f));
            int? expected = Expected(x, y);
            if (expected is { } face)
            {
                interior++;
                if (original[pixel] != face)
                {
                    Assert.Fail($"Pixel {pixel} reports element {original[pixel]} but its authored face is {face}.");
                }
            }
        }
        await Assert.That(interior).IsGreaterThan(Size * Size / 4);

        await Assert.That(capture.PrimId is not null).IsEqualTo(withPrimId);
        await Assert.That(capture.InstanceId).IsNull();
        await Assert.That(primOnly.ElementId).IsNull();
        SilkPrimIdCaptureResult reference = withPrimId ? capture.PrimId! : primOnly.PrimId!;
        await Assert.That(string.Join('|', reference.Identities.Select(static identity => identity.Path)))
            .IsEqualTo("/Faces|/Unsupported");
        for (int pixel = 0; pixel < original.Length; pixel++)
        {
            int index = reference.Values.Span[pixel];
            if ((index < 0 || reference.Identities[index].Path == "/Unsupported") != (original[pixel] < 0))
            {
                Assert.Fail($"Pixel {pixel} element {original[pixel]} disagrees with its prim identity {index}.");
            }
        }
        await Assert.That(elementId.Values.Span.SequenceEqual(original)).IsTrue()
            .Because("later captures and renderer/device disposal must not overwrite owned element indices");
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task DeviceWithoutPickingRefusesElementIdInsteadOfSynthesizingIt(SilkGraphicsBackend backend)
    {
        using var device = new SilkDepthCaptureObservedDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        using var renderer = new SilkMeshRenderer(device);
        SilkMeshRendererConformance.Apply(
            renderer, 1,
            SilkMeshRendererConformance.CreateFrameCommand(Size, Size, SilkMeshRendererConformance.Identity()),
            CreateFacesCommand());

        NotSupportedException? failure = Assert.Throws<NotSupportedException>(
            () => SilkFrameCapture.CaptureRetainedWithDepth(
                renderer, device, Size, Size, RenderSettings.Default,
                new SilkDepthCaptureOptions { IncludeElementId = true }, pageRevision: 1));
        SilkFrameCaptureResult depthOnly = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, Size, Size, RenderSettings.Default, pageRevision: 1);

        await Assert.That(failure!.Message).Contains("Element identity capture requires a pick-capable");
        await Assert.That(depthOnly.Depth).IsNotNull();
        await Assert.That(depthOnly.ElementId).IsNull();
    }

    private static int? Expected(float x, float y)
    {
        foreach ((float left, float bottom, float right, float top, int face) in FaceQuads)
        {
            if (Inside(x, y, left, bottom, right, top, -Margin))
            {
                return face;
            }
            if (Inside(x, y, left, bottom, right, top, Margin))
            {
                return null;
            }
        }
        (float uLeft, float uBottom, float uRight, float uTop) = UnsupportedQuad;
        if (Inside(x, y, uLeft, uBottom, uRight, uTop, -Margin))
        {
            return -1;
        }
        return Inside(x, y, uLeft, uBottom, uRight, uTop, Margin) ? null : -1;
    }

    private static bool Inside(float x, float y, float left, float bottom, float right, float top, float grow) =>
        x > left - grow && x < right + grow && y > bottom - grow && y < top + grow;

    private static byte[] CreateFacesCommand()
    {
        var points = new List<float>();
        var indices = new List<uint>();
        var subprims = new List<int>();
        foreach ((float left, float bottom, float right, float top, int face) in FaceQuads)
        {
            uint first = (uint)(points.Count / 3);
            points.AddRange([left, bottom, 0.5f, right, bottom, 0.5f, right, top, 0.5f, left, top, 0.5f]);
            indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
            subprims.AddRange([face, face]);
        }
        return SilkMeshRendererConformance.CreateMeshCommand(
            1, "/Faces", [.. points], [.. indices], 0, 0, null, 1, [.. subprims], null, null);
    }

    /// <summary>
    /// Builds a mesh whose record clears the authored face identity flag, as a delegate
    /// does when it cannot map emitted triangles back onto authored faces.
    /// </summary>
    private static byte[] CreateUnsupportedCommand()
    {
        (float left, float bottom, float right, float top) = UnsupportedQuad;
        byte[] command = SilkMeshRendererConformance.CreateMeshCommand(
            2, "/Unsupported",
            [left, bottom, 0.5f, right, bottom, 0.5f, right, top, 0.5f, left, top, 0.5f],
            [0, 1, 2, 0, 2, 3], 0, 0, null, 1, [0, 0], null, null);
        BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(236), (uint)SilkSubprimIdentity.None);
        BinaryPrimitives.WriteUInt32LittleEndian(
            command.AsSpan(240), (uint)SilkSubprimUnsupportedReason.Geometry);
        return command;
    }
}
