// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerCameraPropertyNativeTests
{
    [Test]
    [Arguments(false, false, false)]
    [Arguments(true, false, false)]
    [Arguments(false, true, false)]
    [Arguments(true, true, false)]
    [Arguments(false, false, true)]
    [Arguments(true, false, true)]
    public async Task NativePropertyRefreshKeepsOwnedFocusAndEscapeDrainsPendingCapture(
        bool dark, bool closeDuring, bool moveFocus)
    {
        using var files = new ViewerCompletedJobTestFiles();
        string path = Path.Combine(files.Root, "source.usda");
        const string original = "#usda 1.0\ndef Camera \"Camera\" {}\n";
        await File.WriteAllTextAsync(path, original);
        await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
        await using var editor = new ViewerAuthoredEditController(scheduler);
        ViewerPrimInspectorSnapshot snapshot = await scheduler.InvokeAsync(
            static stage => ViewerStageSnapshotBuilder.BuildInspector(stage, "/Camera", 0));
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task blocker = Task.CompletedTask;
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaEditorEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new PropertyEditWindow(editor, "/Camera", snapshot.Attributes, 0, "focusDistance")
            {
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                Button refresh = Required<Button>(window, "RefreshPropertyButton");
                Button close = Required<Button>(window, "ClosePropertyButton");
                Button set = Required<Button>(window, "SetPropertyButton");
                await WaitForEditorAsync(() => set.IsEnabled);
                await Assert.That(refresh.Focus()).IsTrue();
                blocker = scheduler.InvokeAsync(_ =>
                {
                    entered.TrySetResult();
                    if (!release.Wait(TimeSpan.FromSeconds(10), lifetime.Token))
                    {
                        throw new TimeoutException("The test did not release its native scheduler barrier.");
                    }
                    return 0;
                }, lifetime.Token).AsTask();
                await entered.Task.WaitAsync(lifetime.Token);
                refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Assert.That(refresh.IsEnabled).IsFalse();
                await Assert.That(close.IsEnabled).IsTrue();
                await Assert.That(close.IsKeyboardFocusWithin).IsTrue();
                if (closeDuring)
                {
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "\u001b");
                    window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "\u001b");
                    Task closed = window.CloseAsync();
                    await Assert.That(closed.IsCompleted).IsFalse();
                    await Assert.That(window.IsVisible).IsTrue();
                    release.Set();
                    await blocker.WaitAsync(lifetime.Token);
                    await closed.WaitAsync(lifetime.Token);
                    await Assert.That(window.IsVisible).IsFalse();
                    await Assert.That(editor.UndoDepth).IsEqualTo(0);
                }
                else
                {
                    ScrollViewer body = Required<ScrollViewer>(window, "PropertyEditorBody");
                    if (moveFocus)
                    {
                        await Assert.That(body.Focus()).IsTrue();
                    }
                    release.Set();
                    await blocker.WaitAsync(lifetime.Token);
                    await WaitForEditorAsync(() => set.IsEnabled);
                    await Assert.That(moveFocus ? body.IsKeyboardFocusWithin : refresh.IsKeyboardFocusWithin).IsTrue();
                    TextBox input = Required<TextBox>(window, "PropertyValue");
                    input.Text = "not-a-float";
                    await Assert.That(set.Focus()).IsTrue();
                    set.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitForEditorAsync(() => set.IsEnabled);
                    await Assert.That(set.IsKeyboardFocusWithin).IsTrue();
                    await Assert.That(editor.UndoDepth).IsEqualTo(0);
                    await Assert.That(Required<TextBlock>(window, "PropertyEditStatus").Text)
                        .DoesNotContain("Review edit applied.");
                    input.Text = "2.5";
                    set.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitForEditorAsync(() => set.IsEnabled && editor.UndoDepth == 1);
                    await Assert.That(set.IsKeyboardFocusWithin).IsTrue();
                    await Assert.That(await scheduler.InvokeAsync(stage =>
                        ReadAdditionalOptic(stage, "focusDistance", 0))).IsEqualTo(2.5);
                }
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
            }
            finally
            {
                release.Set();
                await blocker.WaitAsync(TimeSpan.FromSeconds(5));
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(original);
    }
}
