// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;

namespace OpenUsd.D3D12CompositionSmoke;

[SupportedOSPlatform("windows")]
internal sealed class SmokeContext : IAsyncDisposable
{
    private int _disposed;

    private SmokeContext(
        UsdStageScheduler scheduler,
        UsdStageRenderSource source,
        OpenUsdSilkSession session,
        D3D12SilkGraphicsDevice device,
        SilkMeshRenderer meshRenderer,
        StagePresentationRenderer presentationRenderer,
        D3D12CompositionViewportPresenter presenter)
    {
        Scheduler = scheduler;
        Source = source;
        Session = session;
        Device = device;
        MeshRenderer = meshRenderer;
        PresentationRenderer = presentationRenderer;
        Presenter = presenter;
    }

    internal UsdStageScheduler Scheduler { get; }

    internal UsdStageRenderSource Source { get; }

    internal OpenUsdSilkSession Session { get; }

    internal D3D12SilkGraphicsDevice Device { get; }

    internal SilkMeshRenderer MeshRenderer { get; }

    internal StagePresentationRenderer PresentationRenderer { get; }

    internal D3D12CompositionViewportPresenter Presenter { get; }

    internal static async Task<SmokeContext> CreateAsync(
        string pluginPath,
        string stagePath)
    {
        UsdStageScheduler? scheduler = null;
        UsdStageRenderSource? source = null;
        OpenUsdSilkSession? session = null;
        D3D12SilkGraphicsDevice? device = null;
        SilkMeshRenderer? meshRenderer = null;
        D3D12CompositionViewportPresenter? presenter = null;
        try
        {
            scheduler = UsdStageScheduler.Open(stagePath);
            source = await scheduler.AcquireRenderSourceAsync().ConfigureAwait(false);
            session = OpenUsdSilkRuntime.Create(pluginPath, source);
            device = D3D12SilkGraphicsDevice.Create(useWarp: false);
            meshRenderer = new SilkMeshRenderer(device);
            var presentationRenderer = new StagePresentationRenderer(session, meshRenderer);
            presenter = new D3D12CompositionViewportPresenter(device, presentationRenderer);
            return new SmokeContext(
                scheduler,
                source,
                session,
                device,
                meshRenderer,
                presentationRenderer,
                presenter);
        }
        catch
        {
            if (presenter is not null)
            {
                await presenter.DisposeAsync().ConfigureAwait(false);
            }
            meshRenderer?.Dispose();
            device?.Dispose();
            session?.Dispose();
            source?.Dispose();
            if (scheduler is not null)
            {
                await scheduler.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await Presenter.DisposeAsync().ConfigureAwait(false);
        MeshRenderer.Dispose();
        Session.Dispose();
        Source.Dispose();
        await Scheduler.DisposeAsync().ConfigureAwait(false);
        Device.Dispose();
    }
}

internal sealed class StagePresentationRenderer(
    OpenUsdSilkSession session,
    SilkMeshRenderer renderer)
    : ISilkPresentationRenderer
{
    private long _frameCount;
    private long _lastMeshUpsertFrame;
    private int _continueRendering = 1;

    internal long FrameCount => Volatile.Read(ref _frameCount);

    internal long LastMeshUpsertFrame => Volatile.Read(ref _lastMeshUpsertFrame);

    internal void Pause() => Volatile.Write(ref _continueRendering, 0);

    internal void Resume() => Volatile.Write(ref _continueRendering, 1);

    public SilkPresentationRenderResult Render(
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long frame = Interlocked.Increment(ref _frameCount);
        using OpenUsdSilkPage page = session.Sync(
            checked((int)colorTarget.Width),
            checked((int)colorTarget.Height),
            0,
            CameraState.Default);
        int meshUpserts = 0;
        using (SilkCommandEnumerator commands = page.GetEnumerator())
        {
            while (commands.MoveNext())
            {
                if (commands.Current.Type == SilkCommandType.MeshUpsert)
                {
                    meshUpserts++;
                }
            }
        }
        SilkMeshRenderResult result = renderer.ApplyAndRender(
            page,
            colorTarget,
            depthTarget,
            new SilkMeshRenderOptions(new SilkColor(0.02f, 0.03f, 0.05f, 1), 1));
        if (meshUpserts != 0)
        {
            Volatile.Write(ref _lastMeshUpsertFrame, frame);
        }
        return new SilkPresentationRenderResult(
            page.Revision,
            result.DrawCount,
            ContinueRendering: Volatile.Read(ref _continueRendering) != 0);
    }
}
