// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using OpenUsd.Geom;
using OpenUsd.Interop;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.NativeProbe;

internal static class SharedStageProbe
{
    internal static async Task RunAsync(
        string pluginPath,
        string directory)
    {
        string path = Path.Combine(directory, "shared-stage-authored.usda");
        File.Delete(path);

        await VerifyBorrowingReentrancyAndCancellationAsync(path).ConfigureAwait(false);
        await VerifyAbandonedSourceFinalizationAsync(path).ConfigureAwait(false);
        await VerifyIndependentLeaseAsync(path).ConfigureAwait(false);
        await VerifySilkSharedStageAsync(pluginPath, directory).ConfigureAwait(false);
        VerifyAccessRetainsStage(path);

        using UsdStage reopened = UsdStage.Open(path);
        if (reopened.GetPrim("/World/Shared").GetDouble("custom:value") != 7)
        {
            throw new InvalidOperationException(
                "The shared-stage scheduler edit did not survive teardown.");
        }
    }

    private static async Task VerifyBorrowingReentrancyAndCancellationAsync(string path)
    {
        await using var scheduler = UsdStageScheduler.Create(path);
        bool borrowedDisposeRejected = false;
        string identifier = await scheduler.InvokeAsync(stage =>
        {
            try
            {
                stage.Dispose();
            }
            catch (UsdStageOwnershipException exception)
                when (exception.Code == UsdStageOwnershipException.ErrorCode &&
                      exception.Message == UsdStageOwnershipException.ErrorMessage)
            {
                borrowedDisposeRejected = true;
            }

            UsdPrim prim = stage.DefinePrim("/World/Shared", "Xform");
            prim.SetDouble("custom:value", 7);
            stage.Save();
            return stage.RootLayerIdentifier;
        }).ConfigureAwait(false);
        if (!borrowedDisposeRejected || string.IsNullOrWhiteSpace(identifier))
        {
            throw new InvalidOperationException(
                "The scheduler callback stage did not enforce borrowed ownership.");
        }

        int reentrancyRejections = await scheduler.InvokeAsync(unusedStage =>
        {
            _ = unusedStage;
            int count = 0;
            count += ExpectReentrancy(() =>
            {
                ConsumeCompleted(scheduler.InvokeAsync(static _ => 1));
            });
            count += ExpectReentrancy(() =>
            {
                ConsumeCompleted(scheduler.EditAsync(
                    static _ => 1,
                    UsdStageInvalidationKind.Property));
            });
            count += ExpectReentrancy(() =>
            {
                ConsumeCompleted(scheduler.AcquireRenderSourceAsync());
            });
            count += ExpectReentrancy(() =>
            {
                ConsumeCompleted(scheduler.DisposeAsync());
            });
            return count;
        }).ConfigureAwait(false);
        if (reentrancyRejections != 4)
        {
            throw new InvalidOperationException(
                "One or more scheduler reentrancy operations were not rejected.");
        }

        using (var preCanceled = new CancellationTokenSource())
        {
            preCanceled.Cancel();
            bool callbackRan = false;
            Task operation = scheduler.InvokeAsync(
                _ => callbackRan = true,
                preCanceled.Token).AsTask();
            await ExpectCanceledAsync(operation).ConfigureAwait(false);
            if (callbackRan)
            {
                throw new InvalidOperationException(
                    "A pre-canceled scheduler work item entered native access.");
            }
        }

        using (var callbackCancellation = new CancellationTokenSource())
        {
            Task<int> operation = scheduler.InvokeAsync<int>(
                _ =>
                {
                    callbackCancellation.Cancel();
                    throw new OperationCanceledException(callbackCancellation.Token);
                },
                callbackCancellation.Token).AsTask();
            await ExpectCanceledAsync(operation).ConfigureAwait(false);
            if (!operation.IsCanceled)
            {
                throw new InvalidOperationException(
                    "A callback-thrown matching cancellation did not cancel its task.");
            }
        }

        await VerifyContentionCancellationAsync(scheduler).ConfigureAwait(false);

        bool runtimeResultRejected = false;
        try
        {
            _ = await scheduler.InvokeAsync<DetachedBase>(
                static _ => new StageBoundDetached()).ConfigureAwait(false);
        }
        catch (UsdStageBoundResultException)
        {
            runtimeResultRejected = true;
        }
        if (!runtimeResultRejected)
        {
            throw new InvalidOperationException(
                "Runtime result validation accepted a stage-bound derived result.");
        }

        _ = await scheduler.InvokeAsync(
            static stage => stage.ChangeSerial).ConfigureAwait(false);
    }

    private static async Task VerifyContentionCancellationAsync(UsdStageScheduler scheduler)
    {
        using UsdStageRenderSource source =
            await scheduler.AcquireRenderSourceAsync().ConfigureAwait(false);
        using UsdStageRenderLease lease = source.AcquireLease();
        using var accessAcquired = new ManualResetEventSlim();
        using var releaseAccess = new ManualResetEventSlim();
        Exception? accessFailure = null;
        var accessThread = new Thread(() =>
        {
            nint access = 0;
            try
            {
                access = OpenUsdNativeRuntime.BeginStageAccess(lease.Native);
                accessAcquired.Set();
                releaseAccess.Wait();
                OpenUsdNativeRuntime.EndStageAccess(access);
                access = 0;
            }
            catch (Exception exception)
            {
                accessFailure = exception;
            }
            finally
            {
                if (access != 0)
                {
                    Environment.FailFast(
                        "The contention probe could not release native stage access.",
                        accessFailure);
                }
                accessAcquired.Set();
            }
        })
        {
            IsBackground = true,
            Name = "OpenUsd contention probe"
        };
        accessThread.Start();
        accessAcquired.Wait();
        if (accessFailure is not null)
        {
            throw accessFailure;
        }

        using var cancellation = new CancellationTokenSource();
        bool callbackRan = false;
        Task operation = scheduler.InvokeAsync(
            _ => callbackRan = true,
            cancellation.Token).AsTask();
        await Task.Delay(100).ConfigureAwait(false);
        cancellation.Cancel();
        await Task.Delay(25).ConfigureAwait(false);
        if (operation.IsCompleted)
        {
            throw new InvalidOperationException(
                "Contention cancellation incorrectly interrupted native lock acquisition.");
        }

        releaseAccess.Set();
        accessThread.Join();
        if (accessFailure is not null)
        {
            throw accessFailure;
        }
        await ExpectCanceledAsync(operation).ConfigureAwait(false);
        if (callbackRan)
        {
            throw new InvalidOperationException(
                "A callback ran after cancellation during native lock contention.");
        }
    }

    private static async Task VerifyAbandonedSourceFinalizationAsync(string path)
    {
        var scheduler = UsdStageScheduler.Open(path);
        WeakReference sourceReference = AcquireAndAbandonSource(scheduler);
        for (int attempt = 0; attempt < 10 && sourceReference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        if (sourceReference.IsAlive)
        {
            throw new InvalidOperationException(
                "An abandoned render source was not finalized.");
        }

        await scheduler.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task VerifyIndependentLeaseAsync(string path)
    {
        var scheduler = UsdStageScheduler.Open(path);
        UsdStageRenderSource? source = null;
        UsdStageRenderLease? lease = null;
        try
        {
            source = await scheduler.AcquireRenderSourceAsync().ConfigureAwait(false);
            lease = source.AcquireLease();
            if (lease.DangerousGetHandle() == 0)
            {
                throw new InvalidOperationException(
                    "The independent render lease returned a null native handle.");
            }

            source.Dispose();
            source.Dispose();
            source = null;
            bool activeLeaseRejected = false;
            try
            {
                await scheduler.DisposeAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                activeLeaseRejected = true;
            }
            if (!activeLeaseRejected)
            {
                throw new InvalidOperationException(
                    "Scheduler disposal accepted an active render child lease.");
            }
            string identifier = lease.Native.WithAccess(
                () => lease.Native.RootLayerIdentifier);
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new InvalidOperationException(
                    "The independent render lease did not retain the stage.");
            }

            lease.Dispose();
            lease.Dispose();
            await scheduler.DisposeAsync().ConfigureAwait(false);
            bool disposedRejected = false;
            try
            {
                _ = lease.DangerousGetHandle();
            }
            catch (ObjectDisposedException)
            {
                disposedRejected = true;
            }
            if (!disposedRejected)
            {
                throw new InvalidOperationException(
                    "A disposed render lease still exposed its native pointer.");
            }
            lease = null;
        }
        finally
        {
            lease?.Dispose();
            source?.Dispose();
            await scheduler.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task VerifySilkSharedStageAsync(
        string pluginPath,
        string directory)
    {
        string path = Path.Combine(directory, "shared-stage-silk.usda");
        File.Delete(path);
        var scheduler = UsdStageScheduler.Create(path);
        UsdStageRenderSource? source = null;
        OpenUsdSilkSession? session = null;
        try
        {
            await scheduler.EditAsync(
                stage =>
                {
                    stage.SetEditTargetToSessionLayer();
                    var mesh = stage.DefineMesh("/World/UnsavedMesh");
                    mesh.SetPoints(
                    [
                        new(0, 0, 0),
                        new(1, 0, 0),
                        new(0, 1, 0)
                    ]);
                    mesh.SetTopology([3], [0, 1, 2]);
                },
                UsdStageInvalidationKind.Topology).ConfigureAwait(false);

            source = await scheduler.AcquireRenderSourceAsync().ConfigureAwait(false);
            session = OpenUsdSilkRuntime.Create(pluginPath, source);
            source.Dispose();
            source = null;
            var scene = new SilkSceneState();

            using (OpenUsdSilkPage page = session.Sync(
                64,
                64,
                camera: CameraState.Default))
            {
                scene.Apply(page);
                if (!ContainsMesh(scene, "/World/UnsavedMesh", expectedFirstX: 0))
                {
                    throw new InvalidOperationException(
                        "hdSilk did not observe the unsaved session-layer mesh.");
                }
            }

            await scheduler.EditAsync(
                stage =>
                {
                    stage.DefineMesh("/World/UnsavedMesh").SetPoints(
                    [
                        new(2, 0, 0),
                        new(1, 0, 0),
                        new(0, 1, 0)
                    ]);
                },
                UsdStageInvalidationKind.Property).ConfigureAwait(false);
            using (OpenUsdSilkPage page = session.Sync(
                64,
                64,
                camera: CameraState.Default))
            {
                scene.Apply(page);
                if (!ContainsMesh(scene, "/World/UnsavedMesh", expectedFirstX: 2))
                {
                    throw new InvalidOperationException(
                        "hdSilk did not observe a live point edit.");
                }
            }

            await scheduler.EditAsync(
                stage => stage.RemovePrim("/World/UnsavedMesh"),
                UsdStageInvalidationKind.Topology).ConfigureAwait(false);
            using (OpenUsdSilkPage page = session.Sync(
                64,
                64,
                camera: CameraState.Default))
            {
                SilkSceneDelta delta = scene.Apply(page);
                if (delta.MeshRemovals != 1 ||
                    scene.Meshes.Values.Any(mesh => mesh.Path == "/World/UnsavedMesh"))
                {
                    throw new InvalidOperationException(
                        "hdSilk did not observe a live prim removal.");
                }
            }

            bool activeSessionRejected = false;
            try
            {
                await scheduler.DisposeAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                activeSessionRejected = true;
            }
            if (!activeSessionRejected)
            {
                throw new InvalidOperationException(
                    "Scheduler disposal accepted an active hdSilk session.");
            }

            session.Dispose();
            session = null;

            source = await scheduler.AcquireRenderSourceAsync().ConfigureAwait(false);
            using (OpenUsdSilkSession concurrent =
                OpenUsdSilkRuntime.Create(pluginPath, source))
            {
                source.Dispose();
                source = null;
                OpenUsdSilkPage? concurrentPage = null;
                Exception? syncError = null;
                Task syncTask = Task.Run(
                    () =>
                    {
                        try
                        {
                            concurrentPage = concurrent.Sync(
                                32,
                                32,
                                camera: CameraState.Default);
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                        catch (Exception exception)
                        {
                            syncError = exception;
                        }
                    });
                Task disposeTask = Task.Run(concurrent.Dispose);
                await Task.WhenAll(syncTask, disposeTask).ConfigureAwait(false);
                concurrentPage?.Dispose();
                if (syncError is not null)
                {
                    throw new InvalidOperationException(
                        "Concurrent hdSilk Sync/Dispose was not serialized.",
                        syncError);
                }
            }

            source = await scheduler.AcquireRenderSourceAsync().ConfigureAwait(false);
            WeakReference abandonedSession =
                CreateAbandonedSilkSession(pluginPath, source);
            source.Dispose();
            source = null;
            for (int attempt = 0; attempt < 5 && abandonedSession.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            if (abandonedSession.IsAlive)
            {
                throw new InvalidOperationException(
                    "An abandoned hdSilk session remained rooted.");
            }

            await scheduler.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            session?.Dispose();
            source?.Dispose();
            await scheduler.DisposeAsync().ConfigureAwait(false);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAbandonedSilkSession(
        string pluginPath,
        UsdStageRenderSource source)
    {
        var session = OpenUsdSilkRuntime.Create(pluginPath, source);
        return new WeakReference(session);
    }

    private static bool ContainsMesh(
        SilkSceneState scene,
        string path,
        float expectedFirstX)
    {
        return scene.Meshes.Values.Any(
            mesh => mesh.Path == path &&
                mesh.Points.Length >= 1 &&
                mesh.Points.Span[0] == expectedFirstX);
    }

    private static void VerifyAccessRetainsStage(string path)
    {
        nint access = BeginAccessAndDropStage(path, out WeakReference stageReference);
        try
        {
            for (int attempt = 0; attempt < 3 && stageReference.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            if (stageReference.IsAlive)
            {
                throw new InvalidOperationException(
                    "The original stage safe handle was not finalized while access retained it.");
            }
        }
        finally
        {
            OpenUsdNativeRuntime.EndStageAccess(access);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AcquireAndAbandonSource(
        UsdStageScheduler scheduler)
    {
        UsdStageRenderSource source = scheduler
            .AcquireRenderSourceAsync()
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return new WeakReference(source);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static nint BeginAccessAndDropStage(string path, out WeakReference stageReference)
    {
        OpenUsdNativeStage stage = OpenUsdNativeRuntime.OpenStage(path);
        nint access = OpenUsdNativeRuntime.BeginStageAccess(stage);
        stageReference = new WeakReference(stage);
        return access;
    }

    private static int ExpectReentrancy(Action operation)
    {
        try
        {
            operation();
        }
        catch (UsdStageSchedulerReentrancyException exception)
            when (exception.Code == UsdStageSchedulerReentrancyException.ErrorCode &&
                  exception.Message == UsdStageSchedulerReentrancyException.ErrorMessage)
        {
            return 1;
        }

        return 0;
    }

    private static async Task ExpectCanceledAsync(Task operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        throw new InvalidOperationException("The scheduler operation was not canceled.");
    }

    private static void ConsumeCompleted(ValueTask operation)
    {
        if (!operation.IsCompleted)
        {
            throw new InvalidOperationException(
                "A reentrant scheduler operation returned an incomplete ValueTask.");
        }
        operation.GetAwaiter().GetResult();
    }

    private static void ConsumeCompleted<T>(ValueTask<T> operation)
    {
        if (!operation.IsCompleted)
        {
            throw new InvalidOperationException(
                "A reentrant scheduler operation returned an incomplete ValueTask.");
        }
        _ = operation.GetAwaiter().GetResult();
    }

    private class DetachedBase : IUsdDetachedResult;

    private sealed class StageBoundDetached : DetachedBase, IUsdStageBound;
}
