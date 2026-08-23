// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

/// <summary>Opens the authored stages the physics render smoke tests drive.</summary>
/// <remarks>
/// A managed-only checkout has no native runtime, and a host with no staged physics runtime has no
/// solver, so both are turned into skips here rather than into a background
/// <see cref="DllNotFoundException"/> or an assertion about an empty world.
/// </remarks>
internal static class ViewerPhysicsTestStages
{
    internal static UsdStageScheduler OpenSchedulerOrSkip(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        string path = Path.Combine(FindRepositoryRoot(), "test-assets", fileName);
        try
        {
            // The scheduler opens the stage on its own thread, so probing here is what turns a
            // managed-only checkout into a skip instead of a background DllNotFoundException.
            using UsdStage probe = UsdStage.Open(path);
        }
        catch (DllNotFoundException exception)
        {
            Skip.Test($"openusd_dotnet native runtime is unavailable: {exception.Message}");
            throw;
        }

        return UsdStageScheduler.Open(path);
    }

    internal static void SkipWhenSolverIsNotStaged(ViewerPhysicsController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        foreach (ViewerPhysicsDiagnosticRow row in controller.Diagnostics)
        {
            if (row.Code.Contains("BACKEND_UNAVAILABLE", StringComparison.Ordinal) ||
                row.Code.Contains("RUNTIME_UNAVAILABLE", StringComparison.Ordinal))
            {
                Skip.Test("The staged native runtime does not provide a physics solver.");
            }
        }

        if (!controller.IsEnabled)
        {
            Skip.Test("The physics controller could not be enabled on this host.");
        }
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

        throw new InvalidOperationException("The repository root could not be located.");
    }
}
