// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

/// <summary>
/// Owns one document's interactive physics simulation: its transport, its pacing, and the bounded
/// per-frame bridge into the active render backend.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing simulates on the UI or render thread.</b> Every lifecycle operation is a request the
/// dedicated physics worker executes; the controller only awaits completions, reads a lock-free
/// status, and consumes the latest complete published frame. The render loop's only physics work is
/// one bounded override batch per rendered frame.
/// </para>
/// <para>
/// <b>Playback is paced by the viewer, not by the transport.</b> The transport stays parked and the
/// controller converts wall-clock time and the playback speed into whole fixed steps. A transport
/// that also drove itself would double-step, and a speed applied inside the transport would have to
/// change the fixed step - which would change the simulation the user is watching. Speed therefore
/// only changes how often the world is asked to advance.
/// </para>
/// <para>
/// <b>Commands are single-flight.</b> One command owns the transport at a time, so a close arriving
/// during a build, or a scrub arriving during a replay, cancels and drains deterministically
/// instead of racing a half-built world.
/// </para>
/// </remarks>
internal sealed class ViewerPhysicsController : IAsyncDisposable
{
    /// <summary>How many controller-authored edits are remembered while they are observed.</summary>
    /// <remarks>
    /// One preview authors one change per chunk, so the bound has to hold a whole multi-chunk
    /// apply plus the clear that may follow it. It stays bounded because dropping the oldest
    /// remembered change only costs one spurious rebuild, never a lost external edit.
    /// </remarks>
    private const int MaxPendingSelfEdits = 256;

    private readonly IViewerPhysicsTransportFactory _factory;
    private readonly IViewerPhysicsClock _clock;
    private readonly IViewerPhysicsAuthoringStage? _authoring;
    private readonly ViewerPhysicsRenderBridge _bridge;
    private readonly ViewerPhysicsEditDebouncer _debouncer;
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Lock _selfEditLock = new();
    private readonly Queue<ViewerPhysicsStageEdit> _selfEdits = new();
    private readonly Queue<ClassifiedEdit> _authoredEdits = new();
    private readonly int _maxStepsPerPump;

    private IViewerPhysicsTransport? _transport;
    private ViewerPhysicsPacer? _pacer;
    private CancellationTokenSource? _seekLifetime;
    private ViewerPhysicsBindingSet _bindingSet = ViewerPhysicsBindingSet.Empty;
    private IReadOnlyList<ViewerPhysicsObjectRow> _objectRows = [];
    private IReadOnlyList<ViewerPhysicsObjectSection> _inspectorSections = [];
    private ulong _inspectorRevision;
    private long _stagedCommands;
    private long _refusedCommands;
    private IReadOnlyList<ViewerPhysicsCapabilityRow> _capabilityRows = [];
    private IReadOnlyList<ViewerPhysicsDiagnosticRow> _diagnosticRows = [];
    private IReadOnlyList<ViewerPhysicsCapabilitySupport>? _capabilitySupport;
    private CapabilityKey _capabilityKey = CapabilityKey.None;
    private ulong _bindingRevision;
    private int _boundIdentities;
    private int _refusedBindings;
    private double _speed = 1d;
    private double _lastPumpSeconds;
    private bool _isPlaying;
    private bool _loop;
    private bool _previewEnabled;
    private bool _previewClearPending;
    private string _error = string.Empty;
    private string _bridgeError = string.Empty;
    private int _bridgeDisabled;
    private int _busy;
    private int _gateBusy;
    private int _pumping;
    private int _clearPending;
    private int _replayPending;
    private int _disposed;

    /// <summary>Initializes a controller that has not created a transport yet.</summary>
    /// <param name="factory">Creates the transport when physics is first requested.</param>
    /// <param name="clock">Reads monotonic time for pacing and edit debouncing.</param>
    /// <param name="capacities">The bounded render storage the bridge preallocates.</param>
    /// <param name="maxStepsPerPump">The most fixed steps one pump may request.</param>
    /// <param name="editDebounceSeconds">The quiet window an edit burst must reach.</param>
    /// <param name="authoring">Authors inspector edits, or <see langword="null"/> when the
    /// document has no writable stage.</param>
    internal ViewerPhysicsController(
        IViewerPhysicsTransportFactory factory,
        IViewerPhysicsClock clock,
        PhysicsRenderCapacities capacities,
        int maxStepsPerPump = 8,
        double editDebounceSeconds = 0.25d,
        IViewerPhysicsAuthoringStage? authoring = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStepsPerPump);
        _factory = factory;
        _clock = clock;
        _authoring = authoring;
        _bridge = new ViewerPhysicsRenderBridge(capacities);
        _debouncer = new ViewerPhysicsEditDebouncer(editDebounceSeconds);
        _maxStepsPerPump = maxStepsPerPump;
    }

    /// <summary>Raised whenever the state the physics UI renders from changed.</summary>
    internal event Action<ViewerPhysicsStatusSnapshot>? StatusChanged;

    /// <summary>Gets the bridge that carries simulated poses into the active backend.</summary>
    internal ViewerPhysicsRenderBridge Bridge => _bridge;

    /// <summary>Gets a value indicating whether a transport exists for the document.</summary>
    internal bool IsEnabled => _transport is not null;

    /// <summary>Gets a value indicating whether a user command currently owns the transport.</summary>
    /// <remarks>
    /// Internally paced steps deliberately do not report themselves as busy. A step is issued about
    /// every frame, so surfacing it would disable and re-enable the whole toolbar many times a
    /// second and make the transport controls unusable for the thing they exist to do.
    /// </remarks>
    internal bool IsBusy => Volatile.Read(ref _busy) != 0;

    /// <summary>Gets a value indicating whether any command owns the transport.</summary>
    internal bool IsCommandInFlight => Volatile.Read(ref _gateBusy) != 0;

    /// <summary>Gets a value indicating whether the viewer is pacing the world forward.</summary>
    internal bool IsPlaying => _isPlaying;

    /// <summary>Gets the observed edit debouncer, for diagnostics and tests.</summary>
    internal ViewerPhysicsEditDebouncer Edits => _debouncer;

    /// <summary>Gets the number of steps pacing dropped because the accumulator is bounded.</summary>
    internal long DroppedCatchUpSteps => _pacer?.DroppedCatchUpSteps ?? 0;

    /// <summary>Gets how completely the simulated identities reached the active backend.</summary>
    internal ViewerPhysicsBindingStats Bindings => new(
        _boundIdentities,
        _refusedBindings,
        _bindingSet.SkippedObjects,
        _bridge.UnresolvedOverrides);

    /// <summary>Gets one row per simulated object, for the inspector's query panel.</summary>
    internal IReadOnlyList<ViewerPhysicsObjectRow> Objects => _objectRows;

    /// <summary>Resolves the stable simulation identity one authored prim drives.</summary>
    /// <param name="primPath">The absolute authored prim path.</param>
    /// <returns>The identity, or zero when the prim drives nothing.</returns>
    /// <remarks>
    /// This answers "which bound object does this prim render", which is a pose question. The
    /// identity a runtime command must carry is a different question with a different answer, and
    /// it is resolved by <see cref="ResolveCommandTarget"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="primPath"/> is null.</exception>
    internal ulong ResolveIdentity(string primPath)
    {
        ArgumentNullException.ThrowIfNull(primPath);
        IReadOnlyList<ViewerPhysicsBinding> bindings = _bindingSet.Bindings;
        for (int index = 0; index < bindings.Count; index++)
        {
            if (string.Equals(bindings[index].PrimPath, primPath, StringComparison.Ordinal))
            {
                return bindings[index].Id;
            }
        }

        return 0UL;
    }

    /// <summary>
    /// Resolves the identity a runtime command must carry to reach one selected object.
    /// </summary>
    /// <param name="section">The inspector section the operator selected.</param>
    /// <param name="required">The command family the interaction needs.</param>
    /// <returns>The identity, or zero when the object does not accept that command family.</returns>
    /// <remarks>
    /// <para>
    /// The section already carries the address the composer gave the object, resolved from the
    /// extraction page when the inspector was read. Nothing is matched against the extractor's own
    /// identity here, because that identity is in a different space and would never match.
    /// </para>
    /// <para>
    /// The render binding table is deliberately not consulted. It holds only the identities that
    /// receive a pose, which a vehicle and a scene never do, so validating against it would refuse
    /// exactly the interactions this method exists to allow. An identity the world does not hold is
    /// reported by the world itself, as a per-object diagnostic the inspector shows.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="section"/> is null.</exception>
    internal static ulong ResolveCommandTarget(
        ViewerPhysicsObjectSection section,
        ViewerPhysicsCommandability required)
    {
        ArgumentNullException.ThrowIfNull(section);
        return section.Accepts(required) ? section.TargetId : 0UL;
    }

    /// <summary>
    /// Gets the revision that changes whenever the bound identities were rebuilt.
    /// </summary>
    /// <remarks>
    /// The inspector rebinds its object list only when this changes. The rows themselves are
    /// immutable and are replaced wholesale by a rebuild, so a paced step - which happens about a
    /// hundred times a second - must not cost a rebind of a list that did not change.
    /// </remarks>
    internal ulong BindingRevision => _bindingRevision;

    /// <summary>Gets a value indicating whether the render bridge was disabled by a failure.</summary>
    internal bool IsBridgeDisabled => Volatile.Read(ref _bridgeDisabled) != 0;

    /// <summary>Gets the failure that disabled the render bridge, or an empty string.</summary>
    internal string BridgeError => _bridgeError;

    /// <summary>
    /// Gets the capability matrix, derived from the built world and the active backend.
    /// </summary>
    /// <remarks>
    /// A capability is only reported as drawn when the world simulates it, the active backend
    /// accepted an override batch, the domain the capability draws through is renderable, and at
    /// least one identity is bound. Claiming otherwise would tell the user their scene is being
    /// drawn while it is in fact frozen.
    /// </remarks>
    internal IReadOnlyList<ViewerPhysicsCapabilityRow> Capabilities
    {
        get
        {
            IViewerPhysicsTransport? transport = _transport;
            if (transport is null)
            {
                _capabilityKey = CapabilityKey.None;
                _capabilitySupport = null;
                _capabilityRows = [];
                return _capabilityRows;
            }

            IReadOnlyList<ViewerPhysicsCapabilitySupport> support = transport.Capabilities;
            bool backend = _bridge.TargetSupportsOverrides && !IsBridgeDisabled;
            bool drawn = backend && _bridge.HasAppliedBatch;
            bool bound = _boundIdentities > 0;
            var key = new CapabilityKey(backend, drawn, bound);
            if (key.Equals(_capabilityKey) && SupportMatches(support))
            {
                // Nothing the matrix is derived from moved, so the same rows are returned. A paced
                // step asks for this list every frame and rebuilding it each time would allocate
                // one list plus one row per capability about a hundred times a second.
                _capabilitySupport = support;
                return _capabilityRows;
            }

            var rows = new List<ViewerPhysicsCapabilityRow>(support.Count);
            for (int index = 0; index < support.Count; index++)
            {
                ViewerPhysicsCapabilitySupport entry = support[index];
                rows.Add(DescribeCapability(entry, backend, drawn, bound));
            }

            _capabilityKey = key;
            _capabilitySupport = support;
            _capabilityRows = rows;
            return rows;
        }
    }

    /// <summary>Reports whether the transport's capability support is the one already described.</summary>
    /// <param name="support">The support list the transport just returned.</param>
    /// <returns><see langword="true"/> when the cached rows still describe it.</returns>
    /// <remarks>
    /// A transport is free to hand back a new list of identical entries on every read, so identity
    /// alone is not enough to decide the matrix is unchanged. The entries are compared instead,
    /// which allocates nothing and cannot mistake a changed capability for an unchanged one.
    /// </remarks>
    private bool SupportMatches(IReadOnlyList<ViewerPhysicsCapabilitySupport> support)
    {
        IReadOnlyList<ViewerPhysicsCapabilitySupport>? cached = _capabilitySupport;
        if (ReferenceEquals(cached, support))
        {
            return true;
        }

        if (cached is null || cached.Count != support.Count)
        {
            return false;
        }

        for (int index = 0; index < support.Count; index++)
        {
            if (!cached[index].Equals(support[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Gets the diagnostics the most recent operation produced.</summary>
    /// <remarks>
    /// The inspector rebinds its diagnostic list whenever this returns a different instance, and it
    /// asks once per painted frame, so a transport that rebuilds an identical list on every read
    /// would rebuild the list under the operator at frame rate. The last list is kept and returned
    /// again while the entries are unchanged; a single changed, added, or removed entry is a
    /// different list and is published immediately.
    /// </remarks>
    internal IReadOnlyList<ViewerPhysicsDiagnosticRow> Diagnostics
    {
        get
        {
            IReadOnlyList<ViewerPhysicsDiagnosticRow>? rows = _transport?.Diagnostics;
            if (rows is null)
            {
                _diagnosticRows = [];
                return _diagnosticRows;
            }

            if (DiagnosticsMatch(rows))
            {
                return _diagnosticRows;
            }

            _diagnosticRows = rows;
            return rows;
        }
    }

    private bool DiagnosticsMatch(IReadOnlyList<ViewerPhysicsDiagnosticRow> rows)
    {
        IReadOnlyList<ViewerPhysicsDiagnosticRow> cached = _diagnosticRows;
        if (ReferenceEquals(cached, rows))
        {
            return true;
        }

        if (cached.Count != rows.Count)
        {
            return false;
        }

        for (int index = 0; index < rows.Count; index++)
        {
            if (!cached[index].Equals(rows[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Gets the complete state the physics UI renders from.</summary>
    internal ViewerPhysicsStatusSnapshot Snapshot
    {
        get
        {
            IViewerPhysicsTransport? transport = _transport;
            if (transport is null)
            {
                return ViewerPhysicsStatusSnapshot.Disabled with { Error = CurrentError };
            }

            return new ViewerPhysicsStatusSnapshot(
                transport.Status,
                IsEnabled: true,
                IsBusy,
                _isPlaying,
                _loop,
                _speed,
                _previewEnabled,
                transport.StartTimeCode,
                transport.EndTimeCode,
                CurrentError);
        }
    }

    private string CurrentError => _error.Length > 0 ? _error : _bridgeError;

    /// <summary>Creates the transport on demand and builds the world.</summary>
    /// <param name="cancellationToken">Cancels the creation and the build.</param>
    internal async Task EnableAsync(CancellationToken cancellationToken = default)
    {
        if (_transport is not null || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        using CancellationTokenSource linked = LinkLifetime(cancellationToken);
        if (!await EnterAsync(linked.Token).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            IViewerPhysicsTransport transport =
                await _factory.CreateAsync(linked.Token).ConfigureAwait(false);
            if (Volatile.Read(ref _disposed) != 0)
            {
                await transport.DisposeAsync().ConfigureAwait(false);
                return;
            }

            _transport = transport;
            _pacer = new ViewerPhysicsPacer(transport.FixedStepSeconds, _maxStepsPerPump);
            await transport.BuildAsync(linked.Token).ConfigureAwait(false);
            await RefreshBindingsAsync(transport, linked.Token).ConfigureAwait(false);
            _error = string.Empty;
        }
        catch (OperationCanceledException)
        {
            // A cancelled enable leaves the controller disabled, which is exactly the state the
            // user asked for by closing or switching the document.
        }
        catch (ViewerPhysicsException exception)
        {
            RecordFailure(exception);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Rebuilds the world from the authored stage.</summary>
    /// <param name="cancellationToken">Cancels the rebuild.</param>
    /// <remarks>
    /// A rebuild discards the world the preview was authored from, so the preview is cleared with
    /// it. Leaving stale simulated poses in the session overlay would show the user a scene that no
    /// world produces, and no later command would ever remove them.
    /// </remarks>
    internal Task RebuildAsync(CancellationToken cancellationToken = default) =>
        RunCommandAsync(
            ViewerPhysicsCommand.Rebuild,
            async (transport, token) =>
            {
                _isPlaying = false;
                _pacer?.Reset();
                _debouncer.Reset();
                RequestOverrideClear();
                await ClearPreviewAsync(transport, token).ConfigureAwait(false);
                await transport.BuildAsync(token).ConfigureAwait(false);
                await RefreshBindingsAsync(transport, token).ConfigureAwait(false);
            },
            cancellationToken);

    /// <summary>Starts pacing the world forward.</summary>
    internal void Play()
    {
        if (!Snapshot.CanRun(ViewerPhysicsCommand.Play))
        {
            return;
        }

        _pacer?.Reset();
        _lastPumpSeconds = _clock.NowSeconds;
        _isPlaying = true;
        RaiseStatusChanged();
    }

    /// <summary>Stops pacing without discarding the world.</summary>
    internal void Pause()
    {
        if (!_isPlaying)
        {
            return;
        }

        _isPlaying = false;
        _pacer?.Reset();
        RaiseStatusChanged();
    }

    /// <summary>Returns to the authored start and restores the authored render state.</summary>
    /// <param name="cancellationToken">Cancels the reset.</param>
    internal Task StopAsync(CancellationToken cancellationToken = default) =>
        RunCommandAsync(
            ViewerPhysicsCommand.Stop,
            async (transport, token) =>
            {
                _isPlaying = false;
                _pacer?.Reset();
                RequestOverrideClear();
                await ClearPreviewAsync(transport, token).ConfigureAwait(false);
                await transport.ResetAsync(token).ConfigureAwait(false);
            },
            cancellationToken);

    /// <summary>Advances the world by exactly one fixed simulation step.</summary>
    /// <param name="cancellationToken">Cancels the step.</param>
    internal Task StepOneFrameAsync(CancellationToken cancellationToken = default) =>
        RunCommandAsync(
            ViewerPhysicsCommand.StepOneFrame,
            (transport, token) => transport.StepAsync(1, token),
            cancellationToken);

    /// <summary>Moves the world to an authored time code, cancelling any in-flight scrub.</summary>
    /// <param name="timeCode">The authored time code to seek to.</param>
    /// <param name="cancellationToken">Cancels the seek.</param>
    internal async Task SeekAsync(
        double timeCode,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeCode),
                timeCode,
                "The seek target must be a finite authored time code.");
        }

        // A scrub is a stream of intents, not a queue of work: only the newest target matters, so
        // an in-flight replay is cancelled rather than left to finish at a stale time code.
        CancellationTokenSource scrub = LinkLifetime(cancellationToken);
        CancellationTokenSource? previous = Interlocked.Exchange(ref _seekLifetime, scrub);
        if (previous is not null)
        {
            await previous.CancelAsync().ConfigureAwait(false);
            previous.Dispose();
        }

        try
        {
            _isPlaying = false;
            _pacer?.Reset();
            await RunCommandAsync(
                ViewerPhysicsCommand.Seek,
                (transport, token) => transport.SeekAsync(timeCode, token),
                scrub.Token,
                ignoreBusy: true).ConfigureAwait(false);
        }
        finally
        {
            if (Interlocked.CompareExchange(ref _seekLifetime, null, scrub) == scrub)
            {
                scrub.Dispose();
            }
        }
    }

    /// <summary>Changes whether playback wraps at the authored end.</summary>
    /// <param name="loop">Whether playback wraps.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    internal async Task SetLoopAsync(bool loop, CancellationToken cancellationToken = default)
    {
        _loop = loop;
        if (_transport is null)
        {
            RaiseStatusChanged();
            return;
        }

        await RunCommandAsync(
            ViewerPhysicsCommand.None,
            (transport, token) => transport.SetLoopAsync(loop, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Changes the playback speed without changing the fixed simulation step.</summary>
    /// <param name="speed">The requested speed multiplier.</param>
    internal void SetSpeed(double speed)
    {
        _speed = ViewerPhysicsSpeeds.Clamp(speed);
        RaiseStatusChanged();
    }

    /// <summary>Applies or clears simulated poses in the session overlay.</summary>
    /// <param name="enabled">Whether preview opinions are authored.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A sentence describing what the preview did, or an empty string when superseded.</returns>
    /// <exception cref="ViewerPhysicsException">
    /// The preview did not complete, in which case the preview stays disabled and the failure is
    /// recorded as a diagnostic rather than being reported to the user as a success.
    /// </exception>
    internal async Task<string> SetPreviewAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        IViewerPhysicsTransport? transport = _transport;
        if (transport is null)
        {
            return string.Empty;
        }

        using CancellationTokenSource linked = LinkLifetime(cancellationToken);
        if (!await EnterAsync(linked.Token).ConfigureAwait(false))
        {
            return string.Empty;
        }

        try
        {
            ViewerPhysicsPreviewOutcome outcome = await transport
                .ApplyPreviewAsync(enabled, linked.Token)
                .ConfigureAwait(false);
            _previewEnabled = enabled;
            RememberSelfEdits(outcome.Edits);
            _error = string.Empty;
            return outcome.Message;
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
        catch (ViewerPhysicsException exception)
        {
            // The overlay may hold whatever the failed apply managed to author, so the preview is
            // reported as off and the next world invalidation clears it. Those partial changes are
            // still the controller's own, so they are suppressed rather than rebuilt from.
            _previewEnabled = false;
            _previewClearPending = enabled;
            RememberSelfEdits(exception.Edits);
            RecordFailure(exception);
            throw;
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Bakes simulated poses into a file-backed destination layer.</summary>
    /// <param name="request">The bake request the dialog produced.</param>
    /// <param name="progress">Receives bounded progress, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the bake, which rolls the destination back.</param>
    /// <returns>What the bake did.</returns>
    internal async Task<ViewerPhysicsBakeOutcome> BakeAsync(
        ViewerPhysicsBakeRequest request,
        IProgress<ViewerPhysicsBakeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ViewerPhysicsBakeValidation validation = ViewerPhysicsBakeValidator.Validate(request);
        if (!validation.IsValid)
        {
            return new ViewerPhysicsBakeOutcome(false, false, 0, validation.Message);
        }

        IViewerPhysicsTransport? transport = _transport;
        if (transport is null)
        {
            return new ViewerPhysicsBakeOutcome(
                false,
                false,
                0,
                "Enable physics for this stage before baking.");
        }

        using CancellationTokenSource linked = LinkLifetime(cancellationToken);
        if (!await EnterAsync(linked.Token).ConfigureAwait(false))
        {
            return new ViewerPhysicsBakeOutcome(
                false,
                false,
                0,
                "The physics controller was disposed before the bake started.");
        }

        try
        {
            _isPlaying = false;
            _pacer?.Reset();
            return await transport
                .BakeAsync(request, progress, linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new ViewerPhysicsBakeOutcome(
                false,
                false,
                0,
                "The bake was cancelled and the destination layer was rolled back.");
        }
        catch (ViewerPhysicsException exception)
        {
            RecordFailure(exception);
            return new ViewerPhysicsBakeOutcome(false, false, 0, exception.Message);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>
    /// Stages one batch of interactive runtime commands for the next simulation step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Interaction does not take the transport command gate. A drag produces a batch per frame and
    /// a lifecycle command can take seconds, so waiting for the gate would either stall the pointer
    /// or queue a burst of stale inputs behind a rebuild. The transport's own bounded queue is what
    /// serializes staging against stepping.
    /// </para>
    /// <para>
    /// Staging never advances the world. A command submitted while playback is paused applies on
    /// the next step the user takes, which is what makes single-stepping an interaction possible,
    /// and the reported outcome says so rather than implying the input already took effect.
    /// </para>
    /// </remarks>
    /// <param name="commands">The commands to stage, in submission order.</param>
    /// <param name="cancellationToken">Cancels the submission.</param>
    /// <returns>What the world accepted and what it refused.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="commands"/> is null.</exception>
    internal async Task<ViewerPhysicsCommandOutcome> SubmitCommandsAsync(
        IReadOnlyList<ViewerPhysicsRuntimeCommand> commands,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commands);
        IViewerPhysicsTransport? transport = _transport;
        if (transport is null || Volatile.Read(ref _disposed) != 0)
        {
            return new ViewerPhysicsCommandOutcome(
                0,
                commands.Count,
                "Enable physics for this stage before driving the simulation.");
        }

        if (commands.Count == 0)
        {
            return ViewerPhysicsCommandOutcome.None;
        }

        ViewerPhysicsTransportStatus status = transport.Status;
        if (status.State is ViewerPhysicsRunState.Invalidated or ViewerPhysicsRunState.Faulted)
        {
            return new ViewerPhysicsCommandOutcome(
                0,
                commands.Count,
                "The built world is stale; rebuild it before driving the simulation.");
        }

        using CancellationTokenSource linked = LinkLifetime(cancellationToken);
        try
        {
            ViewerPhysicsCommandOutcome outcome = await transport
                .SubmitCommandsAsync(commands, linked.Token)
                .ConfigureAwait(false);
            Interlocked.Add(ref _stagedCommands, outcome.Accepted);
            Interlocked.Add(ref _refusedCommands, outcome.Rejected);
            return _isPlaying || outcome.Accepted == 0
                ? outcome
                : outcome with
                {
                    Message = outcome.Message +
                        " Playback is paused, so it applies on the next simulation step.",
                };
        }
        catch (OperationCanceledException)
        {
            return new ViewerPhysicsCommandOutcome(
                0, commands.Count, "The interactive command was cancelled.");
        }
        catch (ViewerPhysicsException exception)
        {
            // An interaction that cannot reach the world must not fault the whole controller: the
            // user is told the input was refused and the simulation keeps running.
            return new ViewerPhysicsCommandOutcome(0, commands.Count, exception.Message);
        }
    }

    /// <summary>Reads every extracted physics object and projects it into inspector sections.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One section per extracted object, or an empty list.</returns>
    internal async Task<IReadOnlyList<ViewerPhysicsObjectSection>> LoadInspectorAsync(
        CancellationToken cancellationToken = default)
    {
        IViewerPhysicsTransport? transport = _transport;
        if (transport is null || Volatile.Read(ref _disposed) != 0)
        {
            return [];
        }

        using CancellationTokenSource linked = LinkLifetime(cancellationToken);
        try
        {
            ViewerPhysicsExtractionDocument document = await transport
                .LoadInspectorAsync(linked.Token)
                .ConfigureAwait(false);
            IReadOnlyList<ViewerPhysicsObjectSection> sections =
                ViewerPhysicsInspectorProjector.Project(document, ResolveFeatures());
            _inspectorSections = sections;
            _inspectorRevision = document.Revision;
            return sections;
        }
        catch (OperationCanceledException)
        {
            return _inspectorSections;
        }
        catch (ViewerPhysicsException exception)
        {
            RecordFailure(exception);
            RaiseStatusChanged();
            return [];
        }
    }

    /// <summary>Gets the inspector sections the most recent read produced.</summary>
    internal IReadOnlyList<ViewerPhysicsObjectSection> InspectorSections => _inspectorSections;

    /// <summary>Gets the extraction revision the inspector sections were read at.</summary>
    internal ulong InspectorRevision => _inspectorRevision;

    /// <summary>Gets the undo and redo history of the physics inspector's authoring.</summary>
    internal ViewerPhysicsEditHistory History { get; } = new();

    /// <summary>Gets the number of runtime commands the world staged.</summary>
    internal long StagedCommands => Interlocked.Read(ref _stagedCommands);

    /// <summary>Gets the number of runtime commands the world refused.</summary>
    internal long RefusedCommands => Interlocked.Read(ref _refusedCommands);

    /// <summary>
    /// Reads the newest simulated position of one identity, for closing an interaction loop.
    /// </summary>
    /// <param name="id">The stable simulation identity.</param>
    /// <param name="position">Receives the newest interpolated position.</param>
    /// <returns><see langword="true"/> when a usable position was read.</returns>
    /// <remarks>
    /// This is a best-effort read of the newest interpolated pose the render bridge produced. It
    /// exists so a drag spring can measure how far the body still is from the pointer, which is a
    /// control loop that self-corrects every frame: a position that is one frame old only changes
    /// the force slightly, so the read deliberately does not synchronise with the render loop. A
    /// value that is not finite is refused rather than turned into a force.
    /// </remarks>
    internal bool TryReadSimulatedPosition(ulong id, out ViewerPhysicsVector3 position)
    {
        position = ViewerPhysicsVector3.Zero;
        if (id == 0UL)
        {
            return false;
        }

        ReadOnlySpan<PhysicsRenderTransformOverride> items = _bridge.Overrides.Items;
        for (int index = 0; index < items.Length; index++)
        {
            if (items[index].Id.Value != id)
            {
                continue;
            }

            UsdVec3d value = items[index].Position;
            var candidate = new ViewerPhysicsVector3(value.X, value.Y, value.Z);
            if (!candidate.IsFinite)
            {
                return false;
            }

            position = candidate;
            return true;
        }

        return false;
    }

    /// <summary>Gets a value indicating whether the document can be authored at all.</summary>
    internal bool CanAuthor => _authoring is not null;

    /// <summary>Reads the value one property currently holds, so an edit can record it.</summary>
    /// <param name="primPath">The prim the property is authored on.</param>
    /// <param name="name">The authored property name.</param>
    /// <param name="kind">The value the property carries.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The current value, or the unauthored value.</returns>
    internal async Task<ViewerPhysicsValue> ReadPropertyAsync(
        string primPath,
        string name,
        ViewerPhysicsValueKind kind,
        CancellationToken cancellationToken = default)
    {
        if (_authoring is not { } authoring)
        {
            return ViewerPhysicsValue.Unauthored(kind);
        }

        using CancellationTokenSource linked = LinkLifetime(cancellationToken);
        try
        {
            return await authoring
                .ReadAsync(primPath, name, kind, linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ViewerPhysicsValue.Unauthored(kind);
        }
    }

    /// <summary>Authors one inspector step and records it in the undo history.</summary>
    /// <param name="step">The step to author.</param>
    /// <param name="cancellationToken">Cancels the step.</param>
    /// <returns>What the step did.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="step"/> is null.</exception>
    internal Task<ViewerPhysicsAuthoringResult> ApplyEditAsync(
        ViewerPhysicsEditStep step,
        CancellationToken cancellationToken = default) =>
        ApplyEditCoreAsync(step, record: true, cancellationToken);

    /// <summary>Reverses the newest recorded step.</summary>
    /// <param name="cancellationToken">Cancels the step.</param>
    /// <returns>What the undo did.</returns>
    internal Task<ViewerPhysicsAuthoringResult> UndoAsync(
        CancellationToken cancellationToken = default) =>
        ReplayHistoryAsync(undo: true, cancellationToken);

    /// <summary>Replays the newest undone step.</summary>
    /// <param name="cancellationToken">Cancels the step.</param>
    /// <returns>What the redo did.</returns>
    internal Task<ViewerPhysicsAuthoringResult> RedoAsync(
        CancellationToken cancellationToken = default) =>
        ReplayHistoryAsync(undo: false, cancellationToken);

    private async Task<ViewerPhysicsAuthoringResult> ReplayHistoryAsync(
        bool undo,
        CancellationToken cancellationToken)
    {
        ViewerPhysicsEditStep taken;
        ViewerPhysicsEditStep applied;
        if (undo)
        {
            if (!History.TryTakeUndo(out taken))
            {
                return new ViewerPhysicsAuthoringResult(
                    0, 0, "There is nothing to undo.", []);
            }

            applied = taken;
        }
        else
        {
            if (!History.TryTakeRedo(out taken))
            {
                return new ViewerPhysicsAuthoringResult(
                    0, 0, "There is nothing to redo.", []);
            }

            applied = taken;
        }

        try
        {
            ViewerPhysicsAuthoringResult result =
                await ApplyEditCoreAsync(applied, record: false, cancellationToken)
                    .ConfigureAwait(false);
            if (result.Applied != 0)
            {
                return result;
            }

            // Nothing reached the stage, so the history must not claim the step was reversed.
            History.Restore(undo ? applied.Reversed() : applied, undo);
            return result;
        }
        catch
        {
            History.Restore(undo ? applied.Reversed() : applied, undo);
            throw;
        }
    }

    private async Task<ViewerPhysicsAuthoringResult> ApplyEditCoreAsync(
        ViewerPhysicsEditStep step,
        bool record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (_authoring is not { } authoring)
        {
            return new ViewerPhysicsAuthoringResult(
                0,
                step.Edits.Count,
                "This document has no writable stage, so physics properties cannot be authored.",
                []);
        }

        using CancellationTokenSource linked = LinkLifetime(cancellationToken);
        ViewerPhysicsAuthoringResult result;
        try
        {
            result = await authoring.ApplyAsync(step, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new ViewerPhysicsAuthoringResult(
                0, step.Edits.Count, "The physics edit was cancelled.", []);
        }
        catch (ViewerPhysicsException exception)
        {
            RecordFailure(exception);
            RaiseStatusChanged();
            return new ViewerPhysicsAuthoringResult(
                0, step.Edits.Count, exception.Message, exception.Edits);
        }

        // The change the step produced is the controller's own, and the controller knows exactly
        // which fields moved. Remembering the serial pair together with that classification is what
        // stops an edit to simulation metadata from rebuilding a world it cannot affect, while an
        // edit to a mass still does.
        RememberAuthoredEdits(result.Edits, ClassifyStep(step));
        if (record && result.Applied != 0)
        {
            History.Record(step, _clock.NowSeconds);
        }

        return result;
    }

    private static ViewerPhysicsEditKind ClassifyStep(ViewerPhysicsEditStep step)
    {
        for (int index = 0; index < step.Edits.Count; index++)
        {
            if (!ViewerPhysicsAuthoringClassifier.IsSimulationNeutral(step.Edits[index].Name))
            {
                return ViewerPhysicsEditKind.Relevant;
            }
        }

        return ViewerPhysicsEditKind.Visual;
    }

    private UsdPhysicsCapability ResolveFeatures()
    {
        // The capability rows carry the exact flag names the physics package produced, so parsing
        // them back is lossless. Deriving the mask here keeps the transport surface free of a
        // second capability representation that could disagree with the matrix the user sees.
        IReadOnlyList<ViewerPhysicsCapabilityRow> rows = Capabilities;
        UsdPhysicsCapability features = UsdPhysicsCapability.None;
        for (int index = 0; index < rows.Count; index++)
        {
            ViewerPhysicsCapabilityRow row = rows[index];
            if (row.IsSupported &&
                Enum.TryParse(row.Name, ignoreCase: false, out UsdPhysicsCapability feature))
            {
                features |= feature;
            }
        }

        return features;
    }

    /// <summary>Observes one classified authored edit.</summary>
    /// <param name="kind">Whether the edit can change simulated behaviour.</param>
    /// <param name="edit">The identity of the observed change, when it is known.</param>
    /// <remarks>
    /// Applying a preview authors poses into the session overlay and that edit comes back as a
    /// stage change. Only the exact change the controller authored is suppressed, matched by the
    /// serial pair that brackets it: a window over time would drop a real edit that happened to
    /// arrive inside it, and would miss the controller's own edit whenever it arrived late.
    /// </remarks>
    internal void NotifyStageChanged(
        ViewerPhysicsEditKind kind,
        ViewerPhysicsStageEdit edit = default)
    {
        if (_transport is null)
        {
            return;
        }

        if (TryConsumeSelfEdit(edit))
        {
            return;
        }

        if (TryConsumeAuthoredEdit(edit, out ViewerPhysicsEditKind authored))
        {
            // The controller authored this exact change and knows precisely which fields moved, so
            // its own classification replaces the caller's conservative one.
            kind = authored;
        }

        _debouncer.Observe(kind, _clock.NowSeconds);
        if (kind == ViewerPhysicsEditKind.Relevant && _isPlaying)
        {
            // Pausing immediately is the honest response: the world the user is watching no longer
            // matches the stage, and continuing to advance it would show simulated motion that the
            // authored scene does not describe.
            _isPlaying = false;
            _pacer?.Reset();
            RaiseStatusChanged();
        }
    }

    /// <summary>
    /// Advances pacing and fires any debounced invalidation. Called once per UI tick.
    /// </summary>
    /// <param name="cancellationToken">Cancels the pump.</param>
    internal async ValueTask PumpAsync(CancellationToken cancellationToken = default)
    {
        IViewerPhysicsTransport? transport = _transport;
        if (transport is null || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        // A UI timer tick that overlaps the previous one would advance pacing twice for the same
        // wall clock and queue a second step behind the first, so only one pump runs at a time.
        if (Interlocked.CompareExchange(ref _pumping, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await PumpCoreAsync(transport, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _pumping, 0);
        }
    }

    private async ValueTask PumpCoreAsync(
        IViewerPhysicsTransport transport,
        CancellationToken cancellationToken)
    {
        double now = _clock.NowSeconds;
        if (_debouncer.ShouldInvalidate(now))
        {
            _isPlaying = false;
            _pacer?.Reset();
            RequestOverrideClear();
            await RunCommandAsync(
                ViewerPhysicsCommand.None,
                async (target, token) =>
                {
                    await ClearPreviewAsync(target, token).ConfigureAwait(false);
                    await target.InvalidateAsync(token).ConfigureAwait(false);
                },
                cancellationToken,
                silent: true).ConfigureAwait(false);
            return;
        }

        if (_previewClearPending)
        {
            await RunCommandAsync(
                ViewerPhysicsCommand.None,
                (target, token) => ClearPreviewAsync(target, token).AsTask(),
                cancellationToken,
                silent: true).ConfigureAwait(false);
            return;
        }

        if (!_isPlaying || _pacer is not { } pacer)
        {
            return;
        }

        // Pacing is only advanced when a step can actually be issued. Advancing first and then
        // discovering the transport is busy would consume the elapsed wall clock and silently drop
        // the steps it was owed, so playback would fall behind by exactly the time it was busy.
        if (IsCommandInFlight)
        {
            return;
        }

        double elapsed = now - _lastPumpSeconds;
        _lastPumpSeconds = now;
        int steps = pacer.Advance(elapsed, _speed);
        if (steps <= 0)
        {
            return;
        }

        await RunCommandAsync(
            ViewerPhysicsCommand.None,
            (target, token) => target.StepAsync(steps, token),
            cancellationToken,
            silent: true).ConfigureAwait(false);

        ViewerPhysicsTransportStatus status = transport.Status;
        if (status.State == ViewerPhysicsRunState.Ended && !_loop)
        {
            _isPlaying = false;
        }

        // The simulated time moved, so the status line and the scrubber have something new to show.
        // The busy flag deliberately did not move with it, which is what keeps the toolbar usable.
        RaiseStatusChanged();
    }

    /// <summary>
    /// Consumes the latest complete published frame and applies one bounded override batch.
    /// </summary>
    /// <remarks>
    /// This runs on the render loop, so it must never block on the physics worker and never touch a
    /// stage. It performs one nonblocking snapshot copy and one backend apply, and nothing else. A
    /// failure inside that work disables the bridge and restores the authored render state instead
    /// of propagating: physics is one feature of a document, and a document must keep rendering
    /// without it.
    /// </remarks>
    /// <param name="renderSeconds">The render clock the overrides are interpolated to.</param>
    /// <param name="target">The active backend the batch is applied to.</param>
    /// <returns>What the pump did.</returns>
    internal ViewerPhysicsFramePumpResult PumpRenderFrame(
        double renderSeconds,
        IViewerPhysicsOverrideTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (Volatile.Read(ref _bridgeDisabled) != 0)
        {
            return default;
        }

        try
        {
            return PumpRenderFrameCore(renderSeconds, target);
        }
        catch (Exception exception)
        {
            DisableBridge(exception, target);
            return default;
        }
    }

    /// <summary>Re-enables the render bridge after the failure that disabled it was addressed.</summary>
    internal void ResetBridgeFailure()
    {
        Volatile.Write(ref _bridgeDisabled, 0);
        _bridgeError = string.Empty;
        RaiseStatusChanged();
    }

    /// <summary>
    /// Disables the render bridge because the caller could not apply a batch.
    /// </summary>
    /// <remarks>
    /// The render loop owns the backend handle, so a failure it observes outside
    /// <see cref="PumpRenderFrame"/> - capturing the target, for instance - cannot be diagnosed
    /// here. It is still a reason to stop feeding that backend, and recording it keeps the reason
    /// visible in the status the inspector renders.
    /// </remarks>
    /// <param name="reason">Why simulated poses are no longer applied.</param>
    internal void DisableRenderBridge(string reason)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);
        Volatile.Write(ref _bridgeDisabled, 1);
        _bridgeError = reason;
        RaiseStatusChanged();
    }

    private ViewerPhysicsFramePumpResult PumpRenderFrameCore(
        double renderSeconds,
        IViewerPhysicsOverrideTarget target)
    {
        if (Interlocked.Exchange(ref _clearPending, 0) != 0)
        {
            _bridge.Clear(target);
            return default;
        }

        IViewerPhysicsTransport? transport = _transport;
        if (transport is null)
        {
            return default;
        }

        if (Interlocked.Exchange(ref _replayPending, 0) != 0)
        {
            int replayed = _bridge.ReplayLatest(target);
            return new ViewerPhysicsFramePumpResult(false, replayed, _bridge.AppliedRevision);
        }

        _ = transport.TryPublishLatestFrame(_bridge.Channel);
        return _bridge.Pump(renderSeconds, target);
    }

    private void DisableBridge(Exception exception, IViewerPhysicsOverrideTarget target)
    {
        Volatile.Write(ref _bridgeDisabled, 1);
        _bridgeError = "Physics rendering was disabled after the override bridge failed: " +
            exception.Message;
        try
        {
            _bridge.Clear(target);
        }
        catch (Exception clearFailure)
        {
            // The backend is already failing; the authored state is restored on the next document
            // or backend it can be restored on, and rendering continues meanwhile.
            _bridgeError += " The authored render state could not be restored: " +
                clearFailure.Message + ".";
        }

        RaiseStatusChanged();
    }

    /// <summary>Asks the render loop to drop every override and restore authored state.</summary>
    internal void RequestOverrideClear() => Volatile.Write(ref _clearPending, 1);

    /// <summary>
    /// Asks the render loop to re-apply the latest overrides after a context loss or backend switch.
    /// </summary>
    internal void RequestOverrideReplay() => Volatile.Write(ref _replayPending, 1);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _isPlaying = false;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        CancellationTokenSource? scrub = Interlocked.Exchange(ref _seekLifetime, null);
        if (scrub is not null)
        {
            await scrub.CancelAsync().ConfigureAwait(false);
            scrub.Dispose();
        }

        // Draining the command gate is what makes closing during a build or a scrub safe: the
        // transport is only released once the request that owns it has observed the cancellation.
        await _commandGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            IViewerPhysicsTransport? transport = _transport;
            _transport = null;
            if (transport is not null)
            {
                await transport.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _commandGate.Release();
        }

        _lifetime.Dispose();
        _commandGate.Dispose();
    }

    private async Task RunCommandAsync(
        ViewerPhysicsCommand command,
        Func<IViewerPhysicsTransport, CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        bool ignoreBusy = false,
        bool silent = false)
    {
        IViewerPhysicsTransport? transport = _transport;
        if (transport is null)
        {
            return;
        }

        if (command != ViewerPhysicsCommand.None)
        {
            ViewerPhysicsCommandAvailability availability = Snapshot.GetAvailability(command);

            // A scrub supersedes the request it interrupted rather than being refused by it, so a
            // busy transport is not a reason to drop the newest target the user asked for: the
            // superseded request has already been cancelled and the gate is about to open.
            bool refused = availability != ViewerPhysicsCommandAvailability.Available &&
                !(ignoreBusy && availability == ViewerPhysicsCommandAvailability.Busy);
            if (refused)
            {
                return;
            }
        }

        using CancellationTokenSource linked = LinkLifetime(cancellationToken);
        if (!await EnterAsync(linked.Token, silent).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            transport = _transport;
            if (transport is null)
            {
                return;
            }

            await operation(transport, linked.Token).ConfigureAwait(false);
            _error = string.Empty;
        }
        catch (OperationCanceledException)
        {
            // A cancelled command is a user intent that was superseded, not a failure to report.
        }
        catch (ViewerPhysicsException exception)
        {
            RecordFailure(exception);
        }
        finally
        {
            Exit(silent);
        }
    }

    private async ValueTask<bool> EnterAsync(CancellationToken cancellationToken, bool silent = false)
    {
        try
        {
            await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            _commandGate.Release();
            return false;
        }

        Volatile.Write(ref _gateBusy, 1);
        if (silent)
        {
            return true;
        }

        Volatile.Write(ref _busy, 1);
        RaiseStatusChanged();
        return true;
    }

    private void Exit(bool silent = false)
    {
        Volatile.Write(ref _gateBusy, 0);
        Volatile.Write(ref _busy, 0);
        try
        {
            _commandGate.Release();
        }
        catch (ObjectDisposedException)
        {
            // The controller was disposed while the command drained; nothing else needs the gate.
        }

        if (!silent)
        {
            RaiseStatusChanged();
        }
    }

    private static ViewerPhysicsCapabilityRow DescribeCapability(
        ViewerPhysicsCapabilitySupport support,
        bool backendSupportsOverrides,
        bool backendDrewBatch,
        bool hasBindings)
    {
        if (!support.IsSupported)
        {
            return new ViewerPhysicsCapabilityRow(support.Name, false, false, support.Detail);
        }

        if (support.Domain is null)
        {
            return new ViewerPhysicsCapabilityRow(
                support.Name,
                true,
                false,
                $"{support.Name} is simulated and draws nothing of its own.");
        }

        if (!backendSupportsOverrides)
        {
            return new ViewerPhysicsCapabilityRow(
                support.Name,
                true,
                false,
                $"{support.Name} is simulated but the active render backend applies no transform " +
                "overrides.");
        }

        if (!hasBindings)
        {
            return new ViewerPhysicsCapabilityRow(
                support.Name,
                true,
                false,
                $"{support.Name} is simulated but no simulated identity is bound to an authored " +
                "prim, so nothing is drawn.");
        }

        if (!backendDrewBatch)
        {
            return new ViewerPhysicsCapabilityRow(
                support.Name,
                true,
                false,
                $"{support.Name} is simulated but the active render backend has not reported " +
                "drawing a pose yet.");
        }

        return new ViewerPhysicsCapabilityRow(
            support.Name,
            true,
            true,
            $"{support.Name} is simulated and the active backend draws it.");
    }

    private async ValueTask RefreshBindingsAsync(
        IViewerPhysicsTransport transport,
        CancellationToken cancellationToken)
    {
        ViewerPhysicsBindingSet set = await transport
            .LoadBindingsAsync(cancellationToken)
            .ConfigureAwait(false);
        _bindingSet = set;

        // A whole new table is published rather than the live one being edited, so a render frame
        // that is mid-resolve keeps the table it started with instead of seeing a half-rebound one.
        var table = new PhysicsRenderBindingTable(_bridge.BindingCapacity);
        var rows = new List<ViewerPhysicsObjectRow>(set.Bindings.Count);
        int bound = 0;
        int refused = 0;
        for (int index = 0; index < set.Bindings.Count; index++)
        {
            ViewerPhysicsBinding binding = set.Bindings[index];

            // A binding source that marks "not instanced" as a negative index means the same thing
            // the render table spells as zero, and a bad index must not take the whole build down.
            int instanceIndex = Math.Max(binding.InstanceIndex, 0);
            bool stored;
            string reason;
            try
            {
                stored = table.TryBind(
                    new PhysicsRenderObjectId(binding.Id, binding.Kind, instanceIndex),
                    binding.PrimPath,
                    instanceIndex);
                reason = " The bounded binding table is full, so it is not drawn.";
            }
            catch (ArgumentException exception)
            {
                // One malformed identity is a reason not to draw that one object. Refusing to build
                // the whole table would take every other simulated object down with it.
                stored = false;
                reason = " It cannot be bound to an authored prim: " + exception.Message;
            }

            if (stored)
            {
                bound++;
            }
            else
            {
                refused++;
            }

            rows.Add(new ViewerPhysicsObjectRow(
                binding.PrimPath,
                binding.Kind.ToString(),
                binding.IsSimulated,
                stored,
                stored ? binding.Detail : binding.Detail + reason));
        }

        _boundIdentities = bound;
        _refusedBindings = refused;
        _objectRows = rows;
        _bindingRevision++;
        _bridge.SetBindings(table);
        RaiseStatusChanged();
    }

    /// <summary>Identifies everything the capability matrix is derived from.</summary>
    /// <remarks>
    /// The transport's own support list is compared separately, by content, because a transport may
    /// return a fresh list of identical entries on every read.
    /// </remarks>
    private readonly record struct CapabilityKey(
        bool BackendSupportsOverrides,
        bool BackendDrewBatch,
        bool HasBindings)
    {
        internal static CapabilityKey None => default;
    }

    private async ValueTask ClearPreviewAsync(
        IViewerPhysicsTransport transport,
        CancellationToken cancellationToken)
    {
        if (!_previewEnabled && !_previewClearPending)
        {
            return;
        }

        _previewEnabled = false;
        _previewClearPending = false;
        try
        {
            ViewerPhysicsPreviewOutcome outcome = await transport
                .ApplyPreviewAsync(false, cancellationToken)
                .ConfigureAwait(false);
            RememberSelfEdits(outcome.Edits);
        }
        catch (ViewerPhysicsException exception)
        {
            // Clearing is best effort: it runs while the world is already being torn down or
            // rebuilt, and refusing to rebuild because the overlay could not be cleared would
            // leave the user with neither a preview nor a world.
            RememberSelfEdits(exception.Edits);
            _error = exception.Message;
        }
    }

    private void RememberSelfEdits(IReadOnlyList<ViewerPhysicsStageEdit> edits)
    {
        for (int index = 0; index < edits.Count; index++)
        {
            RememberSelfEdit(edits[index]);
        }
    }

    private void RememberAuthoredEdits(
        IReadOnlyList<ViewerPhysicsStageEdit> edits,
        ViewerPhysicsEditKind kind)
    {
        if (edits.Count == 0)
        {
            return;
        }

        lock (_selfEditLock)
        {
            for (int index = 0; index < edits.Count; index++)
            {
                if (!edits[index].IsKnown)
                {
                    continue;
                }

                _authoredEdits.Enqueue(new ClassifiedEdit(edits[index], kind));
                while (_authoredEdits.Count > MaxPendingSelfEdits)
                {
                    _ = _authoredEdits.Dequeue();
                }
            }
        }
    }

    private bool TryConsumeAuthoredEdit(
        ViewerPhysicsStageEdit edit,
        out ViewerPhysicsEditKind kind)
    {
        kind = ViewerPhysicsEditKind.Relevant;
        if (!edit.IsKnown)
        {
            return false;
        }

        lock (_selfEditLock)
        {
            if (_authoredEdits.Count == 0)
            {
                return false;
            }

            int count = _authoredEdits.Count;
            var matched = false;
            for (int index = 0; index < count; index++)
            {
                ClassifiedEdit pending = _authoredEdits.Dequeue();
                if (!matched && pending.Edit.IsSameChangeAs(edit))
                {
                    matched = true;
                    kind = pending.Kind;
                    continue;
                }

                _authoredEdits.Enqueue(pending);
            }

            return matched;
        }
    }

    /// <summary>One change the controller authored, with the classification it was authored with.</summary>
    private readonly record struct ClassifiedEdit(
        ViewerPhysicsStageEdit Edit,
        ViewerPhysicsEditKind Kind);

    private void RememberSelfEdit(ViewerPhysicsStageEdit edit)
    {
        if (!edit.IsKnown)
        {
            return;
        }

        lock (_selfEditLock)
        {
            _selfEdits.Enqueue(edit);
            while (_selfEdits.Count > MaxPendingSelfEdits)
            {
                _ = _selfEdits.Dequeue();
            }
        }
    }

    private bool TryConsumeSelfEdit(ViewerPhysicsStageEdit edit)
    {
        if (!edit.IsKnown)
        {
            return false;
        }

        lock (_selfEditLock)
        {
            if (_selfEdits.Count == 0)
            {
                return false;
            }

            int count = _selfEdits.Count;
            bool matched = false;
            for (int index = 0; index < count; index++)
            {
                ViewerPhysicsStageEdit pending = _selfEdits.Dequeue();
                if (!matched && pending.IsSameChangeAs(edit))
                {
                    matched = true;
                    continue;
                }

                _selfEdits.Enqueue(pending);
            }

            return matched;
        }
    }

    private CancellationTokenSource LinkLifetime(CancellationToken cancellationToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);

    private void RecordFailure(ViewerPhysicsException exception)
    {
        _error = exception.Message;
        _isPlaying = false;

        // A faulted world can no longer produce the poses the preview was authored from, so the
        // preview is reported as off and cleared as soon as a command can reach the overlay.
        _previewClearPending |= _previewEnabled;
        _previewEnabled = false;
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(Snapshot);
}
