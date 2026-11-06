// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace OpenUsd.Viewer.Tests;

internal sealed class ViewerCiDiagnostics : IDisposable
{
    private const string EnvironmentVariable = "OPENUSD_VIEWER_CI_DIAGNOSTICS";

    [ThreadStatic]
    private static bool _handlingException;

    private readonly string _scenario;
    private readonly Action<string> _write;
    private readonly List<Visual> _roots = [];
    private IDisposable? _fontChanges;
    private string? _fixtureRoot;
    private Task? _actionTask;
    private Task? _dispatchTask;
    private Task? _closedTask;
    private int _uiThread;
    private int _fontChangesReported;
    private int _fontReported;
    private int _fileReported;
    private int _disposed;

    private ViewerCiDiagnostics(string scenario, Action<string> write)
    {
        _scenario = scenario;
        _write = write;
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
        Write("started");
    }

    internal static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable), "1", StringComparison.Ordinal);

    internal static ViewerCiDiagnostics? StartIfEnabled(string scenario) =>
        IsEnabled ? new ViewerCiDiagnostics(scenario, Console.Error.WriteLine) : null;

    internal static ViewerCiDiagnostics StartControl(string scenario, Action<string> write) => new(scenario, write);

    internal void TrackRoot(Visual root)
    {
        _roots.Add(root);
        TrackUiThread();
        try
        {
            _fontChanges ??= ((AvaloniaProperty)TextBlock.FontStretchProperty).Changed
                .Subscribe(new FontChangeObserver(this));
        }
        catch (Exception exception)
        {
            Write($"font-observer-error type={exception.GetType().Name} message={exception.Message}");
        }
    }

    internal void TrackUiThread() => Volatile.Write(ref _uiThread, Environment.CurrentManagedThreadId);

    internal void TrackFixtureRoot(string root)
    {
        Volatile.Write(ref _fixtureRoot, root);
        Write($"fixture-root={root}");
    }

    internal Task<T> TrackAction<T>(Task<T> task)
    {
        Volatile.Write(ref _actionTask, task);
        Mark("action-tracked");
        return task;
    }

    internal void TrackDispatch(Task task)
    {
        Volatile.Write(ref _dispatchTask, task);
        Mark("dispatch-tracked");
    }

    internal void TrackClosed(Task task)
    {
        Volatile.Write(ref _closedTask, task);
        Mark("closed-tracked");
    }

    internal void Mark(string marker)
    {
        try
        {
            Write($"{marker} {TaskStates()}");
            if (marker.StartsWith("main-close", StringComparison.Ordinal) ||
                marker.Equals("dispatch-failed", StringComparison.Ordinal))
            {
                CaptureSettingsSnapshot(marker);
            }
        }
        catch (Exception exception)
        {
            Write($"diagnostic-error type={exception.GetType().Name}");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            try
            {
                _fontChanges?.Dispose();
            }
            catch (Exception exception)
            {
                Write($"font-observer-dispose-error type={exception.GetType().Name} message={exception.Message}");
            }
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
            Mark("stopped");
        }
    }

    private void OnFontChanged(AvaloniaPropertyChangedEventArgs change)
    {
        try
        {
            if (Volatile.Read(ref _uiThread) != Environment.CurrentManagedThreadId ||
                change.NewValue is not FontStretch stretch || (int)stretch >= 1 ||
                Interlocked.Increment(ref _fontChangesReported) > 16)
            {
                return;
            }
            string name = change.Sender is StyledElement styled ? styled.Name ?? "<unnamed>" : "<unnamed>";
            Write($"font-change type={change.Sender.GetType().Name} name={name} " +
                $"value={(int)stretch} priority={change.Priority}\n{Environment.StackTrace}");
        }
        catch (Exception exception)
        {
            Write($"font-change-error type={exception.GetType().Name} message={exception.Message}");
        }
    }

    private void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (_handlingException)
        {
            return;
        }

        _handlingException = true;
        try
        {
            Exception exception = args.Exception;
            string? root = Volatile.Read(ref _fixtureRoot);
            StringComparison pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            bool font = exception is ArgumentException &&
                exception.Message.Contains("Font stretch must be", StringComparison.Ordinal) &&
                Volatile.Read(ref _uiThread) == Environment.CurrentManagedThreadId;
            bool settingsFile = exception is IOException ioException && root is not null &&
                MatchesSettingsTemporaryFile(ioException, root, pathComparison);
            if (!font && !settingsFile)
            {
                return;
            }

            if (font && Interlocked.Exchange(ref _fontReported, 1) == 0)
            {
                Write($"font-exception {TaskStates()}\n{exception}\nobserving-stack:\n{Environment.StackTrace}");
                CaptureFontSources();
                CaptureSettingsSnapshot("font-exception");
            }
            if (settingsFile && Interlocked.Exchange(ref _fileReported, 1) == 0)
            {
                Write($"settings-file-exception {TaskStates()}\n{exception}" +
                    $"\nobserving-stack:\n{Environment.StackTrace}");
                CaptureSettingsFiles(root!);
            }
        }
        catch (Exception exception)
        {
            Write($"diagnostic-error type={exception.GetType().Name} message={exception.Message}");
        }
        finally
        {
            _handlingException = false;
        }
    }

    private void CaptureFontSources()
    {
        var pending = new Queue<Visual>(_roots);
        var visited = new HashSet<Visual>();
        int candidates = 0;
        while (pending.TryDequeue(out Visual? root))
        {
            if (!visited.Add(root))
            {
                continue;
            }
            if (root is Window window)
            {
                foreach (Window owned in window.OwnedWindows)
                {
                    pending.Enqueue(owned);
                }
            }
            foreach (Visual visual in root.GetVisualDescendants().Prepend(root))
            {
                if (visual is not TextBlock text || (int)text.FontStretch >= 1)
                {
                    continue;
                }

                candidates++;
                AvaloniaPropertyValue value = text.GetDiagnostic(TextBlock.FontStretchProperty);
                Write($"font-candidate type={text.GetType().Name} name={text.Name ?? "<unnamed>"} " +
                    $"value={(int)text.FontStretch} priority={value.Priority}");
                try
                {
                    CaptureFontInheritance(text);
                }
                catch (Exception exception)
                {
                    Write($"font-source-error type={exception.GetType().Name} message={exception.Message}");
                }
            }
        }
        Write($"font-candidates={candidates}");
    }

    private void CaptureFontInheritance(AvaloniaObject target)
    {
        AvaloniaObject? current = target;
        for (int depth = 0; current is not null && depth < 64; depth++)
        {
            AvaloniaPropertyValue value = current.GetDiagnostic(TextBlock.FontStretchProperty);
            string name = current is StyledElement styled ? styled.Name ?? "<unnamed>" : "<unnamed>";
            string numeric = value.Value is FontStretch stretch
                ? ((int)stretch).ToString(CultureInfo.InvariantCulture) : "<not-font-stretch>";
            Write($"font-source depth={depth} type={current.GetType().Name} name={name} " +
                $"value={numeric} priority={value.Priority} current-override={value.IsOverriddenCurrentValue}");
            if (value.Priority != BindingPriority.Inherited)
            {
                return;
            }
            current = (current as IStyleHost)?.StylingParent as AvaloniaObject;
        }
    }

    private void CaptureSettingsFiles(string root)
    {
        string directory = Path.Combine(root, "settings");
        if (!Directory.Exists(directory))
        {
            Write($"settings-directory-missing path={directory}");
            return;
        }

        string[] paths = Directory.EnumerateFiles(directory, "viewer-settings.txt.*.tmp").Take(8).ToArray();
        Write($"settings-temporary-files={paths.Length}");
        foreach (string path in paths)
        {
            try
            {
                using var probe = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                Write($"settings-read-probe=available path={path}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Write($"settings-read-probe=failed type={exception.GetType().Name} " +
                    $"hresult=0x{exception.HResult:X8} path={path}");
            }

            ViewerCiLockOwners.Result owners = ViewerCiLockOwners.Query(path);
            Write($"settings-restart-manager={owners.Status} resource-users={owners.ProcessIds.Length} path={path}");
            foreach (uint processId in owners.ProcessIds)
            {
                Write($"settings-resource-user-pid={processId} path={path}");
            }
        }
    }

    private static bool MatchesSettingsTemporaryFile(
        IOException exception, string root, StringComparison pathComparison)
    {
        string message = exception.Message;
        if (!message.Contains("viewer-settings.txt.", pathComparison) ||
            !message.Contains(".tmp", pathComparison))
        {
            return false;
        }

        string settingsDirectory = Path.Combine(root, "settings");
        string settingsPrefix = settingsDirectory + Path.DirectorySeparatorChar;
        if (message.Contains(settingsPrefix, pathComparison))
        {
            return true;
        }
        if (message.Contains(Path.DirectorySeparatorChar) || message.Contains(Path.AltDirectorySeparatorChar) ||
            !Directory.Exists(settingsDirectory))
        {
            return false;
        }

        return Directory.EnumerateFiles(settingsDirectory, "viewer-settings.txt.*.tmp")
            .Any(path => message.Contains(Path.GetFileName(path), pathComparison));
    }

    private void CaptureSettingsSnapshot(string point)
    {
        string? root = Volatile.Read(ref _fixtureRoot);
        if (root is null)
        {
            return;
        }
        string directory = Path.Combine(root, "settings");
        if (!Directory.Exists(directory))
        {
            Write($"settings-snapshot point={point} directory-missing");
            return;
        }
        string[] paths = Directory.EnumerateFiles(directory, "viewer-settings.txt.*.tmp").Take(8).ToArray();
        Write($"settings-snapshot point={point} count={paths.Length}");
        foreach (string path in paths)
        {
            Write($"settings-snapshot-file point={point} path={path}");
        }
    }

    private string TaskStates() =>
        $"action={TaskState(Volatile.Read(ref _actionTask))} " +
        $"dispatch={TaskState(Volatile.Read(ref _dispatchTask))} " +
        $"closed={TaskState(Volatile.Read(ref _closedTask))}";

    private static string TaskState(Task? task) => task?.Status.ToString() ?? "<not-observed>";

    private void Write(string message)
    {
        try
        {
            _write($"[DEBUG-pr28-viewer] {DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} " +
                $"thread={Environment.CurrentManagedThreadId} scenario={_scenario} {message}");
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Viewer diagnostic sink failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private sealed class FontChangeObserver : IObserver<AvaloniaPropertyChangedEventArgs>
    {
        private readonly ViewerCiDiagnostics _owner;

        internal FontChangeObserver(ViewerCiDiagnostics owner)
        {
            _owner = owner;
        }

        public void OnCompleted() { }

        public void OnError(Exception error) =>
            _owner.Write($"font-observer-error type={error.GetType().Name} message={error.Message}");

        public void OnNext(AvaloniaPropertyChangedEventArgs value) => _owner.OnFontChanged(value);
    }
}
