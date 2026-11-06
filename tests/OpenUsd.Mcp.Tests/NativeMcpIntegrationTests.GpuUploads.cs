// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;

namespace OpenUsd.Mcp.Tests;

public sealed partial class NativeMcpIntegrationTests
{
    [Test]
    [NotInParallel]
    [Arguments("raw")]
    [Arguments("exr")]
    public async Task NativeGpuUploadSequenceUsesHardwareAndReleasesResources(string hdrFormat)
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_TEST_D3D12_GPU_UPLOADS_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_TEST_D3D12_GPU_UPLOADS_REQUIRED=1 with a matched runtime and supported hardware.");
            return;
        }
        await RunNativeSequenceAsync(includeDepth: true, hdrFormat: hdrFormat, bounded: true,
            streaming: true, gpuUploadHeaps: true);
    }

    private sealed class GpuUploadTrackingFactory : IPreviewGraphicsDeviceFactory
    {
        private readonly PreviewGraphicsDeviceFactory _inner = new();
        internal int HardwareGpuUploadCreates { get; private set; }

        public ISilkGraphicsDevice Create(PreviewGraphicsDeviceOptions options)
        {
            ISilkGraphicsDevice device = _inner.Create(options);
            if (OperatingSystem.IsWindows() && device is D3D12SilkGraphicsDevice d3d12 &&
                d3d12.UsesGpuUploadHeaps && !device.Capabilities.IsSoftware)
            {
                HardwareGpuUploadCreates++;
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    scope = "Bounded native MCP fixture, not whole-scene warehouse qualification.",
                    backend = "D3D12",
                    capabilities = device.Capabilities,
                    gpuUploadHeaps = d3d12.UsesGpuUploadHeaps
                }));
            }
            return device;
        }
    }
}
