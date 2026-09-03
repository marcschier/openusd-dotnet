// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.ExceptionServices;

namespace OpenUsd.LiveAuthoring;

/// <summary>
/// Owns one scheduler, one retained render source, and one bounded live-authoring adapter.
/// </summary>
public sealed class UsdLiveAuthoringHost : ILiveAuthoringSink, IAsyncDisposable
{
    private readonly IUsdStageRenderSourceConsumer _consumer;
    private readonly QueuedLiveAuthoringSink _sink;
    private int _disposeState;

    private UsdLiveAuthoringHost(
        UsdStageScheduler scheduler,
        UsdStageRenderSource renderSource,
        IUsdStageRenderSourceConsumer consumer,
        QueuedLiveAuthoringSink sink)
    {
        Scheduler = scheduler;
        RenderSource = renderSource;
        _consumer = consumer;
        _sink = sink;
    }

    /// <summary>Gets the sole stage scheduler used for authoring and render synchronization.</summary>
    public UsdStageScheduler Scheduler { get; }

    /// <summary>Gets the sole retained render source handed to the consumer.</summary>
    public UsdStageRenderSource RenderSource { get; }

    /// <summary>Gets the bounded live-authoring queue.</summary>
    public QueuedLiveAuthoringSink Sink => _sink;

    /// <summary>Creates and attaches a live-authoring host.</summary>
    public static async ValueTask<UsdLiveAuthoringHost> CreateAsync(
        string stagePath,
        LiveAuthoringStageMode stageMode,
        IUsdStageRenderSourceConsumer consumer,
        UsdLiveAuthoringOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagePath);
        ArgumentNullException.ThrowIfNull(consumer);
        options ??= new UsdLiveAuthoringOptions();
        Validate(options);

        UsdStageScheduler scheduler = stageMode switch
        {
            LiveAuthoringStageMode.Create => UsdStageScheduler.Create(
                stagePath,
                options.SchedulerCapacity,
                options.NotificationCapacity),
            LiveAuthoringStageMode.Open => UsdStageScheduler.Open(
                stagePath,
                options.SchedulerCapacity,
                options.NotificationCapacity),
            _ => throw new ArgumentOutOfRangeException(nameof(stageMode))
        };
        UsdStageRenderSource? renderSource = null;
        QueuedLiveAuthoringSink? sink = null;
        try
        {
            await scheduler.EditAsync(
                stage =>
                {
                    if (options.EditLayer == LiveAuthoringEditLayer.Session)
                    {
                        stage.SetEditTargetToSessionLayer();
                    }
                    else
                    {
                        stage.SetEditTargetToRootLayer();
                    }
                },
                UsdStageInvalidationKind.Composition,
                cancellationToken).ConfigureAwait(false);
            renderSource = await scheduler.AcquireRenderSourceAsync(cancellationToken)
                .ConfigureAwait(false);
            await consumer.AttachAsync(scheduler, renderSource, cancellationToken)
                .ConfigureAwait(false);
            sink = new QueuedLiveAuthoringSink(
                new UsdStageBatchExecutor(scheduler, options.EditLayer),
                options.QueueCapacity,
                options.HealthObserver);
            return new UsdLiveAuthoringHost(scheduler, renderSource, consumer, sink);
        }
        catch
        {
            if (sink is not null)
            {
                await sink.DisposeAsync().ConfigureAwait(false);
            }
            await consumer.DisposeAsync().ConfigureAwait(false);
            renderSource?.Dispose();
            await scheduler.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc/>
    public ValueTask<LiveAuthoringAdmissionReceipt> ApplyAsync(
        LiveAuthoringBatch batch,
        CancellationToken cancellationToken = default) =>
        _sink.ApplyAsync(batch, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        Exception? failure = null;
        try
        {
            await _sink.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            await _consumer.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (failure is not null)
        {
            failure = new AggregateException(failure, exception);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            RenderSource.Dispose();
        }
        catch (Exception exception) when (failure is not null)
        {
            failure = new AggregateException(failure, exception);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            await Scheduler.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (failure is not null)
        {
            failure = new AggregateException(failure, exception);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void Validate(UsdLiveAuthoringOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.QueueCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.SchedulerCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.NotificationCapacity);
        if ((uint)options.EditLayer > (uint)LiveAuthoringEditLayer.Root)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }
}
