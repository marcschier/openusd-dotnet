// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering;

/// <summary>Identifies the exact simulation publication represented by a captured frame.</summary>
/// <remarks>Source and review identities are host attestations, not independent verification of file origin.</remarks>
public sealed record RenderSimulationFrameIdentity
{
    /// <summary>Creates bounded simulation provenance for one complete captured frame.</summary>
    public RenderSimulationFrameIdentity(
        Guid epoch, string scenarioIdentity, ulong sourceRevision, ulong publicationRevision,
        ulong stepIndex, double simulationSeconds, double timeCode, double fixedStepSeconds,
        ulong bindingRevision, int bodyCount, int deformationCount, string? reviewIdentity = null)
    {
        if (epoch == Guid.Empty)
        {
            throw new ArgumentException("A recording epoch must be nonempty.", nameof(epoch));
        }
        ValidateIdentity(scenarioIdentity, nameof(scenarioIdentity));
        if (reviewIdentity is not null)
        {
            ValidateIdentity(reviewIdentity, nameof(reviewIdentity));
        }
        if (!double.IsFinite(simulationSeconds) || simulationSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(simulationSeconds));
        }
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(timeCode));
        }
        if (!double.IsFinite(fixedStepSeconds) || fixedStepSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedStepSeconds));
        }
        ArgumentOutOfRangeException.ThrowIfZero(publicationRevision);
        ArgumentOutOfRangeException.ThrowIfNegative(bodyCount);
        ArgumentOutOfRangeException.ThrowIfNegative(deformationCount);
        Epoch = epoch;
        ScenarioIdentity = scenarioIdentity;
        SourceRevision = sourceRevision;
        PublicationRevision = publicationRevision;
        StepIndex = stepIndex;
        SimulationSeconds = simulationSeconds;
        TimeCode = timeCode;
        FixedStepSeconds = fixedStepSeconds;
        BindingRevision = bindingRevision;
        BodyCount = bodyCount;
        DeformationCount = deformationCount;
        ReviewIdentity = reviewIdentity;
    }

    /// <summary>Gets the unique recording epoch.</summary>
    public Guid Epoch { get; }
    /// <summary>Gets the host's scenario identity.</summary>
    public string ScenarioIdentity { get; }
    /// <summary>Gets the pinned authored source revision.</summary>
    public ulong SourceRevision { get; }
    /// <summary>Gets the exact physics publication revision.</summary>
    public ulong PublicationRevision { get; }
    /// <summary>Gets the completed fixed-step index.</summary>
    public ulong StepIndex { get; }
    /// <summary>Gets the simulated seconds since the authored start.</summary>
    public double SimulationSeconds { get; }
    /// <summary>Gets the exact authored time code of the publication.</summary>
    public double TimeCode { get; }
    /// <summary>Gets the fixed simulation step in seconds.</summary>
    public double FixedStepSeconds { get; }
    /// <summary>Gets the binding-table revision used to resolve the frame.</summary>
    public ulong BindingRevision { get; }
    /// <summary>Gets the number of complete body poses in the frame.</summary>
    public int BodyCount { get; }
    /// <summary>Gets the number of complete deformation regions in the frame.</summary>
    public int DeformationCount { get; }
    /// <summary>Gets an optional host-provided review identity.</summary>
    public string? ReviewIdentity { get; }

    private static void ValidateIdentity(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (value.Length > 256 || value.Any(char.IsControl))
        {
            throw new ArgumentException("A simulation identity must be bounded text without control characters.", parameter);
        }
    }
}
