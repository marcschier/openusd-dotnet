// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Render;

namespace OpenUsd.Rendering;

public sealed partial class RenderProductJobPlan
{
    /// <summary>
    /// Reads an authored product and samples its camera in bounded scheduler batches without scene mutation.
    /// </summary>
    /// <remarks>
    /// Null settings select the stage's authored default. Null product selects only a sole product, never
    /// silently the first of several. An observed source revision is checked around every batch.
    /// Callers retain scheduler ownership and exclude or detect writers through the later render.
    /// </remarks>
    public static async ValueTask<RenderProductJobPlan> PrepareAsync(
        UsdStageScheduler scheduler, StageIdentity stage, IReadOnlyList<double> timeCodes,
        RenderSettings renderSettings, string? settingsPath = null, string? productPath = null,
        RenderProductOverrides? overrides = null, ulong? expectedStageRevision = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(timeCodes);
        if (settingsPath is not null)
        {
            UsdPath.ValidateAbsolutePrimPath(settingsPath);
        }
        if (productPath is not null)
        {
            UsdPath.ValidateAbsolutePrimPath(productPath);
        }
        int count = timeCodes.Count;
        if (count is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(timeCodes), "A product requires 1-4096 numeric samples.");
        }
        var times = new double[count];
        for (int index = 0; index < count; index++)
        {
            double value = timeCodes[index];
            if (!double.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(timeCodes), "Product sample times must be finite.");
            }
            times[index] = value;
        }
        cancellationToken.ThrowIfCancellationRequested();
        ProductSelection selection = await scheduler.InvokeAsync(source =>
        {
            ulong serial = source.ChangeSerial;
            if (expectedStageRevision is { } expected && expected != serial)
            {
                throw new InvalidOperationException("The stage revision changed before product preparation.");
            }
            UsdRenderSpecification specification = source.GetRenderSpecification(settingsPath) ??
                throw new ArgumentException(
                    "The active stage has no authored default render settings; supply an explicit settings path.",
                    nameof(settingsPath));
            int selected = -1;
            for (int index = 0; index < specification.Products.Count; index++)
            {
                if (specification.Products[index].Path == productPath ||
                    (productPath is null && specification.Products.Count == 1))
                {
                    selected = index;
                    break;
                }
            }
            if (selected < 0)
            {
                throw new ArgumentException(productPath is null
                    ? "Choose an explicit product when settings do not contain exactly one."
                    : "The requested product is not in the selected settings.", nameof(productPath));
            }
            var request = new RenderProductRequest(specification, selected, overrides);
            _ = ValidateStaticRequest(request);
            RequireRevision(source, serial);
            return new ProductSelection(request, serial);
        }, cancellationToken).ConfigureAwait(false);

        var frames = new RenderPreparedFrame[times.Length];
        for (int start = 0; start < times.Length; start += 16)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int offset = start;
            int length = Math.Min(16, times.Length - start);
            await scheduler.InvokeAsync(source =>
            {
                RequireRevision(source, selection.Revision);
                for (int index = offset; index < offset + length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RenderPreparedFrame frame = selection.Request.PrepareExecutionFrame(source, times[index]);
                    ValidateCamera(frame);
                    frames[index] = frame;
                }
                RequireRevision(source, selection.Revision);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new RenderProductJobPlan(stage, frames, renderSettings, selection.Revision);
    }

    private static void RequireRevision(UsdStage stage, ulong expected)
    {
        if (stage.ChangeSerial != expected)
        {
            throw new InvalidOperationException(
                "The stage changed while the product was prepared; no output was created.");
        }
    }

    private sealed record ProductSelection(RenderProductRequest Request, ulong Revision) : IUsdDetachedResult;
}
