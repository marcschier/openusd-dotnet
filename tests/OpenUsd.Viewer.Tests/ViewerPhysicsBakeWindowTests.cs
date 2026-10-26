// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerPhysicsBakeWindowTests
{
    private static readonly string[] FooterNames = ["BakeValidationText", "BakeCancelButton", "BakeConfirmButton"];

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MinimumBakeDialogKeepsValidationAndActionsInsideClient(bool dark)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new PhysicsBakeWindow(0, 48, "unsupported.txt")
            {
                Width = 480,
                Height = 360,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            var owner = new Window { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            owner.Show();
            Task<ViewerPhysicsBakeRequest?> completion = window.ShowDialog<ViewerPhysicsBakeRequest?>(owner);
            try
            {
                window.UpdateLayout();
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                await Assert.That(Required<Button>(window, "BakeConfirmButton").IsEnabled).IsFalse();
                await Assert.That(Required<TextBlock>(window, "BakeValidationText").Text)
                    .IsEqualTo("The bake destination must be a .usd, .usda, or .usdc file-backed layer.");
                foreach (string name in FooterNames)
                {
                    Control control = Required<Control>(window, name);
                    Point origin = control.TranslatePoint(default, window) ??
                        throw new InvalidOperationException($"Bake control {name} has no window coordinates.");
                    await Assert.That(origin.X).IsGreaterThanOrEqualTo(0);
                    await Assert.That(origin.Y).IsGreaterThanOrEqualTo(0);
                    await Assert.That(control.Bounds.Width).IsGreaterThan(0);
                    await Assert.That(control.Bounds.Height).IsGreaterThan(0);
                    await Assert.That(origin.X + control.Bounds.Width).IsLessThanOrEqualTo(window.ClientSize.Width);
                    await Assert.That(origin.Y + control.Bounds.Height).IsLessThanOrEqualTo(window.ClientSize.Height);
                }
                ScrollViewer body = Required<ScrollViewer>(window, "BakeFormScroll");
                await Assert.That(body.Viewport.Height).IsGreaterThan(0);
                await Assert.That(body.Extent.Height).IsGreaterThan(body.Viewport.Height);
                await Assert.That(body.Focus()).IsTrue();
                int pages = (int)Math.Ceiling(body.Extent.Height / body.Viewport.Height) + 1;
                for (int page = 0; page < pages; page++)
                {
                    body.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageDown });
                    window.UpdateLayout();
                }
                await Assert.That(body.Offset.Y).IsEqualTo(body.Extent.Height - body.Viewport.Height);
                CheckBox save = Required<CheckBox>(window, "BakeSaveCheckBox");
                Point bottom = save.TranslatePoint(default, body) ??
                    throw new InvalidOperationException("Save checkbox has no scroll coordinates.");
                await Assert.That(bottom.Y + save.Bounds.Height).IsGreaterThan(0);
                await Assert.That(bottom.Y + save.Bounds.Height).IsLessThanOrEqualTo(body.Viewport.Height);
                await Assert.That(bottom.X).IsGreaterThanOrEqualTo(0);
                await Assert.That(bottom.X + save.Bounds.Width).IsLessThanOrEqualTo(body.Viewport.Width);
                for (int page = 0; page < pages; page++)
                {
                    body.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageUp });
                    window.UpdateLayout();
                }
                await Assert.That(body.Offset.Y).IsEqualTo(0);
                body.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                await Assert.That(window.IsVisible).IsFalse();
                await Assert.That(await completion).IsNull();
            }
            finally
            {
                window.Close();
                owner.Close();
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    [Test]
    [Arguments(0.0004, 0.0005, "en-US")]
    [Arguments(0.0004, 0.0005, "de-DE")]
    [Arguments(-1.2345678901234567, -0.0004, "en-US")]
    [Arguments(-1.2345678901234567, -0.0004, "de-DE")]
    [Arguments(1.0000000000000002, 1.0000000000000004, "en-US")]
    [Arguments(1.0000000000000002, 1.0000000000000004, "de-DE")]
    [Arguments(0, 48, "en-US")]
    [Arguments(0, 48, "de-DE")]
    public async Task OpeningBakePreservesExactAuthoredRangeWithoutRoundingOrCultureConversion(
        double start, double end, string culture)
    {
        using var files = new ViewerCompletedJobTestFiles();
        string destination = Path.Combine(files.Root, "destination.usda");
        const string original = "#usda 1.0\n# Existing destination must not change while collecting the request.\n";
        await File.WriteAllTextAsync(destination, original);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            PhysicsBakeWindow? window = null;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                window = new PhysicsBakeWindow(start, end, destination);
                window.Show();
                await Assert.That(window.TryBuildRequest(out ViewerPhysicsBakeRequest request)).IsTrue();
                await Assert.That(BitConverter.DoubleToInt64Bits(request.StartTimeCode))
                    .IsEqualTo(BitConverter.DoubleToInt64Bits(start));
                await Assert.That(BitConverter.DoubleToInt64Bits(request.EndTimeCode))
                    .IsEqualTo(BitConverter.DoubleToInt64Bits(end));
                await Assert.That(request.DestinationLayerIdentifier).IsEqualTo(destination);
                await Assert.That(request.SampleStride).IsEqualTo(1d);
                await Assert.That(request.Policy).IsEqualTo(ViewerPhysicsBakePolicy.Reject);
                await Assert.That(request.Save).IsTrue();
                await Assert.That(Required<Button>(window, "BakeConfirmButton").IsEnabled).IsTrue();
                await Assert.That(Required<TextBox>(window, "BakeStartInput").Text).DoesNotContain(",");
                await Assert.That(Required<TextBox>(window, "BakeEndInput").Text).DoesNotContain(",");
            }
            finally
            {
                window?.Close();
                CultureInfo.CurrentCulture = previous;
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
        await Assert.That(await File.ReadAllTextAsync(destination)).IsEqualTo(original);
    }

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing bake control: {name}");

    private static class SkiaAppEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
