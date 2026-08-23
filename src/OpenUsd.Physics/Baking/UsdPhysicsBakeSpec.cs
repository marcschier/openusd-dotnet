// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Baking;

/// <summary>
/// Supplies one immutable result batch per baked time code.
/// </summary>
/// <remarks>
/// The bake never reaches into a running transport: it asks for a complete, detached batch and
/// authors exactly what it is given, so a bake is reproducible from recorded results alone.
/// </remarks>
public interface IUsdPhysicsBakeSource
{
    /// <summary>Produces the complete result batch for one time code.</summary>
    /// <param name="timeCode">The authored time code to produce results for.</param>
    /// <param name="cancellationToken">Cancels producing the batch.</param>
    /// <returns>
    /// The batch to author, or <see langword="null"/> when the source has no results for the time
    /// code, which fails the bake rather than authoring a gap.
    /// </returns>
    ValueTask<UsdPhysicsResultBatch?> GetBatchAsync(
        double timeCode, CancellationToken cancellationToken);
}

/// <summary>
/// Describes an immutable, validated request to bake simulated results into one destination layer.
/// </summary>
public sealed record UsdPhysicsBakeSpec
{
    /// <summary>Initializes a bake spec targeting a whole time range.</summary>
    /// <param name="destinationLayerIdentifier">
    /// The identifier of the writable, file-backed layer in the stage local layer stack that
    /// receives every authored sample.
    /// </param>
    /// <param name="startTimeCode">
    /// The inclusive first time code to sample, or <see langword="null"/> to use the stage start.
    /// </param>
    /// <param name="endTimeCode">
    /// The inclusive last time code to sample, or <see langword="null"/> to use the stage end.
    /// </param>
    /// <param name="sampleStride">
    /// The time codes between successive samples, or <see langword="null"/> for one sample per
    /// time code.
    /// </param>
    /// <param name="options">The authoring options, or <see langword="null"/> for the defaults.</param>
    /// <param name="save">Whether the destination layer is saved after the bake fully succeeds.</param>
    public UsdPhysicsBakeSpec(
        string destinationLayerIdentifier,
        double? startTimeCode = null,
        double? endTimeCode = null,
        double? sampleStride = null,
        UsdPhysicsBakeOptions? options = null,
        bool save = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationLayerIdentifier);
        if (startTimeCode is { } start && !double.IsFinite(start))
        {
            throw new ArgumentOutOfRangeException(
                nameof(startTimeCode), "The start time code must be finite.");
        }
        if (endTimeCode is { } end && !double.IsFinite(end))
        {
            throw new ArgumentOutOfRangeException(
                nameof(endTimeCode), "The end time code must be finite.");
        }
        if (startTimeCode is { } from && endTimeCode is { } to && to < from)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endTimeCode), "The end time code must not precede the start time code.");
        }
        if (sampleStride is { } stride && (!double.IsFinite(stride) || stride <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleStride), "The sample stride must be finite and positive.");
        }

        DestinationLayerIdentifier = destinationLayerIdentifier;
        StartTimeCode = startTimeCode;
        EndTimeCode = endTimeCode;
        SampleStride = sampleStride;
        Options = options ?? UsdPhysicsBakeOptions.Default;
        Save = save;
    }

    /// <summary>Gets the identifier of the destination layer.</summary>
    public string DestinationLayerIdentifier { get; }

    /// <summary>Gets the inclusive first time code to sample, or the stage start when unset.</summary>
    public double? StartTimeCode { get; }

    /// <summary>Gets the inclusive last time code to sample, or the stage end when unset.</summary>
    public double? EndTimeCode { get; }

    /// <summary>Gets the time codes between successive samples, or one time code when unset.</summary>
    public double? SampleStride { get; }

    /// <summary>Gets the authoring options.</summary>
    public UsdPhysicsBakeOptions Options { get; }

    /// <summary>Gets a value indicating whether the destination layer is saved on success.</summary>
    public bool Save { get; }
}
