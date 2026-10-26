// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;

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
}
