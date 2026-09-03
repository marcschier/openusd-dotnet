// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// The Viewer's colour-management commit path, assembled from the production types
/// the window itself uses: the real request pipeline, the real commit rule, the real
/// viewport state mutation, and the same poll-loop gate the window applies. Only the
/// coordinator's accept/refuse decision and the OpenColorIO bake are stubbed, because
/// neither can be driven deterministically from a test.
/// </summary>
/// <remarks>
/// Mutations are serialized exactly as the coordinator serializes them, so a request
/// caught inside its mutation genuinely delays the request that follows it instead of
/// interleaving in an order the coordinator would never produce.
/// </remarks>
internal sealed class ColorManagementShell : IDisposable
{
    private const int MaximumOpeningAttempts = 4;

    private readonly ViewerColorManagementRequestPipeline _pipeline;
    private readonly Func<RenderDisplayTransform, CancellationToken, Task<string?>> _validate;
    private readonly SemaphoreSlim _mutations = new(1, 1);
    private ViewerColorManagement? _opening;
    private long _openingGeneration;
    private long _openingNewestGeneration;

    internal ColorManagementShell(
        Func<RenderDisplayTransform, CancellationToken, Task<string?>>? validate = null)
    {
        _validate = validate ??
            ((transform, cancellationToken) => Task.FromResult<string?>(null));
        _pipeline = new ViewerColorManagementRequestPipeline(_validate);
    }

    /// <summary>
    /// Gets or sets work that runs while a request holds the mutation, which is how a
    /// request is held inside the one suspension point the pipeline cannot see.
    /// </summary>
    internal Func<ViewerColorManagement, Task>? DuringMutationAsync { get; set; }

    /// <summary>
    /// Gets or sets work that runs while a document open is suspended inside the bake of
    /// the transform it intends to open with.
    /// </summary>
    internal Func<RenderDisplayTransform, Task>? DuringOpeningValidationAsync { get; set; }

    /// <summary>
    /// Gets or sets work that runs while a document open is suspended creating the
    /// coordinator, before any state has been published.
    /// </summary>
    internal Func<Task>? DuringCoordinatorCreationAsync { get; set; }

    /// <summary>
    /// Gets or sets work that runs after the coordinator published the state it opened
    /// with and before the opening choice would be committed.
    /// </summary>
    internal Func<Task>? BeforeConfirmationAsync { get; set; }

    internal StageRenderState State { get; private set; } =
        StageRenderState.Default.WithRenderSettings(RenderSettings.PresentationDefault);

    internal ViewerColorManagement Committed { get; private set; } =
        ViewerColorManagement.Default;

    internal string? CommittedTransformKey { get; private set; }

    internal ViewerColorManagement? Pending { get; private set; }

    internal ViewerDeferredColorManagement? Deferred { get; private set; }

    internal ViewerSettings? Persisted { get; private set; }

    internal bool MenuChecked { get; private set; }

    internal bool PollingEnabled { get; private set; }

    internal bool RefuseMutations { get; set; }

    /// <summary>
    /// Gets whether a document change is in flight, which is what makes the window
    /// refuse every mutation and defer every request instead of committing it.
    /// </summary>
    internal bool DocumentBusy { get; private set; }

    /// <summary>
    /// Gets the display transform of every frame the simulated render loop presented,
    /// as <c>"none"</c> or the transform's cache key.
    /// </summary>
    internal List<string> RenderedTransforms { get; } = [];

    internal List<string> Log { get; } = [];

    internal long PipelineVersion => _pipeline.Version;

    /// <summary>Gets whether the newest generation has not reached the state.</summary>
    internal bool HasPendingRequest => _pipeline.HasPendingRequest;

    internal long SupersededResults => _pipeline.SupersededResults;

    internal ViewerColorManagementView ReadColorManagementView() =>
        new(
            Committed,
            CommittedTransformKey,
            State.RenderSettings.DisplayTransform,
            Pending,
            Deferred?.Request,
            _pipeline.HasPendingRequest);

    /// <summary>
    /// Mirrors <c>MainWindow.ApplyColorManagementAsync</c>: validate through the
    /// pipeline, attempt the transactional mutation, and commit only what the
    /// coordinator actually published -- and only while the request is still the
    /// newest one.
    /// </summary>
    internal async Task ApplyColorManagementAsync(ViewerColorManagement request)
    {
        Log.Add(request.Enabled ? "enable-requested" : "clear-requested");
        Pending = request;
        ViewerColorManagementOutcome? decided = await _pipeline.RunAsync(request);
        if (decided is not { } outcome)
        {
            return;
        }

        RenderDisplayTransform? transform = outcome.Transform;
        if (!_pipeline.IsCurrent(outcome.Version))
        {
            return;
        }

        bool applied = !RefuseMutations && !DocumentBusy;
        await _mutations.WaitAsync();
        try
        {
            if (DuringMutationAsync is { } during)
            {
                await during(request);
            }

            if (applied)
            {
                State = ViewerViewportStateMutation.WithDisplayTransform(State, transform);
                Log.Add(State.RenderSettings.DisplayTransform is null
                    ? "state-transform-cleared"
                    : "state-transform-applied");
            }
        }
        finally
        {
            _ = _mutations.Release();
        }

        if (!_pipeline.IsCurrent(outcome.Version))
        {
            return;
        }

        ViewerColorManagementCommit decision = ViewerColorManagementCommit.Decide(
            Committed,
            CommittedTransformKey,
            State.RenderSettings.DisplayTransform,
            request,
            applied ? State.RenderSettings.DisplayTransform : transform,
            outcome.Diagnostic,
            applied);

        Pending = null;
        Committed = decision.Committed;
        CommittedTransformKey = decision.CommittedTransformKey;
        Deferred = decision.Deferred is { } deferred
            ? new ViewerDeferredColorManagement(deferred, outcome.Version)
            : null;
        SyncMenu();
        if (!applied)
        {
            return;
        }

        _pipeline.MarkCommitted(outcome.Version);
        Persisted = ViewerSettings.Default with { ColorManagement = Committed };
    }

    /// <summary>Mirrors <c>MainWindow.ApplySettings</c>'s colour-management half.</summary>
    internal void ApplySettings(ViewerSettings settings)
    {
        Committed = settings.ColorManagement;
        SyncMenu();
        Log.Add(State.RenderSettings.DisplayTransform is null
            ? "settings-applied:transform=none"
            : "settings-applied:transform=active");
    }

    /// <summary>
    /// Mirrors <c>MainWindow.DrainColorManagementRequestsAsync</c>: the deferred
    /// request is replayed only while it is still the newest generation.
    /// </summary>
    internal async Task DrainAsync()
    {
        if (DocumentBusy)
        {
            return;
        }

        if (Deferred is not { } deferred)
        {
            return;
        }

        Deferred = null;
        if (deferred.Generation != _pipeline.Version)
        {
            return;
        }

        await ApplyColorManagementAsync(deferred.Request);
    }

    /// <summary>
    /// Reproduces the one disagreement the reconciliation exists for: the model has
    /// disowned the transform while the state still carries it.
    /// </summary>
    internal void DisownWithoutClearing()
    {
        Committed = Committed with { Enabled = false };
        SyncMenu();
    }

    /// <summary>
    /// Mirrors <c>MainWindow.OpenStageAsync</c>'s colour-management half: the previous
    /// document is stopped, the opening settings are built and generation-checked, the
    /// coordinator is created with them, the opening choice is confirmed, the render loop
    /// starts, and only then -- once the window reports ready -- is anything newer
    /// drained.
    /// </summary>
    /// <remarks>
    /// The three hooks sit at exactly the three suspension points a View &gt; Reset
    /// Layout can land in: inside the opening bake, inside the coordinator's creation,
    /// and immediately before the confirmation.
    /// </remarks>
    internal async Task OpenDocumentAsync()
    {
        DocumentBusy = true;
        try
        {
            // The previous document is gone, so nothing carries a transform and there is
            // no coordinator to read one from.
            State = StageRenderState.Default.WithRenderSettings(
                RenderSettings.PresentationDefault);
            Log.Add("document-stopped");

            RenderSettings openingSettings = await BuildInitialRenderSettingsAsync();

            if (DuringCoordinatorCreationAsync is { } creating)
            {
                DuringCoordinatorCreationAsync = null;
                await creating();
            }

            // The coordinator exists, initialized with exactly those settings.
            State = StageRenderState.Default.WithRenderSettings(openingSettings);
            Log.Add(State.RenderSettings.DisplayTransform is null
                ? "coordinator-opened:transform=none"
                : "coordinator-opened:transform=active");

            if (BeforeConfirmationAsync is { } before)
            {
                BeforeConfirmationAsync = null;
                await before();
            }

            await ConfirmColorManagementOpenAsync();
        }
        finally
        {
            DocumentBusy = false;
        }

        // The render loop only starts once the coordinator has opened and the opening
        // choice has been confirmed, so this is the first frame the document produces.
        RenderFrame();
        await DrainAsync();
        RenderFrame();
    }

    /// <summary>Records what the display would show for the published state.</summary>
    internal void RenderFrame() => RenderedTransforms.Add(
        State.RenderSettings.DisplayTransform?.CacheKey ?? "none");

    /// <summary>
    /// Mirrors <c>MainWindow.BuildInitialRenderSettingsAsync</c>: the choice is captured,
    /// baked, and generation-checked with no suspension point left before the coordinator
    /// is created with the result.
    /// </summary>
    internal async Task<RenderSettings> BuildInitialRenderSettingsAsync()
    {
        for (int attempt = 0; attempt < MaximumOpeningAttempts; attempt++)
        {
            RenderSettings settings = await SelectOpeningRenderSettingsAsync();
            if (!IsOpeningSuperseded())
            {
                return settings;
            }

            Log.Add("opening-choice-superseded");
            DiscardOpening();
        }

        return RenderSettings.PresentationDefault;
    }

    /// <summary>
    /// Mirrors <c>MainWindow.ConfirmColorManagementOpenAsync</c>: a choice a newer
    /// request replaced commits nothing and has its transform taken back out of the
    /// authoritative state before a single frame can carry it.
    /// </summary>
    internal async Task ConfirmColorManagementOpenAsync()
    {
        if (IsOpeningSuperseded())
        {
            Log.Add("opening-choice-abandoned");
            _opening = null;
            if (State.RenderSettings.DisplayTransform is null)
            {
                return;
            }

            // A direct coordinator mutation, not the window's viewport helper: the
            // document is still busy, and the helper refuses under exactly that
            // condition.
            if (RefuseMutations)
            {
                Log.Add("opening-transform-clear-refused");
                return;
            }

            State = ViewerViewportStateMutation.WithDisplayTransform(State, null);
            Log.Add("opening-transform-cleared");
            return;
        }

        if (_opening is not { } opened)
        {
            return;
        }

        _opening = null;
        Committed = opened;
        CommittedTransformKey = State.RenderSettings.DisplayTransform?.CacheKey;
        _pipeline.MarkCommitted(_openingGeneration);
        if (Deferred is { } deferred && deferred.Generation <= _openingGeneration)
        {
            Deferred = null;
        }
        SyncMenu();
        Log.Add("opening-choice-committed");
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        _mutations.Dispose();
    }

    private async Task<RenderSettings> SelectOpeningRenderSettingsAsync()
    {
        RenderSettings settings = RenderSettings.PresentationDefault;
        ViewerOpeningColorManagement opening =
            ViewerDeferredColorManagement.SelectOpeningChoice(
                Committed,
                Deferred,
                _pipeline.Version,
                _pipeline.CommittedVersion);
        if (opening.DiscardDeferred)
        {
            Deferred = null;
        }

        ViewerColorManagement choice = opening.Choice;
        _openingGeneration = opening.Generation;
        _openingNewestGeneration = opening.NewestGeneration;
        Pending = null;

        if (!choice.TryResolve(
                out RenderDisplayTransform? transform,
                out string? diagnostic) ||
            transform is null)
        {
            _opening = diagnostic is null ? choice : choice with { Enabled = false };
            return settings;
        }

        Log.Add("opening-bake-started");
        if (DuringOpeningValidationAsync is { } during)
        {
            DuringOpeningValidationAsync = null;
            await during(transform);
        }

        string? failure = await _validate(transform, CancellationToken.None);
        if (failure is not null)
        {
            _opening = choice with { Enabled = false };
            return settings;
        }

        _opening = choice;
        return ViewerViewportStateMutation.CopyRenderSettings(
            settings,
            outputTransform: RenderOutputTransform.Identity,
            displayTransform: transform);
    }

    private bool IsOpeningSuperseded() =>
        ViewerOpeningColorManagement.IsSuperseded(
            _openingNewestGeneration,
            _pipeline.Version);

    private void DiscardOpening()
    {
        _opening = null;
        _openingGeneration = _pipeline.CommittedVersion;
        _openingNewestGeneration = _pipeline.Version;
    }

    private void SyncMenu()
    {
        MenuChecked = Committed.Enabled;
        PollingEnabled = Committed.Enabled || CommittedTransformKey is not null;
    }
}
