// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd;

/// <summary>Describes authored and blocked value state for an attribute.</summary>
public readonly record struct UsdAttributeValueState(
    bool HasAuthoredValueOpinion,
    bool IsBlocked) : IUsdDetachedResult;
