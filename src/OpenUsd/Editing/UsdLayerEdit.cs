// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Editing;

/// <summary>Exact declaration variability, including optional presence in captured opinions.</summary>
public enum UsdLayerEditVariability
{
    /// <summary>A varying declaration.</summary>
    Varying = 0,
    /// <summary>A uniform declaration.</summary>
    Uniform = 1
}

/// <summary>The operation on one authored address.</summary>
public enum UsdLayerEditOperation
{
    /// <summary>Remove only the affected opinion; do not delete an existing declaration.</summary>
    Clear = 0,
    /// <summary>Set a concrete value with no type coercion.</summary>
    Set = 1,
    /// <summary>Author a default or time-sample value block.</summary>
    Block = 2
}

/// <summary>A deeply immutable typed mutation; creation hints never replace existing declarations.</summary>
public sealed class UsdLayerEdit : IUsdDetachedResult
{
    private UsdLayerEdit(
        UsdLayerEditAddress address, UsdLayerEditOperation operation, UsdLayerEditValue value,
        string creationTypeName, UsdLayerEditVariability creationVariability, bool creationCustom)
    {
        address.Validate();
        _ = UsdEditingValidation.Text(creationTypeName, nameof(creationTypeName));
        if (creationVariability is not (UsdLayerEditVariability.Varying or UsdLayerEditVariability.Uniform))
        {
            throw new ArgumentOutOfRangeException(nameof(creationVariability));
        }
        bool pathList = address.Field is
            UsdLayerEditField.AttributeConnections or UsdLayerEditField.RelationshipTargets;
        if (operation == UsdLayerEditOperation.Block && pathList)
        {
            throw new ArgumentException("Connections and targets cannot be blocked.", nameof(address));
        }
        if (operation == UsdLayerEditOperation.Set)
        {
            if (!value.IsOrdinaryValue || pathList != (value.Kind == UsdLayerEditValueKind.PathList))
            {
                throw new ArgumentException(
                    "The concrete value domain does not match the affected field.", nameof(value));
            }
            if (pathList)
            {
                value.AsPathList().ValidateMutationPaths(address.Field == UsdLayerEditField.AttributeConnections);
            }
        }
        if (address.Field == UsdLayerEditField.RelationshipTargets && creationTypeName.Length != 0)
        {
            throw new ArgumentException("Relationship creation uses an empty type name.", nameof(creationTypeName));
        }
        Address = address;
        Operation = operation;
        Value = value;
        CreationTypeName = creationTypeName;
        CreationVariability = creationVariability;
        CreationCustom = creationCustom;
    }

    /// <summary>Gets the exact authored address.</summary>
    public UsdLayerEditAddress Address { get; }
    /// <summary>Gets the operation.</summary>
    public UsdLayerEditOperation Operation { get; }
    /// <summary>Gets the concrete set value, or Absent for clear and block operations.</summary>
    public UsdLayerEditValue Value { get; }
    /// <summary>Gets the exact registered type token used only when creating an attribute.</summary>
    public string CreationTypeName { get; }
    /// <summary>Gets the variability used only when creating a property.</summary>
    public UsdLayerEditVariability CreationVariability { get; }
    /// <summary>Gets the custom flag used only when creating a property.</summary>
    public bool CreationCustom { get; }

    /// <summary>Creates a concrete set; native validation enforces the exact declared type and role.</summary>
    public static UsdLayerEdit Set(
        UsdLayerEditAddress address, UsdLayerEditValue value, string creationTypeName = "",
        UsdLayerEditVariability creationVariability = UsdLayerEditVariability.Varying, bool creationCustom = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new UsdLayerEdit(
            address, UsdLayerEditOperation.Set, value, creationTypeName, creationVariability, creationCustom);
    }

    /// <summary>Clears only the addressed opinion; an absent property is not created.</summary>
    public static UsdLayerEdit Clear(UsdLayerEditAddress address) =>
        new(address, UsdLayerEditOperation.Clear, UsdLayerEditValue.Absent, "", UsdLayerEditVariability.Varying, false);

    /// <summary>Blocks one default or sample, optionally supplying an exact declaration for creation.</summary>
    public static UsdLayerEdit Block(
        UsdLayerEditAddress address, string creationTypeName = "",
        UsdLayerEditVariability creationVariability = UsdLayerEditVariability.Varying, bool creationCustom = false) =>
        new(address, UsdLayerEditOperation.Block, UsdLayerEditValue.Absent,
            creationTypeName, creationVariability, creationCustom);
}
