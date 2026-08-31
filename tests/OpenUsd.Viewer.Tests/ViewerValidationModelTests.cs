// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerValidationModelTests
{
    [Test]
    public async Task DisplayOrdersResultsBySeverityWhateverOrderTheyArrivedIn()
    {
        UsdValidationValidatorInfo[] validators =
        [
            new("validator.a", "A", "usdValidation", [], [], IsSuite: false, IsTimeDependent: false),
            new("validator.b", "B", "usdValidation", [], [], IsSuite: false, IsTimeDependent: false)
        ];

        // Deliberately reversed: if the input already arrived most-severe
        // first, an implementation that did no ordering at all would pass.
        UsdValidationError[] errors =
        [
            new(UsdValidationSeverity.Info, "validator.b", "last", "Last issue", ["/World/C"]),
            new(UsdValidationSeverity.Warning, "validator.b", "middle", "Middle issue", ["/World/B"]),
            new(UsdValidationSeverity.Error, "validator.a", "first", "First issue", ["/World/A"])
        ];

        ViewerValidationSnapshot snapshot = ViewerValidationSnapshot.Create(
            validators,
            errors,
            TimeSpan.FromMilliseconds(2));
        string state = ViewerValidationFormatter.FormatState(snapshot);
        string details = ViewerValidationFormatter.FormatDetails(snapshot);

        await Assert.That(state).Contains("3 result(s) from 2 registered validator(s)");
        await Assert.That(state).Contains("errors: 1; warnings: 1; info: 1");
        await Assert.That(details.IndexOf("First issue", StringComparison.Ordinal))
            .IsLessThan(details.IndexOf("Middle issue", StringComparison.Ordinal));
        await Assert.That(details.IndexOf("Middle issue", StringComparison.Ordinal))
            .IsLessThan(details.IndexOf("Last issue", StringComparison.Ordinal));
        await Assert.That(details).Contains("Sites: /World/A");
        await Assert.That(details).Contains("Sites: /World/B");
        await Assert.That(details).Contains("Sites: /World/C");

        // Within one severity the arrival order is preserved, because that is
        // the order UsdValidation already made stable across runs.
        UsdValidationError[] sameSeverity =
        [
            new(UsdValidationSeverity.Error, "validator.b", "second", "Second", ["/B"]),
            new(UsdValidationSeverity.Error, "validator.a", "first", "First", ["/A"])
        ];
        ViewerValidationSnapshot stable = ViewerValidationSnapshot.Create(
            validators,
            sameSeverity,
            TimeSpan.Zero);
        await Assert.That(stable.Errors[0].ErrorName).IsEqualTo("second");
        await Assert.That(stable.Errors[1].ErrorName).IsEqualTo("first");
    }

    [Test]
    public async Task ValidationSnapshotsCompareErrorCollectionsByValue()
    {
        UsdValidationError[] leftErrors =
        [
            new(UsdValidationSeverity.Error, "validator", "error", "Message", ["/World"])
        ];
        UsdValidationError[] rightErrors =
        [
            new(UsdValidationSeverity.Error, "validator", "error", "Message", ["/World"])
        ];

        ViewerValidationSnapshot left = ViewerValidationSnapshot.Create(
            [],
            leftErrors,
            TimeSpan.FromMilliseconds(1));
        ViewerValidationSnapshot right = ViewerValidationSnapshot.Create(
            [],
            rightErrors,
            TimeSpan.FromMilliseconds(1));

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
    }

    [Test]
    public async Task TwoRunsOfTheSameResultsCompareEqualDespiteDifferentDurations()
    {
        // Duration is measured wall-clock time and is never equal between two
        // runs. Including it in equality would make every poll of an unchanged
        // stage look like a change, which is the opposite of what a detached
        // snapshot is for.
        UsdValidationError[] errors =
        [
            new(UsdValidationSeverity.Error, "validator", "error", "Message", ["/World"])
        ];

        ViewerValidationSnapshot fast = ViewerValidationSnapshot.Create(
            [],
            errors,
            TimeSpan.FromMilliseconds(1));
        ViewerValidationSnapshot slow = ViewerValidationSnapshot.Create(
            [],
            errors,
            TimeSpan.FromSeconds(9));

        await Assert.That(fast).IsEqualTo(slow);
        await Assert.That(fast.GetHashCode()).IsEqualTo(slow.GetHashCode());
        await Assert.That(fast.Duration).IsNotEqualTo(slow.Duration)
            .Because("the duration is still reported, it is just not part of identity");
        await Assert.That(ViewerValidationFormatter.FormatState(slow)).Contains("9000 ms");
    }

    [Test]
    public async Task ATruncatedSnapshotStillReportsEveryResultItDidNotRetain()
    {
        // A stage can report thousands of results, and this snapshot is copied
        // out of the scheduler and rendered as text, so the retained window is
        // bounded. The counts must still describe the whole run, or a bounded
        // view would quietly become a wrong one.
        int authored = ViewerValidationSnapshot.MaxRetainedErrors + 250;
        var errors = new UsdValidationError[authored];
        for (int index = 0; index < authored; index++)
        {
            UsdValidationSeverity severity = index % 5 == 0
                ? UsdValidationSeverity.Error
                : index % 3 == 0
                    ? UsdValidationSeverity.Warning
                    : UsdValidationSeverity.Info;
            errors[index] = new UsdValidationError(
                severity,
                "validator",
                string.Create(CultureInfo.InvariantCulture, $"error{index:D4}"),
                string.Create(CultureInfo.InvariantCulture, $"Issue {index}"),
                [string.Create(CultureInfo.InvariantCulture, $"/World/Prim{index}")]);
        }

        ViewerValidationSnapshot snapshot = ViewerValidationSnapshot.Create(
            [],
            errors,
            TimeSpan.FromMilliseconds(5));

        await Assert.That(snapshot.Errors.Length)
            .IsEqualTo(ViewerValidationSnapshot.MaxRetainedErrors);
        await Assert.That(snapshot.ReportedCount).IsEqualTo(authored);
        await Assert.That(snapshot.IsTruncated).IsTrue();
        await Assert.That(snapshot.ErrorCount + snapshot.WarningCount + snapshot.InfoCount)
            .IsEqualTo(authored)
            .Because("the severity counts must cover the whole run, not the retained window");
        await Assert.That(snapshot.ErrorCount)
            .IsEqualTo(errors.Count(error => error.Severity == UsdValidationSeverity.Error));

        // Truncation keeps the most severe results: every authored error is
        // retained before any warning, and no info result displaces one.
        int retainedErrors = snapshot.Errors.Count(
            error => error.Severity == UsdValidationSeverity.Error);
        await Assert.That(retainedErrors).IsEqualTo(snapshot.ErrorCount);
        int previousRank = ViewerValidationSnapshot.Rank(UsdValidationSeverity.Error);
        foreach (ViewerValidationErrorSnapshot error in snapshot.Errors)
        {
            int rank = ViewerValidationSnapshot.Rank(error.Severity);
            await Assert.That(rank).IsGreaterThanOrEqualTo(previousRank)
                .Because("retained results are ordered most severe first");
            previousRank = rank;
        }

        string state = ViewerValidationFormatter.FormatState(snapshot);
        await Assert.That(state).Contains($"{authored} result(s)");
        await Assert.That(state)
            .Contains($"Showing the {ViewerValidationSnapshot.MaxRetainedErrors} most severe.");
        await Assert.That(ViewerValidationFormatter.FormatDetails(snapshot))
            .Contains($"... {authored - ViewerValidationSnapshot.MaxRetainedErrors} " +
                "more result(s) not shown");

        // Two runs over the same results retain the same window, so an
        // unchanged stage still compares equal across polls.
        await Assert.That(snapshot).IsEqualTo(
            ViewerValidationSnapshot.Create([], errors, TimeSpan.FromMilliseconds(5)));
    }

    [Test]
    public async Task OneEnormousResultCannotGrowTheSnapshotWithoutBound()
    {
        UsdValidationError[] errors =
        [
            new(
                UsdValidationSeverity.Error,
                "validator",
                "huge",
                new string('m', 40_000),
                [.. Enumerable.Range(0, 500).Select(index => $"/World/Prim{index}")])
        ];

        ViewerValidationSnapshot snapshot = ViewerValidationSnapshot.Create(
            [],
            errors,
            TimeSpan.Zero);

        await Assert.That(snapshot.Errors[0].Message.Length)
            .IsLessThanOrEqualTo(ViewerValidationSnapshot.MaxMessageLength + 32);
        await Assert.That(snapshot.Errors[0].Sites.Length)
            .IsLessThanOrEqualTo(ViewerValidationSnapshot.MaxSitesLength + 32);
        await Assert.That(snapshot.Errors[0].Message).IsNotEqualTo(errors[0].Message);
        await Assert.That(snapshot.ReportedCount).IsEqualTo(1);
        await Assert.That(snapshot.IsTruncated).IsFalse();
    }

    [Test]
    public async Task TheStateLineNamesWhatTheRunActuallyCovered()
    {
        UsdValidationError[] errors =
        [
            new(UsdValidationSeverity.Error, "validator", "error", "Message", ["/World/Cube"])
        ];

        ViewerValidationSnapshot stage = ViewerValidationSnapshot.Create(
            [],
            errors,
            TimeSpan.FromMilliseconds(1));
        ViewerValidationSnapshot prim = ViewerValidationSnapshot.Create(
            [],
            errors,
            TimeSpan.FromMilliseconds(1),
            ViewerValidationScope.Prim,
            "/World/Cube");

        await Assert.That(ViewerValidationFormatter.FormatState(stage))
            .Contains("UsdValidation (whole stage):");
        await Assert.That(ViewerValidationFormatter.FormatState(prim))
            .Contains("UsdValidation (prim /World/Cube):");
        await Assert.That(stage).IsNotEqualTo(prim)
            .Because("results from different scopes are different results");

        // A prim scope with no path cannot claim a prim.
        await Assert.That(ViewerValidationFormatter.FormatState(
                ViewerValidationSnapshot.Create(
                    [],
                    errors,
                    TimeSpan.Zero,
                    ViewerValidationScope.Prim,
                    string.Empty)))
            .Contains("whole stage");
    }

    [Test]
    public async Task AnEmptyRunIsReportedAsCleanRatherThanAsNoRun()
    {
        ViewerValidationSnapshot clean = ViewerValidationSnapshot.Create(
            [
                new("validator", "doc", "usdValidation", [], [], IsSuite: false, IsTimeDependent: false)
            ],
            [],
            TimeSpan.FromMilliseconds(3));

        await Assert.That(ViewerValidationFormatter.FormatState(clean))
            .Contains("0 result(s) from 1 registered validator(s)");
        await Assert.That(ViewerValidationFormatter.FormatDetails(clean))
            .IsEqualTo("No UsdValidation errors were reported.");
        await Assert.That(ViewerValidationFormatter.FormatState(ViewerValidationSnapshot.Empty))
            .IsEqualTo("Open a USD stage to run UsdValidation.");
        await Assert.That(ViewerValidationFormatter.FormatDetails(ViewerValidationSnapshot.Empty))
            .IsEqualTo("No validation results.");
    }

    [Test]
    public async Task TheViewerRunsValidationOnTheSchedulerAndNeverHoldsAPrim()
    {
        string root = FindRepositoryRoot();
        string window = await File.ReadAllTextAsync(
            Path.Combine(root, "src", "OpenUsd.Viewer", "MainWindow.axaml.cs"));

        // The prim must be resolved inside the scheduler callback: a UsdPrim is
        // stage bound and cannot be captured by the UI thread.
        await Assert.That(window).Contains("errors = UsdValidation.Validate(stage.GetPrim(scopePath));");
        await Assert.That(window).Contains("ViewerValidationScope scope = GetSelectedValidationScope();");
        await Assert.That(window).Contains(
            "ValidationScopeSelector.SelectionChanged += OnValidationScopeChanged;");
        await Assert.That(window).DoesNotContain("UsdPrim validationPrim");

        string markup = await File.ReadAllTextAsync(
            Path.Combine(root, "src", "OpenUsd.Viewer", "MainWindow.axaml"));
        await Assert.That(markup).Contains("x:Name=\"ValidationScopeSelector\"");
        await Assert.That(markup).Contains("AutomationProperties.Name=\"UsdValidation scope\"");
    }

    [Test]
    public async Task AValidationRunIsOwnedByTheDocumentGenerationItStartedIn()
    {
        string root = FindRepositoryRoot();
        string window = await File.ReadAllTextAsync(
            Path.Combine(root, "src", "OpenUsd.Viewer", "MainWindow.axaml.cs"));

        // A run that outlives its document must not publish into the next one,
        // and the teardown must wait for it rather than leave it running.
        await Assert.That(window).Contains("int generation = _validationGeneration;");
        await Assert.That(window).Contains(
            "if (IsCurrentValidationGeneration(generation, startingCoordinator))");
        await Assert.That(window).Contains("generation == _validationGeneration &&");
        await Assert.That(window).Contains("ReferenceEquals(_coordinator, startingCoordinator);");
        await Assert.That(window).Contains("private async Task StopValidationAsync()");
        await Assert.That(window).Contains("_validationGeneration++;");
        await Assert.That(window).Contains("await StopValidationAsync();");

        // The teardown invalidates and awaits before the coordinator is
        // disposed, so no run can resume against a disposed scheduler.
        int stopValidation = window.IndexOf("await StopValidationAsync();", StringComparison.Ordinal);
        int disposeCoordinator = window.IndexOf(
            "await _coordinator.DisposeAsync();",
            StringComparison.Ordinal);
        await Assert.That(stopValidation).IsGreaterThan(0);
        await Assert.That(disposeCoordinator).IsGreaterThan(stopValidation)
            .Because("in-flight runs are drained before the coordinator they use is disposed");

        // A UI gesture absorbs every outcome and always re-renders a
        // non-running state, because an async void handler that throws takes
        // the process with it.
        await Assert.That(window).Contains("private async Task RunValidationFromUiAsync(");
        await Assert.That(window).Contains("await RunValidationFromUiAsync(_coordinator, _documentLifetime.Token);");
        int gesture = window.IndexOf(
            "private async Task RunValidationFromUiAsync(",
            StringComparison.Ordinal);
        string gestureBody = window[gesture..window.IndexOf(
            "private async Task RefreshValidationAsync(",
            gesture,
            StringComparison.Ordinal)];
        await Assert.That(gestureBody).Contains("catch (OperationCanceledException)");
        await Assert.That(gestureBody).Contains("catch (Exception exception)");
        await Assert.That(gestureBody)
            .Contains("IsCurrentValidationGeneration(generation, coordinator)")
            .Because("a stale gesture must not re-enable validation during document teardown");
        await Assert.That(gestureBody).Contains("_validationTask is null")
            .Because("a gesture must not clear the busy flag of a document-driven run");
        await Assert.That(gestureBody).Contains("RenderValidation();");

        // Rendering reads the model only; no state is written to a control
        // where a later render would contradict it.
        int render = window.IndexOf("private void RenderValidation()", StringComparison.Ordinal);
        int renderTail = window.IndexOf(
            "ValidationScopeSelector.IsEnabled = RefreshValidationButton.IsEnabled;",
            render,
            StringComparison.Ordinal);
        string renderBody = window[render..(window.IndexOf('}', renderTail) + 1)];
        await Assert.That(renderBody)
            .Contains("ValidationState.Text = ViewerValidationFormatter.FormatState(_validation);");
        await Assert.That(renderBody).DoesNotContain("\"Running UsdValidation...\"")
            .Because("the running state belongs to the snapshot, not to the renderer");
    }

    [Test]
    public async Task OnlyTheCurrentGenerationsResultsArePublished()
    {
        // The guard the Viewer applies, exercised directly: a run that started
        // in an earlier generation, or against a coordinator that has been
        // replaced, must not overwrite the current results.
        ViewerValidationSnapshot current = ViewerValidationSnapshot.Create(
            [],
            [new UsdValidationError(
                UsdValidationSeverity.Error, "validator", "current", "Current", ["/Now"])],
            TimeSpan.Zero);
        ViewerValidationSnapshot stale = ViewerValidationSnapshot.Create(
            [],
            [new UsdValidationError(
                UsdValidationSeverity.Error, "validator", "stale", "Stale", ["/Then"])],
            TimeSpan.Zero);

        object coordinator = new();
        object replacedCoordinator = new();
        int generation = 4;

        static bool isCurrent(int started, object startingCoordinator, int now, object active) =>
            started == now && ReferenceEquals(active, startingCoordinator);

        await Assert.That(isCurrent(generation, coordinator, generation, coordinator)).IsTrue();
        await Assert.That(isCurrent(generation, coordinator, generation + 1, coordinator)).IsFalse()
            .Because("a new document generation invalidates an in-flight run");
        await Assert.That(isCurrent(generation, coordinator, generation, replacedCoordinator))
            .IsFalse()
            .Because("a replaced coordinator invalidates an in-flight run");
        await Assert.That(current).IsNotEqualTo(stale);
    }

    [Test]
    public async Task ResultsWithNoOrUnknownSeverityAreCountedAndStillShowable()
    {
        // UsdValidationSeverity.None exists, and a newer OpenUSD may add a
        // value this build does not know. Counting either as info would
        // mislabel it, and leaving it out of the ranking would make it
        // permanently invisible even when the window has room.
        const UsdValidationSeverity unknown = (UsdValidationSeverity)97;
        UsdValidationError[] errors =
        [
            new(unknown, "validator", "future", "From a newer OpenUSD", ["/World"]),
            new(UsdValidationSeverity.None, "validator", "quiet", "Nothing wrong", ["/World"]),
            new(UsdValidationSeverity.Info, "validator", "note", "Just a note", ["/World"]),
            new(UsdValidationSeverity.Error, "validator", "loud", "Something wrong", ["/World"])
        ];

        ViewerValidationSnapshot snapshot = ViewerValidationSnapshot.Create(
            [],
            errors,
            TimeSpan.Zero);

        await Assert.That(snapshot.UnclassifiedCount).IsEqualTo(2)
            .Because("both the none severity and the unrecognized one are unclassified");
        await Assert.That(snapshot.InfoCount).IsEqualTo(1)
            .Because("a result with no severity is not an informational result");
        await Assert.That(snapshot.ErrorCount + snapshot.WarningCount +
                snapshot.InfoCount + snapshot.UnclassifiedCount)
            .IsEqualTo(snapshot.ReportedCount);

        // Ranked, not dropped: error, info, none, then the unknown value.
        await Assert.That(snapshot.Errors.Length).IsEqualTo(4);
        await Assert.That(snapshot.Errors[0].Severity).IsEqualTo(UsdValidationSeverity.Error);
        await Assert.That(snapshot.Errors[1].Severity).IsEqualTo(UsdValidationSeverity.Info);
        await Assert.That(snapshot.Errors[2].Severity).IsEqualTo(UsdValidationSeverity.None);
        await Assert.That(snapshot.Errors[3].Severity).IsEqualTo(unknown);
        await Assert.That(ViewerValidationSnapshot.Rank(unknown))
            .IsGreaterThan(ViewerValidationSnapshot.Rank(UsdValidationSeverity.None));

        string state = ViewerValidationFormatter.FormatState(snapshot);
        await Assert.That(state).Contains("Unclassified severity: 2.");
        await Assert.That(state).Contains("info: 1");
        string details = ViewerValidationFormatter.FormatDetails(snapshot);
        await Assert.That(details).Contains("Nothing wrong");
        await Assert.That(details).Contains("From a newer OpenUSD");
    }

    [Test]
    public async Task EveryNonResultStateIsPartOfTheSnapshotRatherThanALooseLabel()
    {
        ViewerValidationSnapshot running =
            ViewerValidationSnapshot.Running(ViewerValidationScope.Prim, "/World/Cube");
        ViewerValidationSnapshot noSelection = ViewerValidationSnapshot.NoSelection();
        ViewerValidationSnapshot cancelled =
            ViewerValidationSnapshot.Cancelled(ViewerValidationScope.Stage, string.Empty);
        ViewerValidationSnapshot failed = ViewerValidationSnapshot.Failed(
            ViewerValidationScope.Prim,
            "/World/Cube",
            "Prim '/World/Cube' no longer exists.",
            "System.InvalidOperationException: Prim '/World/Cube' no longer exists.");

        await Assert.That(running.RunState).IsEqualTo(ViewerValidationRunState.Running);
        await Assert.That(ViewerValidationFormatter.FormatState(running))
            .IsEqualTo("Running UsdValidation (prim /World/Cube)...");
        await Assert.That(ViewerValidationFormatter.FormatDetails(running))
            .IsEqualTo("Running UsdValidation...");

        await Assert.That(noSelection.RunState).IsEqualTo(ViewerValidationRunState.NoSelection);
        await Assert.That(ViewerValidationFormatter.FormatState(noSelection))
            .IsEqualTo("Select a prim to validate, or switch the scope to the stage.");

        await Assert.That(cancelled.RunState).IsEqualTo(ViewerValidationRunState.Cancelled);
        await Assert.That(ViewerValidationFormatter.FormatState(cancelled))
            .IsEqualTo("UsdValidation (whole stage) was cancelled.");
        await Assert.That(ViewerValidationFormatter.FormatDetails(cancelled))
            .IsEqualTo("No validation results.");

        await Assert.That(failed.RunState).IsEqualTo(ViewerValidationRunState.Failed);
        await Assert.That(ViewerValidationFormatter.FormatState(failed))
            .IsEqualTo("UsdValidation (prim /World/Cube) failed: Prim '/World/Cube' no longer exists.");
        await Assert.That(ViewerValidationFormatter.FormatDetails(failed))
            .Contains("System.InvalidOperationException");

        // A failure with no reported reason still names one, and each state is
        // a distinct value rather than the same empty snapshot.
        await Assert.That(
                ViewerValidationSnapshot.Failed(ViewerValidationScope.Stage, "", "  ").Message)
            .IsEqualTo("no reason was reported");
        ViewerValidationSnapshot[] states =
            [ViewerValidationSnapshot.Empty, running, noSelection, cancelled, failed];
        for (int left = 0; left < states.Length; left++)
        {
            for (int right = left + 1; right < states.Length; right++)
            {
                await Assert.That(states[left]).IsNotEqualTo(states[right]);
            }
        }
    }

    [Test]
    public async Task PrimScopedValidationNarrowsTheResultsOnARealStage()
    {
        // The Viewer's prim scope calls UsdValidation.Validate(prim), which is
        // a different native entry point from the stage one. This proves the
        // two really differ on a stage that reports a stage-level error, so
        // the scope selector is not a label over identical results.
        string directory = Path.Combine(
            Path.GetDirectoryName(typeof(ViewerValidationModelTests).Assembly.Location)!,
            "viewer-validation-tests",
            $"{nameof(PrimScopedValidationNarrowsTheResultsOnARealStage)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "validation-scope.usda");
        try
        {
            using UsdStage stage = CreateStageOrSkip(path);
            stage.DefinePrim("/World", "Xform");
            stage.DefinePrim("/World/Cube", "Cube");

            IReadOnlyList<UsdValidationValidatorInfo> validators =
                UsdValidation.GetRegisteredValidators();
            IReadOnlyList<UsdValidationError> stageErrors = UsdValidation.Validate(stage);
            IReadOnlyList<UsdValidationError> primErrors =
                UsdValidation.Validate(stage.GetPrim("/World/Cube"));

            ViewerValidationSnapshot stageSnapshot = ViewerValidationSnapshot.Create(
                validators,
                stageErrors,
                TimeSpan.FromMilliseconds(1));
            ViewerValidationSnapshot primSnapshot = ViewerValidationSnapshot.Create(
                validators,
                primErrors,
                TimeSpan.FromMilliseconds(1),
                ViewerValidationScope.Prim,
                "/World/Cube");

            await Assert.That(stageSnapshot.ReportedCount).IsGreaterThan(0)
                .Because("a stage with no default prim reports at least one stage-level error");
            await Assert.That(primSnapshot.ReportedCount)
                .IsLessThan(stageSnapshot.ReportedCount)
                .Because("prim validators cannot report the stage-level errors");
            await Assert.That(ViewerValidationFormatter.FormatState(primSnapshot))
                .Contains("prim /World/Cube");
            await Assert.That(stageSnapshot.Errors.Any(
                    error => error.Message.Contains("defaultPrim", StringComparison.OrdinalIgnoreCase)))
                .IsTrue();
            await Assert.That(primSnapshot.Errors.Any(
                    error => error.Message.Contains("defaultPrim", StringComparison.OrdinalIgnoreCase)))
                .IsFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static UsdStage CreateStageOrSkip(string path)
    {
        try
        {
            return UsdStage.Create(path);
        }
        catch (DllNotFoundException exception)
        {
            Skip.Test($"openusd_dotnet native runtime is unavailable: {exception.Message}");
            throw;
        }
    }

    private static string FindRepositoryRoot()
    {
        string currentDirectory = Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(currentDirectory, "OpenUsd.slnx")))
        {
            return currentDirectory;
        }

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the OpenUSD repository root.");
    }
}
