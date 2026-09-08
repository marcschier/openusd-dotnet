// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Editing;

/// <summary>The recorded filesystem dependency's role in a portable review document.</summary>
public enum UsdReviewDependencyKind
{
    /// <summary>An admitted text-USD source layer.</summary>
    Layer = 0,
    /// <summary>A concrete filesystem asset.</summary>
    Asset = 1
}

/// <summary>An immutable recorded content identity admitted by the native portable-review decoder.</summary>
/// <remarks>
/// The path is provenance, not permission to open, overwrite or publish a file.
/// Inspection only reports these claims; verified opening and import check actual dependencies.
/// </remarks>
public sealed class UsdReviewDependency : IUsdDetachedResult
{
    internal UsdReviewDependency(string path, string sha256, ulong byteLength, UsdReviewDependencyKind kind)
    {
        Path = path;
        Sha256 = sha256;
        ByteLength = byteLength;
        Kind = kind;
    }

    /// <summary>Gets the recorded absolute filesystem path.</summary>
    public string Path { get; }
    /// <summary>Gets the recorded SHA-256 identity as 64 hexadecimal characters.</summary>
    public string Sha256 { get; }
    /// <summary>Gets the recorded file length, not the size of this descriptor.</summary>
    public ulong ByteLength { get; }
    /// <summary>Gets the dependency's role.</summary>
    public UsdReviewDependencyKind Kind { get; }
}
