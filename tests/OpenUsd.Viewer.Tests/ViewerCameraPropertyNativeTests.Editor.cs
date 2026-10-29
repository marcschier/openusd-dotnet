// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUsd.Editing;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerCameraPropertyNativeTests
{
    [Test]
    [Arguments("horizontalApertureOffset", -2.5, "float")]
    [Arguments("verticalApertureOffset", 2.5, "float")]
    [Arguments("focusDistance", 2.5, "float")]
    [Arguments("fStop", 2.5, "float")]
    [Arguments("shutter:open", -0.12500000000000003, "double")]
    [Arguments("shutter:close", 0.37500000000000006, "double")]
    [Arguments("exposure", -2.5, "float")]
    [Arguments("exposure:iso", 200, "float")]
    [Arguments("exposure:time", 0.125, "float")]
    [Arguments("exposure:fStop", 2.5, "float")]
    [Arguments("exposure:responsivity", 0.5, "float")]
    public async Task NativeCameraEditorSetsClearsAndSharesUndoWithoutChangingSource(
        string name, double value, string typeName)
    {
        using var files = new ViewerCompletedJobTestFiles();
        string path = Path.Combine(files.Root, "source.usda");
        const string original = "#usda 1.0\ndef Camera \"Camera\" {}\n";
        await File.WriteAllTextAsync(path, original);
        await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
        await using var editor = new ViewerAuthoredEditController(scheduler);
        ViewerPrimInspectorSnapshot snapshot = await scheduler.InvokeAsync(
            static stage => ViewerStageSnapshotBuilder.BuildInspector(stage, "/Camera", 0));
        double fallback = await scheduler.InvokeAsync(stage => ReadAdditionalOptic(stage, name, 0));
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaEditorEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new PropertyEditWindow(editor, "/Camera", snapshot.Attributes, 0, name);
            window.Show();
            try
            {
                await WaitForEditorAsync(() => Required<Button>(window, "SetPropertyButton").IsEnabled);
                await Assert.That(Required<ComboBox>(window, "PropertySelector").SelectedItem?.ToString())
                    .IsEqualTo($"{name} ({typeName})");
                TextBox input = Required<TextBox>(window, "PropertyValue");
                input.Text = value.ToString("R", CultureInfo.InvariantCulture);
                input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                await WaitForEditorAsync(() =>
                    Required<TextBlock>(window, "PropertyEditStatus").Text == "Review edit applied." &&
                    Required<Button>(window, "ClearPropertyButton").IsEnabled);
                await Assert.That(await scheduler.InvokeAsync(stage => ReadAdditionalOptic(stage, name, 0)))
                    .IsEqualTo(value);
                await Assert.That(editor.UndoDepth).IsEqualTo(1);
                Required<Button>(window, "ClearPropertyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForEditorAsync(() =>
                    Required<TextBlock>(window, "PropertyEditStatus").Text == "Review edit applied." &&
                    Required<Button>(window, "SetPropertyButton").IsEnabled && editor.UndoDepth == 2);
                await Assert.That(await scheduler.InvokeAsync(stage => ReadAdditionalOptic(stage, name, 0)))
                    .IsEqualTo(fallback);
            }
            finally
            {
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            await Assert.That(window.IsVisible).IsFalse();
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
        await Assert.That((await editor.UndoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(await scheduler.InvokeAsync(stage => ReadAdditionalOptic(stage, name, 0)))
            .IsEqualTo(value);
        await Assert.That((await editor.RedoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(await scheduler.InvokeAsync(stage => ReadAdditionalOptic(stage, name, 0)))
            .IsEqualTo(fallback);
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(original);
    }

    private static async Task WaitForEditorAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The native camera editor did not reach the expected state.");
            }
            await Task.Delay(10);
        }
    }

    private static T Required<T>(Control window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing camera edit control: {name}");

    private static class SkiaEditorEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
