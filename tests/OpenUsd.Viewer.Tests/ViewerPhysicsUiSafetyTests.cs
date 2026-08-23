// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Asserts the safety rules the physics user interface has to keep once a simulation is running
/// behind it: a repaint that arrives every step must not fight the user for the toolbar, a status
/// callback that outlives its document must not repaint the next one, a failing render bridge must
/// not take the viewport with it, and a fire-and-forget event handler must not be able to end the
/// process.
/// </summary>
[NotInParallel("AvaloniaControls")]
public sealed class ViewerPhysicsUiSafetyTests
{
    [Test]
    public async Task RepaintingTheSameContentDoesNotTouchTheControl()
    {
        Button button = new() { Content = "Play" };

        await Assert.That(ViewerToolbarState.SetContent(button, "Play")).IsFalse();
        await Assert.That(ViewerToolbarState.SetContent(button, "Pause")).IsTrue();
        await Assert.That(ViewerToolbarState.SetContent(button, "Pause")).IsFalse();
        await Assert.That(button.Content).IsEqualTo("Pause");
    }

    [Test]
    public async Task RepaintingTheSameEnabledStateDoesNotTouchTheControl()
    {
        Button button = new() { IsEnabled = true };

        await Assert.That(ViewerToolbarState.SetEnabled(button, true)).IsFalse();
        await Assert.That(ViewerToolbarState.SetEnabled(button, false)).IsTrue();
        await Assert.That(ViewerToolbarState.SetEnabled(button, false)).IsFalse();
        await Assert.That(button.IsEnabled).IsFalse();
    }

    [Test]
    public async Task AStepPacedRepaintReportsNoLayoutChangeSoTheOverflowIsNotRebuilt()
    {
        // A pumped step repaints the status text but leaves the command buttons alone. If the
        // repaint reported a change the toolbar would re-plan its overflow every few milliseconds
        // and a menu the user opened would close under the pointer.
        Button play = new() { Content = "Pause", IsEnabled = true };
        Button stop = new() { Content = "Stop", IsEnabled = true };
        TextBlock status = new();

        bool layoutChanged = false;
        for (int step = 0; step < 64; step++)
        {
            status.Text = $"t=0.{step:D2}s step {step}";
            layoutChanged |= ViewerToolbarState.SetContent(play, "Pause");
            layoutChanged |= ViewerToolbarState.SetEnabled(play, true);
            layoutChanged |= ViewerToolbarState.SetContent(stop, "Stop");
            layoutChanged |= ViewerToolbarState.SetEnabled(stop, true);
        }

        await Assert.That(layoutChanged).IsFalse();
    }

    [Test]
    public async Task EveryFireAndForgetPhysicsHandlerCatchesItsOwnFailure()
    {
        // An unhandled exception out of an async void handler is unobserved by the outer catch and
        // takes the process down, so each one has to translate and report on its own.
        string source = await ReadPhysicsWindowAsync();

        foreach ((string handler, string body) in EnumerateMethods(source))
        {
            if (!handler.Contains("async void", StringComparison.Ordinal))
            {
                continue;
            }

            await Assert.That(body).Contains("catch");
            await Assert.That(
                body.Contains("ReportPhysicsHandlerFailure", StringComparison.Ordinal) ||
                body.Contains("ShowError", StringComparison.Ordinal)).IsTrue();
        }
    }

    [Test]
    public async Task DetachingADocumentUnsubscribesTheStatusHandlerBeforeDisposingTheController()
    {
        string source = await ReadPhysicsWindowAsync();
        string body = MethodBody(source, "private async Task DetachPhysicsAsync()");

        int unsubscribe = body.IndexOf("StatusChanged -=", StringComparison.Ordinal);
        int dispose = body.IndexOf("DisposeAsync()", StringComparison.Ordinal);

        await Assert.That(unsubscribe).IsGreaterThan(0);
        await Assert.That(dispose).IsGreaterThan(0);
        await Assert.That(unsubscribe).IsLessThan(dispose);
        await Assert.That(body).Contains("_physicsSessionVersion++");
    }

    [Test]
    public async Task AQueuedStatusRepaintIsDiscardedWhenItsDocumentIsGone()
    {
        // Status snapshots are posted to the UI thread, so one can still be in flight when the
        // document closes. Both the ownership and the session version are checked before it paints.
        string source = await ReadPhysicsWindowAsync();
        string body = MethodBody(source, "private void OnPhysicsStatusChanged(");

        await Assert.That(body).Contains("IsCurrentPhysicsSession(owner, version)");
        await Assert.That(CountOccurrences(body, "IsCurrentPhysicsSession(owner, version)"))
            .IsGreaterThanOrEqualTo(2);

        string check = MethodBody(source, "private bool IsCurrentPhysicsSession(");
        await Assert.That(check).Contains("ReferenceEquals(_physics, owner)");
        await Assert.That(check).Contains("version == _physicsSessionVersion");
    }

    [Test]
    public async Task ARepaintNeverOverwritesATimelineTheUserIsDragging()
    {
        string source = await ReadPhysicsWindowAsync();
        string body = MethodBody(source, "private void RenderPhysicsState(");

        await Assert.That(body).Contains("if (!_physicsScrubbing)");
        await Assert.That(MethodBody(source, "private void OnPhysicsScrubPressed("))
            .Contains("_physicsScrubbing = true");
        await Assert.That(MethodBody(source, "private async void OnPhysicsScrubReleased("))
            .Contains("_physicsScrubbing = false");
    }

    [Test]
    public async Task AnOpenOverflowMenuIsNotRebuiltUnderThePointer()
    {
        string source = await ReadPhysicsWindowAsync();
        string body = MethodBody(source, "private void ApplyPhysicsToolbarOverflow(");

        int guard = body.IndexOf("if (_physicsOverflowOpen)", StringComparison.Ordinal);
        await Assert.That(guard).IsGreaterThan(0);
        await Assert.That(body[guard..(guard + 400)]).Contains("return;");
        await Assert.That(body).Contains("_physicsOverflowStale");
    }

    [Test]
    public async Task AFailingPhysicsBridgeIsDisabledInsteadOfStoppingTheRenderLoop()
    {
        string source = await ReadPhysicsWindowAsync();
        string body = MethodBody(source, "private void PumpPhysicsRenderFrame()");

        await Assert.That(body).Contains("catch");
        await Assert.That(body).Contains("_physicsRenderFaulted = true");
        await Assert.That(body).Contains("TryClearFaultedPhysicsOverrides");
        await Assert.That(body).DoesNotContain("throw;");

        string clear = MethodBody(source, "private static void TryClearFaultedPhysicsOverrides(");
        await Assert.That(clear).Contains("DisableRenderBridge");
        await Assert.That(clear).Contains("ClearPhysicsOverrides()");
        await Assert.That(clear).Contains("catch");
    }

    [Test]
    public async Task TheOverlayIsNeverReturnedOutOfTheStageScheduler()
    {
        // Returning a stage-bound object from InvokeAsync<T> trips the scheduler's result guard at
        // run time, which made every preview fail on a native build while every managed test passed.
        string source = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Viewer",
            "ViewerPhysicsTransport.cs"));
        string body = MethodBodyIn(
            source,
            "private async ValueTask<UsdPhysicsPreviewApplier> GetPreviewApplierAsync(");

        // The overlay has to be captured by the callback rather than returned from it, and an
        // expression-bodied lambda would bind to the forbidden generic overload again.
        string compact = string.Concat(body.Where(character => !char.IsWhiteSpace(character)));

        await Assert.That(compact).Contains("_scheduler.InvokeAsync(");
        await Assert.That(compact).DoesNotContain("InvokeAsync<");
        await Assert.That(compact)
            .Contains("_scheduler.InvokeAsync(stage=>{overlay=stage.NormalizeSessionOverlay();}");
    }

    [Test]
    public async Task TheTransportAdapterNeverRebuildsItsMetadataPerRead()
    {
        // Rebuilding the capability matrix or the diagnostic list inside the getters allocated and
        // returned a new reference on every read, which defeated the controller's capability cache
        // and the inspector's ItemsSource guard and rebuilt the whole inspector at frame rate.
        string source = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Viewer",
            "ViewerPhysicsTransport.cs"));
        string compact = string.Concat(source.Where(character => !char.IsWhiteSpace(character)));

        await Assert.That(compact).Contains(
            "publicIReadOnlyList<ViewerPhysicsCapabilitySupport>Capabilities=>" +
            "_metadata.GetCapabilities(_transport.Capabilities.Features);");
        await Assert.That(compact).Contains(
            "publicIReadOnlyList<ViewerPhysicsDiagnosticRow>Diagnostics=>" +
            "_metadata.GetDiagnostics(_transport.Diagnostics);");

        // Enum.GetValues allocates a fresh array per call, so the adapter must never call it.
        await Assert.That(source).DoesNotContain("Enum.GetValues");
    }

    private static string MethodBodyIn(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"The source no longer declares '{signature}'.");
        }

        return ReadBlock(source, start);
    }

    private static async Task<string> ReadPhysicsWindowAsync() =>
        await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Viewer",
            "MainWindow.Physics.cs"));

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"MainWindow.Physics.cs no longer declares '{signature}'.");
        }

        return ReadBlock(source, start);
    }

    private static IEnumerable<(string Signature, string Body)> EnumerateMethods(string source)
    {
        int index = 0;
        while (true)
        {
            int start = source.IndexOf("    private ", index, StringComparison.Ordinal);
            if (start < 0)
            {
                yield break;
            }

            int newline = source.IndexOf('\n', start);
            if (newline < 0)
            {
                yield break;
            }

            yield return (source[start..newline], ReadBlock(source, start));
            index = newline;
        }
    }

    private static string ReadBlock(string source, int start)
    {
        int open = source.IndexOf('{', start);
        if (open < 0)
        {
            return source[start..];
        }

        int depth = 0;
        for (int index = open; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[start..(index + 1)];
                }
            }
        }

        return source[start..];
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        string currentDirectory = Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(currentDirectory, "OpenUsd.slnx")))
        {
            return currentDirectory;
        }

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the OpenUSD repository root.");
    }
}
