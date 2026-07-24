// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using OpenUsd.Interop;

namespace OpenUsd.NativeProbe;

internal static class StageAccessEndProbe
{
    private const string ChildArgument = "--stage-access-end-child";
    private const string InvalidArgumentScenario = "invalid-argument";
    private const string InvalidArgumentContinuedMarker =
        "OPENUSD_STAGE_ACCESS_INVALID_ARGUMENT_CONTINUED";
    private const string WrongThreadScenario = "wrong-thread";

    internal static bool TryRunChild(string[] args, out int exitCode)
    {
        if (args.Length != 2 ||
            !string.Equals(args[0], ChildArgument, StringComparison.Ordinal))
        {
            exitCode = 0;
            return false;
        }

        exitCode = args[1] switch
        {
            InvalidArgumentScenario => RunInvalidArgumentChild(),
            WrongThreadScenario => RunWrongThreadChild(),
            _ => 64
        };
        return true;
    }

    internal static async Task RunParentAsync(OpenUsdNativeStage stage)
    {
        ChildResult wrongThread = await RunChildAsync(WrongThreadScenario).ConfigureAwait(false);
        if (wrongThread.ExitCode == 0 ||
            !wrongThread.Output.Contains(
                OpenUsdNativeRuntime.StageAccessWrongThreadFailFastMarker,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "WrongThread did not terminate the child through the expected fail-fast path.");
        }

        ChildResult invalidArgument =
            await RunChildAsync(InvalidArgumentScenario).ConfigureAwait(false);
        if (invalidArgument.ExitCode != 0 ||
            !invalidArgument.Output.Contains(
                InvalidArgumentContinuedMarker,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "InvalidArgument did not throw normally and allow the child to continue.");
        }

        var callbackFailure = new CallbackProbeException();
        Exception? observedFailure = null;
        try
        {
            stage.WithAccess(() => throw callbackFailure);
        }
        catch (Exception exception)
        {
            observedFailure = exception;
        }

        if (!ReferenceEquals(callbackFailure, observedFailure))
        {
            throw new InvalidOperationException(
                "A successful stage-access end did not rethrow the original callback exception.");
        }

        Console.WriteLine("Stage access end handling passed.");
    }

    private static int RunInvalidArgumentChild()
    {
        try
        {
            OpenUsdNativeRuntime.HandleStageAccessEndFailure(
                new OpenUsdNativeException(
                    OpenUsdNativeStatus.InvalidArgument,
                    "Injected invalid stage-access guard."));
        }
        catch (OpenUsdNativeException exception)
            when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
        {
            Console.WriteLine(InvalidArgumentContinuedMarker);
            return 0;
        }

        return 65;
    }

    private static int RunWrongThreadChild()
    {
        OpenUsdNativeRuntime.HandleStageAccessEndFailure(
            new OpenUsdNativeException(
                OpenUsdNativeStatus.WrongThread,
                "Injected wrong-thread stage-access end."));
        return 66;
    }

    private static async Task<ChildResult> RunChildAsync(string scenario)
    {
        using var process = new Process
        {
            StartInfo = CreateChildStartInfo(scenario)
        };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start the stage-access child probe.");
        }

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        string output = string.Concat(
            await standardOutput.ConfigureAwait(false),
            await standardError.ConfigureAwait(false));
        return new ChildResult(process.ExitCode, output);
    }

    private static ProcessStartInfo CreateChildStartInfo(string scenario)
    {
        string processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current executable path is unavailable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        if (string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            string[] commandLine = Environment.GetCommandLineArgs();
            if (commandLine.Length == 0 || string.IsNullOrEmpty(commandLine[0]))
            {
                throw new InvalidOperationException(
                    "The framework-dependent probe assembly path is unavailable.");
            }
            startInfo.ArgumentList.Add(Path.GetFullPath(commandLine[0]));
        }

        startInfo.ArgumentList.Add(ChildArgument);
        startInfo.ArgumentList.Add(scenario);
        return startInfo;
    }

    private sealed class CallbackProbeException : Exception;

    private readonly record struct ChildResult(int ExitCode, string Output);
}
