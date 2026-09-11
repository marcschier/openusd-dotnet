// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUsd.Render;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerAuthoredRenderProductTests
{
    [Test]
    public async Task SnapshotKeepsUnsupportedProductsVisibleWithoutAdmittingThemAsBeauty()
    {
        UsdRenderSpecification specification = CreateSpecification([
            Product("/Render/ProductBeauty", [0, 1]),
            Product("/Render/ProductUnsupported", [2])
        ]);

        ViewerAuthoredRenderProductSnapshot snapshot =
            ViewerAuthoredRenderProductSelection.CreateSnapshot(specification, "/Render/ProductUnsupported");

        await Assert.That(snapshot.Products.Count).IsEqualTo(2);
        await Assert.That(snapshot.Products[0].CanRender).IsTrue();
        await Assert.That(snapshot.Products[0].VariableSummary).Contains("raw:color/half4");
        await Assert.That(snapshot.Products[0].VariableSummary).Contains("raw:depth/float");
        await Assert.That(snapshot.Products[1].CanRender).IsFalse();
        await Assert.That(snapshot.Products[1].UnsupportedReason).Contains("unsupported 'id' / 'int'");
        await Assert.That(snapshot.Products[1].Label).StartsWith("* /Render/ProductUnsupported");
    }

    [Test]
    public async Task SnapshotRefusesAnOversizedCatalogInsteadOfSilentlyHidingProducts()
    {
        UsdRenderSpecification specification = CreateSpecification(
            Enumerable.Range(0, 257).Select(index => Product($"/Render/Product{index}", [0])).ToArray());

        ViewerAuthoredRenderProductSnapshot atLimit = ViewerAuthoredRenderProductSelection.CreateSnapshot(
            CreateSpecification(specification.Products.Take(256).ToArray()), "/Render/Product255");
        await Assert.That(atLimit.Products.Count).IsEqualTo(256);
        await Assert.That(atLimit.Error).IsNull();
        await Assert.That(atLimit.Products[255].Label).StartsWith("* /Render/Product255");

        ViewerAuthoredRenderProductSnapshot snapshot =
            ViewerAuthoredRenderProductSelection.CreateSnapshot(specification, "/Render/Product256");

        await Assert.That(snapshot.SettingsPath).IsEqualTo("/Render/Settings");
        await Assert.That(snapshot.Products).IsEmpty();
        await Assert.That(snapshot.Error).Contains("257");
        await Assert.That(snapshot.Error).Contains("256");
    }

    [Test]
    [Arguments("purpose", "Unknown included purpose")]
    [Arguments("binding-length", "128 characters")]
    public async Task SnapshotUsesTheSharedProductAdmissionRules(string failure, string expectedReason)
    {
        UsdRenderSpecification original = CreateSpecification([Product("/Render/ProductBeauty", [0])]);
        var specification = new UsdRenderSpecification(
            original.SettingsPath, original.Products, original.RenderVariables,
            failure == "purpose" ? ["custom"] : original.IncludedPurposes,
            failure == "binding-length" ? [new string('x', 129)] : original.MaterialBindingPurposes,
            original.RenderingColorSpace, original.NamespacedSettingNames);

        ViewerAuthoredRenderProductSnapshot snapshot =
            ViewerAuthoredRenderProductSelection.CreateSnapshot(specification, null);

        await Assert.That(snapshot.Products.Count).IsEqualTo(1);
        await Assert.That(snapshot.Products[0].Path).IsEqualTo("/Render/ProductBeauty");
        await Assert.That(snapshot.Products[0].CanRender).IsFalse();
        await Assert.That(snapshot.Products[0].UnsupportedReason).Contains(expectedReason);
    }

    [Test]
    public async Task OutputParentRequiresAnExistingAbsoluteDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "viewer-authored-products-" + Guid.NewGuid().ToString("N"));
        try
        {
            await Assert.That(ViewerAuthoredRenderProductSelection.ValidateOutputParent("relative"))
                .Contains("absolute");
            await Assert.That(ViewerAuthoredRenderProductSelection.ValidateOutputParent(root))
                .Contains("must already exist");
            Directory.CreateDirectory(root);
            await Assert.That(ViewerAuthoredRenderProductSelection.ValidateOutputParent(root)).IsNull();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task RangeBoundsRejectInvalidInputsBeforePublication()
    {
        await Assert.That(() => new ViewerRenderSequenceRange(0, 4097, 1))
            .Throws<ArgumentException>();
        await Assert.That(() => new ViewerRenderSequenceRange(0, 1, 0))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task DialogHostOwnsItsDispatcherAfterAnotherThreadHasUsedAvalonia()
    {
        _ = Dispatcher.UIThread;
        int calls = 0;
        for (int run = 0; run < 2; run++)
        {
            await RunUiAsync(async () =>
            {
                await Assert.That(Dispatcher.UIThread.CheckAccess()).IsTrue();
                await Task.Yield();
                await Assert.That(Dispatcher.UIThread.CheckAccess()).IsTrue();
                await Assert.That(Application.Current).IsTypeOf<App>();
                calls++;
            });
        }
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task DialogLifecycleDoesNotCreateFoldersSwitchSelectionOrCloseBeforeDraining()
    {
        await RunUiAsync(async () =>
        {
            await ExerciseNoImplicitDirectoryCreationAsync();
            await ExerciseUnsupportedSelectionPreservationAsync();
            await ExerciseSettingsMismatchAndExrRefusalAsync();
            await ExerciseStaleRefreshFailureDoesNotClearCurrentProductsAsync();
            await ExerciseAggregateCleanupFailureIsSurfacedAsync();
            await ExerciseRestoreFailurePreventsCompletedDirectoryPublicationAsync();
            string root = Path.Combine(Path.GetTempPath(), "viewer-authored-products-close-" +
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var queryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var renderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int queryCalls = 0;
            var window = new AuthoredRenderProductWindow(
                0,
                0,
                "unit renderer",
                async (_, _, token) =>
                {
                    if (Interlocked.Increment(ref queryCalls) == 1)
                    {
                        queryStarted.SetResult();
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    }
                    return ViewerAuthoredRenderProductSelection.CreateSnapshot(
                        CreateSpecification([Product("/Render/ProductBeauty", [0])]),
                        null);
                },
                async (_, _, token) =>
                {
                    renderStarted.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    throw new InvalidOperationException("Render should be canceled.");
                });
            try
            {
                window.Show();
                await queryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                window.Close();
                window.Close();
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));

                var renderWindow = new AuthoredRenderProductWindow(
                    0,
                    0,
                    "unit renderer",
                    (_, _, _) => ValueTask.FromResult(ViewerAuthoredRenderProductSelection.CreateSnapshot(
                        CreateSpecification([Product("/Render/ProductBeauty", [0])]),
                        null)),
                    async (request, _, token) =>
                    {
                        renderStarted.SetResult();
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                        throw new InvalidOperationException("Render should be canceled.");
                    });
                try
                {
                    renderWindow.Show();
                    await WaitUntilAsync(
                        () => Required<ComboBox>(renderWindow, "ProductSelector").SelectedItem is not null);
                    Required<TextBox>(renderWindow, "ProductOutputFolder").Text = root;
                    Required<Button>(renderWindow, "ProductRenderButton")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await renderStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    renderWindow.Close();
                    renderWindow.Close();
                    await renderWindow.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    await Assert.That(Directory.EnumerateFileSystemEntries(root)).IsEmpty();
                }
                finally
                {
                    await renderWindow.CloseAsync();
                }
            }
            finally
            {
                await window.CloseAsync();
                Directory.Delete(root, recursive: true);
            }
        });
    }

    private static async Task ExerciseNoImplicitDirectoryCreationAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "viewer-authored-products-dialog-" +
            Guid.NewGuid().ToString("N"));
        var window = new AuthoredRenderProductWindow(
            0,
            0,
            "unit renderer",
            (_, _, _) => ValueTask.FromResult(ViewerAuthoredRenderProductSelection.CreateSnapshot(
                CreateSpecification([Product("/Render/ProductBeauty", [0])]),
                null)),
            (_, _, _) => throw new InvalidOperationException("Dialog construction must not render."));
        try
        {
            window.Show();
            await WaitUntilAsync(() => Required<ComboBox>(window, "ProductSelector").SelectedItem is not null);
            TextBox output = Required<TextBox>(window, "ProductOutputFolder");
            await Assert.That(output.Text).IsNotEqualTo(@"D:\artifacts\viewer-authored-products");
            output.Text = root;
            await WaitUntilAsync(() => Required<TextBlock>(window, "ProductStatus").Text?.Contains(
                "must already exist", StringComparison.Ordinal) == true);
            await Assert.That(Directory.Exists(root)).IsFalse();
            await Assert.That(Required<Button>(window, "ProductRenderButton").IsEnabled).IsFalse();
            window.Close();
            await window.CloseAsync();
            await Assert.That(Directory.Exists(root)).IsFalse();
        }
        finally
        {
            await window.CloseAsync();
        }
    }

    private static async Task ExerciseUnsupportedSelectionPreservationAsync()
    {
        UsdRenderSpecification specification = CreateSpecification([
            Product("/Render/ProductBeauty", [0]),
            Product("/Render/ProductUnsupported", [2])
        ]);
        var window = new AuthoredRenderProductWindow(
            0,
            0,
            "unit renderer",
            (_, selected, _) => ValueTask.FromResult(
                ViewerAuthoredRenderProductSelection.CreateSnapshot(specification, selected)),
            (_, _, _) => throw new InvalidOperationException("Refresh must not render."));
        try
        {
            window.Show();
            await WaitUntilAsync(() => Required<ComboBox>(window, "ProductSelector").ItemCount == 2);
            ComboBox selector = Required<ComboBox>(window, "ProductSelector");
            selector.SelectedIndex = 1;
            await WaitUntilAsync(() => !Required<Button>(window, "ProductRenderButton").IsEnabled);
            Required<Button>(window, "ProductRefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => selector.SelectedItem is ViewerAuthoredRenderProductItem item &&
                item.Path == "/Render/ProductUnsupported");
            await Assert.That(Required<TextBlock>(window, "ProductStatus").Text)
                .Contains("unsupported 'id' / 'int'");
        }
        finally
        {
            await window.CloseAsync();
        }
    }

    private static async Task ExerciseSettingsMismatchAndExrRefusalAsync()
    {
        UsdRenderSpecification specification = CreateSpecification([
            new UsdRenderProductSpecification(
                    "/Render/ProductBeauty",
                    "beauty",
                    "raster",
                    "/World/Camera",
                    5,
                    3,
                    1,
                    "expandAperture",
                    new UsdVec2f(20, 10),
                    new UsdVec4f(0.25f, 0, 0.75f, 1),
                    disableMotionBlur: true,
                    disableDepthOfField: true,
                    [0],
                    [])
        ]);
        var window = new AuthoredRenderProductWindow(
            0,
            0,
            "unit renderer",
            (_, selected, _) => ValueTask.FromResult(
                ViewerAuthoredRenderProductSelection.CreateSnapshot(specification, selected)),
            (_, _, _) => throw new InvalidOperationException("Admission test must not render."));
        try
        {
            window.Show();
            await WaitUntilAsync(() => Required<Button>(window, "ProductRenderButton").IsEnabled);
            Required<TextBox>(window, "ProductSettingsPath").Text = "/Render/OtherSettings";
            await WaitUntilAsync(() => !Required<Button>(window, "ProductRenderButton").IsEnabled);
            await Assert.That(Required<TextBlock>(window, "ProductStatus").Text).Contains("Refresh products");
            Required<TextBox>(window, "ProductSettingsPath").Text = "/Render/Settings";
            Required<Button>(window, "ProductRefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => Required<Button>(window, "ProductRenderButton").IsEnabled);
            Required<ComboBox>(window, "ProductHdrFormat").SelectedIndex = 1;
            await WaitUntilAsync(() => !Required<Button>(window, "ProductRenderButton").IsEnabled);
            await Assert.That(Required<TextBlock>(window, "ProductStatus").Text)
                .Contains("EXR output currently refuses");
        }
        finally
        {
            await window.CloseAsync();
        }
    }

    private static async Task ExerciseStaleRefreshFailureDoesNotClearCurrentProductsAsync()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var window = new AuthoredRenderProductWindow(
            0,
            0,
            "unit renderer",
            async (_, selected, token) =>
            {
                int call = Interlocked.Increment(ref calls);
                if (call == 1)
                {
                    return ViewerAuthoredRenderProductSelection.CreateSnapshot(
                        CreateSpecification([Product("/Render/ProductBeauty", [0])]),
                        selected);
                }
                first.SetResult();
                await releaseFailure.Task.WaitAsync(token);
                throw new InvalidOperationException("late stale query failure");
            },
            (_, _, _) => throw new InvalidOperationException("Refresh failure test must not render."));
        try
        {
            window.Show();
            await WaitUntilAsync(() => Required<ComboBox>(window, "ProductSelector").ItemCount == 1);
            Required<Button>(window, "ProductRefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Required<TextBox>(window, "ProductSettingsPath").Text = "/Render/OtherSettings";
            releaseFailure.SetResult();
            await Task.Delay(50);
            await Assert.That(Required<ComboBox>(window, "ProductSelector").ItemCount).IsEqualTo(1);
            await Assert.That(Required<TextBlock>(window, "ProductStatus").Text).Contains("Refresh products");
        }
        finally
        {
            await window.CloseAsync();
        }
    }

    private static async Task ExerciseAggregateCleanupFailureIsSurfacedAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "viewer-authored-products-aggregate-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var window = new AuthoredRenderProductWindow(
            0,
            0,
            "unit renderer",
            (_, selected, _) => ValueTask.FromResult(
                ViewerAuthoredRenderProductSelection.CreateSnapshot(
                    CreateSpecification([Product("/Render/ProductBeauty", [0])]),
                    selected)),
            (_, _, _) => throw new AggregateException(
                "Product capture cleanup failed.",
                new IOException("capturer dispose failed"),
                new IOException("session dispose failed")));
        try
        {
            window.Show();
            await WaitUntilAsync(() => Required<Button>(window, "ProductRenderButton").IsEnabled);
            Required<TextBox>(window, "ProductOutputFolder").Text = root;
            Required<Button>(window, "ProductRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => Required<TextBlock>(window, "ProductStatus").Text?.Contains(
                "Product render failed", StringComparison.Ordinal) == true);
            await Assert.That(Required<TextBlock>(window, "ProductStatus").Text)
                .Contains("Product capture cleanup failed");
            await Assert.That(Directory.EnumerateFileSystemEntries(root)).IsEmpty();
        }
        finally
        {
            await window.CloseAsync();
            Directory.Delete(root, recursive: true);
        }
    }
    private static async Task ExerciseRestoreFailurePreventsCompletedDirectoryPublicationAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "viewer-authored-products-restore-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string output = Path.Combine(root, "job");
            StageRenderState state = StageRenderState.Create(new StageIdentity("restore.usda"))
                .WithViewport(new ViewportDimensions(1, 1));
            var request = new RenderDiskJobRequest(output, [state]);
            ValueTask<ViewerFrameCaptureResult> capture(StageRenderState _, CancellationToken __) =>
                ValueTask.FromResult(new ViewerFrameCaptureResult(
                    1, 1, new byte[] { 255, 0, 0, 255 }, ViewerFrameRowOrder.TopDown));
            ValueTask restore() => throw new IOException("controlled cleanup failure");

            await Assert.That(async () => await ViewerRenderSequenceRunner.ExecuteAsync(
                request, capture, restore, _ => { }, CancellationToken.None)).Throws<IOException>();
            await Assert.That(Directory.Exists(output)).IsFalse();
            await Assert.That(Directory.EnumerateFileSystemEntries(root)).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static UsdRenderSpecification CreateSpecification(
        IReadOnlyList<UsdRenderProductSpecification> products) => new(
            "/Render/Settings",
            products,
            [
                Variable("/Render/Vars/Color", "half4", "color"),
                Variable("/Render/Vars/Depth", "float", "depth"),
                Variable("/Render/Vars/Id", "int", "id")
            ],
            ["default", "render"],
            ["full"],
            string.Empty,
            []);

    private static UsdRenderProductSpecification Product(string path, IReadOnlyList<int> variables) => new(
        path,
        $"product-{variables.Count}",
        "raster",
        "/World/Camera",
        5,
        3,
        1,
        "expandAperture",
        new UsdVec2f(20, 10),
        new UsdVec4f(0, 0, 1, 1),
        disableMotionBlur: true,
        disableDepthOfField: true,
        variables,
        []);

    private static UsdRenderVariableSpecification Variable(
        string path,
        string dataType,
        string sourceName) => new(path, dataType, sourceName, "raw", []);

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing product control: {name}");

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The product dialog did not reach the expected state.");
            }
            await Task.Delay(20);
        }
    }

    private static async Task RunUiAsync(Func<Task> action)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            await action();
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }
}
