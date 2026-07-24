// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed class ShadeConnectedSourceValidationTests
{
    [Test]
    public async Task ConnectedSourceDecoderPreservesValidTriples()
    {
        OpenUsdNativeShadeConnection[] sources =
            OpenUsdNativeRuntime.DecodeConnectedShadeSources(
            [
                "/World/ShaderA", "surface", "2",
                "/World/ShaderB", "roughness", "1",
                "/München/着色器", "albédo", "2",
            ]);

        await Assert.That(sources.Length).IsEqualTo(3);
        await Assert.That(sources[0])
            .IsEqualTo(new OpenUsdNativeShadeConnection(
                "/World/ShaderA",
                "surface",
                OpenUsdNativeShadeAttributeType.Output));
        await Assert.That(sources[1])
            .IsEqualTo(new OpenUsdNativeShadeConnection(
                "/World/ShaderB",
                "roughness",
                OpenUsdNativeShadeAttributeType.Input));
        await Assert.That(sources[2])
            .IsEqualTo(new OpenUsdNativeShadeConnection(
                "/München/着色器",
                "albédo",
                OpenUsdNativeShadeAttributeType.Output));
    }

    [Test]
    public async Task ConnectedSourceDecoderRejectsMalformedTriplesAndPaths()
    {
        string[][] malformed =
        [
            [],
            ["/World/Shader"],
            ["/World/Shader", "surface"],
            ["/World/Shader", "surface", "2", "trailing"],
            ["", "surface", "2"],
            ["relative/Shader", "surface", "2"],
            ["/", "surface", "2"],
            ["/World//Shader", "surface", "2"],
            [null!, "surface", "2"],
            ["/World/Shader", "", "2"],
            ["/World/Shader", " ", "2"],
            ["/World/Shader", "surface", ""],
            ["/World/Shader", "surface", "0"],
            ["/World/Shader", "surface", "3"],
            ["/World/Shader", "surface", " 1"],
            ["/World/Shader", "surface", "invalid"],
        ];

        foreach (string[] values in malformed)
        {
            OpenUsdNativeException exception = CaptureNativeFailure(
                () => OpenUsdNativeRuntime.DecodeConnectedShadeSources(values));
            await Assert.That(exception.Status)
                .IsEqualTo(OpenUsdNativeStatus.NativeError);
        }
    }

    private static OpenUsdNativeException CaptureNativeFailure(Action action)
    {
        try
        {
            action();
        }
        catch (OpenUsdNativeException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected an OpenUsdNativeException.");
    }
}
