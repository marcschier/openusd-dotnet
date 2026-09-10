// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia;
using Avalonia.Threading;
using OpenUsd.Render;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerAuthoredRenderProductNativeTests
{
    private static async Task ExerciseRunningProductTransitionsAsync(
        MainWindow owner, ViewerStageSession session, AuthoredRenderProductWindow initial,
        string outputParent, Func<Task<ViewerStageSession>> prepareReload, Task ownerClosed)
    {
        AuthoredRenderProductWindow product = initial;
        foreach (string action in new[] { "cancel", "double-close", "reload", "close" })
        {
            if (!product.IsVisible)
            {
                await WaitUntilAsync(() => Required<MenuItem>(owner, "RenderAuthoredProductMenuItem").IsEnabled);
                Required<MenuItem>(owner, "RenderAuthoredProductMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await WaitUntilAsync(() => owner.OwnedWindows.OfType<AuthoredRenderProductWindow>().Any());
                product = owner.OwnedWindows.OfType<AuthoredRenderProductWindow>().Single();
                await WaitUntilAsync(() => Required<ComboBox>(product, "ProductSelector").SelectedItem is not null);
            }
            Required<ComboBox>(product, "ProductSelector").SelectedIndex = 0;
            Required<ComboBox>(product, "ProductHdrFormat").SelectedIndex = 0;
            Required<TextBox>(product, "ProductStartTime").Text = "0";
            Required<TextBox>(product, "ProductEndTime").Text = "16";
            Required<TextBox>(product, "ProductStep").Text = "1";
            Required<TextBox>(product, "ProductOutputFolder").Text = outputParent;
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            await Assert.That(Required<Button>(product, "ProductRenderButton").IsEnabled).IsTrue();
            int published = Directory.GetDirectories(outputParent).Length;
            var before = session.CurrentRenderState;
            Task<ViewerStageSession>? reloaded = action == "reload" ? prepareReload() : null;
            TextBlock status = Required<TextBlock>(product, "ProductStatus");
            bool triggered = false;
            void OnProgress(object? sender, AvaloniaPropertyChangedEventArgs args)
            {
                if (triggered || args.Property != TextBlock.TextProperty ||
                    status.Text?.StartsWith("Rendering: 1/", StringComparison.Ordinal) != true)
                {
                    return;
                }
                triggered = true;
                switch (action)
                {
                    case "cancel":
                        Required<Button>(product, "ProductCancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;
                    case "double-close":
                        product.Close();
                        product.Close();
                        break;
                    case "reload":
                        Required<MenuItem>(owner, "ReloadStageMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        break;
                    case "close":
                        owner.Close();
                        break;
                }
            }
            status.PropertyChanged += OnProgress;
            try
            {
                Required<Button>(product, "ProductRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntilAsync(() => triggered && !product.IsRunning);
                if (action is "cancel" or "double-close")
                {
                    await Assert.That(session.CurrentRenderState.Camera).IsEqualTo(before.Camera);
                    await Assert.That(session.CurrentRenderState.Time).IsEqualTo(before.Time);
                    await Assert.That(session.CurrentRenderState.Display).IsEqualTo(before.Display);
                }
                if (action == "double-close")
                {
                    await product.CloseAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                if (reloaded is not null)
                {
                    ViewerStageSession prior = session;
                    session = await reloaded.WaitAsync(TimeSpan.FromSeconds(40));
                    await Assert.That(session).IsNotSameReferenceAs(prior);
                    await Assert.That(product.IsVisible).IsFalse();
                    await Assert.That(async () => await prior.Scheduler.InvokeAsync(static stage => stage.ChangeSerial))
                        .Throws<ObjectDisposedException>();
                }
                if (action == "close")
                {
                    await ownerClosed.WaitAsync(TimeSpan.FromSeconds(15));
                    await Assert.That(product.IsVisible).IsFalse();
                }
                await Assert.That(Directory.GetDirectories(outputParent).Length).IsEqualTo(published);
                await Assert.That(Directory.GetDirectories(outputParent, ".openusd-render-*")).IsEmpty();
            }
            finally
            {
                status.PropertyChanged -= OnProgress;
            }
        }
    }

    private static async Task ExerciseProductDialogLifetimesAsync(string root)
    {
        var owner = new Window { Width = 640, Height = 480 };
        owner.Show();
        try
        {
            foreach (bool fail in new[] { false, true })
            {
                await CloseDuringProductQueryAsync(owner, root, fail);
            }
            await CloseDuringProductRenderAsync(owner, root);
            await StaleProductQueryCannotReplaceCurrentSelectionAsync(owner, root);
            await ProductCleanupFailuresAreSurfacedAsync(owner, root);
        }
        finally
        {
            owner.Close();
        }
    }

    private static async Task CloseDuringProductQueryAsync(Window owner, string root, bool fail)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ViewerAuthoredRenderProductSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken queryToken = default;
        string[] before = Directory.GetFileSystemEntries(root);
        var window = new AuthoredRenderProductWindow(0, 0, "D3D12", (_, _, token) =>
        {
            queryToken = token;
            entered.TrySetResult();
            return new ValueTask<ViewerAuthoredRenderProductSnapshot>(release.Task);
        }, static (_, _, _) => throw new InvalidOperationException("No render should start."));
        try
        {
            window.Show(owner);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task closed = window.CloseAsync();
            window.Close();
            await Assert.That(closed.IsCompleted).IsFalse();
            await Assert.That(window.IsVisible).IsTrue();
            await Assert.That(queryToken.IsCancellationRequested).IsTrue();
            if (fail)
            {
                release.SetException(new ArgumentException("Old query failure after close."));
            }
            else
            {
                release.SetResult(DialogSnapshot("/Late"));
            }
            await closed.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(window.IsVisible).IsFalse();
            await Assert.That(Required<TextBox>(window, "ProductSettingsPath").Text).IsNullOrEmpty();
            await Assert.That(Directory.GetFileSystemEntries(root)).IsEquivalentTo(before);
        }
        finally
        {
            release.TrySetResult(DialogSnapshot("/Late"));
            await window.CloseAsync();
        }
    }

    private static async Task CloseDuringProductRenderAsync(Window owner, string root)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken renderToken = default;
        var window = new AuthoredRenderProductWindow(0, 0, "D3D12",
            static (_, _, _) => ValueTask.FromResult(DialogSnapshot("/Settings")),
            async (_, _, token) =>
            {
                renderToken = token;
                entered.TrySetResult();
                await release.Task;
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("The controlled render must be cancelled.");
            });
        try
        {
            window.Show(owner);
            await WaitUntilAsync(() => Required<ComboBox>(window, "ProductSelector").SelectedItem is not null);
            Required<TextBox>(window, "ProductOutputFolder").Text = root;
            Required<Button>(window, "ProductRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task closed = window.CloseAsync();
            window.Close();
            await Assert.That(closed.IsCompleted).IsFalse();
            await Assert.That(window.IsVisible).IsTrue();
            await Assert.That(renderToken.IsCancellationRequested).IsTrue();
            release.SetResult();
            await closed.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(window.IsVisible).IsFalse();
            await Assert.That(Required<TextBox>(window, "ProductOutputLocation").Text).IsNullOrEmpty();
        }
        finally
        {
            release.TrySetResult();
            await window.CloseAsync();
        }
    }

    private static async Task StaleProductQueryCannotReplaceCurrentSelectionAsync(Window owner, string root)
    {
        int queries = 0;
        string? backendReason = null;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stale = new TaskCompletionSource<ViewerAuthoredRenderProductSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new AuthoredRenderProductWindow(0, 0, "D3D12", (path, _, _) =>
        {
            queries++;
            if (queries == 2)
            {
                entered.TrySetResult();
                return new ValueTask<ViewerAuthoredRenderProductSnapshot>(stale.Task);
            }
            return ValueTask.FromResult(DialogSnapshot(path ?? "/One"));
        }, static (_, _, _) => throw new InvalidOperationException("No render should start."), () => backendReason);
        try
        {
            window.Show(owner);
            await WaitUntilAsync(() => Required<ComboBox>(window, "ProductSelector").SelectedItem is not null);
            Required<TextBox>(window, "ProductOutputFolder").Text = root;
            Required<Button>(window, "ProductRefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Required<TextBox>(window, "ProductSettingsPath").Text = "/Two";
            await Assert.That(Required<Button>(window, "ProductRenderButton").IsEnabled).IsFalse();
            stale.SetException(new ArgumentException("This stale failure must not replace the current catalog."));
            await WaitUntilAsync(() => Required<Button>(window, "ProductRefreshButton").IsEnabled);
            await Assert.That(Required<TextBlock>(window, "ProductStatus").Text).Contains("Refresh");
            await Assert.That(Required<ComboBox>(window, "ProductSelector").ItemCount).IsEqualTo(1);
            Required<Button>(window, "ProductRefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => Required<Button>(window, "ProductRenderButton").IsEnabled);
            await Assert.That(queries).IsEqualTo(3);
            backendReason = "Unsupported renderer.";
            window.UpdateContext("Storm");
            await Assert.That(Required<Button>(window, "ProductRenderButton").IsEnabled).IsFalse();
            await Assert.That(Required<TextBlock>(window, "ProductStatus").Text).IsEqualTo(backendReason);
            backendReason = null;
            window.UpdateContext("D3D12");
            await Assert.That(Required<Button>(window, "ProductRenderButton").IsEnabled).IsTrue();
        }
        finally
        {
            stale.TrySetResult(DialogSnapshot("/Stale"));
            await window.CloseAsync();
        }
    }

    private static async Task ProductCleanupFailuresAreSurfacedAsync(Window owner, string root)
    {
        using var registry = new ViewerProductResourceRegistry();
        ViewerProductResourceRegistry.Lease resources = registry.CreateLease();
        var failingResource = resources.Own(new CleanupFailure());
        bool hadStagedFrame = false;
        string destination = Path.Combine(root, "cleanup-must-not-publish");
        var window = new AuthoredRenderProductWindow(0, 0, "D3D12",
            static (_, _, _) => ValueTask.FromResult(DialogSnapshot("/Settings")),
            async (_, _, token) =>
            {
                StageRenderState first = StageRenderState.Create(new StageIdentity("cleanup-fixture"))
                    .WithViewport(new ViewportDimensions(1, 1));
                var request = new RenderDiskJobRequest(destination, [first, first.WithTime(new StageTime(1))]);
                try
                {
                    return await ViewerRenderSequenceRunner.ExecuteAsync(request,
                        static (_, _) => ValueTask.FromResult(
                            new ViewerFrameCaptureResult(1, 1, new byte[4], ViewerFrameRowOrder.TopDown)),
                        () =>
                        {
                            string staging = Directory.GetDirectories(root, ".openusd-render-*").Single();
                            hadStagedFrame = File.Exists(Path.Combine(staging, "frame-000000.png"));
                            resources.Dispose();
                            return ValueTask.CompletedTask;
                        },
                        static _ => { }, token);
                }
                finally
                {
                    resources.Dispose();
                }
            });
        try
        {
            window.Show(owner);
            await WaitUntilAsync(() => Required<ComboBox>(window, "ProductSelector").SelectedItem is not null);
            Required<TextBox>(window, "ProductOutputFolder").Text = root;
            Required<Button>(window, "ProductRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => !window.IsRunning);
            await Assert.That(Required<TextBlock>(window, "ProductStatus").Text).StartsWith("Product render failed");
            await Assert.That(Required<TextBox>(window, "ProductOutputLocation").Text).IsNullOrEmpty();
            await Assert.That(window.IsVisible).IsTrue();
            await Assert.That(hadStagedFrame).IsTrue();
            await Assert.That(Directory.Exists(destination)).IsFalse();
            await Assert.That(Directory.GetDirectories(root, ".openusd-render-*")).IsEmpty();
            await Assert.That(failingResource.Attempts).IsEqualTo(2);
            await Assert.That(registry.Count).IsEqualTo(1);
            registry.Dispose();
            await Assert.That(failingResource.Attempts).IsEqualTo(3);
            await Assert.That(registry.Count).IsEqualTo(0);
        }
        finally
        {
            await window.CloseAsync();
        }
    }

    private sealed class CleanupFailure : IDisposable
    {
        internal int Attempts { get; private set; }

        public void Dispose()
        {
            Attempts++;
            if (Attempts <= 2)
            {
                throw new IOException("Controlled native release failure.");
            }
        }
    }

    private static ViewerAuthoredRenderProductSnapshot DialogSnapshot(string settingsPath) =>
        ViewerAuthoredRenderProductSelection.CreateSnapshot(new UsdRenderSpecification(settingsPath,
            [new UsdRenderProductSpecification("/Product", "ignored.exr", "raster", "/Camera",
                2, 2, 1, "expandAperture", new UsdVec2f(20, 20), new UsdVec4f(0, 0, 1, 1), true, true, [0], [])],
            [new UsdRenderVariableSpecification("/Color", "half4", "color", "raw", [])],
            ["default"], ["full"], "", []), null);
}
