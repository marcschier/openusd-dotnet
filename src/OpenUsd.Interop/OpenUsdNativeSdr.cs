// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

/// <summary>Identifies whether a native Sdr shader property is an input or an output.</summary>
internal enum OpenUsdNativeSdrPropertyDirection
{
    /// <summary>An input property.</summary>
    Input = 0,
    /// <summary>An output property.</summary>
    Output = 1
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct OpenUsdNativeSdrPropertyRecord(
    int Direction,
    int IsArray,
    int IsConnectable,
    int Reserved,
    nuint StringOffset,
    nuint StringCount);

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct OpenUsdNativeSdrNodeDefinitionRecord(
    int IsValid,
    int Reserved,
    nuint StringOffset,
    nuint StringCount,
    nuint PropertyOffset,
    nuint PropertyCount);

/// <summary>One shader input or output detached from native Sdr registry storage.</summary>
internal readonly record struct OpenUsdNativeSdrProperty(
    string Name,
    string Type,
    OpenUsdNativeSdrPropertyDirection Direction,
    bool IsArray,
    bool IsConnectable);

/// <summary>One shader node definition detached from native Sdr/Ndr registry storage.</summary>
internal readonly record struct OpenUsdNativeSdrNodeDefinition(
    string Identifier,
    string Name,
    string Function,
    string ShadingSystem,
    string Context,
    string ResolvedDefinitionUri,
    string ResolvedImplementationUri,
    string ImplementationName,
    OpenUsdNativeSdrProperty[] Properties,
    bool IsValid);

/// <summary>
/// One decoded shader node-definition registry page, detached from native storage. Mirrors
/// openusd_sdr_node_definition_view's OPENUSD_SDR_NODE_DEFINITION_FLAG_TRUNCATED bit: callers
/// must not treat <see cref="Definitions"/> as complete when <see cref="IsTruncated"/> is set.
/// </summary>
internal readonly record struct OpenUsdNativeSdrNodeDefinitionPage(
    OpenUsdNativeSdrNodeDefinition[] Definitions,
    bool IsTruncated);
