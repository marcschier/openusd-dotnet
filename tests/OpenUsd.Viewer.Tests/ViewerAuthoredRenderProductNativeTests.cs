// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed partial class ViewerAuthoredRenderProductNativeTests
{
    [Test]
    public async Task FileAndPaletteRenderAuthoredProductPixelsAndRefuseUnsupportedOutputs()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_AUTHORED_PRODUCT_SMOKE") != "1")
        {
            Skip.Test(
                "Run through eng\\run-viewer-workflow-tests.ps1 -Scenario authored-product " +
                "with OPENUSD_VIEWER_TEST_NATIVE_ROOT.");
        }

        string runtime = Environment.GetEnvironmentVariable("OPENUSD_VIEWER_TEST_NATIVE_ROOT") ??
            throw new InvalidOperationException("OPENUSD_VIEWER_TEST_NATIVE_ROOT is required.");
        if (!Path.IsPathFullyQualified(runtime) ||
            !File.Exists(Path.Combine(runtime, "bin", "openusd_hdsilk.dll")))
        {
            throw new InvalidOperationException(
                "OPENUSD_VIEWER_TEST_NATIVE_ROOT must be the matched session-6 runtime root.");
        }
        string evidenceRoot = Environment.GetEnvironmentVariable("OPENUSD_VIEWER_AUTHORED_PRODUCT_EVIDENCE_ROOT") ??
            Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT") ??
            throw new InvalidOperationException("The Viewer workflow evidence root is required.");
        if (!Path.IsPathFullyQualified(evidenceRoot) || !Directory.Exists(evidenceRoot))
        {
            throw new ArgumentException("Authored-product evidence root must be an existing absolute directory.");
        }
        string root = Path.Combine(evidenceRoot, $"viewer-authored-product-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Program.BuildAvaloniaApp().SetupWithoutStarting();
                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        await ExerciseAuthoredProductAsync(root);
                        await ExerciseProductDialogLifetimesAsync(root);
                        completion.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                    finally
                    {
                        ViewerStartupOptions.Initialize([]);
                        lifetime.Cancel();
                    }
                });
                Dispatcher.UIThread.MainLoop(lifetime.Token);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Viewer authored RenderProduct workflow"
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();
        bool stopped;
        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(125));
        }
        finally
        {
            lifetime.Cancel();
            stopped = thread.Join(TimeSpan.FromSeconds(5));
        }
        await Assert.That(stopped).IsTrue();
    }

    private static async Task ExerciseAuthoredProductAsync(string root)
    {
        string stagePath = Path.Combine(root, "product.usda");
        string stage = ProductScene
            .Replace("int2 resolution = (16, 16)", "int2 resolution = (32, 16)", StringComparison.Ordinal)
            .Replace("token productName = \"../must-not-be-used.exr\"",
                "token productName = \"../must-not-be-used.exr\"\n    float4 dataWindowNDC = (0.25, 0, 0.75, 1)",
                StringComparison.Ordinal)
            .Replace("rel products = </Product>", "rel products = [</Product>, </UnsupportedProduct>]",
                StringComparison.Ordinal)
            .Replace("double3 xformOp:translate = (-1, 1, -4)",
                "double3 xformOp:translate.timeSamples = { 0: (-1, 1, -4), 2: (1, 1, -4) }",
                StringComparison.Ordinal);
        stage += """

        def RenderProduct "UnsupportedProduct" {
            token productName = "unsupported.exr"
            rel orderedVars = </UnsupportedVar>
        }
        def RenderVar "UnsupportedVar" {
            token dataType = "int"
            string sourceName = "id"
            token sourceType = "raw"
        }
        """;
        await File.WriteAllTextAsync(stagePath, stage);
        byte[] sourceBytes = await File.ReadAllBytesAsync(stagePath);
        string outputParent = Path.Combine(root, "outputs");
        Directory.CreateDirectory(outputParent);
        string statusFile = Path.Combine(root, "viewer-status.log");
        Environment.SetEnvironmentVariable("OPENUSD_STATUS_FILE", statusFile);
        var opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        ViewerStartupOptions.Initialize(new ViewerHostOptions
        {
            StagePath = stagePath,
            StageCameraPath = "/Camera",
            Renderer = ViewerNativeCaptureBackend.Kind.ToString(),
            PluginPath = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH"),
            StageReadyAsync = (session, _) =>
            {
                opened.TrySetResult(session);
                return Task.CompletedTask;
            }
        });
        using var store = new ViewerSettingsStore(Path.Combine(root, "settings"));
        await store.SaveAsync(ViewerSettings.Default with { ThemePreference = ViewerThemePreference.Dark });
        var window = new MainWindow(new RecentStageStore(root), store)
        {
            Width = 960,
            Height = 600
        };
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        try
        {
            window.Show();
            window.UpdateLayout();
            ViewerStageSession session;
            try
            {
                session = await opened.Task.WaitAsync(TimeSpan.FromSeconds(45));
            }
            catch (TimeoutException exception)
            {
                string status = File.Exists(statusFile) ? await File.ReadAllTextAsync(statusFile) : "no status file";
                throw new TimeoutException("The Viewer did not report StageReadyAsync. Status:\n" + status, exception);
            }
            await WaitUntilAsync(() => Required<MenuItem>(window, "RenderAuthoredProductMenuItem").IsEnabled);
            ViewerNativeCaptureBackend.Require(session);
            StageRenderState before = session.CurrentRenderState;
            string editTarget = await session.Scheduler.InvokeAsync(static stage => stage.EditTargetLayerIdentifier);
            await Assert.That(before.Viewport.Width == 16 && before.Viewport.Height == 16).IsFalse();

            Required<MenuItem>(window, "CommandPaletteMenuItem")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Window palette = window.OwnedWindows.Single(child => child.Title == "Search commands");
            Required<TextBox>(palette, "CommandSearch").Text = "authored product";
            await WaitUntilAsync(() =>
                Required<ListBox>(palette, "CommandResults").SelectedItem is
                    ListBoxItem { Tag: ViewerCommandIds.FileRenderAuthoredProduct });
            palette.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await WaitUntilAsync(
                () => window.OwnedWindows.Any(child => child.Title == "Render authored RenderProduct"));
            Window product = window.OwnedWindows.Single(child => child.Title == "Render authored RenderProduct");
            await WaitUntilAsync(() => Required<ComboBox>(product, "ProductSelector").SelectedItem is not null);
            await Assert.That(product.Owner).IsSameReferenceAs(window);
            await Assert.That(product.ActualThemeVariant).IsEqualTo(ThemeVariant.Dark);
            await Assert.That(AutomationProperties.GetName(Required<MenuItem>(window, "RenderAuthoredProductMenuItem")))
                .IsEqualTo("Render authored RenderProduct");
            ComboBox selector = Required<ComboBox>(product, "ProductSelector");
            await Assert.That(selector.ItemCount).IsEqualTo(2);
            await Assert.That(Required<TextBlock>(product, "ProductDetails").Text).Contains("32 x 16");
            await Assert.That(Required<TextBlock>(product, "ProductDetails").Text).Contains("/Camera");
            await Assert.That(Required<TextBlock>(product, "ProductDetails").Text).Contains("raw:color/half4");
            await Assert.That(Required<Button>(product, "ProductViewResultsButton").IsEnabled).IsFalse();

            string fullOutput = await RenderSelectedAsync(product, outputParent, start: "0", end: "2", step: "2");
            await AssertProductOutputAsync(fullOutput);
            CompletedRenderJobWindow results = await ViewerCompletedJobPreviewJourney.OpenAsync(
                product, "ProductViewResultsButton", fullOutput, [0, 1], [0, 2]);
            await ViewerCompletedJobPreviewJourney.CloseAsync(results, product, "ProductViewResultsButton");
            await ViewerCompletedJobPreviewJourney.RefuseTamperedOutputAsync(
                product, "ProductViewResultsButton", fullOutput, "frame-000001.png");
            string configuredStatusFile = ViewerStartupOptions.StatusFile ??
                throw new InvalidOperationException("The product journey requires a configured status log.");
            string previewLog = await File.ReadAllTextAsync(configuredStatusFile);
            await Assert.That(previewLog).Contains("Could not preview results:");
            await Assert.That(previewLog).Contains("SHA256");

            Required<TextBox>(product, "ProductSettingsPath").Text = "/SettingsPreview";
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            await Assert.That(Required<Button>(product, "ProductRenderButton").IsEnabled).IsFalse();
            Required<Button>(product, "ProductRefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => Required<Button>(product, "ProductRenderButton").IsEnabled);
            string previewOutput = await RenderSelectedAsync(product, outputParent, start: "0", end: "0", step: "1");
            byte[] previewHalf = await File.ReadAllBytesAsync(Path.Combine(previewOutput, "frame-000000.hdr.rgba16f"));
            byte[] fullHalf = await File.ReadAllBytesAsync(Path.Combine(fullOutput, "frame-000000.hdr.rgba16f"));
            await Assert.That(previewHalf.SequenceEqual(fullHalf)).IsFalse();
            ViewerNativeCaptureBackend.RecordComposition(
                window, session, Path.Combine(root, "product-composition.json"));
            await AssertHalf(previewHalf, 4, 4, "000000540000003C");
            Required<TextBox>(product, "ProductSettingsPath").Text = "/SettingsFull";
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            Required<Button>(product, "ProductRefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => Required<Button>(product, "ProductRenderButton").IsEnabled);
            Required<ComboBox>(product, "ProductHdrFormat").SelectedIndex = 1;
            await Assert.That(Required<Button>(product, "ProductRenderButton").IsEnabled).IsFalse();
            await Assert.That(Required<TextBlock>(product, "ProductStatus").Text)
                .Contains("EXR output currently refuses");
            int outputsBeforeUnsupported = Directory.GetDirectories(outputParent).Length;
            selector.SelectedIndex = 1;
            await WaitUntilAsync(() => !Required<Button>(product, "ProductRenderButton").IsEnabled);
            await Assert.That(Required<TextBlock>(product, "ProductStatus").Text).Contains("unsupported 'id' / 'int'");
            await Assert.That(Directory.GetDirectories(outputParent).Length).IsEqualTo(outputsBeforeUnsupported);

            await Assert.That(session.CurrentRenderState.Camera).IsEqualTo(before.Camera);
            await Assert.That(session.CurrentRenderState.Time).IsEqualTo(before.Time);
            await Assert.That(session.CurrentRenderState.Display).IsEqualTo(before.Display);
            await Assert.That(session.CurrentRenderState.Selection).IsEqualTo(before.Selection);
            await Assert.That(session.CurrentRenderState.RenderSettings).IsEqualTo(before.RenderSettings);
            await Assert.That(await session.Scheduler.InvokeAsync(static stage => stage.EditTargetLayerIdentifier))
                .IsEqualTo(editTarget);
            await Assert.That((await File.ReadAllBytesAsync(stagePath)).SequenceEqual(sourceBytes)).IsTrue();
            await Assert.That(File.Exists(Path.Combine(outputParent, "..", "must-not-be-used.exr"))).IsFalse();
            CompletedRenderJobWindow reloading = await ViewerCompletedJobPreviewJourney.OpenAsync(
                product, "ProductViewResultsButton", previewOutput, [0], [0]);
            Bitmap[] reloadingImages = Required<ListBox>(reloading, "ResultsFrames").Items
                .Cast<CompletedRenderJobTile>().Select(static tile => tile.Image).ToArray();
            ViewerStageSession previous = session;
            opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            Required<MenuItem>(window, "ReloadStageMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            session = await opened.Task.WaitAsync(TimeSpan.FromSeconds(40));
            await Assert.That(session).IsNotSameReferenceAs(previous);
            await ViewerCompletedJobPreviewJourney.AssertReleasedAsync(reloading, reloadingImages);
            await Assert.That(product.IsVisible).IsFalse();
            await Assert.That((await File.ReadAllBytesAsync(stagePath)).SequenceEqual(sourceBytes)).IsTrue();
            await ExerciseRunningProductTransitionsAsync(window, session, (AuthoredRenderProductWindow)product,
                outputParent, () =>
                {
                    opened = new TaskCompletionSource<ViewerStageSession>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    return opened.Task;
                }, closed.Task);
            await Assert.That((await File.ReadAllBytesAsync(stagePath)).SequenceEqual(sourceBytes)).IsTrue();
        }
        finally
        {
            foreach (Window owned in window.OwnedWindows.ToArray())
            {
                owned.Close();
            }
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Environment.SetEnvironmentVariable("OPENUSD_STATUS_FILE", null);
        }
    }

    private static async Task<string> RenderSelectedAsync(
        Window product, string outputParent, string start, string end, string step)
    {
        Required<ComboBox>(product, "ProductHdrFormat").SelectedIndex = 0;
        Required<TextBox>(product, "ProductStartTime").Text = start;
        Required<TextBox>(product, "ProductEndTime").Text = end;
        Required<TextBox>(product, "ProductStep").Text = step;
        Required<TextBox>(product, "ProductOutputFolder").Text = outputParent;
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        await Assert.That(Required<Button>(product, "ProductRenderButton").IsEnabled).IsTrue();
        Required<Button>(product, "ProductRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitUntilAsync(() =>
            !((AuthoredRenderProductWindow)product).IsRunning &&
            Required<TextBlock>(product, "ProductStatus").Text?.StartsWith("Completed", StringComparison.Ordinal) ==
            true && Required<Button>(product, "ProductRenderButton").IsEnabled);
        return Required<TextBox>(product, "ProductOutputLocation").Text ??
            throw new InvalidOperationException("A completed product render must expose its output location.");
    }

    private static async Task AssertProductOutputAsync(string output)
    {
        await Assert.That(output).DoesNotContain("must-not-be-used");
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(output, "manifest.json")));
        JsonElement frames = manifest.RootElement.GetProperty("frames");
        await Assert.That(frames.GetArrayLength()).IsEqualTo(2);
        for (int index = 0; index < 2; index++)
        {
            JsonElement frame = frames[index];
            await Assert.That(frame.GetProperty("width").GetInt32()).IsEqualTo(16);
            await Assert.That(frame.GetProperty("height").GetInt32()).IsEqualTo(16);
            await Assert.That(frame.GetProperty("timeCode").GetDouble()).IsEqualTo(index * 2d);
            JsonElement raster = frame.GetProperty("productRaster");
            await Assert.That(raster.GetProperty("fullWidth").GetInt32()).IsEqualTo(32);
            await Assert.That(raster.GetProperty("fullHeight").GetInt32()).IsEqualTo(16);
            await Assert.That(raster.GetProperty("dataWindowMinX").GetInt32()).IsEqualTo(8);
            await Assert.That(raster.GetProperty("dataWindowMinY").GetInt32()).IsEqualTo(0);
            await Assert.That(File.Exists(Path.Combine(output, $"frame-{index:D6}.png"))).IsTrue();
            byte[] half = await File.ReadAllBytesAsync(Path.Combine(output, $"frame-{index:D6}.hdr.rgba16f"));
            await AssertHalf(half, 4, 4, index == 0 ? "005400000000003C" : "000000000000003C");
            await AssertHalf(half, 12, 4, "005400000000003C");
        }
    }

    private static async Task AssertHalf(byte[] pixels, int x, int y, string expected) =>
        await Assert.That(Convert.ToHexString(pixels.AsSpan((y * 16 + x) * 8, 8))).IsEqualTo(expected);

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing product control: {name}");

    private static async Task WaitUntilAsync(Func<bool> condition, Func<string>? describe = null)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(35);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    "The authored-product workflow did not reach the expected state. " + describe?.Invoke());
            }
            await Task.Delay(20);
        }
    }

    private const string ProductScene = """
        #usda 1.0
        (
            renderSettingsPrimPath = "/SettingsFull"
            metersPerUnit = 1
            upAxis = "Y"
        )
        def Camera "Camera" {
            token projection = "orthographic"
            float horizontalAperture = 40
            float verticalAperture = 40
            float2 clippingRange = (1, 11)
        }
        def RenderSettings "SettingsFull" {
            rel camera = </Camera>
            rel products = </Product>
            int2 resolution = (16, 16)
            uniform token[] includedPurposes = ["default", "render"]
            uniform token[] materialBindingPurposes = ["full"]
        }
        def RenderSettings "SettingsPreview" {
            rel camera = </Camera>
            rel products = </Product>
            int2 resolution = (16, 16)
            uniform token[] includedPurposes = ["default", "proxy"]
            uniform token[] materialBindingPurposes = ["preview"]
        }
        def RenderProduct "Product" {
            token productName = "../must-not-be-used.exr"
            rel orderedVars = </Color>
        }
        def RenderVar "Color" {
            token dataType = "half4"
            string sourceName = "color"
            token sourceType = "raw"
        }
        def Scope "Looks" {
            def Material "Full" {
                token outputs:surface.connect = </Looks/Full/Shader.outputs:surface>
                def Shader "Shader" {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0, 0, 0)
                    color3f inputs:emissiveColor = (64, 0, 0)
                    int inputs:useSpecularWorkflow = 1
                    color3f inputs:specularColor = (0, 0, 0)
                    token outputs:surface
                }
            }
            def Material "Preview" {
                token outputs:surface.connect = </Looks/Preview/Shader.outputs:surface>
                def Shader "Shader" {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0, 0, 0)
                    color3f inputs:emissiveColor = (0, 64, 0)
                    int inputs:useSpecularWorkflow = 1
                    color3f inputs:specularColor = (0, 0, 0)
                    token outputs:surface
                }
            }
            def Material "Legacy" {
                token outputs:surface.connect = </Looks/Legacy/Shader.outputs:surface>
                def Shader "Shader" {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0, 0, 0)
                    color3f inputs:emissiveColor = (0, 0, 64)
                    int inputs:useSpecularWorkflow = 1
                    color3f inputs:specularColor = (0, 0, 0)
                    token outputs:surface
                }
            }
        }
        def Xform "World" (prepend apiSchemas = ["MaterialBindingAPI"]) {
            rel material:binding = </Looks/Legacy>
            rel material:binding:full = </Looks/Full>
            rel material:binding:preview = </Looks/Preview>
            def Cube "Default" {
                double size = 0.75
                double3 xformOp:translate = (-1, 1, -4)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def Cube "Render" {
                uniform token purpose = "render"
                double size = 0.75
                double3 xformOp:translate = (1, 1, -4)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def Xform "ProxyParent" {
                uniform token purpose = "proxy"
                def Cube "InheritedProxy" {
                    double size = 0.75
                    double3 xformOp:translate = (-1, -1, -4)
                    uniform token[] xformOpOrder = ["xformOp:translate"]
                }
            }
            def Cube "Guide" {
                uniform token purpose = "guide"
                double size = 0.75
                double3 xformOp:translate = (1, -1, -4)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
        }
        """;
}
