// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Editing;

/// <summary>Identifies an exact authored property field in one layer.</summary>
public enum UsdLayerEditField
{
    /// <summary>The attribute's default opinion.</summary>
    Default = 0,
    /// <summary>One exact numeric time sample, not the entire sample map.</summary>
    TimeSample = 1,
    /// <summary>The attribute's complete connection list operation.</summary>
    AttributeConnections = 2,
    /// <summary>The relationship's complete target list operation.</summary>
    RelationshipTargets = 3
}

/// <summary>An absolute, non-variant property path and an exact authored field address.</summary>
public readonly record struct UsdLayerEditAddress : IUsdDetachedResult
{
    /// <summary>Creates an address; only time samples permit a nonzero, finite time code.</summary>
    public UsdLayerEditAddress(string path, UsdLayerEditField field, double timeCode = 0)
    {
        UsdEditingValidation.PropertyPath(path, nameof(path));
        if (field is < UsdLayerEditField.Default or > UsdLayerEditField.RelationshipTargets)
        {
            throw new ArgumentOutOfRangeException(nameof(field));
        }
        if (!double.IsFinite(timeCode) || (field != UsdLayerEditField.TimeSample && timeCode != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(timeCode), "Only finite sample times are supported.");
        }
        Path = path;
        Field = field;
        TimeCode = timeCode == 0 ? 0 : timeCode;
    }

    /// <summary>Gets the absolute property path, limited to 32 path elements.</summary>
    public string Path { get; }

    /// <summary>Gets the affected authored field.</summary>
    public UsdLayerEditField Field { get; }

    /// <summary>Gets the sample time, or positive zero for all other fields.</summary>
    public double TimeCode { get; }

    internal void Validate() => UsdEditingValidation.PropertyPath(Path, nameof(Path));
}
