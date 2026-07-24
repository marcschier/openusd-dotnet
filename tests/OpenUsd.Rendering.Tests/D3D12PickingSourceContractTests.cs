// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

public sealed class D3D12PickingSourceContractTests
{
    [Test]
    public async Task PickingUsesCheckedPsoAndRootConstantTokenBinding()
    {
        string root = FindRepositoryRoot();
        string picking = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.D3D12",
            "D3D12SilkGraphicsDevice.Picking.cs"));

        await Assert.That(picking).Contains(": ISilkPickingGraphicsDevice");
        await Assert.That(picking).Contains(": ISilkPickGraphicsCommandList");
        await Assert.That(picking).Contains("RootParameterType.TypeCbv");
        await Assert.That(picking).Contains("new RootDescriptor(0, 0)");
        await Assert.That(picking).Contains("RootParameterType.Type32BitConstants");
        await Assert.That(picking).Contains("new RootConstants(1, 0, 4)");
        await Assert.That(picking).Contains("Format.FormatR8G8B8A8Unorm");
        await Assert.That(picking).Contains("DSVFormat = Format.FormatD32Float");
        await Assert.That(picking).Contains("SampleDesc = new SampleDesc(1, 0)");
        await Assert.That(picking).Contains("CullMode = CullMode.None");
        await Assert.That(picking).Contains("DepthFunc = ComparisonFunc.LessEqual");
    }

    [Test]
    public async Task PickingPreallocatesThreeReusableAlignedExecutionSlots()
    {
        string root = FindRepositoryRoot();
        string picking = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.D3D12",
            "D3D12SilkGraphicsDevice.Picking.cs"));
        string copyMethod = SliceMethod(
            picking,
            "public void CopyRgba8Pixel(");

        await Assert.That(picking).Contains("PickReadbackRowPitch = 256");
        await Assert.That(picking).Contains("new HeapProperties(HeapType.Readback)");
        await Assert.That(picking).Contains("ResourceStates.CopyDest");
        await Assert.That(picking).Contains("_device->CreateCommandAllocator");
        await Assert.That(picking).Contains("_device->CreateCommandList");
        await Assert.That(picking).Contains("_device->CreateFence");
        await Assert.That(picking).Contains("_allocator->Reset()");
        await Assert.That(picking).Contains("_commands->Reset(_allocator, null)");
        await Assert.That(picking).Contains("ArePickCompletionsHeldForTesting");
        await Assert.That(copyMethod).DoesNotContain("CreateCommittedResource");
    }

    [Test]
    public async Task PickingCopiesOneTopLeftPixelAndRestoresTrackedStates()
    {
        string root = FindRepositoryRoot();
        string commands = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.D3D12",
            "D3D12SilkGraphicsDevice.Offscreen.cs"));
        string documentation = await File.ReadAllTextAsync(Path.Combine(
            root,
            "docs",
            "rendering.md"));

        await Assert.That(commands).Contains("new SubresourceFootprint(");
        await Assert.That(commands).Contains("PickReadbackRowPitch");
        await Assert.That(commands).Contains("var sourceBox = new Box(");
        await Assert.That(commands).Contains("command.PickCoordinate.X + 1");
        await Assert.That(commands).Contains("command.PickCoordinate.Y + 1");
        await Assert.That(commands).Contains("nativeCommands->CopyTextureRegion(");
        await Assert.That(commands).Contains("ResourceStates.CopySource");
        await Assert.That(commands).Contains("pickPreviousState");
        await Assert.That(commands).Contains(
            "finalStates[pickSource] = pickPreviousState");
        await Assert.That(commands).Contains("if (pickPipeline is null)");
        await Assert.That(documentation).Contains(
            "D3D12 keeps three persistently mapped 256-byte readback slots");
    }

    private static string SliceMethod(string value, string signature)
    {
        int startIndex = value.IndexOf(signature, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            throw new InvalidOperationException(
                $"Could not find source method '{signature}'.");
        }
        int bodyStart = value.IndexOf('{', startIndex);
        if (bodyStart < 0)
        {
            throw new InvalidOperationException(
                $"Could not find source body for '{signature}'.");
        }

        int depth = 0;
        for (int index = bodyStart; index < value.Length; index++)
        {
            if (value[index] == '{')
            {
                depth++;
            }
            else if (value[index] == '}' && --depth == 0)
            {
                return value[startIndex..(index + 1)];
            }
        }
        throw new InvalidOperationException(
            $"Could not find source method end for '{signature}'.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
