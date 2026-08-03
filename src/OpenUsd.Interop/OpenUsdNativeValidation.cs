// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct OpenUsdNativeValidationMetadataRecord(
    int IsSuite,
    int IsTimeDependent,
    nuint StringOffset,
    nuint StringCount,
    nuint KeywordOffset,
    nuint KeywordCount,
    nuint SchemaTypeOffset,
    nuint SchemaTypeCount);

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct OpenUsdNativeValidationErrorRecord(
    int Severity,
    nuint StringOffset,
    nuint StringCount,
    nuint SiteOffset,
    nuint SiteCount);

internal readonly record struct OpenUsdNativeValidationMetadata(
    string Name,
    string Documentation,
    string PluginName,
    string[] Keywords,
    string[] SchemaTypes,
    bool IsSuite,
    bool IsTimeDependent);

internal readonly record struct OpenUsdNativeValidationError(
    int Severity,
    string ValidatorName,
    string ErrorName,
    string Message,
    string[] Sites);
