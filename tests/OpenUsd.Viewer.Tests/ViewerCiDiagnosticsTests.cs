// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerCiDiagnosticsTests
{
    [Test]
    public async Task InjectedDispatcherFontFailureReportsInheritedSourceAndPendingAction()
    {
        RequireDiagnostics();
        var messages = new ConcurrentQueue<string>();
        using var diagnostics = ViewerCiDiagnostics.StartControl("font-control", message =>
        {
            messages.Enqueue(message);
            Console.Error.WriteLine(message);
        });
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaEntryPoint));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<int> action()
        {
            var parent = new StackPanel { Name = "DiagnosticFontParent" };
            var text = new TextBlock { Name = "DiagnosticFontText", Text = "diagnostic" };
            parent.Children.Add(text);
            diagnostics.TrackRoot(parent);
            parent.SetValue(TextElement.FontStretchProperty, (FontStretch)0);
            diagnostics.TrackClosed(closed.Task);
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    text.Measure(new Size(100, 100));
                }
                finally
                {
                    release.TrySetResult();
                }
            });
            await release.Task;
            return 0;
        }

        Task<int> dispatched = session.Dispatch<int>(() => diagnostics.TrackAction(action()), lifetime.Token);
        diagnostics.TrackDispatch(dispatched);
        try
        {
            await Assert.That(async () => await dispatched.WaitAsync(lifetime.Token))
                .ThrowsExactly<ArgumentException>().WithMessage("Font stretch must be > 1.");
        }
        finally
        {
            release.TrySetResult();
        }

        string[] output = messages.ToArray();
        await Assert.That(output.Any(message => message.Contains(
            "font-change type=StackPanel name=DiagnosticFontParent value=0 priority=LocalValue",
            StringComparison.Ordinal))).IsTrue();
        await Assert.That(output.Any(message =>
            message.Contains("font-exception action=WaitingForActivation", StringComparison.Ordinal) &&
            message.Contains("closed=WaitingForActivation", StringComparison.Ordinal) &&
            message.Contains("PushFrame", StringComparison.Ordinal))).IsTrue();
        await Assert.That(output.Any(message => message.Contains(
            "font-candidate type=TextBlock name=DiagnosticFontText value=0 priority=Inherited",
            StringComparison.Ordinal))).IsTrue();
        await Assert.That(output.Any(message => message.Contains(
            "font-source depth=1 type=StackPanel name=DiagnosticFontParent value=0 priority=LocalValue",
            StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task LockedTemporarySettingsFileReportsTaskStatesAndOwner()
    {
        RequireDiagnostics();
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("The temporary-file sharing control requires Windows.");
        }

        var messages = new ConcurrentQueue<string>();
        using var diagnostics = ViewerCiDiagnostics.StartControl("settings-control", message =>
        {
            messages.Enqueue(message);
            Console.Error.WriteLine(message);
        });
        using var files = new ViewerCompletedJobTestFiles();
        diagnostics.TrackFixtureRoot(files.Root);
        var action = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatched = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = diagnostics.TrackAction(action.Task);
        diagnostics.TrackDispatch(dispatched.Task);
        diagnostics.TrackClosed(closed.Task);
        string directory = Path.Combine(files.Root, "settings");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "viewer-settings.txt.control.tmp");
        using (var writer = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            diagnostics.Mark("main-close-returned");
            await Assert.That(() => Directory.Delete(files.Root, recursive: true)).ThrowsExactly<IOException>();
            await Assert.That(() => throw new IOException($"Cannot remove {path}."))
                .ThrowsExactly<IOException>();

            string[] output = messages.ToArray();
            await Assert.That(output.Any(message =>
                message.Contains("settings-snapshot-file point=main-close-returned", StringComparison.Ordinal) &&
                message.Contains(path, StringComparison.OrdinalIgnoreCase))).IsTrue();
            await Assert.That(output.Any(message =>
                message.Contains("settings-file-exception action=WaitingForActivation", StringComparison.Ordinal) &&
                message.Contains("dispatch=WaitingForActivation", StringComparison.Ordinal) &&
                message.Contains("closed=WaitingForActivation", StringComparison.Ordinal))).IsTrue();
            await Assert.That(output.Any(message =>
                message.Contains("settings-read-probe=failed", StringComparison.Ordinal) &&
                message.Contains("hresult=0x80070020", StringComparison.Ordinal) &&
                message.Contains(path, StringComparison.OrdinalIgnoreCase))).IsTrue();
            await Assert.That(output.Any(message => message.Contains(
                $"settings-resource-user-pid={Environment.ProcessId} ", StringComparison.Ordinal))).IsTrue();
        }
    }

    [Test]
    public async Task TemporaryFileNameOnlyIsMatchedToItsFixture()
    {
        RequireDiagnostics();
        var messages = new ConcurrentQueue<string>();
        using var diagnostics = ViewerCiDiagnostics.StartControl("filename-control", messages.Enqueue);
        using var files = new ViewerCompletedJobTestFiles();
        string directory = Path.Combine(files.Root, "settings");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "viewer-settings.txt.control.tmp");
        File.WriteAllText(path, "test");
        diagnostics.TrackFixtureRoot(files.Root);

        await Assert.That(() => throw new IOException("Cannot remove viewer-settings.txt.other.tmp."))
            .ThrowsExactly<IOException>();
        await Assert.That(messages.Any(message => message.Contains(
            "settings-file-exception", StringComparison.Ordinal))).IsFalse();

        string unrelatedDirectory = Path.Combine(files.Root, "other");
        Directory.CreateDirectory(unrelatedDirectory);
        string unrelatedPath = Path.Combine(unrelatedDirectory, Path.GetFileName(path));
        File.WriteAllText(unrelatedPath, "other");
        await Assert.That(() => throw new IOException($"Cannot remove {unrelatedPath}."))
            .ThrowsExactly<IOException>();
        await Assert.That(messages.Any(message => message.Contains(
            "settings-file-exception", StringComparison.Ordinal))).IsFalse();

        await Assert.That(() => throw new IOException($"Cannot remove {Path.GetFileName(path)}."))
            .ThrowsExactly<IOException>();
        await Assert.That(messages.Any(message => message.Contains(
            "settings-file-exception", StringComparison.Ordinal))).IsTrue();
        await Assert.That(messages.Any(message =>
            message.Contains("settings-read-probe=available", StringComparison.Ordinal) &&
            message.Contains(path, StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task DiagnosticsRemainAttachedWithoutAnAmbientTestContext()
    {
        RequireDiagnostics();
        var messages = new ConcurrentQueue<string>();
        using var diagnostics = ViewerCiDiagnostics.StartControl("output-context-control", messages.Enqueue);
        using var files = new ViewerCompletedJobTestFiles();
        string directory = Path.Combine(files.Root, "settings");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "viewer-settings.txt.control.tmp");
        File.WriteAllText(path, "test");
        diagnostics.TrackFixtureRoot(files.Root);

        Task trigger;
        using (ExecutionContext.SuppressFlow())
        {
            trigger = Task.Run(() =>
            {
                diagnostics.TrackUiThread();
                try
                {
                    throw new ArgumentException("Font stretch must be > 1.");
                }
                catch (ArgumentException)
                {
                }
                try
                {
                    throw new IOException($"Cannot remove {path}.");
                }
                catch (IOException)
                {
                }
            });
        }
        await trigger;

        await Assert.That(messages.Any(message => message.Contains(
            "font-exception", StringComparison.Ordinal))).IsTrue();
        await Assert.That(messages.Any(message => message.Contains(
            "settings-file-exception", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task DiagnosticsAreRetainedInFailureReport()
    {
        RequireDiagnostics();
        TestContext context = TestContext.Current ??
            throw new InvalidOperationException("The owning test context is unavailable.");
        using var diagnostics = ViewerCiDiagnostics.StartIfEnabled("report-control") ??
            throw new InvalidOperationException("Viewer diagnostics are not enabled.");
        using var files = new ViewerCompletedJobTestFiles();
        string directory = Path.Combine(files.Root, "settings");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "viewer-settings.txt.control.tmp");
        File.WriteAllText(path, "test");
        diagnostics.TrackFixtureRoot(files.Root);
        diagnostics.TrackUiThread();

        try
        {
            throw new ArgumentException("Font stretch must be > 1.");
        }
        catch (ArgumentException)
        {
        }
        try
        {
            throw new IOException($"Cannot remove {path}.");
        }
        catch (IOException)
        {
        }

        await Assert.That(context.GetErrorOutput().Contains(
            "font-exception", StringComparison.Ordinal)).IsTrue();
        await Assert.That(context.GetErrorOutput().Contains(
            "settings-file-exception", StringComparison.Ordinal)).IsTrue();
        if (Environment.GetEnvironmentVariable("OPENUSD_VIEWER_CI_DIAGNOSTIC_FORCE_REPORT_FAILURE") == "1")
        {
            throw new InvalidOperationException("Synthetic failure for Viewer diagnostic report verification.");
        }
    }

    [Test]
    public async Task DiagnosticSinkFailureDoesNotReplaceTheFontException()
    {
        RequireDiagnostics();
        int fontWrites = 0;
        using var diagnostics = ViewerCiDiagnostics.StartControl("sink-control", message =>
        {
            if (message.Contains("font-exception", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref fontWrites);
            }
            throw new InvalidOperationException("Diagnostic sink failed.");
        });
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaEntryPoint));
        await session.Dispatch(async () =>
        {
            var text = new TextBlock { Text = "diagnostic", FontStretch = (FontStretch)0 };
            diagnostics.TrackRoot(text);
            await Assert.That(() => text.Measure(new Size(100, 100)))
                .ThrowsExactly<ArgumentException>().WithMessage("Font stretch must be > 1.");
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
        await Assert.That(Volatile.Read(ref fontWrites)).IsEqualTo(1);
    }

    private static void RequireDiagnostics()
    {
        if (!ViewerCiDiagnostics.IsEnabled)
        {
            Skip.Test("Set OPENUSD_VIEWER_CI_DIAGNOSTICS=1 to run the synthetic diagnostic controls.");
        }
    }

    private static class SkiaEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
