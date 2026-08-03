// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Interop;

/// <summary>
/// Describes the native library name and ABI version expected by the managed packages.
/// </summary>
public static class OpenUsdNativeContract
{
    /// <summary>Gets the platform-neutral native import name.</summary>
    public const string LibraryName = "openusd_dotnet";

    /// <summary>Gets the ninth version of the project-owned native ABI.</summary>
    public const uint AbiVersion = 9;

    /// <summary>Gets the capabilities required by this managed contract.</summary>
    public const ulong RequiredCapabilities = 0xFFF;
}
