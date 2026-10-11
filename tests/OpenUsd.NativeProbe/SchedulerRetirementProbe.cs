// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Interop;

namespace OpenUsd.NativeProbe;

internal static class SchedulerRetirementProbe
{
    internal static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.GetFullPath(Path.Combine(directory, $"scheduler-review-{Guid.NewGuid():N}.usda"));
        const string sourceText = "#usda 1.0\ndef Xform \"World\"\n{\n    double weight = 7\n}\n";
        await File.WriteAllTextAsync(path, sourceText).ConfigureAwait(false);
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                if ((OpenUsdNativeRuntime.Capabilities & OpenUsdNativeContract.RequiredCapabilities) !=
                    OpenUsdNativeContract.RequiredCapabilities)
                {
                    throw new InvalidOperationException(
                        "The platform is missing the required native ABI capabilities.");
                }
                using (UsdStage ordinary = UsdStage.Open(path))
                {
                    if (ordinary.GetPrim("/World").GetDouble("weight") != 7)
                    {
                        throw new InvalidOperationException(
                            "Ordinary source opening failed on the unsupported review platform.");
                    }
                }

                OpenUsdNativeException? refusal = null;
                try
                {
                    await using UsdStageScheduler unsupported = UsdStageScheduler.OpenForReview(path, 1, 2);
                }
                catch (OpenUsdNativeException error)
                {
                    refusal = error;
                }
                if (refusal is not { Status: OpenUsdNativeStatus.NativeError } ||
                    !refusal.Message.Contains("requires Windows stable-read leases", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Verified source opening must explicitly refuse the unsupported platform.", refusal);
                }
                if (await File.ReadAllTextAsync(path).ConfigureAwait(false) != sourceText)
                {
                    throw new InvalidOperationException("Refused verified source opening changed the source file.");
                }
                Console.WriteLine(
                    "VERIFIED_REVIEW_PLATFORM_REFUSAL_OK: native capabilities, ordinary Open, " +
                    "explicit Windows-lease refusal, unchanged source.");
                return;
            }

            await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(path, 1, 2);
            UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
                static stage => stage.CaptureReviewSourceBinding()).ConfigureAwait(false);
            using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync().ConfigureAwait(false);
            using UsdStageRenderLease retained = source.AcquireLease();
            await using (UsdStageRetirementLease retirement = await scheduler.TryPrepareRetirementAsync(
                stage => binding.HasSamePayload(stage.CaptureReviewSourceBinding())).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The source origin was not retained."))
            {
                bool writerRefused = false;
                try
                {
                    await scheduler.EditAsync(
                        static stage => stage.GetPrim("/World").SetDouble("weight", 31),
                        UsdStageInvalidationKind.Property).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    writerRefused = true;
                }
                double value = await retirement.InvokeCleanupAsync(
                    static stage => stage.GetPrim("/World").GetDouble("weight")).ConfigureAwait(false);
                if (!writerRefused || value != 7)
                {
                    throw new InvalidOperationException("A foreign writer crossed the retirement fence.");
                }
            }

            if (await scheduler.InvokeAsync(
                static stage => stage.GetPrim("/World").GetDouble("weight")).ConfigureAwait(false) != 7)
            {
                throw new InvalidOperationException("Aborting retirement did not preserve the stage.");
            }
            await using UsdStageRetirementLease committed = await scheduler.TryPrepareRetirementAsync(
                stage => binding.HasSamePayload(stage.CaptureReviewSourceBinding())).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The resumed verified document was not admitted.");
            retained.Dispose();
            source.Dispose();
            await committed.CommitAsync().ConfigureAwait(false);
            if (await File.ReadAllTextAsync(path).ConfigureAwait(false) != sourceText)
            {
                throw new InvalidOperationException("Scheduler retirement changed the source file.");
            }
            Console.WriteLine("Verified-source scheduler retirement, owner cleanup, abort and commit passed.");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
