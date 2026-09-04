// Copyright (c) marcschier. Licensed under the MIT License.

using System.Reflection;
using System.Runtime.CompilerServices;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Drives the production presentation adapters and backend sessions with a renderer that
/// really refused a display transform, and reads what the composition layer would see.
/// </summary>
/// <remarks>
/// <para>
/// The gap this pins is not hypothetical. <c>ISilkStagePresentationRenderer</c> defaults
/// <c>DisplayTransformDiagnostics</c> to <c>default</c>, which is the inactive,
/// success-shaped value. A composition session reads its diagnostics through that
/// interface, so before the backends overrode it the Viewer would keep a checked
/// "Colour management" menu item while the renderer had already fallen back to
/// untransformed linear colour. Asserting on the source text of the override is not
/// enough - the assertions below construct the production adapter types and read the
/// interface property.
/// </para>
/// <para>
/// The Storm sessions cannot host an Avalonia control here, so they are materialized
/// without their constructor and their state field is set directly. Everything read
/// afterwards is the production property body.
/// </para>
/// </remarks>
public sealed class ViewerDisplayTransformWiringTests
{
    [Test]
    public async Task ARendererThatRefusedADeletedConfigReportsItThroughTheD3D12Adapter()
    {
        string missing = Path.Combine(
            Path.GetDirectoryName(typeof(ViewerDisplayTransformWiringTests).Assembly.Location)!,
            "viewer-wiring-deleted-config.ocio");
        if (File.Exists(missing))
        {
            File.Delete(missing);
        }

        using var device = new DisplayTransformCapableSilkDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(32, 32));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(32, 32));

        var transform = new RenderDisplayTransform(
            missing,
            "linear",
            "sRGB",
            "view",
            latticeSize: RenderDisplayTransform.MinimumLatticeSize);
        _ = renderer.Render(
            color,
            depth,
            SilkMeshRenderOptions.Default with { DisplayTransform = transform });

        // The renderer itself has to have failed, or the rest proves nothing.
        await Assert.That(renderer.DisplayTransformDiagnostics.Status)
            .IsEqualTo(SilkDisplayTransformStatus.ConfigUnavailable);
        await Assert.That(renderer.DisplayTransformDiagnostic).IsNotNull();

        ISilkStagePresentationRenderer presentation = new D3D12StagePresentationRenderer(
            new NoOpSilkSessionAdapter(),
            renderer,
            StageRenderState.Default);

        await Assert.That(presentation.DisplayTransformDiagnostics.Status)
            .IsEqualTo(SilkDisplayTransformStatus.ConfigUnavailable);
        await Assert.That(presentation.DisplayTransformDiagnostics.Failures)
            .IsEqualTo(renderer.DisplayTransformDiagnostics.Failures);
        await Assert.That(presentation.DisplayTransformDiagnostic).IsNotNull();
        await Assert.That(presentation.DisplayTransformDiagnostic!.Code).IsEqualTo(
            SilkRenderDiagnosticCodes.DisplayTransformConfigUnavailable);
    }

    [Test]
    [Arguments(typeof(VulkanStagePresentationRenderer))]
    [Arguments(typeof(MetalStagePresentationRenderer))]
    public async Task ASwappingPresentationAdapterReportsItsCurrentRenderersRefusal(
        Type adapterType)
    {
        string missing = Path.Combine(
            Path.GetDirectoryName(typeof(ViewerDisplayTransformWiringTests).Assembly.Location)!,
            "viewer-wiring-deleted-config-" + adapterType.Name + ".ocio");
        if (File.Exists(missing))
        {
            File.Delete(missing);
        }

        using var device = new DisplayTransformCapableSilkDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(32, 32));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(32, 32));

        _ = renderer.Render(
            color,
            depth,
            SilkMeshRenderOptions.Default with
            {
                DisplayTransform = new RenderDisplayTransform(
                    missing,
                    "linear",
                    "sRGB",
                    "view",
                    latticeSize: RenderDisplayTransform.MinimumLatticeSize),
            });

        object adapter = RuntimeHelpers.GetUninitializedObject(adapterType);

        // Before a renderer exists the honest answer is "nothing ran", not "applied".
        var typed = (ISilkStagePresentationRenderer)adapter;
        await Assert.That(typed.DisplayTransformDiagnostics.Status)
            .IsEqualTo(SilkDisplayTransformStatus.Inactive);
        await Assert.That(typed.DisplayTransformDiagnostic).IsNull();

        FieldInfo current = adapterType.GetField(
            "_currentRenderer",
            BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException(
                adapterType.Name + " no longer keeps a current renderer.");
        current.SetValue(adapter, renderer);

        await Assert.That(typed.DisplayTransformDiagnostics.Status)
            .IsEqualTo(SilkDisplayTransformStatus.ConfigUnavailable);
        await Assert.That(typed.DisplayTransformDiagnostic).IsNotNull();
        await Assert.That(typed.DisplayTransformDiagnostic!.Code).IsEqualTo(
            SilkRenderDiagnosticCodes.DisplayTransformConfigUnavailable);
    }

    [Test]
    [Arguments("StormHostedBackendSession")]
    [Arguments("StormNativeHostedBackendSession")]
    public async Task AStormSessionReportsUnsupportedDeviceForARequestedTransform(
        string sessionTypeName)
    {
        Type sessionType =
            typeof(ViewerRenderCoordinator).Assembly.GetType(
                "OpenUsd.Viewer." + sessionTypeName) ??
            throw new InvalidOperationException(sessionTypeName + " no longer exists.");

        await Assert.That(
            typeof(IViewerDisplayTransformDiagnosticsSource).IsAssignableFrom(sessionType))
            .IsTrue();

        object session = RuntimeHelpers.GetUninitializedObject(sessionType);
        FieldInfo state = sessionType.GetField(
            "_state",
            BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException(sessionTypeName + " no longer keeps state.");

        string config = Path.Combine(
            Path.GetDirectoryName(typeof(ViewerDisplayTransformWiringTests).Assembly.Location)!,
            "storm-unsupported.ocio");
        var requested = StageRenderState.Default.WithRenderSettings(
            RenderSettings.PresentationDefault with
            {
                DisplayTransform = new RenderDisplayTransform(config, "linear", "sRGB", "view"),
            });

        state.SetValue(session, StageRenderState.Default);
        var source = (IViewerDisplayTransformDiagnosticsSource)session;
        await Assert.That(source.DisplayTransformDiagnostics?.Status)
            .IsEqualTo(SilkDisplayTransformStatus.Inactive);
        await Assert.That(source.DisplayTransformDiagnostic).IsNull();

        state.SetValue(session, requested);
        await Assert.That(source.DisplayTransformDiagnostics?.Status)
            .IsEqualTo(SilkDisplayTransformStatus.UnsupportedDevice);
        await Assert.That(source.DisplayTransformDiagnostic).IsNotNull();
        await Assert.That(source.DisplayTransformDiagnostic!.Code).IsEqualTo(
            SilkRenderDiagnosticCodes.DisplayTransformDeviceUnsupported);
    }

    [Test]
    public async Task AStaleFailureNeverDisablesATransformThatHasSinceSucceeded()
    {
        // The production defect: diagnostics are cumulative and read asynchronously, so
        // a refusal for config A can be observed after config B has been committed and
        // is rendering correctly. A rule that looks only at the status would disable a
        // working transform and clear it from the authoritative state.
        string missing = Path.Combine(
            Path.GetDirectoryName(typeof(ViewerDisplayTransformWiringTests).Assembly.Location)!,
            "viewer-wiring-stale-failure.ocio");
        if (File.Exists(missing))
        {
            File.Delete(missing);
        }

        var refused = new RenderDisplayTransform(
            missing,
            "linear",
            "sRGB",
            "view",
            latticeSize: RenderDisplayTransform.MinimumLatticeSize);
        var applied = new RenderDisplayTransform(
            missing + ".replacement",
            "linear",
            "TestDisplay",
            "TestView",
            latticeSize: RenderDisplayTransform.MinimumLatticeSize);

        SilkDisplayTransformDiagnostics staleReport =
            new SilkDisplayTransformDiagnostics() with
            {
                Status = SilkDisplayTransformStatus.ConfigUnavailable,
                Failures = 1,
                RequestKey = refused.CacheKey,
            };
        var staleDiagnostic = new RenderDiagnostic(
            RenderDiagnosticSeverity.Error,
            SilkRenderDiagnosticCodes.DisplayTransformConfigUnavailable,
            "The superseded display transform configuration is unavailable.");

        await Assert.That(staleReport.Status)
            .IsEqualTo(SilkDisplayTransformStatus.ConfigUnavailable);
        await Assert.That(staleReport.RequestKey).IsEqualTo(refused.CacheKey);

        // The user has already switched to the working config, so this is what the
        // authoritative state carries when the stale report finally arrives.
        ViewerColorManagementSyncResult stale = ViewerColorManagementSync.Compute(
            requestedEnabled: true,
            committedRequestKey: applied.CacheKey,
            hasPendingRequest: false,
            staleReport.Status,
            staleReport.RequestKey,
            staleDiagnostic);

        await Assert.That(stale.State).IsEqualTo(ViewerColorManagementState.Pending);
        await Assert.That(stale.Enabled).IsTrue();
        await Assert.That(stale.ClearTransform).IsFalse();
        await Assert.That(stale.Status).IsNull();

        // Now the renderer catches up and reports that the committed transform applied.
        SilkDisplayTransformDiagnostics fresh =
            new SilkDisplayTransformDiagnostics() with
            {
                Status = SilkDisplayTransformStatus.Applied,
                RequestKey = applied.CacheKey,
            };

        await Assert.That(fresh.Status).IsEqualTo(SilkDisplayTransformStatus.Applied);
        await Assert.That(fresh.RequestKey).IsEqualTo(applied.CacheKey);

        ViewerColorManagementSyncResult current = ViewerColorManagementSync.Compute(
            requestedEnabled: true,
            committedRequestKey: applied.CacheKey,
            hasPendingRequest: false,
            fresh.Status,
            fresh.RequestKey,
            diagnostic: null);

        await Assert.That(current.State).IsEqualTo(ViewerColorManagementState.Active);
        await Assert.That(current.ClearTransform).IsFalse();

        // And the correlated failure is still acted on when it *is* the current request.
        ViewerColorManagementSyncResult owned = ViewerColorManagementSync.Compute(
            requestedEnabled: true,
            committedRequestKey: refused.CacheKey,
            hasPendingRequest: false,
            staleReport.Status,
            staleReport.RequestKey,
            staleDiagnostic);

        await Assert.That(owned.State).IsEqualTo(ViewerColorManagementState.Failed);
        await Assert.That(owned.ClearTransform).IsTrue();
    }

    [Test]
    public async Task APendingSelectionIsNeverJudgedAgainstItsPredecessorsReport()
    {
        string missing = Path.Combine(
            Path.GetDirectoryName(typeof(ViewerDisplayTransformWiringTests).Assembly.Location)!,
            "viewer-wiring-pending.ocio");
        if (File.Exists(missing))
        {
            File.Delete(missing);
        }

        using var device = new DisplayTransformCapableSilkDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(32, 32));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(32, 32));

        _ = renderer.Render(
            color,
            depth,
            SilkMeshRenderOptions.Default with
            {
                DisplayTransform = new RenderDisplayTransform(
                    missing,
                    "linear",
                    "sRGB",
                    "view",
                    latticeSize: RenderDisplayTransform.MinimumLatticeSize),
            });
        SilkDisplayTransformDiagnostics report = renderer.DisplayTransformDiagnostics;

        // A validation is still running, so the committed state is in motion. Acting on
        // the renderer's report here would decide against a request that has not been
        // evaluated yet.
        ViewerColorManagementSyncResult pending = ViewerColorManagementSync.Compute(
            requestedEnabled: true,
            committedRequestKey: report.RequestKey,
            hasPendingRequest: true,
            report.Status,
            report.RequestKey,
            renderer.DisplayTransformDiagnostic);

        await Assert.That(pending.State).IsEqualTo(ViewerColorManagementState.Pending);
        await Assert.That(pending.ClearTransform).IsFalse();
        await Assert.That(pending.Enabled).IsTrue();
    }

    [Test]
    public async Task ADisabledChoiceNeverLeavesATransformCommitted()
    {
        // The other half of the invariant: the menu and the settings say colour
        // management is off, so a transform must not still be in the authoritative
        // state colouring the image.
        ViewerColorManagementSyncResult leftover = ViewerColorManagementSync.Compute(
            requestedEnabled: false,
            committedRequestKey: "ocio:leftover",
            hasPendingRequest: false,
            SilkDisplayTransformStatus.Applied,
            backendRequestKey: "ocio:leftover",
            diagnostic: null);

        await Assert.That(leftover.State).IsEqualTo(ViewerColorManagementState.Disabled);
        await Assert.That(leftover.ClearTransform).IsTrue();

        ViewerColorManagementSyncResult settled = ViewerColorManagementSync.Compute(
            requestedEnabled: false,
            committedRequestKey: null,
            hasPendingRequest: false,
            SilkDisplayTransformStatus.Inactive,
            backendRequestKey: null,
            diagnostic: null);

        await Assert.That(settled.State).IsEqualTo(ViewerColorManagementState.Disabled);
        await Assert.That(settled.ClearTransform).IsFalse();
    }

    [Test]
    public async Task TheViewerReconciliationIsCorrelatedInProduction()
    {
        string root = FindRepositoryRoot();
        string source = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "MainWindow.ColorManagement.cs"));

        // The rule is only worth anything if production feeds it the correlation and the
        // pending flag rather than defaults.
        await Assert.That(source).Contains("_committedDisplayTransformKey,");
        await Assert.That(source).Contains(
            "_colorManagementRequests?.HasPendingRequest ?? false,");
        await Assert.That(source).Contains("diagnostics?.RequestKey,");

        // And nothing is committed before the coordinator confirms the mutation.
        // And nothing is committed before the coordinator confirms the mutation, using
        // the state it published rather than the one that was requested.
        await Assert.That(source).Contains(
            "ViewerStateMutationResult mutation = await TryApplyViewportStateAsync(");
        await Assert.That(source).Contains("ViewerColorManagementCommit.Decide(");
        await Assert.That(source).Contains(
            "mutation.PublishedState.RenderSettings.DisplayTransform,");
        await Assert.That(source).Contains(
            "pipeline.MarkCommitted(outcome.Version);");
        await Assert.That(source).Contains(
            "ViewerDeferredColorManagement.SelectOpeningChoice(");
        await Assert.That(source).Contains(
            "_colorManagementRequests?.MarkCommitted(_openingColorManagementGeneration);");
        await Assert.That(source).DoesNotContain("_pendingColorManagementRequests");

        // The open commit runs only after the coordinator exists, and the drain that
        // replays anything newer runs only after the window reports ready.
        string window = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "MainWindow.axaml.cs"));
        int assignIndex = window.IndexOf("_coordinator = coordinator;", StringComparison.Ordinal);
        int confirmIndex = window.IndexOf(
            "await ConfirmColorManagementOpenAsync(",
            StringComparison.Ordinal);
        int readyIndex = window.IndexOf(
            "SetReady($\"Opened {normalizedPath}\");",
            StringComparison.Ordinal);
        int drainIndex = window.IndexOf(
            "await DrainColorManagementRequestsAsync();",
            StringComparison.Ordinal);
        await Assert.That(assignIndex).IsGreaterThan(0);
        await Assert.That(confirmIndex).IsGreaterThan(assignIndex);
        await Assert.That(readyIndex).IsGreaterThan(confirmIndex);
        await Assert.That(drainIndex).IsGreaterThan(readyIndex);
    }
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class NoOpSilkSessionAdapter : IViewerSilkSessionAdapter
    {
        public OpenUsdSilkPage Sync(int width, int height, ViewerFrameRequest request) =>
            throw new NotSupportedException(
                "The wiring tests never synchronize a stage.");
    }

    /// <summary>
    /// A device that claims display-transform capability so the pass gets as far as
    /// asking for a lattice, and can carry a successful pass through to
    /// <see cref="SilkDisplayTransformStatus.Applied"/>.
    /// </summary>
    private sealed class DisplayTransformCapableSilkDevice
        : ISilkGraphicsDevice, ISilkDisplayTransformGraphicsDevice
    {
        public SilkGraphicsBackend Backend => SilkGraphicsBackend.Vulkan;

        public SilkGraphicsCapabilities Capabilities { get; } =
            new("Viewer wiring test", "1", SupportsCompute: false, IsSoftware: true);

        public ulong DisplayTransformDeviceGeneration => 1;

        public ISilkDisplayTransformGraphicsPipeline CreateDisplayTransformGraphicsPipeline(
            SilkDisplayTransformPipelineDescriptor descriptor)
        {
            descriptor.Validate();
            return new WiringDisplayTransformPipeline(descriptor);
        }

        public ISilkDisplayTransformBinding CreateDisplayTransformBinding(
            SilkDisplayTransformBindingDescriptor descriptor)
        {
            descriptor.Validate();
            return new WiringDisplayTransformBinding(descriptor);
        }

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage) =>
            new WiringBuffer(size, usage);

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            new WiringTexture(
                new SilkTextureDescriptor(
                    width,
                    height,
                    format,
                    SilkTextureDescriptor.GetDefaultUsage(format)));

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) =>
            new WiringTexture(descriptor);

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
            new WiringSampler(descriptor);

        public ISilkGraphicsShaderModule CreateShaderModule(
            SilkShaderModuleDescriptor descriptor) =>
            new WiringShaderModule(descriptor);

        public ISilkGraphicsBindingLayout CreateBindingLayout(
            SilkBindingLayoutDescriptor descriptor) =>
            new WiringBindingLayout(descriptor);

        public ISilkGraphicsShaderProgram CreateShaderProgram(
            SilkShaderProgramDescriptor descriptor) =>
            new WiringShaderProgram(descriptor.BindingLayout);

        public ISilkGraphicsPipeline CreateGraphicsPipeline(
            SilkGraphicsPipelineDescriptor descriptor) =>
            new WiringPipeline(descriptor);

        public ISilkComputeBindingLayout CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeShaderProgram CreateComputeShaderProgram(
            SilkComputeShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputePipeline CreateComputePipeline(
            SilkComputePipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsCommandList CreateCommandList() => new WiringCommandList();

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) =>
            new WiringSubmission();

        public void WaitIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class WiringBuffer(nuint size, SilkBufferUsage usage)
        : SilkGraphicsBufferBase(size, usage)
    {
        private readonly byte[] _data = new byte[checked((int)size)];

        public override void Write(ReadOnlySpan<byte> data, nuint offset = 0)
        {
            _ = ValidateWrite(data.Length, offset);
            data.CopyTo(_data.AsSpan(checked((int)offset)));
        }

        public override void ReadbackForTesting(Span<byte> destination)
        {
            _ = ValidateReadback(destination.Length);
            _data.CopyTo(destination);
        }

        protected override void ReleaseNative()
        {
        }
    }

    private sealed class WiringTexture(SilkTextureDescriptor descriptor)
        : SilkGraphicsTextureBase(descriptor)
    {
        public override void ReadbackForTesting(Span<byte> destination) => destination.Clear();

        public override void ReadbackForTesting(Span<float> destination) => destination.Clear();

        protected override void ReleaseNative()
        {
        }
    }

    private sealed class WiringSampler(SilkSamplerDescriptor descriptor) : ISilkGraphicsSampler
    {
        public SilkSamplerDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class WiringShaderModule(SilkShaderModuleDescriptor descriptor)
        : ISilkGraphicsShaderModule
    {
        public SilkShaderModuleDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class WiringBindingLayout(SilkBindingLayoutDescriptor descriptor)
        : ISilkGraphicsBindingLayout
    {
        public SilkBindingLayoutDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class WiringShaderProgram(ISilkGraphicsBindingLayout bindingLayout)
        : ISilkGraphicsShaderProgram
    {
        public ISilkGraphicsBindingLayout BindingLayout { get; } = bindingLayout;

        public void Dispose()
        {
        }
    }

    private sealed class WiringPipeline(SilkGraphicsPipelineDescriptor descriptor)
        : ISilkGraphicsPipeline
    {
        public SilkGraphicsPipelineDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class WiringCommandList
        : ISilkGraphicsCommandList, ISilkDisplayTransformGraphicsCommandList
    {
        public void BeginDisplayTransformRendering(
            SilkDisplayTransformRenderingDescriptor descriptor) => descriptor.Validate();

        public void SetDisplayTransformGraphicsPipeline(
            ISilkDisplayTransformGraphicsPipeline pipeline)
        {
        }

        public void SetDisplayTransformBinding(ISilkDisplayTransformBinding binding)
        {
        }

        public void DrawDisplayTransformFullscreenTriangle()
        {
        }

        public void UploadTexture(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source)
        {
        }

        public void ClearColor(ISilkGraphicsTexture texture, SilkColor color)
        {
        }

        public void ClearDepth(ISilkGraphicsTexture texture, float depth)
        {
        }

        public void BeginRendering(SilkRenderingDescriptor descriptor)
        {
        }

        public void SetGraphicsPipeline(ISilkGraphicsPipeline pipeline)
        {
        }

        public void SetViewport(SilkViewport viewport)
        {
        }

        public void SetScissor(SilkScissor scissor)
        {
        }

        public void SetVertexBuffer(ISilkGraphicsBuffer buffer)
        {
        }

        public void SetIndexBuffer(ISilkGraphicsBuffer buffer)
        {
        }

        public void SetUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer)
        {
        }

        public void SetTexture(uint setIndex, uint binding, ISilkGraphicsTexture texture)
        {
        }

        public void SetSampler(uint setIndex, uint binding, ISilkGraphicsSampler sampler)
        {
        }

        public void DrawIndexed(uint indexCount)
        {
        }

        public void DrawIndexedInstanced(uint indexCount, uint instanceCount)
        {
        }

        public void EndRendering()
        {
        }

        public void SetComputePipeline(ISilkComputePipeline pipeline)
        {
        }

        public void SetStorageBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer)
        {
        }

        public void SetComputeUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer)
        {
        }

        public void Dispatch(uint elementCount)
        {
        }

        public void BufferBarrier(ISilkGraphicsBuffer buffer)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class WiringSubmission : ISilkGraphicsSubmission
    {
        public bool IsCompleted => true;

        public void Wait()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class WiringDisplayTransformPipeline(
        SilkDisplayTransformPipelineDescriptor descriptor)
        : ISilkDisplayTransformGraphicsPipeline
    {
        public SilkDisplayTransformPipelineDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class WiringDisplayTransformBinding(
        SilkDisplayTransformBindingDescriptor descriptor)
        : ISilkDisplayTransformBinding
    {
        public SilkDisplayTransformBindingDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }
}
