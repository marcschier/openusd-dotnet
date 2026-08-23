// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Extraction;

/// <summary>Bounds and switches for one physics extraction.</summary>
/// <remarks>
/// Every capacity defaults to the built-in bound. Lowering one is how a caller keeps a page
/// small enough for a constrained budget; the extraction then reports truncation instead of
/// growing without limit.
/// </remarks>
public sealed record UsdPhysicsExtractionOptions
{
    /// <summary>Gets the shared options every extraction uses unless told otherwise.</summary>
    public static UsdPhysicsExtractionOptions Default { get; } = new();

    /// <summary>Gets the time code to sample the stage at.</summary>
    /// <remarks>A non finite value requests the stage default time code.</remarks>
    public double TimeCode { get; init; } = double.NaN;

    /// <summary>Gets whether collider mesh points and indices are recorded.</summary>
    public bool IncludeMeshData { get; init; } = true;

    /// <summary>Gets whether authored physics properties without a canonical key are recorded.</summary>
    /// <remarks>
    /// Unmapped properties always take part in the content fingerprint. This switch only decides
    /// whether they also occupy property records, so turning it on never changes the fingerprint.
    /// </remarks>
    public bool IncludeUnmapped { get; init; }

    /// <summary>Gets whether prims whose composed visibility is invisible are skipped.</summary>
    public bool SkipInvisible { get; init; }

    /// <summary>Gets whether prims whose composed purpose is guide are skipped.</summary>
    public bool SkipGuide { get; init; } = true;

    /// <summary>Gets the largest number of objects to record, or zero for the built-in bound.</summary>
    public int MaxObjects { get; init; }

    /// <summary>Gets the largest number of properties to record, or zero for the bound.</summary>
    public int MaxProperties { get; init; }

    /// <summary>Gets the largest number of relationships to record, or zero for the bound.</summary>
    public int MaxRelationships { get; init; }

    /// <summary>Gets the largest number of targets to record, or zero for the bound.</summary>
    public int MaxTargets { get; init; }

    /// <summary>Gets the largest number of numbers to record, or zero for the bound.</summary>
    public int MaxNumbers { get; init; }

    /// <summary>Gets the largest number of texts to record, or zero for the bound.</summary>
    public int MaxTexts { get; init; }

    /// <summary>Gets the largest number of points to record, or zero for the bound.</summary>
    public int MaxPoints { get; init; }

    /// <summary>Gets the largest number of triangle indices to record, or zero for the bound.</summary>
    public int MaxIndices { get; init; }

    /// <summary>Gets the largest number of diagnostics to record, or zero for the bound.</summary>
    public int MaxDiagnostics { get; init; }

    /// <summary>Gets the largest string byte count to record, or zero for the bound.</summary>
    public int MaxStringBytes { get; init; }

    internal PhysicsExtractNativeOptions ToNative()
    {
        uint flags = 0;
        if (IncludeMeshData)
        {
            flags |= PhysicsExtractAbi.OptionIncludeMeshData;
        }
        if (IncludeUnmapped)
        {
            flags |= PhysicsExtractAbi.OptionIncludeUnmapped;
        }
        if (SkipInvisible)
        {
            flags |= PhysicsExtractAbi.OptionSkipInvisible;
        }
        if (SkipGuide)
        {
            flags |= PhysicsExtractAbi.OptionSkipGuide;
        }

        return new PhysicsExtractNativeOptions
        {
            StructSize = PhysicsExtractNativeMethods.OptionsBytes,
            Version = PhysicsExtractAbi.OptionsVersion,
            TimeCode = TimeCode,
            Flags = flags,
            MaxObjects = Bound(MaxObjects, PhysicsExtractAbi.SectionObjects),
            MaxProperties = Bound(MaxProperties, PhysicsExtractAbi.SectionProperties),
            MaxRelationships = Bound(MaxRelationships, PhysicsExtractAbi.SectionRelationships),
            MaxTargets = Bound(MaxTargets, PhysicsExtractAbi.SectionTargets),
            MaxNumbers = Bound(MaxNumbers, PhysicsExtractAbi.SectionNumbers),
            MaxTexts = Bound(MaxTexts, PhysicsExtractAbi.SectionTexts),
            MaxPoints = Bound(MaxPoints, PhysicsExtractAbi.SectionPoints),
            MaxIndices = Bound(MaxIndices, PhysicsExtractAbi.SectionIndices),
            MaxDiagnostics = Bound(MaxDiagnostics, PhysicsExtractAbi.SectionDiagnostics),
            MaxStringBytes = Bound(MaxStringBytes, PhysicsExtractAbi.SectionStrings),
            Reserved0 = 0,
        };
    }

    private static uint Bound(int requested, int section)
    {
        if (requested < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requested),
                requested,
                $"The {PhysicsExtractAbi.Name(section)} capacity cannot be negative.");
        }
        int capacity = PhysicsExtractAbi.Capacity(section);
        return requested == 0 ? 0u : (uint)Math.Min(requested, capacity);
    }
}
