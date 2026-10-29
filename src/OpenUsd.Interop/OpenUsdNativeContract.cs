// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Interop;

/// <summary>
/// Describes the native library name and ABI version expected by the managed packages.
/// </summary>
public static class OpenUsdNativeContract
{
    private const ulong CoreCapabilities = 0x7FFFF7FFF;
    private const ulong SchemaFacadeCapabilities = 1UL << 15;

    /// <summary>Gets the platform-neutral native import name.</summary>
    public const string LibraryName = "openusd_dotnet";

    /// <summary>Gets the twenty-sixth version of the project-owned native ABI.</summary>
    public const uint AbiVersion = 26;

    /// <summary>Gets the capabilities required by this managed contract.</summary>
    public const ulong RequiredCapabilities = CoreCapabilities | SchemaFacadeCapabilities;

    /// <summary>Identifies the guarded EXR output API; supported handle profiles remain platform-specific.</summary>
    public const ulong ImageExrOutputCapability = 1UL << 32;

    /// <summary>Identifies the float4[] and color4f[] attribute array API.</summary>
    public const ulong AttributeVec4fArraysCapability = 1UL << 33;

    /// <summary>Identifies the composed UsdGeom stage linear units (metersPerUnit and upAxis) API.</summary>
    public const ulong StageLinearUnitsCapability = 1UL << 34;
}
