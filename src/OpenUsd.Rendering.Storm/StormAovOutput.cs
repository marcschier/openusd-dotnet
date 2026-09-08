// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Rendering.Storm;

/// <summary>Reports native output or identity-table availability explicitly.</summary>
public enum StormAovStatus : uint
{
    /// <summary>The optional identity table was not requested.</summary>
    NotRequested = 0,
    /// <summary>The native output was produced and read back successfully.</summary>
    Ready = 1,
    /// <summary>No supported output representation exists.</summary>
    Unsupported = 2,
    /// <summary>The selected native route did not produce the output.</summary>
    Absent = 3,
}

/// <summary>Describes the actual native scalar representation, not display encoding.</summary>
public enum StormAovFormat : uint
{
    /// <summary>No pixel payload is available.</summary>
    None = 0,
    /// <summary>Four native IEEE half-precision components.</summary>
    Float16Vec4 = 1,
    /// <summary>One native IEEE single-precision value.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name",
        Justification = "The format discriminator names the exact native scalar storage type.")]
    Float32 = 2,
    /// <summary>One native signed 32-bit integer.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name",
        Justification = "The format discriminator names the exact native scalar storage type.")]
    Int32 = 3,
    /// <summary>Four raw normalized unsigned bytes.</summary>
    UNorm8Vec4 = 4,
}

/// <summary>Identifies the row origin of an available output.</summary>
public enum StormAovOrigin : uint
{
    /// <summary>No pixel payload is available.</summary>
    None = 0,
    /// <summary>The first row is the top row.</summary>
    TopLeft = 1,
}

/// <summary>States the proven convention of an available depth output.</summary>
public enum StormAovDepthConvention : uint
{
    /// <summary>This is not an available depth output.</summary>
    None = 0,
    /// <summary>OpenGL normalized window depth in [0,1], clear 1; not linear distance.</summary>
    OpenGlWindow = 1,
}

/// <summary>One native half-precision render-color pixel without precision inflation.</summary>
/// <param name="Red">Red component.</param>
/// <param name="Green">Green component.</param>
/// <param name="Blue">Blue component.</param>
/// <param name="Alpha">Alpha component.</param>
public readonly record struct StormAovColor(Half Red, Half Green, Half Blue, Half Alpha);

/// <summary>One raw quantized Neye pixel, not a general signed float-normal encoding.</summary>
/// <param name="X">Raw X byte.</param>
/// <param name="Y">Raw Y byte.</param>
/// <param name="Z">Raw Z byte.</param>
/// <param name="W">Raw fourth byte.</param>
public readonly record struct StormAovNeye(byte X, byte Y, byte Z, byte W);

/// <summary>Immutable metadata for one explicitly named native output.</summary>
public abstract class StormAovOutput
{
    private protected StormAovOutput(in StormAovNative.Output output)
    {
        Kind = output.Kind;
        Status = output.Status;
        Format = output.Format;
        Width = (int)output.Width;
        Height = (int)output.Height;
        RowStrideBytes = (int)output.RowStrideBytes;
        Origin = output.Origin;
        ResolvedMultisample = output.Flags != 0;
        DepthConvention = output.DepthConvention;
    }

    /// <summary>Gets the exact requested output kind.</summary>
    public StormAovKind Kind { get; }
    /// <summary>Gets actual availability; no synthetic pixel payload replaces a gap.</summary>
    public StormAovStatus Status { get; }
    /// <summary>Gets the actual native representation.</summary>
    public StormAovFormat Format { get; }
    /// <summary>Gets physical width, or zero for unavailable output.</summary>
    public int Width { get; }
    /// <summary>Gets physical height, or zero for unavailable output.</summary>
    public int Height { get; }
    /// <summary>Gets the native tight byte stride.</summary>
    public int RowStrideBytes { get; }
    /// <summary>Gets the row origin.</summary>
    public StormAovOrigin Origin { get; }
    /// <summary>Gets whether the native buffer was resolved from multisampling.</summary>
    public bool ResolvedMultisample { get; }
    /// <summary>Gets the explicit depth convention.</summary>
    public StormAovDepthConvention DepthConvention { get; }

    internal static StormAovOutput Unavailable(in StormAovNative.Output output) =>
        new UnavailableOutput(in output);

    private sealed class UnavailableOutput(in StormAovNative.Output output) : StormAovOutput(in output);
}

/// <summary>Owns typed, top-down pixels with no mutable array or SyncRoot escape.</summary>
/// <typeparam name="TPixel">The representation associated with the output format.</typeparam>
public sealed class StormAovOutput<TPixel> : StormAovOutput
    where TPixel : unmanaged
{
    internal StormAovOutput(in StormAovNative.Output output, TPixel[] pixels)
        : base(in output)
    {
        Pixels = new OwnedReadOnlyList<TPixel>(pixels);
    }

    /// <summary>Gets detached immutable pixels in top-down row-major order.</summary>
    public IReadOnlyList<TPixel> Pixels { get; }

    /// <summary>Reads one detached pixel without native interop.</summary>
    /// <param name="x">Zero-based physical column.</param>
    /// <param name="y">Zero-based physical row from the top.</param>
    /// <returns>The typed pixel value.</returns>
    public TPixel GetPixel(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        return Pixels[(y * Width) + x];
    }
}
