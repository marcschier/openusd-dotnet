// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Metal;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Mcp;

public sealed record PreviewGraphicsDeviceOptions(bool UseWarpOnWindows = true)
{
    /// <summary>Gets whether Windows previews require supported GPU-local upload heaps.</summary>
    /// <remarks>
    /// Requires UseWarpOnWindows=false. Custom factories must enforce or refuse this option;
    /// the legacy delegate-based factory constructor refuses it rather than ignoring it.
    /// </remarks>
    public bool D3D12GpuUploadHeaps { get; init; }
}

public interface IPreviewGraphicsDeviceFactory
{
    ISilkGraphicsDevice Create(PreviewGraphicsDeviceOptions options);
}

public sealed class PreviewGraphicsDeviceFactory : IPreviewGraphicsDeviceFactory
{
    private readonly Func<PreviewGraphicsDeviceOptions, ISilkGraphicsDevice> _createD3D12;
    private readonly Func<ISilkGraphicsDevice> _createVulkan;
    private readonly Func<ISilkGraphicsDevice> _createMetal;

    public PreviewGraphicsDeviceFactory()
    {
        _createD3D12 = CreateD3D12;
        _createVulkan = CreateVulkan;
        _createMetal = CreateMetal;
    }

    public PreviewGraphicsDeviceFactory(
        Func<bool, ISilkGraphicsDevice> createD3D12,
        Func<ISilkGraphicsDevice> createVulkan,
        Func<ISilkGraphicsDevice> createMetal)
    {
        ArgumentNullException.ThrowIfNull(createD3D12);
        _createD3D12 = options => options.D3D12GpuUploadHeaps
            ? throw new NotSupportedException("The legacy injected D3D12 factory cannot enforce GPU-upload heaps.")
            : createD3D12(options.UseWarpOnWindows);
        _createVulkan = createVulkan ?? throw new ArgumentNullException(nameof(createVulkan));
        _createMetal = createMetal ?? throw new ArgumentNullException(nameof(createMetal));
    }

    public ISilkGraphicsDevice Create(PreviewGraphicsDeviceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.D3D12GpuUploadHeaps && (!OperatingSystem.IsWindows() || options.UseWarpOnWindows))
        {
            throw new NotSupportedException("GPU-upload heaps require Windows D3D12 hardware, not WARP.");
        }
        if (OperatingSystem.IsWindows())
        {
            return _createD3D12(options);
        }

        if (OperatingSystem.IsLinux())
        {
            return _createVulkan();
        }

        if (OperatingSystem.IsMacOS())
        {
            return _createMetal();
        }

        throw new PlatformNotSupportedException(
            "Preview capture supports D3D12 on Windows, Vulkan on Linux, and Metal on macOS.");
    }

    private static D3D12SilkGraphicsDevice CreateD3D12(PreviewGraphicsDeviceOptions options)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        return D3D12SilkGraphicsDevice.Create(options.UseWarpOnWindows, options.D3D12GpuUploadHeaps);
    }

    private static VulkanSilkGraphicsDevice CreateVulkan()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException();
        }

        return VulkanSilkGraphicsDevice.Create();
    }

    private static MetalSilkGraphicsDevice CreateMetal()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException();
        }

        return MetalSilkGraphicsDevice.Create();
    }
}
