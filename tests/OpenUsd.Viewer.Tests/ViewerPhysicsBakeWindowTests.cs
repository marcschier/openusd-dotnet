// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerPhysicsBakeWindowTests
{
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
