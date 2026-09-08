// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUsd.Geom;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerRenderSequenceNativeTests
{
    private static async Task ExerciseCancellationAsync(
        MainWindow window, ViewerStageSession session, SequenceFramePicker picker, string root, string outputParent)
    {
        WindowClosingBehavior closingBehavior = window.ClosingBehavior;
        TextBox time = Required<TextBox>(window, "CurrentTimeInput");
        time.Text = "2";
        time.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        await WaitUntilAsync(() => session.CurrentRenderState.Time.TimeCode == 2);
        StageRenderState before = session.CurrentRenderState;
        string beforeImage = Path.Combine(root, "before-cancel.png");
        await CaptureStillAsync(window, session, picker, beforeImage);
        Window sequence = await OpenSequenceAsync(window, outputParent, includeData: true);
        TextBlock status = Required<TextBlock>(sequence, "SequenceStatus");
        bool cancelled = false;
        bool colorControlsPaused = false;
        void cancelOnSecondFrame(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (!cancelled && args.Property == TextBlock.TextProperty &&
                status.Text?.StartsWith("Rendering: 1/3", StringComparison.Ordinal) == true)
            {
                cancelled = true;
                colorControlsPaused =
                    !Required<MenuItem>(window, "RenderColorManagementEnabledMenuItem").IsEnabled &&
                    !Required<MenuItem>(window, "RenderColorManagementChooseConfigMenuItem").IsEnabled &&
                    !Required<MenuItem>(window, "RenderColorManagementClearConfigMenuItem").IsEnabled;
                Required<Button>(sequence, "SequenceCancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }
        status.PropertyChanged += cancelOnSecondFrame;
        try
        {
            Required<Button>(sequence, "SequenceRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => !((RenderImageSequenceWindow)sequence).IsRunning);
            await Assert.That(cancelled).IsTrue();
            await Assert.That(colorControlsPaused).IsTrue();
            await Assert.That(status.Text).StartsWith("Cancelled.");
            await Assert.That(Required<TextBox>(sequence, "SequenceOutputLocation").Text).IsEqualTo(string.Empty);
            await Assert.That(Directory.GetDirectories(outputParent).Length).IsEqualTo(1);
            await AssertRestoredAsync(session, before);
            await Assert.That(window.ClosingBehavior).IsEqualTo(closingBehavior);
            await Assert.That(Required<Button>(window, "PlayPauseButton").Content).IsEqualTo("_Play");
        }
        finally
        {
            status.PropertyChanged -= cancelOnSecondFrame;
            await ((RenderImageSequenceWindow)sequence).CloseAsync();
        }
        string afterImage = Path.Combine(root, "after-cancel.png");
        await CaptureStillAsync(window, session, picker, afterImage);
        ViewerCapturePair pair = await ViewerCaptureComparison.LoadAsync(
            beforeImage, afterImage, CancellationToken.None);
        await Assert.That(pair.Before.Rgba.SequenceEqual(pair.After.Rgba)).IsTrue()
            .Because("restoration must reach the actual renderer, not just the Viewer's state fields");
    }

    private static async Task ExercisePendingReloadAsync(
        MainWindow window, ViewerStageSession original, string outputParent, Task<ViewerStageSession> reopened)
    {
        ViewportDimensions viewport = original.CurrentRenderState.Viewport;
        Window sequence = await OpenSequenceAsync(window, outputParent, includeData: true);
        TextBlock status = Required<TextBlock>(sequence, "SequenceStatus");
        bool requested = false;
        bool reloadEnabled = false;
        Task mutation = Task.CompletedTask;
        void reloadOnSecondFrame(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (!requested && args.Property == TextBlock.TextProperty &&
                status.Text?.StartsWith("Rendering: 1/3", StringComparison.Ordinal) == true)
            {
                requested = true;
                mutation = original.Scheduler.EditAsync(static stage =>
                {
                    stage.SetEditTargetToSessionLayer();
                    _ = stage.DefinePrim("/World/LateBeforeReload", "Xform");
                }, UsdStageInvalidationKind.Composition).AsTask();
                MenuItem reload = Required<MenuItem>(window, "ReloadStageMenuItem");
                reloadEnabled = reload.IsEnabled;
                reload.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
        }
        status.PropertyChanged += reloadOnSecondFrame;
        try
        {
            Required<Button>(sequence, "SequenceRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => reopened.IsCompleted ||
                window.OwnedWindows.OfType<DocumentChangesWindow>().Any());
            if (window.OwnedWindows.OfType<DocumentChangesWindow>().FirstOrDefault() is { } changes)
            {
                Required<Button>(changes, "DiscardDocumentChangesButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            ViewerStageSession current = await reopened.WaitAsync(TimeSpan.FromSeconds(30));
            await mutation;
            await Assert.That(requested).IsTrue();
            await Assert.That(reloadEnabled).IsTrue()
                .Because("Reload must be an available cancel-and-drain document transition during a sequence");
            await Assert.That(current).IsNotSameReferenceAs(original);
            await Assert.That(current.CurrentRenderState.Viewport).IsEqualTo(viewport);
            await Assert.That(Directory.GetDirectories(outputParent).Length).IsEqualTo(1);
            await Assert.That(sequence.IsVisible).IsFalse();
            await Assert.That(Required<MenuItem>(window, "CaptureFrameMenuItem").IsEnabled).IsTrue();
            await Assert.That(await current.Scheduler.InvokeAsync(static stage =>
                stage.HasPrim("/World/LateBeforeReload") || stage.HasPrim("/World/AddedDuringSequence"))).IsFalse();
            await WaitUntilAsync(() => current.PickingBackend is IViewerRenderedPickStateSource rendered &&
                rendered.LastRenderedPickState?.State == current.CurrentRenderState);
            await Assert.That(current.CurrentRenderState.Camera.View.M41).IsEqualTo(0f);
            await Assert.That(current.CurrentRenderState.Camera.View.M43).IsEqualTo(-8f);
            await Assert.That(Required<TreeView>(window, "StageHierarchy").Items.OfType<TreeViewItem>()
                .SelectMany(static item => item.Items.OfType<TreeViewItem>())
                .Any(static item => item.Tag is ViewerHierarchyTreeNode
                {
                    Entry.Path: "/World/LateBeforeReload" or "/World/AddedDuringSequence"
                })).IsFalse();
        }
        finally
        {
            status.PropertyChanged -= reloadOnSecondFrame;
        }
    }

    private static async Task ExerciseChangedSourceAsync(
        MainWindow window, ViewerStageSession session, SequenceFramePicker picker, string root, string outputParent)
    {
        StageRenderState before = session.CurrentRenderState;
        string beforeImage = Path.Combine(root, "before-failure.png");
        await CaptureStillAsync(window, session, picker, beforeImage);
        Window sequence = await OpenSequenceAsync(window, outputParent, includeData: true);
        TextBlock status = Required<TextBlock>(sequence, "SequenceStatus");
        Task mutation = Task.CompletedTask;
        bool requested = false;
        void changeOnSecondFrame(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (!requested && args.Property == TextBlock.TextProperty &&
                status.Text?.StartsWith("Rendering: 1/3", StringComparison.Ordinal) == true)
            {
                requested = true;
                mutation = session.Scheduler.EditAsync(static stage =>
                {
                    using UsdLayer container = stage.GetSessionLayer();
                    container.SetMetadata("comment", "independent writer during image sequence");
                }, UsdStageInvalidationKind.Composition).AsTask();
            }
        }
        status.PropertyChanged += changeOnSecondFrame;
        try
        {
            Required<Button>(sequence, "SequenceRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => !((RenderImageSequenceWindow)sequence).IsRunning);
            await mutation;
            await Assert.That(requested).IsTrue();
            await Assert.That(status.Text).Contains("The stage changed during the sequence");
            await Assert.That(Required<TextBox>(sequence, "SequenceOutputLocation").Text).IsEqualTo(string.Empty);
            await Assert.That(Directory.GetDirectories(outputParent).Length).IsEqualTo(1);
            await AssertRestoredAsync(session, before);
            await Assert.That(await session.Scheduler.InvokeAsync(static stage =>
            {
                using UsdLayer container = stage.GetSessionLayer();
                return container.GetMetadataString("comment");
            })).IsEqualTo("independent writer during image sequence");
        }
        finally
        {
            status.PropertyChanged -= changeOnSecondFrame;
            await ((RenderImageSequenceWindow)sequence).CloseAsync();
        }
        string afterImage = Path.Combine(root, "after-failure.png");
        await CaptureStillAsync(window, session, picker, afterImage);
        ViewerCapturePair pair = await ViewerCaptureComparison.LoadAsync(
            beforeImage, afterImage, CancellationToken.None);
        await Assert.That(pair.Before.Rgba.SequenceEqual(pair.After.Rgba)).IsTrue();
    }

    private static async Task ExercisePendingCloseAsync(
        MainWindow window, ViewerStageSession session, string outputParent, Task closed)
    {
        Window sequence = await OpenSequenceAsync(window, outputParent, includeData: true);
        TextBlock status = Required<TextBlock>(sequence, "SequenceStatus");
        bool requested = false;
        Task mutation = Task.CompletedTask;
        void closeOnSecondFrame(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (!requested && args.Property == TextBlock.TextProperty &&
                status.Text?.StartsWith("Rendering: 1/3", StringComparison.Ordinal) == true)
            {
                requested = true;
                mutation = session.Scheduler.EditAsync(static stage =>
                {
                    using UsdLayer layer = stage.GetSessionLayer();
                    layer.SetMetadata("comment", "queued source notice before close");
                }, UsdStageInvalidationKind.Composition).AsTask();
                window.Close();
            }
        }
        status.PropertyChanged += closeOnSecondFrame;
        try
        {
            Required<Button>(sequence, "SequenceRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            try
            {
                await WaitUntilAsync(() => closed.IsCompleted ||
                    window.OwnedWindows.OfType<DocumentChangesWindow>().Any());
                if (window.OwnedWindows.OfType<DocumentChangesWindow>().FirstOrDefault() is { } changes)
                {
                    Required<Button>(changes, "DiscardDocumentChangesButton")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                await closed.WaitAsync(TimeSpan.FromSeconds(15));
                await mutation;
            }
            catch (TimeoutException exception)
            {
                throw new TimeoutException(
                    $"Pending close: requested={requested}; sequence={status.Text}; " +
                    $"viewer={Required<TextBlock>(window, "ViewerStatus").Text}; " +
                    $"children={string.Join(", ", window.OwnedWindows.Select(static child => child.Title))}.",
                    exception);
            }
            await Assert.That(requested).IsTrue();
            await Assert.That(sequence.IsVisible).IsFalse();
            await Assert.That(Directory.GetDirectories(outputParent).Length).IsEqualTo(1);
            await Assert.That(async () => await session.Scheduler.InvokeAsync(
                static stage => stage.RootLayerIdentifier, CancellationToken.None))
                .Throws<ObjectDisposedException>();
        }
        finally
        {
            status.PropertyChanged -= closeOnSecondFrame;
        }
    }

    private static async Task ExerciseChangedCameraAsync(
        MainWindow window, ViewerStageSession session, SequenceFramePicker picker, string root, string outputParent)
    {
        TreeView hierarchy = Required<TreeView>(window, "StageHierarchy");
        TreeViewItem world = hierarchy.Items.OfType<TreeViewItem>().Single(
            static item => item.Tag is ViewerHierarchyTreeNode { Entry.Path: "/World" });
        world.IsExpanded = true;
        hierarchy.SelectedItem = world.Items.OfType<TreeViewItem>().Single(
            static item => item.Tag is ViewerHierarchyTreeNode { Entry.Path: "/World/Camera" });
        Required<TabControl>(window, "InspectorTabs").SelectedItem = Required<TabItem>(window, "ValueTab");
        await WaitUntilAsync(() => Required<TextBox>(window, "InspectorPropertyQuery").IsEnabled);
        Required<TextBox>(window, "InspectorPropertyQuery").Text = "horizontalAperture";
        await WaitUntilAsync(() => SequencePropertyText(window).Contains("Value: 36", StringComparison.Ordinal));
        StageRenderState before = session.CurrentRenderState;
        await Assert.That(before.Time.TimeCode).IsEqualTo(2d);
        string beforeImage = Path.Combine(root, "before-camera-edit.png");
        await CaptureStillAsync(window, session, picker, beforeImage);
        Window sequence = await OpenSequenceAsync(window, outputParent, includeData: true);
        TextBlock status = Required<TextBlock>(sequence, "SequenceStatus");
        var editedTransform = new UsdMatrix4d(
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            2, 1, 10, 1);
        Task mutation = Task.CompletedTask;
        bool requested = false;
        void editOnSecondFrame(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (!requested && args.Property == TextBlock.TextProperty &&
                status.Text?.StartsWith("Rendering: 1/3", StringComparison.Ordinal) == true)
            {
                requested = true;
                mutation = session.Scheduler.EditAsync(stage =>
                {
                    stage.SetEditTargetToSessionLayer();
                    UsdGeomCamera camera = UsdGeomCamera.Wrap(stage.GetPrim("/World/Camera"));
                    camera.SetTransform(editedTransform, 2);
                    camera.HorizontalAperture = 72;
                    _ = stage.DefinePrim("/World/AddedDuringSequence", "Xform");
                }, UsdStageInvalidationKind.Composition).AsTask();
            }
        }
        status.PropertyChanged += editOnSecondFrame;
        try
        {
            Required<Button>(sequence, "SequenceRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => !((RenderImageSequenceWindow)sequence).IsRunning);
            await mutation;
            await Assert.That(requested).IsTrue();
            await Assert.That(status.Text).Contains("The stage changed during the sequence");
            await Assert.That(await session.Scheduler.InvokeAsync(stage =>
                UsdGeomCamera.Wrap(stage.GetPrim("/World/Camera")).GetTransform(2))).IsEqualTo(editedTransform);
            await Assert.That(await session.Scheduler.InvokeAsync(static stage =>
                UsdGeomCamera.Wrap(stage.GetPrim("/World/Camera")).HorizontalAperture)).IsEqualTo(72f);
            await WaitUntilAsync(() => session.CurrentRenderState.Camera.View.M41 == -2 &&
                session.CurrentRenderState.Camera.View.M42 == -1 &&
                session.CurrentRenderState.Camera.View.M43 == -10 &&
                Math.Abs(session.CurrentRenderState.Camera.Projection.M11 - 1.25f) < 0.00001f);
            await WaitUntilAsync(() => Required<TreeView>(window, "StageHierarchy").Items.OfType<TreeViewItem>()
                .SelectMany(static item => item.Items.OfType<TreeViewItem>())
                .Any(static item => item.Tag is
                    ViewerHierarchyTreeNode { Entry.Path: "/World/AddedDuringSequence" }));
            await WaitUntilAsync(() => SequencePropertyText(window).Contains("Value: 72", StringComparison.Ordinal));
            await Assert.That(session.CurrentRenderState.Time).IsEqualTo(before.Time);
            await Assert.That(session.CurrentRenderState.Selection).IsEqualTo(before.Selection);
            await Assert.That(session.CurrentRenderState.Display).IsEqualTo(before.Display);
            await Assert.That(session.CurrentRenderState.RenderSettings).IsEqualTo(before.RenderSettings);
            await Assert.That(session.CurrentRenderState.Viewport).IsEqualTo(before.Viewport);
            await Assert.That(Directory.GetDirectories(outputParent).Length).IsEqualTo(1);
        }
        finally
        {
            status.PropertyChanged -= editOnSecondFrame;
            await ((RenderImageSequenceWindow)sequence).CloseAsync();
        }
        string afterImage = Path.Combine(root, "after-camera-edit.png");
        await CaptureStillAsync(window, session, picker, afterImage);
        ViewerCapturePair pair = await ViewerCaptureComparison.LoadAsync(
            beforeImage, afterImage, CancellationToken.None);
        await Assert.That(pair.Before.Rgba.SequenceEqual(pair.After.Rgba)).IsFalse()
            .Because("the resumed authored-camera viewport must adopt the surviving native edit without user input");
    }

    private static string SequencePropertyText(MainWindow window) => string.Join("\n",
        Required<StackPanel>(window, "ValueRows").Children.OfType<StackPanel>()
            .SelectMany(static row => row.Children.OfType<TextBlock>())
            .Select(static text => text.Text));

    private static async Task<Window> OpenSequenceAsync(
        MainWindow window, string outputParent, bool includeData = false)
    {
        MenuItem action = Required<MenuItem>(window, "RenderImageSequenceMenuItem");
        await WaitUntilAsync(() => action.IsEnabled);
        action.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await WaitUntilAsync(() => window.OwnedWindows.OfType<RenderImageSequenceWindow>().Any());
        RenderImageSequenceWindow sequence = window.OwnedWindows.OfType<RenderImageSequenceWindow>().Single();
        Required<TextBox>(sequence, "SequenceStartTime").Text = "1";
        Required<TextBox>(sequence, "SequenceEndTime").Text = "3";
        Required<TextBox>(sequence, "SequenceStep").Text = "1";
        Required<TextBox>(sequence, "SequenceOutputFolder").Text = outputParent;
        Required<CheckBox>(sequence, "SequenceHdrColorData").IsChecked = includeData;
        Required<CheckBox>(sequence, "SequenceDepthData").IsChecked = includeData;
        await WaitUntilAsync(() => Required<Button>(sequence, "SequenceRenderButton").IsEnabled);
        return sequence;
    }

    private static async Task CaptureStillAsync(
        MainWindow window, ViewerStageSession session, SequenceFramePicker picker, string path)
    {
        await WaitUntilAsync(() => session.PickingBackend is IViewerRenderedPickStateSource source &&
            source.LastRenderedPickState?.State == session.CurrentRenderState);
        picker.Paths.Enqueue(path);
        MenuItem capture = Required<MenuItem>(window, "CaptureFrameMenuItem");
        await WaitUntilAsync(() => capture.IsEnabled);
        capture.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await WaitUntilAsync(() => File.Exists(path) && capture.IsEnabled);
    }

    private static async Task AssertRestoredAsync(ViewerStageSession session, StageRenderState before)
    {
        await Assert.That(session.CurrentRenderState.Time).IsEqualTo(before.Time);
        await Assert.That(session.CurrentRenderState.Camera).IsEqualTo(before.Camera);
        await Assert.That(session.CurrentRenderState.Display).IsEqualTo(before.Display);
        await Assert.That(session.CurrentRenderState.RenderSettings).IsEqualTo(before.RenderSettings);
        await Assert.That(session.CurrentRenderState.Selection).IsEqualTo(before.Selection);
        await Assert.That(session.CurrentRenderState.Viewport).IsEqualTo(before.Viewport);
    }

    private sealed class SequenceFramePicker : IViewerFrameFilePicker
    {
        internal Queue<string> Paths { get; } = new();

        public Task<string?> SaveFrameAsync(Window owner, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(Paths.Dequeue());
        }
    }
}
