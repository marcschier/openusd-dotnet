// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd;

/// <summary>Describes why a <see cref="UsdAttribute"/> try operation returned <see langword="false"/>.</summary>
public enum UsdAttributeTryFailureReason
{
    /// <summary>The operation succeeded.</summary>
    None = 0,

    /// <summary>The attribute was not present on the owning prim.</summary>
    AttributeNotFound = 1,

    /// <summary>
    /// A set operation was declined because the supplied value kind is not compatible with the
    /// attribute's declared USD type; choose a value kind matching <see cref="UsdAttribute.TypeName"/>.
    /// </summary>
    TypeIncompatible = 2,

    /// <summary>
    /// A get operation found an attribute whose authored value could not be represented by
    /// <see cref="UsdScalarValue"/>; use a typed accessor or the throwing <see cref="UsdAttribute.GetValue()"/>
    /// API for details.
    /// </summary>
    UnsupportedValueType = 3,

    /// <summary>The underlying native OpenUSD call failed.</summary>
    NativeCallFailed = 4
}
