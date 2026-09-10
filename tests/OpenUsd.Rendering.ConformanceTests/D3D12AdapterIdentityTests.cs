// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using OpenUsd.Rendering.Silk.D3D12;
using Silk.NET.Core.Native;
using Silk.NET.DXGI;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
[SupportedOSPlatform("windows")]
public sealed class D3D12AdapterIdentityTests
{
    [Test]
    [Arguments(0u, false)]
    [Arguments(1u, false)]
    [Arguments(2u, true)]
    [Arguments(3u, true)]
    public async Task AdapterIdentityUsesTheDriverDescriptionAndActualSoftwareFlag(uint flags, bool software)
    {
        RequireWindows();
        (string name, bool isSoftware) = FromDescription("Driver-provided adapter", flags);
        await Assert.That(name).IsEqualTo("Driver-provided adapter");
        await Assert.That(isSoftware).IsEqualTo(software);
    }

    [Test]
    [Arguments(0)]
    [Arguments(128)]
    public async Task EmptyOrUnterminatedAdapterDescriptionsAreRefused(int length)
    {
        RequireWindows();
        await Assert.ThrowsAsync<InvalidDataException>(
            () => Task.Run(() => FromDescription(new string('x', length), 0)));
    }

    [Test]
    public async Task MaximumTerminatedAdapterDescriptionPreservesEveryCharacter()
    {
        RequireWindows();
        string expected = new('x', 127);
        (string name, bool isSoftware) = FromDescription(expected, 0);
        await Assert.That(name).IsEqualTo(expected);
        await Assert.That(isSoftware).IsFalse();
    }

    [Test]
    public async Task WarpCapabilitiesMatchTheActualDxgiAdapter()
    {
        RequireWindows();
        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        (string expected, bool software) = ReadNativeIdentity(device);
        await Assert.That(device.Capabilities.DeviceName).IsEqualTo(expected);
        await Assert.That(device.Capabilities.IsSoftware).IsEqualTo(software);
        await Assert.That(software).IsTrue();
        await Assert.That(device.Capabilities.DeviceName).IsNotEqualTo("D3D12 WARP");
    }

    private static unsafe (string Name, bool IsSoftware) FromDescription(string name, uint flags)
    {
        AdapterDesc1 description = default;
        name.AsSpan().CopyTo(new Span<char>(description.Description, 128));
        description.Flags = flags;
        return D3D12SilkGraphicsDevice.ReadAdapterIdentity(description);
    }

    private static unsafe (string Name, bool IsSoftware) ReadNativeIdentity(D3D12SilkGraphicsDevice device)
    {
        AdapterDesc1 description;
        SilkMarshal.ThrowHResult(device.Adapter->GetDesc1(&description));
        return (new string(description.Description), (description.Flags & (uint)AdapterFlag.Software) != 0);
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("DXGI adapter identity is a Windows backend contract.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
    }
}
