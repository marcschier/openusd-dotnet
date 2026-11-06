// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Rendering.Silk;

/// <summary>Shares independently measured texture-preparation pools across devices within one process.</summary>
/// <remarks>
/// At least one pool must be configured. Reuse the same object for viewport and product renderers.
/// These are logical ownership ceilings, not a total process or VRAM limit. Source geometry, detached
/// capture results, native codec/OCIO scratch, imported presentation images and driver overhead are excluded.
/// </remarks>
public sealed class SilkTexturePreparationBudgets
{
    /// <summary>Creates an immutable set of existing shared pools without choosing implicit ceilings.</summary>
    public SilkTexturePreparationBudgets(
        SilkCpuTextureBudget? cpuTextureBudget,
        SilkGpuTextureBudget? gpuTextureBudget,
        SilkGpuStagingBudget? gpuStagingBudget)
    {
        if (cpuTextureBudget is null && gpuTextureBudget is null && gpuStagingBudget is null)
        {
            throw new ArgumentException("At least one texture-preparation budget must be configured.");
        }
        CpuTextureBudget = cpuTextureBudget;
        GpuTextureBudget = gpuTextureBudget;
        GpuStagingBudget = gpuStagingBudget;
    }

    /// <summary>Gets the shared managed texture-pixel and command-copy pool, when configured.</summary>
    public SilkCpuTextureBudget? CpuTextureBudget { get; }

    /// <summary>Gets the shared owned RHI texture-payload pool, when configured.</summary>
    public SilkGpuTextureBudget? GpuTextureBudget { get; }

    /// <summary>Gets the shared backend upload/readback payload pool, when configured.</summary>
    public SilkGpuStagingBudget? GpuStagingBudget { get; }

    /// <summary>Configures all requested domains before a newly created device allocates render resources.</summary>
    /// <remarks>
    /// Unsupported domains fail explicitly. A caller owns cleanup if configuration or later initialization fails.
    /// Reapplying the same pools is idempotent; existing allocations or different configured pools are refused.
    /// </remarks>
    public void ConfigureDevice(ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if ((CpuTextureBudget is not null &&
                device is not (SilkGraphicsDeviceLifetimeBase and ISilkCpuTextureAdmissionDevice)) ||
            (GpuTextureBudget is not null && device is not ISilkTextureAdmissionDevice) ||
            (GpuStagingBudget is not null && device is not ISilkStagingAdmissionDevice))
        {
            throw new NotSupportedException("The device cannot enforce all configured texture-preparation budgets.");
        }
        CpuTextureBudget?.ConfigureDevice(device);
        GpuTextureBudget?.ConfigureDevice(device);
        GpuStagingBudget?.ConfigureDevice(device);
    }

    /// <summary>Parses independent positive decimal ceilings; all-null configuration disables the policy.</summary>
    public static SilkTexturePreparationBudgets? FromConfiguration(
        string? cpuTextureBytes, string? gpuTextureBytes, string? gpuStagingBytes)
    {
        ulong? cpu = Parse(cpuTextureBytes, nameof(cpuTextureBytes));
        ulong? texture = Parse(gpuTextureBytes, nameof(gpuTextureBytes));
        ulong? staging = Parse(gpuStagingBytes, nameof(gpuStagingBytes));
        return cpu is null && texture is null && staging is null ? null : new(
            cpu is { } cpuBytes ? new(cpuBytes) : null,
            texture is { } textureBytes ? new(textureBytes) : null,
            staging is { } stagingBytes ? new(stagingBytes) : null);
    }

    private static ulong? Parse(string? configured, string parameterName)
    {
        if (configured is null)
        {
            return null;
        }
        if (!ulong.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value) || value == 0)
        {
            throw new ArgumentException("The byte ceiling must be a positive base-10 integer.", parameterName);
        }
        return value;
    }
}
