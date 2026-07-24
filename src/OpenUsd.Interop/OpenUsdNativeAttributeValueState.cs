// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Interop;

/// <summary>Describes authored and blocked value state for a native attribute.</summary>
internal readonly record struct OpenUsdNativeAttributeValueState(
    bool HasAuthoredValueOpinion,
    bool IsBlocked);
