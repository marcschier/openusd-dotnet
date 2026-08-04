// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Interop;

/// <summary>
/// Describes the native library name and ABI version expected by the managed packages.
/// </summary>
public static class OpenUsdNativeContract
{
    private const ulong CoreCapabilities = (1UL << 0) | (1UL << 1) | (1UL << 2) | (1UL << 3) | (1UL << 4) | (1UL << 5) | (1UL << 6) | (1UL << 7) | (1UL << 8) | (1UL << 9) | (1UL << 10) | (1UL << 11) | (1UL << 12) | (1UL << 13) | (1UL << 14) | (1UL << 16);
    private const ulong SchemaFacadeCapabilities = 1UL << 15;

    /// <summary>Gets the platform-neutral native import name.</summary>
    public const string LibraryName = "openusd_dotnet";

    /// <summary>Gets the fourteenth version of the project-owned native ABI.</summary>
    public const uint AbiVersion = 14;

    /// <summary>Gets the capabilities required by this managed contract.</summary>
    public const ulong RequiredCapabilities = CoreCapabilities | SchemaFacadeCapabilities;
}
