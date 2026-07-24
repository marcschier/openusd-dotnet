// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;

namespace OpenUsd.D3D12CompositionSmoke;

internal static class SmokeStatus
{
    private static readonly object Gate = new();
    private static string? _statusFile;

    internal static void Initialize()
    {
        _statusFile = Environment.GetEnvironmentVariable("OPENUSD_STATUS_FILE");
        string? logFile = Environment.GetEnvironmentVariable("OPENUSD_LOG_FILE");
        if (!string.IsNullOrWhiteSpace(logFile))
        {
            string path = Path.GetFullPath(logFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Trace.Listeners.Add(new TextWriterTraceListener(path));
            Trace.AutoFlush = true;
        }
    }

    internal static void Write(string status)
    {
        lock (Gate)
        {
            Console.WriteLine(status);
            if (!string.IsNullOrWhiteSpace(_statusFile))
            {
                string path = Path.GetFullPath(_statusFile);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, status + Environment.NewLine);
            }
        }
    }

    internal static string Value(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Replace(' ', '_');
}
