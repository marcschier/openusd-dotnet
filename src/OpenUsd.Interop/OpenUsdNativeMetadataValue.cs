// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

/// <summary>
/// Discriminates the value held by a <see cref="OpenUsdNativeMetadataValue"/> tagged union.
/// </summary>
internal enum OpenUsdNativeMetadataKind
{
    String = 0,
    Bool = 1,
    Int64 = 2,
    Double = 3
}

/// <summary>
/// A blittable, ABI-matching tagged union used to marshal <c>openusd_metadata_value</c>
/// across the native boundary. String payloads travel through a separate buffer parameter.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct OpenUsdNativeMetadataValue
{
    internal uint StructSize;
    internal int Kind;
    internal int BoolValue;
    internal long Int64Value;
    internal double DoubleValue;
}
