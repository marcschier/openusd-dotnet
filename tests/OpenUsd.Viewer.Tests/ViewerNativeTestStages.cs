// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

internal static class ViewerNativeTestStages
{
    internal static UsdStageScheduler OpenSchedulerOrSkip(string path)
    {
        try
        {
            using UsdStage probe = UsdStage.Open(path);
        }
        catch (DllNotFoundException exception) when (
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_TEST_NATIVE_ROOT") is null)
        {
            Skip.Test($"openusd_dotnet native runtime is unavailable: {exception.Message}");
            throw;
        }
        return UsdStageScheduler.Open(path);
    }
}
