// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerAuthoredEditControllerTests
{
    [Test]
    public async Task AccountedMemoryEvictsOldReachWithoutLosingTheLatestReviewState()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "authored-controller", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path, "#usda 1.0\ndef Xform \"Body\" {}\n");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            var address = new UsdLayerEditAddress("/Body.review:labels", UsdLayerEditField.Default);
            string[] values = Enumerable.Repeat(new string('x', 2048), 128).ToArray();
            for (int index = 0; index < 24; index++)
            {
                values[0] = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                ViewerAuthoredEditCapture before = await editor.CaptureAsync([address]);
                await editor.ApplyAsync(before,
                    [UsdLayerEdit.Set(address, UsdLayerEditValue.FromStringArray(values), "string[]")],
                    "Labels", Guid.NewGuid());
            }

            await Assert.That(editor.RetainedBytes).IsLessThanOrEqualTo(32L * 1024 * 1024);
            await Assert.That(editor.UndoDepth).IsGreaterThan(0);
            await Assert.That(editor.UndoDepth).IsLessThan(24);
            ViewerAuthoredEditCapture latest = await editor.CaptureAsync([address]);
            await Assert.That(latest.Snapshot.Opinions[0].Value.AsStringArray()[0]).IsEqualTo("23");
            ViewerAuthoredEditResult undone = await editor.UndoAsync();
            await Assert.That(undone.After!.Opinions[0].Value.AsStringArray()[0]).IsEqualTo("22");
            ViewerAuthoredEditResult redone = await editor.RedoAsync();
            await Assert.That(redone.After!.Opinions[0].Value.AsStringArray()[0]).IsEqualTo("23");
            await Assert.That(editor.RetainedBytes).IsLessThanOrEqualTo(32L * 1024 * 1024);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task HistoryAccountsForNativePacketsAndRetainedValueStorage()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "authored-controller", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path, "#usda 1.0\ndef Xform \"Body\" {}\n");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            var address = new UsdLayerEditAddress("/Body.review:text", UsdLayerEditField.Default);
            ViewerAuthoredEditCapture before = await editor.CaptureAsync([address]);
            ViewerAuthoredEditResult applied = await editor.ApplyAsync(before,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromString(new string('x', 4096)), "string")], "Text");

            long packetBytes = before.Snapshot.ByteLength + (long)applied.After!.ByteLength;
            await Assert.That(editor.RetainedBytes).IsGreaterThan(packetBytes + 4096)
                .Because("the snapshot retains its packet, a separate value packet, and declaration DTO storage");
            long retained = editor.RetainedBytes;
            await editor.UndoAsync();
            await Assert.That(editor.RetainedBytes).IsEqualTo(retained);
            await Assert.That(editor.RedoDepth).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task LateSimulationStartStopAndRestartPreserveReviewIdentityAndSharedHistory()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "authored-controller", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        const string source = "#usda 1.0\ndef Xform \"Body\" {\n custom double review:value = 7\n}\n";
        await File.WriteAllTextAsync(path, source);
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
            ViewerAuthoredEditCapture first = await editor.CaptureAsync([address]);
            ViewerAuthoredEditResult applied = await editor.ApplyAsync(first,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(4), "double")], "Before preview");
            UsdSessionOverlay? overlay = null;
            try
            {
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    await scheduler.InvokeAsync(stage => { overlay = stage.NormalizeSessionOverlay(); });
                    await Assert.That(overlay!.UserLayerIdentifier).IsEqualTo(first.LayerIdentifier);
                    ViewerAuthoredEditCapture active = await editor.CaptureAsync([address]);
                    await Assert.That(active.Snapshot.Identity).IsEqualTo(first.Snapshot.Identity);
                    await Assert.That(active.Snapshot.HasSamePayload(applied.After)).IsTrue();
                    await Assert.That(editor.UndoDepth).IsEqualTo(1);

                    ViewerAuthoredEditResult undone = await editor.UndoAsync();
                    await Assert.That(undone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
                    await Assert.That(undone.After!.Opinions[0].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
                    await Assert.That(editor.UndoDepth).IsEqualTo(0);
                    await Assert.That(editor.RedoDepth).IsEqualTo(1);
                    await Assert.That(await scheduler.InvokeAsync(
                        static stage => stage.GetPrim("/Body").GetDouble("review:value"))).IsEqualTo(7d);

                    applied = await editor.RedoAsync();
                    await Assert.That(applied.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
                    await Assert.That(applied.After!.Identity).IsEqualTo(first.Snapshot.Identity);
                    await Assert.That(applied.After.Opinions[0].Value.AsDouble()).IsEqualTo(4d);
                    await scheduler.InvokeAsync(_ => overlay.Dispose());
                    overlay = null;
                    ViewerAuthoredEditCapture stopped = await editor.CaptureAsync([address]);
                    await Assert.That(stopped.Snapshot.Identity).IsEqualTo(first.Snapshot.Identity);
                    await Assert.That(stopped.Snapshot.HasSamePayload(applied.After)).IsTrue();
                }
                await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(source);
            }
            finally
            {
                await scheduler.InvokeAsync(_ => overlay?.Dispose());
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task UnsafeContainerMetadataRefusesNormalizationWithoutChangingTheReviewOrUndo()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "authored-controller", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path,
            "#usda 1.0\ndef Xform \"Body\" {\n custom double review:value = 7\n}\n");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
            ViewerAuthoredEditCapture before = await editor.CaptureAsync([address]);
            ViewerAuthoredEditResult applied = await editor.ApplyAsync(before,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(4), "double")], "Review");
            await scheduler.EditAsync(stage =>
            {
                using UsdLayer container = stage.GetSessionLayer();
                container.SetMetadata("comment", "External container opinion");
            }, UsdStageInvalidationKind.Composition);
            await Assert.That(async () =>
            {
                await scheduler.InvokeAsync(stage =>
                {
                    using UsdSessionOverlay rejected = stage.NormalizeSessionOverlay();
                });
            }).Throws<OpenUsd.Interop.OpenUsdNativeException>();

            ViewerAuthoredEditCapture unchanged = await editor.CaptureAsync([address]);
            await Assert.That(unchanged.LayerIdentifier).IsEqualTo(before.LayerIdentifier);
            await Assert.That(unchanged.Snapshot.HasSamePayload(applied.After)).IsTrue();
            await Assert.That(editor.UndoDepth).IsEqualTo(1);
            await Assert.That((await editor.UndoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(await scheduler.InvokeAsync(
                static stage => stage.GetPrim("/Body").GetDouble("review:value"))).IsEqualTo(7d);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ExplicitGesturesCoalesceAcrossPausesButNotUnrelatedRevisionChanges()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "authored-controller", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path, "#usda 1.0\ndef Xform \"Body\" {}\n");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
            var foreign = new UsdLayerEditAddress("/Body.review:foreign", UsdLayerEditField.Default);
            Guid gesture = Guid.NewGuid();
            ViewerAuthoredEditCapture first = await editor.CaptureAsync([address]);
            await editor.ApplyAsync(first,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(1), "double")], "Drag", gesture);
            await Task.Delay(650);
            ViewerAuthoredEditCapture second = await editor.CaptureAsync([address]);
            await editor.ApplyAsync(second,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(2))], "Drag", gesture);
            await Assert.That(editor.UndoDepth).IsEqualTo(1);
            await scheduler.EditAsync(stage =>
            {
                using UsdLayer review = stage.GetUserReviewLayer();
                return review.CompareAndApply(review.CaptureAuthored([foreign]),
                    [UsdLayerEdit.Set(foreign, UsdLayerEditValue.FromDouble(42), "double")]);
            }, UsdStageInvalidationKind.Property);
            ViewerAuthoredEditCapture third = await editor.CaptureAsync([address]);
            await editor.ApplyAsync(third,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(3))], "Drag", gesture);
            await Assert.That(editor.UndoDepth).IsEqualTo(2);
            ViewerAuthoredEditCapture fourth = await editor.CaptureAsync([address]);
            await editor.ApplyAsync(fourth,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(4))], "Next gesture", Guid.NewGuid());
            await Assert.That(editor.UndoDepth).IsEqualTo(3);
            await Assert.That((await editor.UndoAsync()).After!.Opinions[0].Value.AsDouble()).IsEqualTo(3d);
            await Assert.That((await editor.UndoAsync()).After!.Opinions[0].Value.AsDouble()).IsEqualTo(2d);
            await Assert.That((await editor.UndoAsync()).After!.Opinions[0].PropertyKind)
                .IsEqualTo(UsdLayerPropertyKind.Absent);
            await Assert.That((await editor.CaptureAsync([foreign])).Snapshot.Opinions[0].Value.AsDouble())
                .IsEqualTo(42d);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ARejectedTypedBatchAndPreInvocationCancellationLeaveAuthoredStateAndHistoryUntouched()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "authored-controller", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path, "#usda 1.0\n");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            UsdLayerEditAddress[] addresses =
            [
                new("/Body.review:value", UsdLayerEditField.Default),
                new("/Body.review:flag", UsdLayerEditField.Default)
            ];
            ViewerAuthoredEditCapture before = await editor.CaptureAsync(addresses);
            await Assert.That(async () =>
            {
                await editor.ApplyAsync(before,
                [
                    UsdLayerEdit.Set(addresses[0], UsdLayerEditValue.FromDouble(1), "double"),
                    UsdLayerEdit.Set(addresses[1], UsdLayerEditValue.FromDouble(2), "bool")
                ], "Invalid batch");
            }).Throws<OpenUsd.Interop.OpenUsdNativeException>();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.That(async () =>
            {
                await editor.ApplyAsync(before,
                [
                    UsdLayerEdit.Set(addresses[0], UsdLayerEditValue.FromDouble(1), "double"),
                    UsdLayerEdit.Set(addresses[1], UsdLayerEditValue.FromBoolean(true), "bool")
                ], "Cancelled batch", cancellationToken: cancellation.Token);
            }).Throws<OperationCanceledException>();

            await Assert.That(editor.UndoDepth).IsEqualTo(0);
            ViewerAuthoredEditCapture after = await editor.CaptureAsync(addresses);
            await Assert.That(after.Snapshot.Opinions.All(
                static opinion => opinion.PropertyKind == UsdLayerPropertyKind.Absent)).IsTrue();
            await Assert.That(await scheduler.InvokeAsync(static stage => stage.HasPrim("/Body"))).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ExternalCheckpointRestoreInvalidatesHistoryWithoutReplayingIntoTheNewGeneration()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "authored-controller", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path, "#usda 1.0\n");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
            ViewerAuthoredEditCapture before = await editor.CaptureAsync([address]);
            UsdLayerCheckpoint empty = await editor.CaptureReviewCheckpointAsync();
            await editor.ApplyAsync(before,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(1), "double")], "Value");
            UsdLayerCheckpointRestoreResult restored = await scheduler.EditAsync(stage =>
            {
                using UsdLayer review = stage.GetUserReviewLayer();
                return review.RestoreCheckpoint(review.CaptureCheckpoint(), empty);
            }, UsdStageInvalidationKind.Full);
            await Assert.That(restored.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);

            ViewerAuthoredEditResult stale = await editor.UndoAsync();
            await Assert.That(stale.Outcome).IsEqualTo(UsdLayerEditOutcome.StaleTarget);
            await Assert.That(editor.UndoDepth).IsEqualTo(1);
            await Assert.That(editor.RedoDepth).IsEqualTo(0);
            await Assert.That((await editor.CaptureAsync([address])).Snapshot.Opinions[0].PropertyKind)
                .IsEqualTo(UsdLayerPropertyKind.Absent);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SettingAnUnchangedOpinionAfterADisjointRevisionPreservesPendingRedo()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "authored-controller", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path, "#usda 1.0\ndef Xform \"Body\" {}\n");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
            var foreign = new UsdLayerEditAddress("/Body.review:foreign", UsdLayerEditField.Default);
            await scheduler.EditAsync(stage =>
            {
                using UsdLayer review = stage.GetUserReviewLayer();
                return review.CompareAndApply(review.CaptureAuthored([address, foreign]),
                [
                    UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(2), "double"),
                    UsdLayerEdit.Set(foreign, UsdLayerEditValue.FromDouble(3), "double")
                ]);
            }, UsdStageInvalidationKind.Property);
            ViewerAuthoredEditCapture initial = await editor.CaptureAsync([address]);
            await editor.ApplyAsync(initial,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(8))], "Value");
            await editor.UndoAsync();
            long retained = editor.RetainedBytes;

            ViewerAuthoredEditCapture captured = await editor.CaptureAsync([address]);
            UsdLayerEditResult external = await scheduler.EditAsync(stage =>
            {
                using UsdLayer review = stage.GetUserReviewLayer();
                return review.CompareAndApply(review.CaptureAuthored([foreign]),
                    [UsdLayerEdit.Set(foreign, UsdLayerEditValue.FromDouble(13))]);
            }, UsdStageInvalidationKind.Property);
            await Assert.That(external.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            ViewerAuthoredEditResult noChange = await editor.ApplyAsync(
                captured, [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(2))], "Unchanged value");

            await Assert.That(noChange.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(noChange.After!.Revision).IsGreaterThan(captured.Snapshot.Revision);
            await Assert.That(noChange.After.Opinions[0].Value.AsDouble()).IsEqualTo(2d);
            await Assert.That(editor.UndoDepth).IsEqualTo(0);
            await Assert.That(editor.RedoDepth).IsEqualTo(1);
            await Assert.That(editor.RetainedBytes).IsEqualTo(retained);
            await Assert.That((await editor.RedoAsync()).After!.Opinions[0].Value.AsDouble()).IsEqualTo(8d);
            await Assert.That((await editor.CaptureAsync([foreign])).Snapshot.Opinions[0].Value.AsDouble())
                .IsEqualTo(13d);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ClearingAnAlreadyAbsentOpinionDoesNotEraseRedoOrCreateAnUndoStep()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "authored-controller", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path, "#usda 1.0\ndef Xform \"Body\" {}\n");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
            var foreign = new UsdLayerEditAddress("/Body.review:foreign", UsdLayerEditField.Default);
            await scheduler.EditAsync(stage =>
            {
                using UsdLayer review = stage.GetUserReviewLayer();
                return review.CompareAndApply(review.CaptureAuthored([foreign]),
                    [UsdLayerEdit.Set(foreign, UsdLayerEditValue.FromDouble(3), "double")]);
            }, UsdStageInvalidationKind.Property);
            ViewerAuthoredEditCapture initial = await editor.CaptureAsync([address]);
            await editor.ApplyAsync(initial,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(8), "double")], "Value");
            await editor.UndoAsync();

            ViewerAuthoredEditCapture absent = await editor.CaptureAsync([address]);
            await scheduler.EditAsync(stage =>
            {
                using UsdLayer review = stage.GetUserReviewLayer();
                return review.CompareAndApply(review.CaptureAuthored([foreign]),
                    [UsdLayerEdit.Set(foreign, UsdLayerEditValue.FromDouble(13))]);
            }, UsdStageInvalidationKind.Property);
            ViewerAuthoredEditResult noChange = await editor.ApplyAsync(
                absent, [UsdLayerEdit.Clear(address)], "Clear absent");

            await Assert.That(noChange.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(noChange.After!.Revision).IsGreaterThan(absent.Snapshot.Revision);
            await Assert.That(noChange.After.Opinions[0].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
            await Assert.That(editor.UndoDepth).IsEqualTo(0);
            await Assert.That(editor.RedoDepth).IsEqualTo(1);
            await Assert.That((await editor.RedoAsync()).After!.Opinions[0].Value.AsDouble()).IsEqualTo(8d);
            await Assert.That((await editor.CaptureAsync([foreign])).Snapshot.Opinions[0].Value.AsDouble())
                .IsEqualTo(13d);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task FocusedPropertyCaptureUsesTheLocalDeclarationButNeverItsWeakerValueAsUndoState()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "authored-controller", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path,
            "#usda 1.0\ndef Xform \"Body\" {\n custom double3 xformOp:translate = (1, 2, 3)\n}\n");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            ViewerPropertyEditCapture property = await editor.CapturePropertyAsync(
                "/Body", "xformOp:translate", UsdLayerEditField.Default, 0);
            await Assert.That(property.TypeName).IsEqualTo("double3");
            await Assert.That(property.Custom).IsTrue();
            await Assert.That(property.Capture.Snapshot.Opinions[0].PropertyKind)
                .IsEqualTo(UsdLayerPropertyKind.Absent);
            UsdLayerEditAddress address = property.Capture.Snapshot.Addresses[0];
            ViewerAuthoredEditResult applied = await editor.ApplyAsync(property.Capture,
            [
                UsdLayerEdit.Set(address, UsdLayerEditValue.FromVec3d(new UsdVec3d(4, 5, 6)),
                    property.TypeName, property.Variability, property.Custom)
            ], "Translate");
            await Assert.That(applied.After!.Opinions[0].Value.AsVec3d()).IsEqualTo(new UsdVec3d(4, 5, 6));
            await Assert.That((await editor.UndoAsync()).After!.Opinions[0].PropertyKind)
                .IsEqualTo(UsdLayerPropertyKind.Absent);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task PhysicsRejectsAnOverflowingVectorBeforeAuthoringAnyPartOfTheBatch()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "authored-controller", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path, "#usda 1.0\ndef Xform \"Body\" {}\n");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            var physics = new ViewerPhysicsDocumentAuthoringStage(editor);
            ViewerPhysicsAuthoringResult result = await physics.ApplyAsync(new ViewerPhysicsEditStep("Invalid batch",
            [
                new("/Body", "review:value", "Value",
                    ViewerPhysicsValue.Unauthored(ViewerPhysicsValueKind.Number),
                    ViewerPhysicsValue.FromNumber(4)),
                new("/Body", "review:vector", "Vector",
                    ViewerPhysicsValue.Unauthored(ViewerPhysicsValueKind.Vector3),
                    ViewerPhysicsValue.FromVector(new ViewerPhysicsVector3(double.MaxValue, 0, 0)))
            ]), CancellationToken.None);

            await Assert.That(result.Applied).IsEqualTo(0);
            await Assert.That(result.Rejected).IsEqualTo(2);
            await Assert.That(result.Message).Contains("finite");
            await Assert.That(editor.UndoDepth).IsEqualTo(0);
            ViewerAuthoredEditCapture actual = await editor.CaptureAsync(
            [
                new("/Body.review:value", UsdLayerEditField.Default),
                new("/Body.review:vector", UsdLayerEditField.Default)
            ]);
            await Assert.That(actual.Snapshot.Opinions.All(
                static opinion => opinion.PropertyKind == UsdLayerPropertyKind.Absent)).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ReviewUndoRestoresTargetAbsenceInsteadOfAuthoringTheWeakerRootValue()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "authored-controller", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        const string source = """
            #usda 1.0
            def Xform "Body"
            {
                custom double review:value = 7
            }
            """;
        await File.WriteAllTextAsync(path, source);
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
            ViewerAuthoredEditCapture capture = await editor.CaptureAsync([address]);
            await Assert.That(capture.Snapshot.Opinions[0].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);

            ViewerAuthoredEditResult applied = await editor.ApplyAsync(
                capture, [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(9), "double", creationCustom: true)],
                "Adjust review value");

            await Assert.That(applied.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(editor.CanUndo).IsTrue();
            ViewerAuthoredEditResult undone = await editor.UndoAsync();
            await Assert.That(undone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(undone.After!.Opinions[0].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
            await Assert.That(editor.CanRedo).IsTrue();
            ViewerAuthoredEditResult redone = await editor.RedoAsync();
            await Assert.That(redone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(redone.After!.Opinions[0].Value.AsDouble()).IsEqualTo(9d);
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(source);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task UndoPreservesDisjointExternalOpinionsAndRefusesAConflictingOpinion()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "authored-controller", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path, "#usda 1.0\n");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            var edited = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
            var foreign = new UsdLayerEditAddress("/Body.review:foreign", UsdLayerEditField.Default);
            ViewerAuthoredEditCapture first = await editor.CaptureAsync([edited]);
            await editor.ApplyAsync(first,
                [UsdLayerEdit.Set(edited, UsdLayerEditValue.FromDouble(9), "double")], "Change");
            await scheduler.EditAsync(stage =>
            {
                using UsdLayer layer = stage.GetUserReviewLayer();
                UsdLayerAuthoredSnapshot before = layer.CaptureAuthored([foreign]);
                return layer.CompareAndApply(before,
                    [UsdLayerEdit.Set(foreign, UsdLayerEditValue.FromDouble(42), "double")]);
            }, UsdStageInvalidationKind.Property);

            ViewerAuthoredEditResult undo = await editor.UndoAsync();
            await Assert.That(undo.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            UsdLayerAuthoredSnapshot disjoint = await scheduler.InvokeAsync(stage =>
            {
                using UsdLayer layer = stage.GetUserReviewLayer();
                return layer.CaptureAuthored([foreign]);
            });
            await Assert.That(disjoint.Opinions[0].Value.AsDouble()).IsEqualTo(42d);
            await editor.RedoAsync();
            await scheduler.EditAsync(stage =>
            {
                using UsdLayer layer = stage.GetUserReviewLayer();
                return layer.CompareAndApply(layer.CaptureAuthored([edited]),
                    [UsdLayerEdit.Set(edited, UsdLayerEditValue.FromDouble(11))]);
            }, UsdStageInvalidationKind.Property);

            ViewerAuthoredEditResult conflict = await editor.UndoAsync();
            await Assert.That(conflict.Outcome).IsEqualTo(UsdLayerEditOutcome.Conflict);
            await Assert.That(editor.UndoDepth).IsEqualTo(1);
            await Assert.That(editor.RedoDepth).IsEqualTo(0);
            UsdLayerAuthoredSnapshot current = await scheduler.InvokeAsync(stage =>
            {
                using UsdLayer layer = stage.GetUserReviewLayer();
                return layer.CaptureAuthored([edited]);
            });
            await Assert.That(current.Opinions[0].Value.AsDouble()).IsEqualTo(11d);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task OneStepPreservesBlocksSamplesAndListOperationsThroughUndoAndRedo()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "authored-controller", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path, "#usda 1.0\n");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            UsdLayerEditAddress[] addresses =
            [
                new("/Body.review:value", UsdLayerEditField.Default),
                new("/Body.review:value", UsdLayerEditField.TimeSample, 2),
                new("/Body.links", UsdLayerEditField.RelationshipTargets)
            ];
            ViewerAuthoredEditCapture capture = await editor.CaptureAsync(addresses);
            ViewerAuthoredEditResult applied = await editor.ApplyAsync(capture,
            [
                UsdLayerEdit.Block(addresses[0], "double", creationCustom: true),
                UsdLayerEdit.Set(addresses[1], UsdLayerEditValue.FromDouble(12), "double", creationCustom: true),
                UsdLayerEdit.Set(addresses[2],
                    UsdLayerEditValue.FromPathList(new UsdPathListEdit(false, prependedItems: ["/Target"])))
            ], "Review opinions");

            await Assert.That(applied.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(applied.After!.Opinions[0].Value.Kind).IsEqualTo(UsdLayerEditValueKind.Block);
            await Assert.That(applied.After.Opinions[1].Value.AsDouble()).IsEqualTo(12d);
            await Assert.That(applied.After.Opinions[2].Value.AsPathList().PrependedItems.Single())
                .IsEqualTo("/Target");
            ViewerAuthoredEditResult undone = await editor.UndoAsync();
            await Assert.That(undone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(undone.After!.Opinions.All(
                static opinion => opinion.PropertyKind == UsdLayerPropertyKind.Absent)).IsTrue();
            ViewerAuthoredEditResult redone = await editor.RedoAsync();
            await Assert.That(redone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(redone.After!.Opinions[0].Value.Kind).IsEqualTo(UsdLayerEditValueKind.Block);
            await Assert.That(redone.After.Opinions[1].Value.AsDouble()).IsEqualTo(12d);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task PhysicsAndOrdinaryReviewEditsShareOneChronologicalHistory()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "authored-controller", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path, "#usda 1.0\ndef Xform \"Body\" {}\n");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            var physics = new ViewerPhysicsDocumentAuthoringStage(editor);
            var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
            ViewerAuthoredEditCapture captured = await editor.CaptureAsync([address]);
            await editor.ApplyAsync(captured,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(4), "double")], "Ordinary edit");

            ViewerPhysicsAuthoringResult applied = await physics.ApplyAsync(new ViewerPhysicsEditStep("Physics edit",
            [
                new ViewerPhysicsEdit("/Body", "openUsdPhysics:body:sleepThreshold", "Sleep threshold",
                    ViewerPhysicsValue.FromNumber(999), ViewerPhysicsValue.FromNumber(0.5))
            ]), CancellationToken.None);

            await Assert.That(applied.Succeeded).IsTrue();
            await Assert.That(editor.UndoDepth).IsEqualTo(2);
            await Assert.That(physics.DocumentEditor).IsSameReferenceAs(editor);
            ViewerAuthoredEditResult undoPhysics = await editor.UndoAsync();
            await Assert.That(undoPhysics.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(undoPhysics.After!.Opinions[0].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent)
                .Because("the legacy supplied Before value must not replace native target-local state");
            ViewerAuthoredEditResult undoOrdinary = await editor.UndoAsync();
            await Assert.That(undoOrdinary.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(undoOrdinary.After!.Addresses[0].Path).IsEqualTo("/Body.review:value");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SharedPhysicsControllerUsesOnlyTheNormalizedReviewAndPreservesSimulationAndEditTarget()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "authored-controller", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path, "#usda 1.0\ndef Xform \"Body\" {}\n");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            UsdSessionOverlay? overlay = null;
            try
            {
                await scheduler.InvokeAsync(stage =>
                {
                    overlay = stage.NormalizeSessionOverlay();
                    using UsdLayer simulation = stage.GetLocalLayer(overlay.PhysicsLayerIdentifier);
                    stage.SetEditTarget(simulation);
                    stage.GetPrim("/Body").SetDouble("review:value", 99);
                    stage.SetEditTargetToRootLayer();
                });
                await using var editor = new ViewerAuthoredEditController(scheduler);
                await using var physics = new ViewerPhysicsController(
                    new ViewerPhysicsTransportFactory(scheduler), ViewerPhysicsStopwatchClock.Instance,
                    ViewerPhysicsRenderCapacities.Default, authoring: new ViewerPhysicsDocumentAuthoringStage(editor));
                ViewerPhysicsAuthoringResult applied = await physics.ApplyEditAsync(
                    new ViewerPhysicsEditStep("Physics",
                [
                    new("/Body", "review:value", "Value",
                        ViewerPhysicsValue.FromNumber(777), ViewerPhysicsValue.FromNumber(4))
                ]));
                await Assert.That(applied.Succeeded).IsTrue();
                await Assert.That(physics.History).IsSameReferenceAs(editor);
                await Assert.That(editor.UndoDepth).IsEqualTo(1);
                await Assert.That((await physics.UndoAsync()).Succeeded).IsTrue();
                await Assert.That(editor.RedoDepth).IsEqualTo(1);
                await Assert.That((await physics.RedoAsync()).Succeeded).IsTrue();

                var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
                ViewerAuthoredEditCapture review = await editor.CaptureAsync([address]);
                await Assert.That(review.LayerIdentifier).IsEqualTo(overlay!.UserLayerIdentifier);
                await Assert.That(review.Snapshot.Opinions[0].Value.AsDouble()).IsEqualTo(4d);
                double composed = await scheduler.InvokeAsync(stage =>
                {
                    if (stage.EditTargetLayerIdentifier != stage.RootLayerIdentifier)
                    {
                        throw new InvalidOperationException("Review history changed the stage edit target.");
                    }
                    return stage.GetPrim("/Body").GetDouble("review:value");
                });
                await Assert.That(composed).IsEqualTo(99d);
            }
            finally
            {
                await scheduler.InvokeAsync(_ => overlay?.Dispose());
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
