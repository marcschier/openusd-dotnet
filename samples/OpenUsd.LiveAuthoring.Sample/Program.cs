// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd;
using OpenUsd.LiveAuthoring;

string stagePath = Path.GetFullPath(
    args.Length > 0
        ? args[0]
        : Path.Combine(AppContext.BaseDirectory, "live-authoring.usda"));
File.Delete(stagePath);
string referenceAsset = Path.Combine(AppContext.BaseDirectory, "Assets", "reference.usda");
string payloadAsset = Path.Combine(AppContext.BaseDirectory, "Assets", "payload.usda");

var renderConsumer = new FakeRenderSourceConsumer();
UsdLiveAuthoringHost host = await UsdLiveAuthoringHost.CreateAsync(
    stagePath,
    LiveAuthoringStageMode.Create,
    renderConsumer,
    new UsdLiveAuthoringOptions
    {
        QueueCapacity = 2,
        EditLayer = LiveAuthoringEditLayer.Session
    });
UsdStageScheduler scheduler = host.Scheduler;
UsdStageRenderSource renderSource = host.RenderSource;
try
{
    LiveAuthoringBatchResult properties = await host.ApplyAsync(new LiveAuthoringBatch(
        1,
        [
            new DefinePrimUpdate("/World", "Xform"),
            new DefinePrimUpdate("/World/Sensor", "Xform"),
            new DefinePrimUpdate("/World/Target", "Xform"),
            new SetScalarUpdate(
                "/World/Sensor",
                "custom:temperature",
                LiveScalarValue.FromDouble(21.5)),
            new SetScalarUpdate(
                "/World/Sensor",
                "custom:temperature",
                LiveScalarValue.FromDouble(22.75),
                TimeCode: 1),
            new SetRelationshipTargetsUpdate(
                "/World/Sensor",
                "custom:target",
                ["/World/Target"])
        ]));

    LiveAuthoringBatchResult composition = await host.ApplyAsync(new LiveAuthoringBatch(
        2,
        [
            new DefinePrimUpdate("/World/Reference", "Xform"),
            new SetReferenceUpdate("/World/Reference", referenceAsset, "/ReferenceAsset"),
            new DefinePrimUpdate("/World/Payload", "Xform"),
            new SetPayloadUpdate("/World/Payload", payloadAsset, "/PayloadAsset"),
            new DefinePrimUpdate("/World/Instance", "Xform"),
            new SetReferenceUpdate("/World/Instance", referenceAsset, "/ReferenceAsset"),
            new SetInstanceableUpdate("/World/Instance", true),
            new DefinePrimUpdate("/World/Inactive", "Xform"),
            new SetActiveUpdate("/World/Inactive", false),
            new DefinePrimUpdate("/World/VariantHost", "Xform"),
            new SetVariantSelectionUpdate(
                "/World/VariantHost",
                "look",
                ["red", "blue"],
                "red")
        ]));

    LiveAuthoringBatchResult variantChange = await host.ApplyAsync(new LiveAuthoringBatch(
        3,
        [
            new SetVariantSelectionUpdate(
                "/World/VariantHost",
                "look",
                ["red", "blue"],
                "blue")
        ],
        coalescingKey: "variant-look"));

    bool stageBoundRejected;
    try
    {
        _ = await host.Scheduler.InvokeAsync(
            stage => stage.GetPrim("/World/Sensor"));
        stageBoundRejected = false;
    }
    catch (UsdStageBoundResultException)
    {
        stageBoundRejected = true;
    }

    string verification = await host.Scheduler.InvokeAsync(stage =>
    {
        UsdPrim sensor = stage.GetPrim("/World/Sensor");
        UsdPrim variant = stage.GetPrim("/World/VariantHost");
        bool sessionLayer = stage.EditTargetLayerIdentifier == stage.SessionLayerIdentifier;
        bool relationship = sensor.GetRelationshipTargets("custom:target")
            .SequenceEqual(["/World/Target"]);
        bool reference = stage.GetPrim("/World/Reference").GetDouble("custom:sourceValue") == 7.5;
        bool payload = stage.GetPrim("/World/Payload").GetString("custom:payloadValue") == "loaded";
        bool active = !stage.GetPrim("/World/Inactive").IsActive();
        bool instanceable = stage.GetPrim("/World/Instance").IsInstanceable();
        bool variants = variant.GetVariantNames("look").Order().SequenceEqual(["blue", "red"]) &&
            variant.GetVariantSelection("look") == "blue";
        bool scalar = sensor.GetDouble("custom:temperature") == 21.5 &&
            sensor.GetDouble("custom:temperature", 1) == 22.75;
        return string.Join(
            ", ",
            $"session={sessionLayer}",
            $"scalar={scalar}",
            $"relationship={relationship}",
            $"reference={reference}",
            $"payload={payload}",
            $"active={active}",
            $"instanceable={instanceable}",
            $"variants={variants}");
    });

    bool exactIdentity = renderConsumer.IsAttachedTo(host);
    if (!exactIdentity ||
        !stageBoundRejected ||
        verification.Contains("False", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("The live-authoring verification failed.");
    }

    Console.WriteLine($"render source identity retained: {exactIdentity}");
    Console.WriteLine($"stage-bound result rejected: {stageBoundRejected}");
    Console.WriteLine(
        $"property batch serials: {properties.BeforeChangeSerial}..{properties.AfterChangeSerial}");
    Console.WriteLine($"composition invalidation: {composition.Invalidation}");
    Console.WriteLine($"variant change sequence: {variantChange.LastSequence}");
    Console.WriteLine(verification);
}
finally
{
    await host.DisposeAsync();
}

bool sourceDisposed = throwsObjectDisposed(renderSource.AcquireLease);
bool schedulerDisposed = await throwsObjectDisposedAsync(
    () => scheduler.InvokeAsync(static _ => true).AsTask());
if (!renderConsumer.Disposed || !sourceDisposed || !schedulerDisposed)
{
    throw new InvalidOperationException("The live-authoring host did not dispose cleanly.");
}
Console.WriteLine("clean disposal: True");

static bool throwsObjectDisposed(Func<IDisposable> action)
{
    try
    {
        using IDisposable value = action();
        return false;
    }
    catch (ObjectDisposedException)
    {
        return true;
    }
}

static async Task<bool> throwsObjectDisposedAsync(Func<Task> action)
{
    try
    {
        await action().ConfigureAwait(false);
        return false;
    }
    catch (ObjectDisposedException)
    {
        return true;
    }
}

sealed class FakeRenderSourceConsumer : IUsdStageRenderSourceConsumer
{
    private UsdStageRenderLease? _lease;
    private UsdStageRenderSource? _source;
    private UsdStageScheduler? _scheduler;

    public bool Disposed { get; private set; }

    public ValueTask AttachAsync(
        UsdStageScheduler scheduler,
        UsdStageRenderSource renderSource,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _scheduler = scheduler;
        _source = renderSource;
        _lease = renderSource.AcquireLease();
        return ValueTask.CompletedTask;
    }

    public bool IsAttachedTo(UsdLiveAuthoringHost host) =>
        ReferenceEquals(_scheduler, host.Scheduler) &&
        ReferenceEquals(_source, host.RenderSource) &&
        _lease is not null;

    public ValueTask DisposeAsync()
    {
        _lease?.Dispose();
        _lease = null;
        _source = null;
        _scheduler = null;
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
