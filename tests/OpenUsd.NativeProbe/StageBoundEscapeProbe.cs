// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;
using OpenUsd.Lux;
using OpenUsd.Shade;
using OpenUsd.Skel;

namespace OpenUsd.NativeProbe;

internal static class StageBoundEscapeProbe
{
    internal static async Task RunAsync(string directory)
    {
        string path = Path.Combine(directory, "stage-bound-escape.usda");
        File.Delete(path);
        await using var scheduler = UsdStageScheduler.Create(path);

        bool staticCallbackRan = false;
        await ExpectRejectedAsync(() => scheduler.InvokeAsync<UsdStage>(stage =>
        {
            staticCallbackRan = true;
            return stage;
        }).AsTask()).ConfigureAwait(false);
        if (staticCallbackRan)
        {
            throw new InvalidOperationException(
                "A statically forbidden callback was enqueued.");
        }

        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync(stage => stage.GetRootLayer()).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync(stage => stage.GetPrim("/World")).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync(
                stage => stage.GetPrim("/World").GetAttribute("custom:value")).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync(stage => stage.DefineXform("/World/Xform")).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync(stage => new[] { stage.GetPrim("/World") }).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync(
                stage => new List<UsdPrim> { stage.GetPrim("/World") }).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync(
                stage => (Name: "World", Prim: stage.GetPrim("/World"))).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync(
                stage => Task.FromResult(stage.GetPrim("/World"))).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync(
                stage => ValueTask.FromResult(stage.GetPrim("/World"))).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.EditAsync(
                stage => stage.GetPrim("/World"),
                UsdStageInvalidationKind.Topology).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync(
                stage => new StageBox(stage)).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync(
                stage => new PrimContainer(stage.GetPrim("/World"))).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync(
                static _ => LazyValues()).AsTask())
            .ConfigureAwait(false);

        await scheduler.InvokeAsync(stage =>
        {
            stage.DefinePrim("/World", "Xform");
        }).ConfigureAwait(false);

        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(static stage => stage).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(
                stage => stage.GetPrim("/World")).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(
                stage => stage.GetRootLayer()).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(
                stage => stage.GetPrim("/World").GetAttribute("custom:value")).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(
                stage => stage.GetPrim("/World").GetRelationship("targets")).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(
                stage => stage.DefineXform("/World/Geom")).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(
                stage => stage.DefineMaterial("/World/Material")).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(
                stage => stage.DefineSphereLight("/World/Light")).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(
                stage => stage.DefineSkeleton("/World/Skeleton")).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(
                stage => new object[] { stage.GetPrim("/World") }).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(
                stage => new List<object> { stage.GetPrim("/World") }).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<IReadOnlyList<object>>(
                stage => new List<object> { stage.GetPrim("/World") }).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(
                stage => (Value: (object)stage.GetPrim("/World"), Count: 1)).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(
                stage => Task.FromResult(stage.GetPrim("/World"))).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.EditAsync<object>(
                stage =>
                {
                    stage.DefinePrim("/World/Edited", "Xform");
                    return stage.GetPrim("/World/Edited");
                },
                UsdStageInvalidationKind.Topology).AsTask())
            .ConfigureAwait(false);
        _ = await scheduler.EditAsync(
            stage =>
            {
                stage.DefinePrim("/World/Edited", "Xform");
                return 1;
            },
            UsdStageInvalidationKind.Topology).ConfigureAwait(false);

        string identifier = await scheduler.InvokeAsync(
            static stage => stage.RootLayerIdentifier).ConfigureAwait(false);
        bool editSurvived = await scheduler.InvokeAsync(
            static stage => stage.HasPrim("/World/Edited")).ConfigureAwait(false);
        int detached = await scheduler.InvokeAsync(static _ => 42).ConfigureAwait(false);
        var record = await scheduler.InvokeAsync(
            static _ => new DetachedResult("allowed", 7)).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(identifier) ||
            !editSurvived ||
            detached != 42 ||
            record != new DetachedResult("allowed", 7))
        {
            throw new InvalidOperationException(
                "The scheduler did not remain usable after rejecting stage-bound results.");
        }
    }

    private static async Task ExpectRejectedAsync(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (UsdStageBoundResultException exception)
            when (exception.Code == UsdStageBoundResultException.ErrorCode &&
                  exception.Message == UsdStageBoundResultException.ErrorMessage)
        {
            return;
        }

        throw new InvalidOperationException(
            "A stage-bound scheduler result was not rejected with the stable contract error.");
    }

    private readonly record struct DetachedResult(
        string Name,
        int Count) : IUsdDetachedResult;

    private sealed record StageBox(UsdStage Stage);

    private readonly record struct PrimContainer(UsdPrim Prim);

    private static IEnumerable<int> LazyValues()
    {
        throw new InvalidOperationException("Lazy values must not be enumerated.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
