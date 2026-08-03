// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct OpenUsdNativePcpNodeRecord(
    int ParentIndex,
    int ArcType,
    int IsCulled,
    int IsInert,
    int IsDueToAncestor,
    int HasSpecs,
    int CanContributeSpecs,
    int NamespaceDepth,
    int DepthBelowIntroduction,
    int SiblingIndexAtOrigin,
    nuint StringOffset,
    nuint StringCount,
    nuint LayerOffset,
    nuint LayerCount);

internal readonly record struct OpenUsdNativePcpPrimIndex(
    OpenUsdNativePcpNode[] Nodes,
    string[] Errors);

internal readonly record struct OpenUsdNativePcpNode(
    int ParentIndex,
    int ArcType,
    bool IsCulled,
    bool IsInert,
    bool IsDueToAncestor,
    bool HasSpecs,
    bool CanContributeSpecs,
    int NamespaceDepth,
    int DepthBelowIntroduction,
    int SiblingIndexAtOrigin,
    string SitePath,
    string IntroPath,
    string PathAtIntroduction,
    string PathAtOriginRootIntroduction,
    string LayerStackIdentifier,
    string[] LayerIdentifiers);
