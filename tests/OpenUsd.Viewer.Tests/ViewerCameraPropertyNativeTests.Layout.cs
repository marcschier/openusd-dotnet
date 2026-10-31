// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUsd.Editing;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerCameraPropertyNativeTests
{
    private static readonly string[] PropertyActionNames =
    [
        "SetPropertyButton", "ClearPropertyButton", "BlockPropertyButton",
        "RefreshPropertyButton", "ClosePropertyButton"
    ];

    [Test]
    [Arguments(500, 420, false)]
    [Arguments(500, 420, true)]
    [Arguments(700, 540, false)]
    [Arguments(700, 540, true)]
    public async Task NativePropertyEditorKeepsActionsReachableWithLongAuthoredText(
        int width, int height, bool dark)
    {
        using var files = new ViewerCompletedJobTestFiles();
        string path = Path.Combine(files.Root, "source.usda");
        string value = string.Join(' ', Enumerable.Repeat("Historical authored camera description", 100));
        string original = $"#usda 1.0\ndef Camera \"Camera\" {{\n custom string review:description = \"{value}\"\n}}\n";
        await File.WriteAllTextAsync(path, original);
        await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
        await using var editor = new ViewerAuthoredEditController(scheduler);
        ViewerPrimInspectorSnapshot snapshot = await scheduler.InvokeAsync(
            static stage => ViewerStageSnapshotBuilder.BuildInspector(stage, "/Camera", 0));
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaEditorEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new PropertyEditWindow(editor, "/Camera", snapshot.Attributes, 0, "review:description")
            {
                Width = width,
                Height = height,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                await WaitForEditorAsync(() => Required<Button>(window, "SetPropertyButton").IsEnabled);
                window.UpdateLayout();
                await Assert.That(Required<TextBlock>(window, "PropertyComposedValue").Text)
                    .IsEqualTo($"Composed display snapshot (read-only): " +
                        snapshot.Attributes.Single(attribute => attribute.Name == "review:description").Value);
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                foreach (string name in PropertyActionNames)
                {
                    Button action = Required<Button>(window, name);
                    Point origin = action.TranslatePoint(default, window) ??
                        throw new InvalidOperationException($"Property action {name} has no window coordinates.");
                    await Assert.That(action.IsVisible).IsTrue();
                    await Assert.That(action.Bounds.Width).IsGreaterThan(0);
                    await Assert.That(action.Bounds.Height).IsGreaterThan(0);
                    await Assert.That(origin.X).IsGreaterThanOrEqualTo(0);
                    await Assert.That(origin.Y).IsGreaterThanOrEqualTo(0);
                    await Assert.That(origin.X + action.Bounds.Width).IsLessThanOrEqualTo(window.ClientSize.Width);
                    await Assert.That(origin.Y + action.Bounds.Height).IsLessThanOrEqualTo(window.ClientSize.Height);
                }
                TextBox input = Required<TextBox>(window, "PropertyValue");
                await Assert.That(input.Focus()).IsTrue();
                input.BringIntoView();
                window.UpdateLayout();
                Point location = input.TranslatePoint(default, window) ??
                    throw new InvalidOperationException("Property value has no window coordinates.");
                await Assert.That(location.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(location.Y + input.Bounds.Height).IsLessThanOrEqualTo(window.ClientSize.Height);
                await Assert.That(input.IsKeyboardFocusWithin).IsTrue();
                ScrollViewer body = Required<ScrollViewer>(window, "PropertyEditorBody");
                await Assert.That(body.Viewport.Height).IsGreaterThan(0);
                if (width == 500)
                {
                    await Assert.That(body.Extent.Height).IsGreaterThan(body.Viewport.Height);
                }
                else
                {
                    await Assert.That(body.Extent.Height).IsEqualTo(body.Viewport.Height);
                }
                await Assert.That(body.Focus()).IsTrue();
                int pages = (int)Math.Ceiling(body.Extent.Height / body.Viewport.Height) + 1;
                for (int page = 0; page < pages; page++)
                {
                    body.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageDown });
                    window.UpdateLayout();
                }
                await Assert.That(body.Offset.Y).IsEqualTo(body.Extent.Height - body.Viewport.Height);
                TextBlock status = Required<TextBlock>(window, "PropertyEditStatus");
                Point end = status.TranslatePoint(default, body) ??
                    throw new InvalidOperationException("Property status has no scroll coordinates.");
                await Assert.That(end.Y + status.Bounds.Height).IsLessThanOrEqualTo(body.Viewport.Height);
                for (int page = 0; page < pages; page++)
                {
                    body.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageUp });
                    window.UpdateLayout();
                }
                await Assert.That(body.Offset.Y).IsEqualTo(0);
                body.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(window.IsVisible).IsFalse();
            }
            finally
            {
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
        await Assert.That(editor.UndoDepth).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(original);
    }

    [Test]
    [Arguments(500, 420, false)]
    [Arguments(500, 420, true)]
    [Arguments(700, 540, false)]
    [Arguments(700, 540, true)]
    public async Task NativePropertyEditorCanKeyboardEditBothEndsOfLongTextAndUndoExactly(
        int width, int height, bool dark)
    {
        using var files = new ViewerCompletedJobTestFiles();
        string path = Path.Combine(files.Root, "source.usda");
        const string original =
            "#usda 1.0\ndef Camera \"Camera\" {\n custom string review:description = \"source\"\n}\n";
        await File.WriteAllTextAsync(path, original);
        await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
        await using var editor = new ViewerAuthoredEditController(scheduler);
        ViewerPrimInspectorSnapshot snapshot = await scheduler.InvokeAsync(
            static stage => ViewerStageSnapshotBuilder.BuildInspector(stage, "/Camera", 0));
        string draft = string.Concat(Enumerable.Repeat("Long authored review text. ", 150));
        string edited = "A" + draft + "Z";
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaEditorEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new PropertyEditWindow(editor, "/Camera", snapshot.Attributes, 0, "review:description")
            {
                Width = width,
                Height = height,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                Button set = Required<Button>(window, "SetPropertyButton");
                await WaitForEditorAsync(() => set.IsEnabled);
                TextBox input = Required<TextBox>(window, "PropertyValue");
                input.Text = draft;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                await Assert.That(input.Focus()).IsTrue();
                input.CaretIndex = 0;
                window.KeyPress(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
                window.KeyRelease(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                await Assert.That(input.CaretIndex).IsEqualTo(draft.Length);
                ScrollViewer editorScroll = input.GetVisualDescendants().OfType<ScrollViewer>().Single();
                await Assert.That(editorScroll.Offset.X).IsGreaterThan(0);
                window.KeyTextInput("Z");
                window.KeyPress(Key.Home, RawInputModifiers.Control, PhysicalKey.Home, null);
                window.KeyRelease(Key.Home, RawInputModifiers.Control, PhysicalKey.Home, null);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                await Assert.That(input.CaretIndex).IsEqualTo(0);
                await Assert.That(editorScroll.Offset.X).IsEqualTo(0);
                window.KeyTextInput("A");
                await Assert.That(input.Text).IsEqualTo(edited);
                await Assert.That(set.Focus()).IsTrue();
                set.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForEditorAsync(() => set.IsEnabled && editor.UndoDepth == 1);
                await Assert.That(Required<TextBlock>(window, "PropertyEditStatus").Text)
                    .IsEqualTo("Review edit applied.");
                await Assert.That(input.Text).IsEqualTo(edited);
                await Assert.That(Required<TextBlock>(window, "PropertyTargetState").Text).Contains(edited);
                // The applied state text reflows the body; settle that layout so bringing the input into
                // view on focus uses its final position.
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                await Assert.That(input.Focus()).IsTrue();
                window.KeyPress(Key.Home, RawInputModifiers.Control, PhysicalKey.Home, null);
                window.KeyRelease(Key.Home, RawInputModifiers.Control, PhysicalKey.Home, null);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                await Assert.That(input.CaretIndex).IsEqualTo(0);
                await Assert.That(editorScroll.Offset.X).IsEqualTo(0);
                ScrollViewer body = Required<ScrollViewer>(window, "PropertyEditorBody");
                Point location = input.TranslatePoint(default, body) ??
                    throw new InvalidOperationException("The long review input has no body coordinates.");
                await Assert.That(location.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(location.Y + input.Bounds.Height).IsLessThanOrEqualTo(body.Viewport.Height);
                await Assert.That(input.IsKeyboardFocusWithin).IsTrue();
            }
            finally
            {
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
        ViewerPropertyEditCapture capture = await editor.CapturePropertyAsync(
            "/Camera", "review:description", UsdLayerEditField.Default, 0);
        await Assert.That(capture.Capture.Snapshot.Opinions.Single().Value.AsString()).IsEqualTo(edited);
        await Assert.That((await editor.UndoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        capture = await editor.CapturePropertyAsync("/Camera", "review:description", UsdLayerEditField.Default, 0);
        await Assert.That(capture.Capture.Snapshot.Opinions.Single().PropertyKind)
            .IsEqualTo(UsdLayerPropertyKind.Absent);
        await Assert.That((await editor.RedoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        capture = await editor.CapturePropertyAsync("/Camera", "review:description", UsdLayerEditField.Default, 0);
        await Assert.That(capture.Capture.Snapshot.Opinions.Single().Value.AsString()).IsEqualTo(edited);
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(original);
    }
}
