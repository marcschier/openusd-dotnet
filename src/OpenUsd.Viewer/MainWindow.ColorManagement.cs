// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer;

/// <summary>
/// The Viewer's colour-management control surface: one Render-menu toggle plus a config
/// chooser, and nothing on the toolbar.
/// </summary>
/// <remarks>
/// The authoritative value lives in <see cref="StageRenderState.RenderSettings"/> like
/// every other viewport display setting, so the display transform reaches live
/// presentation and ordinary captures through exactly the same path as lighting, shadows,
/// and background colour. The persisted <see cref="ViewerColorManagement"/> is only the
/// choice the Viewer restores at start-up, and it is restored into the very first state a
/// newly opened coordinator is initialized with, not merely into the menu.
/// </remarks>
public sealed partial class MainWindow
{
    private static readonly TimeSpan ColorManagementPollInterval =
        TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How many times an open rebuilds its colour-management choice before it gives up
    /// and opens untransformed.
    /// </summary>
    /// <remarks>
    /// Each rebuild is caused by a request that started across the previous one, so the
    /// loop terminates on its own in every realistic case. The bound is what keeps a
    /// pathological stream of requests -- a held-down menu accelerator, say -- from
    /// keeping a document open suspended indefinitely. Giving up opens with no display
    /// transform, which is the one choice that can never render a superseded enable.
    /// </remarks>
    private const int MaximumOpeningColorManagementAttempts = 4;

    private ViewerColorManagement _colorManagement = ViewerColorManagement.Default;
    private ViewerColorManagement? _pendingColorManagement;
    private ViewerDeferredColorManagement? _deferredColorManagement;
    private ViewerColorManagement? _openingColorManagement;
    private long _openingColorManagementGeneration;
    private long _openingColorManagementNewestGeneration;
    private string? _committedDisplayTransformKey;
    private ViewerColorManagementPoller? _colorManagementPoller;
    private ViewerColorManagementRequestPipeline? _colorManagementRequests;
    private string? _lastColorManagementStatus;

    private void InitializeColorManagementMenu()
    {
        RenderColorManagementEnabledMenuItem.Click += OnRenderColorManagementEnabledClick;
        RenderColorManagementChooseConfigMenuItem.Click +=
            OnRenderColorManagementChooseConfigClick;
        RenderColorManagementClearConfigMenuItem.Click +=
            OnRenderColorManagementClearConfigClick;
        _colorManagementRequests = new ViewerColorManagementRequestPipeline(
            static (transform, cancellationToken) => Task.Run(
                () => ViewerColorManagementValidation.Validate(transform),
                cancellationToken));
        _colorManagementPoller = new ViewerColorManagementPoller(
            ColorManagementPollInterval,
            async _ => await Dispatcher.UIThread.InvokeAsync(
                SynchronizeColorManagementFromBackendAsync,
                DispatcherPriority.Background));
        _colorManagementPoller.Start();
    }

    private void ApplyColorManagementSettings(ViewerColorManagement colorManagement)
    {
        ArgumentNullException.ThrowIfNull(colorManagement);
        _colorManagement = colorManagement;
        SyncColorManagementMenu();
    }

    /// <summary>
    /// Builds the render settings a newly opened stage starts with, so a restored
    /// colour-managed transform is in effect from the first presented frame instead of
    /// only after the user toggles something.
    /// </summary>
    /// <remarks>
    /// The restored transform is validated exactly the way an interactively chosen one
    /// is: it is baked before it is allowed into the opening state, so a config that was
    /// deleted, replaced, or made incompatible while the Viewer was closed disables the
    /// setting and reports why, instead of briefly claiming an active transform that the
    /// first frame then contradicts. The bake runs on a worker and is awaited, never
    /// waited on, so the dispatcher thread is not blocked on it.
    /// <para>
    /// A selection made while there was no coordinator, or while a document change was
    /// in flight, is replayed here rather than dropped -- but only if it is still the
    /// newest request. A deferred choice that a later request superseded is discarded,
    /// because replaying it would re-apply a decision the user has already changed.
    /// Nothing is committed here either: the opening choice is held aside and becomes the
    /// committed one only once the coordinator has actually opened, so an open that
    /// fails leaves the deferred request intact for the next attempt.
    /// </para>
    /// <para>
    /// The choice is generation-checked again after the bake and before the settings are
    /// handed to the coordinator, with no suspension point left in between. A View &gt;
    /// Reset Layout clear started across the bake takes a newer pipeline generation while
    /// committing nothing -- the document is busy, so its mutation is refused and merely
    /// deferred -- which leaves the committed model, the cached key, and the consumed
    /// deferral all still describing the world before the reset. The generation is the
    /// only trace of it, so a stale choice is dropped and the settings are rebuilt from
    /// the newest generation rather than opening a coordinator with a transform the reset
    /// has already declared gone.
    /// </para>
    /// </remarks>
    internal async Task<RenderSettings> BuildInitialRenderSettingsAsync()
    {
        for (int attempt = 0; attempt < MaximumOpeningColorManagementAttempts; attempt++)
        {
            RenderSettings settings = await SelectOpeningRenderSettingsAsync();
            if (!IsOpeningColorManagementSuperseded())
            {
                return settings;
            }

            DiscardOpeningColorManagement();
        }

        // Requests are still arriving faster than the choice can be rebuilt. Opening
        // untransformed can never render a superseded enable, and whatever is newest is
        // either still inside the pipeline or deferred, so the drain applies it once the
        // window reports ready.
        return RenderSettings.PresentationDefault;
    }

    /// <summary>
    /// Captures and validates one opening choice, without checking whether a newer
    /// request replaced it while it was being validated.
    /// </summary>
    private async Task<RenderSettings> SelectOpeningRenderSettingsAsync()
    {
        RenderSettings settings = RenderSettings.PresentationDefault;
        ViewerColorManagementRequestPipeline? requests = _colorManagementRequests;
        ViewerOpeningColorManagement opening =
            ViewerDeferredColorManagement.SelectOpeningChoice(
                _colorManagement,
                _deferredColorManagement,
                requests?.Version ?? 0,
                requests?.CommittedVersion ?? 0);
        if (opening.DiscardDeferred)
        {
            _deferredColorManagement = null;
        }

        ViewerColorManagement choice = opening.Choice;
        _openingColorManagementGeneration = opening.Generation;
        _openingColorManagementNewestGeneration = opening.NewestGeneration;
        _pendingColorManagement = null;

        if (!choice.TryResolve(
                out RenderDisplayTransform? transform,
                out string? diagnostic) ||
            transform is null)
        {
            if (diagnostic is not null)
            {
                ReportColorManagementStatus(diagnostic);
                choice = choice with { Enabled = false };
            }
            _openingColorManagement = choice;
            return settings;
        }

        RenderDisplayTransform restored = transform;
        string? failure = await Task.Run(
            () => ViewerColorManagementValidation.Validate(restored))
            .ConfigureAwait(true);
        if (failure is not null)
        {
            ReportColorManagementStatus(failure);
            _openingColorManagement = choice with { Enabled = false };
            return settings;
        }

        _openingColorManagement = choice;
        return ViewerViewportStateMutation.CopyRenderSettings(
            settings,
            outputTransform: RenderOutputTransform.Identity,
            displayTransform: restored);
    }

    /// <summary>
    /// Gets whether a request started after the opening choice was captured, so the
    /// captured choice may no longer be used, committed, or rendered.
    /// </summary>
    private bool IsOpeningColorManagementSuperseded()
    {
        ViewerColorManagementRequestPipeline? requests = _colorManagementRequests;
        return requests is not null &&
            ViewerOpeningColorManagement.IsSuperseded(
                _openingColorManagementNewestGeneration,
                requests.Version);
    }

    /// <summary>
    /// Drops a captured opening choice a newer request replaced, and re-reads the
    /// generations so the next capture is judged against the newest one.
    /// </summary>
    private void DiscardOpeningColorManagement()
    {
        ViewerColorManagementRequestPipeline? requests = _colorManagementRequests;
        _openingColorManagement = null;
        _openingColorManagementGeneration = requests?.CommittedVersion ?? 0;
        _openingColorManagementNewestGeneration = requests?.Version ?? 0;
    }

    /// <summary>
    /// Commits the opening colour-management choice once the coordinator has opened.
    /// </summary>
    /// <remarks>
    /// The committed key is read back from the coordinator's own published state rather
    /// than from the settings that were requested, so it describes the image rather than
    /// the intention. Only the generation the opening choice actually represents is
    /// marked, so a request whose validation is still in flight stays pending. Newer
    /// requests are not replayed here: the document is still busy at this point and a
    /// replay would be deferred straight back. They are drained once the window is ready.
    /// <para>
    /// The generation is checked once more first, because the coordinator's creation is
    /// itself a suspension point: a View &gt; Reset Layout clear can start across it, and
    /// it commits nothing while the document is busy, so nothing but the generation
    /// records that the captured choice has been replaced. A superseded choice commits
    /// nothing at all -- not the model, not the key, not the generation, and not the
    /// deferred clear the drain still has to replay.
    /// </para>
    /// </remarks>
    internal async Task ConfirmColorManagementOpenAsync(
        ViewerRenderCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        if (IsOpeningColorManagementSuperseded())
        {
            await AbandonOpeningColorManagementAsync(coordinator, cancellationToken);
            return;
        }

        if (_openingColorManagement is not { } opened)
        {
            return;
        }

        _openingColorManagement = null;
        _colorManagement = opened;
        _committedDisplayTransformKey =
            coordinator.CurrentState.RenderSettings.DisplayTransform?.CacheKey;
        _colorManagementRequests?.MarkCommitted(_openingColorManagementGeneration);
        if (_deferredColorManagement is { } deferred &&
            deferred.Generation <= _openingColorManagementGeneration)
        {
            _deferredColorManagement = null;
        }
        SyncColorManagementMenu();
    }

    /// <summary>
    /// Drops an opening choice a newer request replaced, and takes the transform it
    /// opened with back out of the authoritative state.
    /// </summary>
    /// <remarks>
    /// The coordinator may already have been created with the stale transform, because
    /// the request that superseded the choice can arrive while the coordinator is being
    /// created. The render loop has not started yet, so removing the transform here is
    /// what keeps it out of every frame; leaving it to the deferred clear would not,
    /// because the drain only runs once the window reports ready.
    /// <para>
    /// Nothing is committed either way. A clear the backend refuses leaves the previously
    /// committed model, key, and persisted profile exactly as they were -- the deferred
    /// clear is still recorded and the reconciliation poll loop is still armed, so the
    /// repair is retried rather than claimed.
    /// </para>
    /// </remarks>
    private async Task AbandonOpeningColorManagementAsync(
        ViewerRenderCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        _openingColorManagement = null;
        if (coordinator.CurrentState.RenderSettings.DisplayTransform is null)
        {
            return;
        }

        try
        {
            _ = await coordinator.TryMutateStateAsync(
                static state => ViewerViewportStateMutation.WithDisplayTransform(state, null),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ReportColorManagementStatus(
                "The display transform this document opened with could not be removed " +
                $"after the layout reset superseded it: {exception.Message}");
        }
    }

    /// <summary>
    /// Replays a colour-management request that outlived the open, once the window is no
    /// longer busy.
    /// </summary>
    /// <remarks>
    /// Draining before <c>_documentBusy</c> clears would defer the request straight back,
    /// because that is exactly the condition the mutation refuses under. Running it after
    /// the window reports ready is what makes the replay actually reach the coordinator.
    /// <para>
    /// The deferred request is replayed only if it is still the newest generation at the
    /// moment of the drain. Replaying an older one would start a fresh request that
    /// cancels the newer validation still running, and would then apply a decision the
    /// user has already replaced, so a superseded deferral is discarded outright without
    /// ever entering the pipeline.
    /// </para>
    /// </remarks>
    internal async Task DrainColorManagementRequestsAsync()
    {
        if (_documentBusy)
        {
            return;
        }

        ViewerDeferredColorManagement? deferred = _deferredColorManagement;
        if (deferred is not { } newer)
        {
            return;
        }

        _deferredColorManagement = null;
        long newest = _colorManagementRequests?.Version ?? 0;
        if (newer.Generation != newest)
        {
            return;
        }

        await ApplyColorManagementAsync(newer.Request);
    }

    /// <summary>Gets the pending, not yet committed, colour-management selection.</summary>
    internal ViewerColorManagement? PendingColorManagement => _pendingColorManagement;

    /// <summary>Gets the selection waiting for a coordinator to replay it onto.</summary>
    internal ViewerColorManagement? DeferredColorManagement =>
        _deferredColorManagement?.Request;

    /// <summary>Gets the cache key of the committed display transform, if any.</summary>
    internal string? CommittedDisplayTransformKey => _committedDisplayTransformKey;

    /// <summary>Gets the committed colour-management choice.</summary>
    internal ViewerColorManagement CommittedColorManagement => _colorManagement;

    /// <summary>Gets whether the newest request has not yet reached the state.</summary>
    internal bool HasPendingColorManagementRequest =>
        _colorManagementRequests?.HasPendingRequest ?? false;

    private void DisableColorManagement(string reason)
    {
        ReportColorManagementStatus(reason);
        _colorManagement = _colorManagement with { Enabled = false };
        SyncColorManagementMenu();
    }

    private void SyncColorManagementMenu()
    {
        RenderColorManagementEnabledMenuItem.IsChecked = _colorManagement.Enabled;
        RenderColorManagementClearConfigMenuItem.IsEnabled =
            _colorManagement.ConfigPath.Length != 0;
        string configPath = _colorManagement.ResolveConfigPath();
        RenderColorManagementEnabledMenuItem.IsEnabled = configPath.Length != 0;
        ToolTip.SetTip(
            RenderColorManagementEnabledMenuItem,
            configPath.Length == 0
                ? "Choose an OpenColorIO config, or set the OCIO environment variable to " +
                    "an absolute path, to enable the display transform."
                : $"OpenColorIO config: {configPath}");
        if (_colorManagementPoller is not null)
        {
            // The committed key, not only the toggle: a state that still carries a
            // transform the model has already disowned is precisely the disagreement the
            // reconciliation repairs, so disarming the loop there would leave the
            // viewport colour managed with nothing left watching it.
            _colorManagementPoller.IsEnabled =
                _colorManagement.Enabled || _committedDisplayTransformKey is not null;
        }
    }

    private async void OnRenderColorManagementEnabledClick(object? sender, RoutedEventArgs e)
    {
        bool enabled = RenderColorManagementEnabledMenuItem.IsChecked;
        await ApplyColorManagementAsync(_colorManagement with { Enabled = enabled });
    }

    private async void OnRenderColorManagementChooseConfigClick(
        object? sender,
        RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose OpenColorIO config",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("OpenColorIO config")
                    {
                        Patterns = ["*.ocio"],
                    },
                    FilePickerFileTypes.All,
                ],
            });
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                PlatformNotSupportedException)
        {
            ReportColorManagementStatus(
                $"The OpenColorIO config could not be chosen: {exception.Message}");
            return;
        }

        if (files.Count == 0)
        {
            return;
        }

        string? path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            ReportColorManagementStatus(
                "The chosen OpenColorIO config has no local path this Viewer can open.");
            return;
        }
        if (path.Length > RenderDisplayTransform.MaximumConfigPathLength)
        {
            ReportColorManagementStatus(
                "The chosen OpenColorIO config path is longer than the supported " +
                $"{RenderDisplayTransform.MaximumConfigPathLength} characters.");
            return;
        }

        await ApplyColorManagementAsync(_colorManagement with
        {
            ConfigPath = path,
            Enabled = true,
        });
    }

    private async void OnRenderColorManagementClearConfigClick(
        object? sender,
        RoutedEventArgs e)
    {
        await ApplyColorManagementAsync(_colorManagement with
        {
            ConfigPath = string.Empty,
            Enabled = false,
        });
    }

    private async Task ApplyColorManagementAsync(ViewerColorManagement colorManagement)
    {
        if (!colorManagement.IsValid())
        {
            ReportColorManagementStatus(
                "The OpenColorIO selection contains a value outside the supported range.");
            SyncColorManagementMenu();
            return;
        }

        ViewerColorManagementRequestPipeline? pipeline = _colorManagementRequests;
        if (pipeline is null)
        {
            // Nothing can carry the request yet, so it is remembered rather than lost.
            _deferredColorManagement = new ViewerDeferredColorManagement(colorManagement, 0);
            return;
        }

        // The selection is pending, not committed. Nothing about the committed view --
        // the menu, the persisted model, the committed key, the authoritative state --
        // moves until the coordinator has actually accepted the mutation, so a request
        // that cannot be applied leaves all four agreeing with each other.
        _pendingColorManagement = colorManagement;

        // Resolving only proves the names are well formed. Baking proves the config
        // exists, parses, and actually contains the requested colour space, display,
        // view, and look -- which is the difference between offering a transform and
        // claiming one. The bake is shared with the renderer through the same cache, so
        // it costs nothing the first frame would not have cost anyway. The pipeline
        // discards this result outright if the user changed the setting again while the
        // bake was running, so a slow config can never overwrite a newer choice.
        ViewerColorManagementOutcome? decided;
        try
        {
            decided = await pipeline.RunAsync(colorManagement);
        }
        catch (ObjectDisposedException)
        {
            pipeline.AbandonNewestRequest();
            _pendingColorManagement = null;
            return;
        }

        if (decided is not { } outcome)
        {
            // Superseded. The newer request owns the outcome; this one must not commit,
            // must not clear the pending marker, and must not move the generation.
            return;
        }

        RenderDisplayTransform? transform = outcome.Transform;

        if (!pipeline.IsCurrent(outcome.Version))
        {
            // A newer request -- a Reset Layout clear, for instance -- started between
            // the pipeline releasing this result and the mutation being attempted. It
            // must not reach the coordinator: applying it would colour manage an image
            // the newer request is about to declare clean, and it would do so out of the
            // order the user made the two decisions in.
            return;
        }

        ViewerStateMutationResult mutation = await TryApplyViewportStateAsync(
            state => ViewerViewportStateMutation.WithDisplayTransform(state, transform),
            outcome.Resolved
                ? $"Display transform: OpenColorIO ({colorManagement.ResolveConfigPath()})."
                : "Display transform: none.");

        if (!pipeline.IsCurrent(outcome.Version))
        {
            // The mutation is itself a suspension point, and a newer request can start
            // across it. Committing here would move the menu, the persisted choice, and
            // the cached key to a decision that has already been replaced -- and after a
            // reset, would resurrect the very transform the reset just reported gone. The
            // newer request owns all of it, including the pending marker and the deferral.
            return;
        }

        // The published state, not the requested one: a backend that threw or was
        // cancelled leaves the coordinator holding what it had before, and committing the
        // request would make the menu, the settings, and the key describe an image that
        // was never produced. The committed view is read *now*, after the await, never
        // from a snapshot taken before it: an open may have committed a newer choice
        // while this request was validating, and restoring the pre-await snapshot would
        // roll that newer commit back.
        ViewerColorManagementCommit decision = ViewerColorManagementCommit.Decide(
            _colorManagement,
            _committedDisplayTransformKey,
            mutation.PublishedState.RenderSettings.DisplayTransform,
            colorManagement,
            mutation.Applied
                ? mutation.PublishedState.RenderSettings.DisplayTransform
                : transform,
            outcome.Diagnostic,
            mutation.Applied);

        _pendingColorManagement = null;
        _colorManagement = decision.Committed;
        _committedDisplayTransformKey = decision.CommittedTransformKey;
        _deferredColorManagement = decision.Deferred is { } deferredRequest
            ? new ViewerDeferredColorManagement(deferredRequest, outcome.Version)
            : null;
        SyncColorManagementMenu();

        if (!mutation.Applied)
        {
            // No coordinator, a document change in flight, or a backend that refused.
            // The request is deferred and replayed into the settings the next document
            // opens with, never silently discarded, and nothing is committed meanwhile.
            return;
        }

        pipeline.MarkCommitted(outcome.Version);

        if (outcome.Diagnostic is not null)
        {
            // A requested-but-unusable transform is named here rather than quietly
            // leaving the image untransformed, and the toggle is turned back off so the
            // menu never claims a transform that is not running.
            ReportColorManagementStatus(outcome.Diagnostic);
        }

        await SaveSettingsAsync();
    }

    /// <summary>
    /// Reconciles the menu and the authoritative state with what the renderer actually
    /// did on its most recent frame, so a transform that was refused at run time -- a
    /// config deleted while the Viewer is open, for instance -- stops being claimed.
    /// </summary>
    /// <remarks>
    /// The reconciliation is correlated with the committed request, not merely with the
    /// enabled flag. Diagnostics are cumulative and observed asynchronously, so a slow
    /// failure reported for a transform that has since been replaced would otherwise
    /// disable a transform that is running correctly, and a request still being
    /// validated would be judged against the renderer's report on its predecessor.
    /// <para>
    /// Repair is committed the same way an interactive request is: the clear goes through
    /// the transactional mutation first, and the disabled model, the cached key, and the
    /// persisted settings move only once the coordinator has published a state without a
    /// transform. A busy window, a cancelled lifetime, or a backend that refuses leaves
    /// the prior commit exactly as it was and records a generation-tagged deferral, and
    /// the poll loop keeps running so the repair is attempted again.
    /// </para>
    /// </remarks>
    internal async Task SynchronizeColorManagementFromBackendAsync()
    {
        ViewerRenderCoordinator? coordinator = _coordinator;
        if (coordinator is null)
        {
            return;
        }

        SilkDisplayTransformDiagnostics? diagnostics = coordinator.DisplayTransformDiagnostics;
        ViewerColorManagementSyncResult result = ViewerColorManagementSync.Compute(
            _colorManagement.Enabled,
            _committedDisplayTransformKey,
            _colorManagementRequests?.HasPendingRequest ?? false,
            diagnostics?.Status,
            diagnostics?.RequestKey,
            coordinator.DisplayTransformDiagnostic);

        if (result.State is ViewerColorManagementState.Pending or
            ViewerColorManagementState.Active)
        {
            return;
        }

        if (!result.ClearTransform)
        {
            if (result.State == ViewerColorManagementState.Disabled)
            {
                return;
            }
            CommitReconciledStatus(result);
            await SaveSettingsAsync();
            return;
        }

        ViewerStateMutationResult mutation = await TryApplyViewportStateAsync(
            static state => ViewerViewportStateMutation.WithDisplayTransform(state, null),
            "Display transform: none.");
        if (!mutation.Applied ||
            mutation.PublishedState.RenderSettings.DisplayTransform is not null)
        {
            // The repair did not reach the image. Nothing is committed, the previous
            // commit stands, and the request is recorded so an open replays it. The poll
            // loop stays armed, so the next tick tries again.
            _deferredColorManagement = new ViewerDeferredColorManagement(
                _colorManagement with { Enabled = false },
                _colorManagementRequests?.Version ?? 0);
            return;
        }

        _committedDisplayTransformKey = null;
        CommitReconciledStatus(result);
        await SaveSettingsAsync();
    }

    private void CommitReconciledStatus(ViewerColorManagementSyncResult result)
    {
        _colorManagement = _colorManagement with { Enabled = result.Enabled };
        SyncColorManagementMenu();
        if (result.Status is not null &&
            !string.Equals(result.Status, _lastColorManagementStatus, StringComparison.Ordinal))
        {
            _lastColorManagementStatus = result.Status;
            ReportColorManagementStatus(result.Status);
        }
    }

    private void ReportColorManagementStatus(string status)
    {
        ViewerStatus.Text = status;
        ViewerStartupOptions.WriteStatus(status);
    }

    /// <summary>
    /// Cancels the colour-management poll loop and awaits the tick that may be in
    /// flight, so nothing can run against state that closing is about to tear down.
    /// </summary>
    internal async Task StopColorManagementPollingAsync()
    {
        ViewerColorManagementPoller? poller = Volatile.Read(ref _colorManagementPoller);
        if (poller is null)
        {
            return;
        }

        await poller.StopAsync();
        _ = Interlocked.Exchange(ref _colorManagementPoller, null);
        Interlocked.Exchange(ref _colorManagementRequests, null)?.Dispose();
    }

    /// <summary>
    /// Cancels the colour-management poll loop without blocking.
    /// </summary>
    /// <remarks>
    /// Disposal runs on the dispatcher thread the tick marshals back to, so waiting for
    /// the drain here would deadlock. Closing already drained it;
    /// this is the backstop for the paths that never went through closing.
    /// </remarks>
    internal void StopColorManagementPolling()
    {
        Interlocked.Exchange(ref _colorManagementPoller, null)?.Dispose();
        Interlocked.Exchange(ref _colorManagementRequests, null)?.Dispose();
    }

    /// <summary>Gets whether the colour-management poll loop is still retained.</summary>
    internal bool HasColorManagementTimer => _colorManagementPoller is not null;

    /// <summary>
    /// Reads the committed colour-management view against the coordinator's published
    /// state, so a decision is made about the image rather than about the intention.
    /// </summary>
    /// <remarks>
    /// The requests that have committed nothing yet are part of the view. A pending one
    /// is still inside its validation or its mutation and a deferred one is waiting for
    /// the next open, so neither shows up in the model, the cached key, or the state's
    /// transform -- and a reset that could not see them would leave them free to land
    /// afterwards.
    /// </remarks>
    private ViewerColorManagementView CurrentColorManagementView() =>
        new(
            _colorManagement,
            _committedDisplayTransformKey,
            _coordinator?.CurrentState.RenderSettings.DisplayTransform,
            _pendingColorManagement,
            _deferredColorManagement?.Request,
            _colorManagementRequests?.HasPendingRequest ?? false);

    /// <summary>
    /// Runs View &gt; Reset Layout, clearing an active OpenColorIO display transform
    /// through the transactional request pipeline before any default is committed.
    /// </summary>
    /// <remarks>
    /// A busy document, a cancelled lifetime, or a backend that refuses is carried by the
    /// existing deferral semantics: only the layout half of the profile is applied, the
    /// committed colour-management choice stands, the menu keeps claiming the transform
    /// that is still running, and the poll loop stays armed so the clear is retried.
    /// <para>
    /// The clear is requested whenever anything claims, carries, or could still produce a
    /// transform -- including a request whose bake is still running and one deferred for
    /// the next open. Requesting it takes a newer pipeline generation, which is what
    /// cancels and discards the older request and replaces the older deferral, so an
    /// enable made just before the reset can never commit or be replayed after the reset
    /// has reported success.
    /// </para>
    /// </remarks>
    internal async Task ResetLayoutAsync()
    {
        ViewerLayoutResetOutcome outcome = await ViewerLayoutReset.RunAsync(
            ViewerSettings.Default,
            CurrentColorManagementView,
            ApplyColorManagementAsync,
            ApplySettings);
        ViewerStatus.Text = outcome.Status;
        ViewerStartupOptions.WriteStatus(outcome.Status);
    }
}

/// <summary>
/// Holds the Viewer's shared lattice cache, so a transform the menu validated is the very
/// same baked lattice the renderer goes on to use.
/// </summary>
internal static class ViewerColorManagementValidation
{
    internal static SilkDisplayTransformLatticeCache Lattices { get; } = new();

    /// <summary>
    /// Bakes the transform, returning <see langword="null"/> on success or the bounded
    /// reason it cannot be honoured.
    /// </summary>
    internal static string? Validate(RenderDisplayTransform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        try
        {
            _ = Lattices.Get(transform);
            return null;
        }
        catch (SilkDisplayTransformException exception)
        {
            return exception.Message;
        }
    }
}
