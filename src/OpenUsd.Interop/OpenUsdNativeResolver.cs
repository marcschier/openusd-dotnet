// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenUsd.Interop;

/// <summary>
/// Mirrors one native resolved-asset record produced by the bulk resolver ABI.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct OpenUsdNativeResolvedAssetRecord(
    int Resolved,
    int ContextDependent,
    int TimestampValid,
    int Reserved,
    double ModificationTime,
    nuint IdentifierOffset,
    nuint ResolvedPathOffset,
    nuint ExtensionOffset,
    nuint AssetVersionOffset,
    nuint AssetNameOffset);

/// <summary>
/// Carries one decoded resolved asset returned by a bulk resolution request.
/// </summary>
internal readonly record struct OpenUsdNativeResolvedAsset(
    string AssetPath,
    string Identifier,
    string ResolvedPath,
    string Extension,
    string AssetVersion,
    string AssetName,
    bool IsResolved,
    bool IsContextDependent,
    double? ModificationTime);

/// <summary>
/// Carries one plugin discovered by the OpenUSD plugin registry.
/// </summary>
internal readonly record struct OpenUsdNativePlugin(
    string Name,
    string Kind,
    bool IsLoaded,
    string Path,
    string ResourcePath);

/// <summary>
/// Owns an immutable native <c>ArResolverContext</c> copy.
/// </summary>
internal sealed class OpenUsdNativeResolverContext : SafeHandleZeroOrMinusOneIsInvalid
{
    internal OpenUsdNativeResolverContext(nint handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        OpenUsdNativeRuntime.ReleaseResolverContext(handle);
        return true;
    }
}
