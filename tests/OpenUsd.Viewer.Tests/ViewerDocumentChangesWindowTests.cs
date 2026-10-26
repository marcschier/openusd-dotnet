// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerDocumentChangesWindowTests
{
    private static readonly string[] ActionNames =
    [
        "SaveDocumentReviewButton", "DiscardDocumentChangesButton", "CancelDocumentTransitionButton"
    ];

    [Test]
    [Arguments(480, 320, false)]
    [Arguments(480, 320, true)]
    [Arguments(640, 420, false)]
    [Arguments(640, 420, true)]
    public async Task LongSaveRefusalsKeepDocumentDecisionsVisibleAndEscapeSafelyCancels(
        int width, int height, bool dark)
    {
        string restriction = string.Join(' ', Enumerable.Repeat("Exact source save refusal remains authoritative.", 80));
        var state = new ViewerDocumentState(Guid.NewGuid(),
            new("source", ViewerDocumentLayerRole.Source, "original-source.usda", 2, true, true),
            new("review", ViewerDocumentLayerRole.Review, null, 3, true, true), "review")
        {
            HasOtherLayerChanges = true
        };
        string[] layers = Enumerable.Range(0, 10).Select(static index => $"changed-layer-{index}.usda").ToArray();
        var document = new ViewerDocumentObservation(state, [], layers);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new DocumentChangesWindow(document, ViewerDocumentTransition.Close,
                saveRestriction: restriction)
            {
                Width = width,
                Height = height,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                window.UpdateLayout();
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                await Assert.That(Required<TextBlock>(window, "DocumentSaveRestriction").Text).IsEqualTo(restriction);
                await Assert.That(Required<TextBlock>(window, "UnsavedDocumentDetails").Text)
                    .Contains("Separately dirty SOURCE: source");
                await Assert.That(Required<TextBlock>(window, "UnsavedDocumentDetails").Text)
                    .Contains("And 2 other changed layers.");
                await Assert.That(Required<Button>(window, "SaveDocumentReviewButton").IsEnabled).IsFalse();
                foreach (string name in ActionNames)
                {
                    Button action = Required<Button>(window, name);
                    Point origin = action.TranslatePoint(default, window) ??
                        throw new InvalidOperationException($"Document action {name} has no window coordinates.");
                    await Assert.That(action.IsVisible).IsTrue();
                    await Assert.That(action.Bounds.Width).IsGreaterThan(0);
                    await Assert.That(action.Bounds.Height).IsGreaterThan(0);
                    await Assert.That(origin.X).IsGreaterThanOrEqualTo(0);
                    await Assert.That(origin.Y).IsGreaterThanOrEqualTo(0);
                    await Assert.That(origin.X + action.Bounds.Width).IsLessThanOrEqualTo(window.ClientSize.Width);
                    await Assert.That(origin.Y + action.Bounds.Height).IsLessThanOrEqualTo(window.ClientSize.Height);
                }
                ScrollViewer body = Required<ScrollViewer>(window, "DocumentChangesBodyScroll");
                await Assert.That(body.Viewport.Height).IsGreaterThan(0);
                await Assert.That(body.Extent.Height).IsGreaterThan(body.Viewport.Height);
                body.Offset = new Vector(0, body.Extent.Height - body.Viewport.Height);
                window.UpdateLayout();
                TextBlock refusal = Required<TextBlock>(window, "DocumentSaveRestriction");
                Point end = refusal.TranslatePoint(default, body) ??
                    throw new InvalidOperationException("The save refusal has no scroll-body coordinates.");
                await Assert.That(end.Y + refusal.Bounds.Height).IsGreaterThan(0);
                await Assert.That(end.Y + refusal.Bounds.Height).IsLessThanOrEqualTo(body.Viewport.Height);
                body.Offset = default;
                window.UpdateLayout();
                Point start = Required<TextBlock>(window, "UnsavedDocumentDetails").TranslatePoint(default, body) ??
                    throw new InvalidOperationException("Unsaved details have no scroll-body coordinates.");
                await Assert.That(start.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(start.Y).IsLessThan(body.Viewport.Height);
                Button cancel = Required<Button>(window, "CancelDocumentTransitionButton");
                await Assert.That(cancel.IsKeyboardFocusWithin).IsTrue();
                cancel.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                await Assert.That(window.IsVisible).IsFalse();
                await Assert.That(window.Choice).IsEqualTo(ViewerDocumentUnsavedChoice.Cancel);
                await Assert.That(document.State).IsSameReferenceAs(state);
                await Assert.That(document.State.Source.HasChanges).IsTrue();
                await Assert.That(document.State.Review!.HasChanges).IsTrue();
            }
            finally
            {
                window.Close();
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing document control: {name}");

    private static class SkiaAppEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
