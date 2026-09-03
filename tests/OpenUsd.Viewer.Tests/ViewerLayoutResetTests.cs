// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Drives the production View &gt; Reset Layout path -- <see cref="ViewerLayoutReset"/>
/// over a real <see cref="ViewerColorManagementRequestPipeline"/>, the real
/// <see cref="ViewerColorManagementCommit"/> rule, and the real viewport state mutation --
/// and asserts what the operator sees when the reset succeeds and when the clear cannot
/// reach the image.
/// </summary>
/// <remarks>
/// The defect these pin: Reset Layout applied the whole default profile in one
/// synchronous step, committing the default colour-management model, menu, cached key,
/// and persisted profile while the coordinator was still presenting an OpenColorIO
/// display transform. That unchecked the menu item and disarmed the reconciliation poll
/// loop -- the two things that would have noticed -- so the viewport stayed colour
/// managed with nothing left claiming it.
/// </remarks>
public sealed class ViewerLayoutResetTests
{
    private static readonly string ConfigPath = Path.Combine(
        AppContext.BaseDirectory, "layout-reset.ocio");

    private static ViewerColorManagement EnabledChoice => new()
    {
        Enabled = true,
        ConfigPath = ConfigPath,
        Display = "sRGB",
        View = "Film",
    };

    [Test]
    public async Task ResetClearsAnActiveTransformBeforeCommittingTheDefaults()
    {
        using var shell = new ColorManagementShell();
        await shell.ApplyColorManagementAsync(EnabledChoice);

        // Precondition: the transform is running and everything agrees it is.
        await Assert.That(shell.State.RenderSettings.DisplayTransform).IsNotNull();
        await Assert.That(shell.CommittedTransformKey).IsNotNull();
        await Assert.That(shell.MenuChecked).IsTrue();
        await Assert.That(shell.PollingEnabled).IsTrue();
        shell.Log.Clear();

        ViewerLayoutResetOutcome outcome = await ViewerLayoutReset.RunAsync(
            ViewerSettings.Default,
            shell.ReadColorManagementView,
            shell.ApplyColorManagementAsync,
            shell.ApplySettings);

        await Assert.That(outcome.ClearAttempted).IsTrue();
        await Assert.That(outcome.Cleared).IsTrue();
        await Assert.That(outcome.IsConsistent).IsTrue();

        // The image first, then the defaults: the settings are applied only after the
        // coordinator published a state without the transform.
        await Assert.That(string.Join("|", shell.Log)).IsEqualTo(
            "clear-requested|state-transform-cleared|settings-applied:transform=none");

        // All four views agree, and they agree on "no transform".
        await Assert.That(shell.State.RenderSettings.DisplayTransform).IsNull();
        await Assert.That(shell.CommittedTransformKey).IsNull();
        await Assert.That(shell.Committed).IsEqualTo(ViewerColorManagement.Default);
        await Assert.That(shell.MenuChecked).IsFalse();
        await Assert.That(shell.PollingEnabled).IsFalse();
        await Assert.That(shell.Deferred).IsNull();
        await Assert.That(outcome.Applied).IsEqualTo(ViewerSettings.Default);
        await Assert.That(shell.Persisted!.ColorManagement)
            .IsEqualTo(ViewerColorManagement.Default);
    }

    [Test]
    public async Task AFailedClearLeavesTheMenuModelKeyAndImageAgreeing()
    {
        using var shell = new ColorManagementShell();
        await shell.ApplyColorManagementAsync(EnabledChoice);
        string activeKey = shell.CommittedTransformKey!;
        RenderDisplayTransform activeTransform = shell.State.RenderSettings.DisplayTransform!;

        // The document goes busy, or the backend refuses: the mutation does not reach the
        // image, which is exactly the deferral case the pipeline already models.
        shell.RefuseMutations = true;
        shell.Log.Clear();

        ViewerLayoutResetOutcome outcome = await ViewerLayoutReset.RunAsync(
            ViewerSettings.Default,
            shell.ReadColorManagementView,
            shell.ApplyColorManagementAsync,
            shell.ApplySettings);

        await Assert.That(outcome.ClearAttempted).IsTrue();
        await Assert.That(outcome.Cleared).IsFalse();
        await Assert.That(outcome.IsConsistent).IsTrue();

        // The viewport is still transformed, so nothing may claim otherwise.
        await Assert.That(shell.State.RenderSettings.DisplayTransform)
            .IsEqualTo(activeTransform);
        await Assert.That(shell.CommittedTransformKey).IsEqualTo(activeKey);
        await Assert.That(shell.Committed.Enabled).IsTrue();
        await Assert.That(shell.Committed.ConfigPath).IsEqualTo(ConfigPath);
        await Assert.That(shell.MenuChecked).IsTrue();

        // And the loop that would repair it is still armed.
        await Assert.That(shell.PollingEnabled).IsTrue();

        // The request is recorded rather than discarded, so the next open replays it.
        await Assert.That(shell.Deferred).IsNotNull();
        await Assert.That(shell.Deferred!.Value.Request.Enabled).IsFalse();

        // The layout half of the reset still happened; only the colour-management half
        // was withheld, and the applied profile states the choice that is actually running.
        await Assert.That(outcome.Applied.WindowWidth)
            .IsEqualTo(ViewerSettings.Default.WindowWidth);
        await Assert.That(outcome.Applied.SelectedTabId)
            .IsEqualTo(ViewerSettings.Default.SelectedTabId);
        await Assert.That(outcome.Applied.DiagnosticsVisible).IsFalse();
        await Assert.That(outcome.Applied.ColorManagement).IsEqualTo(shell.Committed);
        await Assert.That(outcome.Applied.ColorManagement)
            .IsNotEqualTo(ViewerColorManagement.Default);
    }

    [Test]
    public async Task AFailedClearIsRetriedSuccessfullyOnceTheDocumentIsReady()
    {
        using var shell = new ColorManagementShell();
        await shell.ApplyColorManagementAsync(EnabledChoice);
        shell.RefuseMutations = true;
        _ = await ViewerLayoutReset.RunAsync(
            ViewerSettings.Default,
            shell.ReadColorManagementView,
            shell.ApplyColorManagementAsync,
            shell.ApplySettings);
        await Assert.That(shell.State.RenderSettings.DisplayTransform).IsNotNull();

        shell.RefuseMutations = false;
        ViewerLayoutResetOutcome retried = await ViewerLayoutReset.RunAsync(
            ViewerSettings.Default,
            shell.ReadColorManagementView,
            shell.ApplyColorManagementAsync,
            shell.ApplySettings);

        await Assert.That(retried.Cleared).IsTrue();
        await Assert.That(retried.IsConsistent).IsTrue();
        await Assert.That(shell.State.RenderSettings.DisplayTransform).IsNull();
        await Assert.That(shell.CommittedTransformKey).IsNull();
        await Assert.That(shell.MenuChecked).IsFalse();
        await Assert.That(shell.PollingEnabled).IsFalse();
    }

    [Test]
    public async Task AStateThatStillCarriesADisownedTransformIsStillCleared()
    {
        // The model already says colour management is off while the state carries a
        // transform. A reset that only consulted the model would conclude there was
        // nothing to clear and would leave the viewport colour managed for good.
        using var shell = new ColorManagementShell();
        await shell.ApplyColorManagementAsync(EnabledChoice);
        shell.DisownWithoutClearing();

        await Assert.That(shell.Committed.Enabled).IsFalse();
        await Assert.That(shell.State.RenderSettings.DisplayTransform).IsNotNull();
        await Assert.That(shell.ReadColorManagementView().HasActiveDisplayTransform)
            .IsTrue();
        await Assert.That(shell.PollingEnabled)
            .IsTrue()
            .Because("the loop that repairs the disagreement must stay armed");

        ViewerLayoutResetOutcome outcome = await ViewerLayoutReset.RunAsync(
            ViewerSettings.Default,
            shell.ReadColorManagementView,
            shell.ApplyColorManagementAsync,
            shell.ApplySettings);

        await Assert.That(outcome.ClearAttempted).IsTrue();
        await Assert.That(outcome.Cleared).IsTrue();
        await Assert.That(shell.State.RenderSettings.DisplayTransform).IsNull();
        await Assert.That(shell.PollingEnabled).IsFalse();
    }

    [Test]
    public async Task AResetWithNothingActiveNeverEntersThePipeline()
    {
        using var shell = new ColorManagementShell();

        ViewerLayoutResetOutcome outcome = await ViewerLayoutReset.RunAsync(
            ViewerSettings.Default,
            shell.ReadColorManagementView,
            shell.ApplyColorManagementAsync,
            shell.ApplySettings);

        await Assert.That(outcome.ClearAttempted).IsFalse();
        await Assert.That(outcome.Cleared).IsTrue();
        await Assert.That(outcome.Applied).IsEqualTo(ViewerSettings.Default);
        await Assert.That(shell.PipelineVersion).IsEqualTo(0L);
        await Assert.That(string.Join("|", shell.Log))
            .IsEqualTo("settings-applied:transform=none");
    }

    [Test]
    public async Task ASlowEnableCannotCommitAfterAResetReportsSuccess()
    {
        // The bake is still running, so nothing is committed, no key is cached, and the
        // state carries no transform: a reset that judged only those three would decide
        // there was nothing to clear, skip the pipeline, and be contradicted the moment
        // the bake landed.
        var bake = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var baking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var shell = new ColorManagementShell((transform, cancellationToken) =>
        {
            _ = cancellationToken.Register(() => cancelled.TrySetResult());
            baking.TrySetResult();
            return bake.Task;
        });

        Task enable = shell.ApplyColorManagementAsync(EnabledChoice);
        await baking.Task;

        ViewerColorManagementView before = shell.ReadColorManagementView();
        await Assert.That(before.HasActiveDisplayTransform).IsFalse();
        await Assert.That(before.HasOutstandingRequest).IsTrue();
        await Assert.That(before.RequiresClear).IsTrue();
        await Assert.That(before.IsCleared).IsFalse();

        ViewerLayoutResetOutcome outcome = await ViewerLayoutReset.RunAsync(
            ViewerSettings.Default,
            shell.ReadColorManagementView,
            shell.ApplyColorManagementAsync,
            shell.ApplySettings);

        await Assert.That(outcome.ClearAttempted)
            .IsTrue()
            .Because("the pending enable can only be superseded from inside the pipeline");
        await Assert.That(outcome.Cleared).IsTrue();
        await Assert.That(outcome.IsConsistent).IsTrue();
        await Assert.That(outcome.Applied).IsEqualTo(ViewerSettings.Default);

        // The reset cancelled the bake as well as superseding it, so a config that is
        // still being read is told to stop rather than merely being ignored later.
        await cancelled.Task;

        // The bake finally succeeds, after the reset already reported success. Its result
        // must be discarded outright.
        bake.SetResult(null);
        await enable;

        await Assert.That(shell.SupersededResults).IsGreaterThanOrEqualTo(1L);
        await Assert.That(shell.State.RenderSettings.DisplayTransform).IsNull();
        await Assert.That(shell.CommittedTransformKey).IsNull();
        await Assert.That(shell.Committed).IsEqualTo(ViewerColorManagement.Default);
        await Assert.That(shell.MenuChecked).IsFalse();
        await Assert.That(shell.PollingEnabled).IsFalse();
        await Assert.That(shell.Pending).IsNull();
        await Assert.That(shell.Deferred).IsNull();
        await Assert.That(string.Join("|", shell.Log)).IsEqualTo(
            "enable-requested|clear-requested|state-transform-cleared|" +
            "settings-applied:transform=none");
    }

    [Test]
    public async Task AnEnableCaughtInsideItsMutationCannotCommitAfterAReset()
    {
        // The one suspension point the pipeline cannot see through: the request has left
        // validation and is inside the transactional mutation, so it is past every check
        // the pipeline makes on its own.
        var mutating = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var shell = new ColorManagementShell();
        shell.DuringMutationAsync = async request =>
        {
            if (!request.Enabled)
            {
                return;
            }

            mutating.TrySetResult();
            await release.Task;
        };

        Task enable = shell.ApplyColorManagementAsync(EnabledChoice);
        await mutating.Task;
        await Assert.That(shell.ReadColorManagementView().RequiresClear).IsTrue();

        Task<ViewerLayoutResetOutcome> reset = ViewerLayoutReset.RunAsync(
            ViewerSettings.Default,
            shell.ReadColorManagementView,
            shell.ApplyColorManagementAsync,
            shell.ApplySettings);

        // The enable's mutation lands first, exactly as the coordinator would order them,
        // and the reset's clear follows it.
        release.SetResult();
        await enable;
        ViewerLayoutResetOutcome outcome = await reset;

        await Assert.That(outcome.ClearAttempted).IsTrue();
        await Assert.That(outcome.Cleared).IsTrue();
        await Assert.That(outcome.IsConsistent).IsTrue();

        // The stale mutation really did reach the state -- and still committed nothing.
        await Assert.That(string.Join("|", shell.Log)).IsEqualTo(
            "enable-requested|clear-requested|state-transform-applied|" +
            "state-transform-cleared|settings-applied:transform=none");
        await Assert.That(shell.State.RenderSettings.DisplayTransform).IsNull();
        await Assert.That(shell.CommittedTransformKey).IsNull();
        await Assert.That(shell.Committed).IsEqualTo(ViewerColorManagement.Default);
        await Assert.That(shell.MenuChecked).IsFalse();
        await Assert.That(shell.PollingEnabled).IsFalse();
        await Assert.That(shell.Pending).IsNull();
        await Assert.That(shell.Deferred).IsNull();
    }

    [Test]
    public async Task AResetReplacesADeferredEnableWithADefaultClear()
    {
        // No coordinator, or a document change in flight: the enable decided but could
        // not reach the state, so it waits to be replayed by the next open. Nothing is
        // committed and the viewport carries nothing, yet the enable is still live.
        using var shell = new ColorManagementShell { RefuseMutations = true };
        await shell.ApplyColorManagementAsync(EnabledChoice);
        await Assert.That(shell.Deferred).IsNotNull();
        await Assert.That(shell.Deferred!.Value.Request.Enabled).IsTrue();
        long enableGeneration = shell.Deferred.Value.Generation;

        ViewerColorManagementView before = shell.ReadColorManagementView();
        await Assert.That(before.HasActiveDisplayTransform).IsFalse();
        await Assert.That(before.RequiresClear).IsTrue();
        shell.Log.Clear();

        ViewerLayoutResetOutcome outcome = await ViewerLayoutReset.RunAsync(
            ViewerSettings.Default,
            shell.ReadColorManagementView,
            shell.ApplyColorManagementAsync,
            shell.ApplySettings);

        await Assert.That(outcome.ClearAttempted).IsTrue();

        // The deferral is replaced, not merely added to: what the next open replays is
        // the reset's own default clear, at a newer generation.
        await Assert.That(shell.Deferred).IsNotNull();
        await Assert.That(shell.Deferred!.Value.Request).IsEqualTo(ViewerColorManagement.Default);
        await Assert.That(shell.Deferred.Value.Generation).IsGreaterThan(enableGeneration);

        // The image carries nothing and the only thing waiting is a clear, so the reset
        // reports the clean defaults it actually restored.
        await Assert.That(outcome.Cleared).IsTrue();
        await Assert.That(outcome.IsConsistent).IsTrue();
        await Assert.That(outcome.Applied).IsEqualTo(ViewerSettings.Default);

        // The open that replays it opens with the clear, never with the superseded enable.
        ViewerOpeningColorManagement opening =
            ViewerDeferredColorManagement.SelectOpeningChoice(
                shell.Committed,
                shell.Deferred,
                shell.PipelineVersion,
                committedGeneration: enableGeneration);
        await Assert.That(opening.Choice.Enabled).IsFalse();
        await Assert.That(opening.DiscardDeferred).IsFalse();

        // And the replay itself leaves the viewport untransformed.
        shell.RefuseMutations = false;
        await shell.DrainAsync();
        await Assert.That(shell.State.RenderSettings.DisplayTransform).IsNull();
        await Assert.That(shell.Committed).IsEqualTo(ViewerColorManagement.Default);
        await Assert.That(shell.MenuChecked).IsFalse();
        await Assert.That(shell.PollingEnabled).IsFalse();
        await Assert.That(shell.Deferred).IsNull();
        await Assert.That(shell.ReadColorManagementView().IsCleared).IsTrue();
    }

    [Test]
    public async Task TheResetSupersedesPendingAndDeferredRequestsInProduction()
    {
        string root = FindRepositoryRoot();
        string colorManagement = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "MainWindow.ColorManagement.cs"));
        string layoutReset = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "ViewerLayoutReset.cs"));

        // The window's view carries the requests that have committed nothing yet, so the
        // reset can see the very thing the committed views cannot show it.
        int view = colorManagement.IndexOf(
            "private ViewerColorManagementView CurrentColorManagementView()",
            StringComparison.Ordinal);
        await Assert.That(view).IsGreaterThan(0);
        string viewBody = colorManagement[view..(view + 500)];
        await Assert.That(viewBody).Contains("_pendingColorManagement,");
        await Assert.That(viewBody).Contains("_deferredColorManagement?.Request,");
        await Assert.That(viewBody).Contains("_colorManagementRequests?.HasPendingRequest ?? false");

        // And the reset enters the pipeline for them, not only for a committed transform.
        await Assert.That(layoutReset).Contains("bool attempted = before.RequiresClear;");
        await Assert.That(layoutReset).DoesNotContain(
            "bool attempted = before.HasActiveDisplayTransform;");

        // A request that is no longer the newest neither reaches the coordinator nor
        // commits, on either side of the mutation it may be suspended in.
        int apply = colorManagement.IndexOf(
            "private async Task ApplyColorManagementAsync(",
            StringComparison.Ordinal);
        await Assert.That(apply).IsGreaterThan(0);
        string applyBody = colorManagement[apply..];
        int end = applyBody.IndexOf(
            "internal async Task SynchronizeColorManagementFromBackendAsync",
            StringComparison.Ordinal);
        await Assert.That(end).IsGreaterThan(0);
        applyBody = applyBody[..end];

        int mutation = applyBody.IndexOf(
            "await TryApplyViewportStateAsync(",
            StringComparison.Ordinal);
        await Assert.That(mutation).IsGreaterThan(0);
        string guard = "if (!pipeline.IsCurrent(outcome.Version))";
        await Assert.That(applyBody[..mutation]).Contains(guard);
        await Assert.That(applyBody[mutation..]).Contains(guard);
        int commit = applyBody.IndexOf(
            "ViewerColorManagementCommit.Decide(",
            StringComparison.Ordinal);
        await Assert.That(commit).IsGreaterThan(
            applyBody.LastIndexOf(guard, StringComparison.Ordinal));
    }

    [Test]
    public async Task TheResetMenuHandlerRunsTheTransactionalReset()
    {
        string root = FindRepositoryRoot();
        string menus = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "MainWindow.Menus.cs"));
        string colorManagement = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "MainWindow.ColorManagement.cs"));

        int handler = menus.IndexOf(
            "private async void OnResetLayoutClick(",
            StringComparison.Ordinal);
        await Assert.That(handler).IsGreaterThan(0);
        string handlerBody = menus[handler..(handler + 400)];
        await Assert.That(handlerBody).Contains("await ResetLayoutAsync();");

        // The reset must not bypass the pipeline by applying the default profile itself.
        await Assert.That(menus).DoesNotContain("ApplySettings(ViewerSettings.Default);");

        int reset = colorManagement.IndexOf(
            "internal async Task ResetLayoutAsync()",
            StringComparison.Ordinal);
        await Assert.That(reset).IsGreaterThan(0);
        string resetBody = colorManagement[reset..];
        int end = resetBody.IndexOf("\n    }", StringComparison.Ordinal);
        await Assert.That(end).IsGreaterThan(0);
        resetBody = resetBody[..end];

        // The same request/mutation pipeline the menu uses, and the settings applied
        // through the one shared applier.
        await Assert.That(resetBody).Contains("ViewerLayoutReset.RunAsync(");
        await Assert.That(resetBody).Contains("CurrentColorManagementView");
        await Assert.That(resetBody).Contains("ApplyColorManagementAsync");
        await Assert.That(resetBody).Contains("ApplySettings");
    }

    [Test]
    public async Task ThePollLoopStaysArmedWhileTheStateCarriesATransform()
    {
        string root = FindRepositoryRoot();
        string source = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "MainWindow.ColorManagement.cs"));

        int index = source.IndexOf(
            "private void SyncColorManagementMenu()",
            StringComparison.Ordinal);
        await Assert.That(index).IsGreaterThan(0);
        string body = source[index..];
        int end = body.IndexOf("\n    }", StringComparison.Ordinal);
        body = body[..end];

        await Assert.That(body).Contains(
            "_colorManagement.Enabled || _committedDisplayTransformKey is not null;");
        await Assert.That(body)
            .DoesNotContain("IsEnabled = _colorManagement.Enabled;");
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
}
