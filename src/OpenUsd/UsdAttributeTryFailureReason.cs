// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd;

/// <summary>Describes why a <see cref="UsdAttribute"/> try operation returned <see langword="false"/>.</summary>
public enum UsdAttributeTryFailureReason
{
    /// <summary>The operation succeeded.</summary>
    None = 0,

    /// <summary>The attribute was not present on the owning prim.</summary>
    AttributeNotFound = 1,

    /// <summary>The supplied value kind is not compatible with the attribute's declared USD type.</summary>
    TypeIncompatible = 2,

    /// <summary>The attribute value exists but cannot be represented by <see cref="UsdScalarValue"/>.</summary>
    UnsupportedValueType = 3,

    /// <summary>The underlying native OpenUSD call failed.</summary>
    NativeCallFailed = 4
}
