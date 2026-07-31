// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OpenUsd.Rendering;

/// <summary>Describes the deterministic camera-space headlight used for parity rendering.</summary>
/// <param name="Direction">The camera-space direction from the shaded point toward the light.</param>
/// <param name="Intensity">The scalar multiplier applied to <paramref name="Color"/>.</param>
/// <param name="Color">The linear RGB light colour before <paramref name="Intensity"/> is applied.</param>
/// <param name="Ambient">The scalar ambient term applied equally to red, green, and blue.</param>
public readonly record struct RenderHeadlight(
    Vector3 Direction,
    float Intensity,
    Vector3 Color,
    float Ambient)
{
    /// <summary>
    /// Gets the canonical deterministic headlight both renderers must agree on.
    /// </summary>
    /// <remarks>
    /// Storm reports its own value across the native ABI and hdSilk cannot
    /// reference the Storm package, so without one shared constant the two would
    /// be free to drift silently and every parity colour comparison would be
    /// meaningless. A conformance test asserts the native Storm headlight equals
    /// this.
    /// </remarks>
    public static RenderHeadlight Deterministic { get; } = new(
        new Vector3(0, 0, 1),
        1,
        Vector3.One,
        0);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeRenderHeadlight
{
    private const uint Version = 1;

    public NativeRenderHeadlight()
    {
        StructSize = (uint)Unsafe.SizeOf<NativeRenderHeadlight>();
        VersionValue = Version;
        _directionX = 0;
        _directionY = 0;
        _directionZ = 0;
        _intensity = 0;
        _colorX = 0;
        _colorY = 0;
        _colorZ = 0;
        _ambient = 0;
    }

    internal RenderHeadlight ToRenderHeadlight() =>
        new(
            new Vector3(_directionX, _directionY, _directionZ),
            _intensity,
            new Vector3(_colorX, _colorY, _colorZ),
            _ambient);

    internal readonly uint StructSize;
    internal readonly uint VersionValue;
    private readonly float _directionX;
    private readonly float _directionY;
    private readonly float _directionZ;
    private readonly float _intensity;
    private readonly float _colorX;
    private readonly float _colorY;
    private readonly float _colorZ;
    private readonly float _ambient;
}
