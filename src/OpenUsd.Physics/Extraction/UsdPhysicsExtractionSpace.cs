// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Extraction;

/// <summary>
/// Maps positions and orientations between stage space and simulation space.
/// </summary>
/// <remarks>
/// The extraction already reports transforms in simulation space, which is metres and Y up.
/// This helper exists so a caller can map a simulation result back onto the stage it came from
/// without guessing: the page header carries the exact units and up axis that were applied, so
/// the inverse is exact rather than approximate.
/// </remarks>
public readonly struct UsdPhysicsExtractionSpace : IEquatable<UsdPhysicsExtractionSpace>
{
    private const double HalfRootTwo = 0.70710678118654752440;

    private readonly double _metersPerUnit;
    private readonly UsdPhysicsExtractionUpAxis _upAxis;

    private UsdPhysicsExtractionSpace(double metersPerUnit, UsdPhysicsExtractionUpAxis upAxis)
    {
        _metersPerUnit = metersPerUnit;
        _upAxis = upAxis;
    }

    /// <summary>Gets the stage metersPerUnit this mapping applies.</summary>
    public double MetersPerUnit => _metersPerUnit == 0.0 ? 1.0 : _metersPerUnit;

    /// <summary>Gets the stage up axis this mapping rotates from.</summary>
    public UsdPhysicsExtractionUpAxis UpAxis => _upAxis;

    /// <summary>Reads the mapping a page was produced with.</summary>
    /// <param name="page">The extraction page.</param>
    /// <returns>The mapping between the stage space and simulation space.</returns>
    public static UsdPhysicsExtractionSpace FromPage(UsdPhysicsExtractionPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new UsdPhysicsExtractionSpace(page.MetersPerUnit, page.UpAxis);
    }

    /// <summary>Creates a mapping directly from stage metadata.</summary>
    /// <param name="metersPerUnit">The stage metersPerUnit.</param>
    /// <param name="upAxis">The stage up axis.</param>
    /// <returns>The mapping between the stage space and simulation space.</returns>
    public static UsdPhysicsExtractionSpace Create(
        double metersPerUnit, UsdPhysicsExtractionUpAxis upAxis)
    {
        if (!double.IsFinite(metersPerUnit) || metersPerUnit <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(metersPerUnit), metersPerUnit, "metersPerUnit must be positive finite.");
        }
        return new UsdPhysicsExtractionSpace(metersPerUnit, upAxis);
    }

    /// <summary>Compares two mappings for equality.</summary>
    /// <param name="left">The left mapping.</param>
    /// <param name="right">The right mapping.</param>
    /// <returns><see langword="true"/> when both mappings are the same.</returns>
    public static bool operator ==(
        UsdPhysicsExtractionSpace left, UsdPhysicsExtractionSpace right) => left.Equals(right);

    /// <summary>Compares two mappings for inequality.</summary>
    /// <param name="left">The left mapping.</param>
    /// <param name="right">The right mapping.</param>
    /// <returns><see langword="true"/> when the mappings differ.</returns>
    public static bool operator !=(
        UsdPhysicsExtractionSpace left, UsdPhysicsExtractionSpace right) => !left.Equals(right);

    /// <summary>Maps a stage position into simulation space.</summary>
    /// <param name="position">The stage position in stage units.</param>
    /// <returns>The simulation position in metres.</returns>
    public (double X, double Y, double Z) ToSimulation((double X, double Y, double Z) position)
    {
        (double x, double y, double z) = Rotate(position, forward: true);
        return (x * MetersPerUnit, y * MetersPerUnit, z * MetersPerUnit);
    }

    /// <summary>Maps a simulation position back into stage space.</summary>
    /// <param name="position">The simulation position in metres.</param>
    /// <returns>The stage position in stage units.</returns>
    public (double X, double Y, double Z) ToStage((double X, double Y, double Z) position)
    {
        double scale = 1.0 / MetersPerUnit;
        var scaled = (position.X * scale, position.Y * scale, position.Z * scale);
        return Rotate(scaled, forward: false);
    }

    /// <summary>Maps a stage direction into simulation space without any unit scaling.</summary>
    /// <param name="direction">The stage direction.</param>
    /// <returns>The direction expressed in simulation space.</returns>
    /// <remarks>
    /// Quantities such as a gravity direction or an angular velocity change basis but never
    /// change length unit, so they must not pick up the metres per unit factor.
    /// </remarks>
    public (double X, double Y, double Z) ToSimulationDirection(
        (double X, double Y, double Z) direction) => Rotate(direction, forward: true);

    /// <summary>Maps a simulation direction back into stage space without unit scaling.</summary>
    /// <param name="direction">The simulation direction.</param>
    /// <returns>The direction expressed in stage space.</returns>
    public (double X, double Y, double Z) ToStageDirection(
        (double X, double Y, double Z) direction) => Rotate(direction, forward: false);

    /// <summary>Maps a stage orientation into simulation space.</summary>
    /// <param name="rotation">The stage orientation as w, x, y, z.</param>
    /// <returns>The simulation orientation as w, x, y, z.</returns>
    public (double W, double X, double Y, double Z) ToSimulation(
        (double W, double X, double Y, double Z) rotation) =>
        Normalize(Multiply(UpRotation(forward: true), rotation));

    /// <summary>Maps a simulation orientation back into stage space.</summary>
    /// <param name="rotation">The simulation orientation as w, x, y, z.</param>
    /// <returns>The stage orientation as w, x, y, z.</returns>
    public (double W, double X, double Y, double Z) ToStage(
        (double W, double X, double Y, double Z) rotation) =>
        Normalize(Multiply(UpRotation(forward: false), rotation));

    /// <inheritdoc/>
    public bool Equals(UsdPhysicsExtractionSpace other) =>
        _metersPerUnit.Equals(other._metersPerUnit) && _upAxis == other._upAxis;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is UsdPhysicsExtractionSpace other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_metersPerUnit, _upAxis);

    private static (double W, double X, double Y, double Z) Multiply(
        (double W, double X, double Y, double Z) left,
        (double W, double X, double Y, double Z) right) => (
        (left.W * right.W) - (left.X * right.X) - (left.Y * right.Y) - (left.Z * right.Z),
        (left.W * right.X) + (left.X * right.W) + (left.Y * right.Z) - (left.Z * right.Y),
        (left.W * right.Y) - (left.X * right.Z) + (left.Y * right.W) + (left.Z * right.X),
        (left.W * right.Z) + (left.X * right.Y) - (left.Y * right.X) + (left.Z * right.W));

    private static (double W, double X, double Y, double Z) Normalize(
        (double W, double X, double Y, double Z) value)
    {
        double length = Math.Sqrt(
            (value.W * value.W) + (value.X * value.X) +
            (value.Y * value.Y) + (value.Z * value.Z));
        if (length == 0.0)
        {
            return (1.0, 0.0, 0.0, 0.0);
        }
        return (value.W / length, value.X / length, value.Y / length, value.Z / length);
    }

    private (double W, double X, double Y, double Z) UpRotation(bool forward)
    {
        double sign = forward ? 1.0 : -1.0;
        return _upAxis switch
        {
            UsdPhysicsExtractionUpAxis.Z => (HalfRootTwo, -HalfRootTwo * sign, 0.0, 0.0),
            UsdPhysicsExtractionUpAxis.X => (HalfRootTwo, 0.0, 0.0, HalfRootTwo * sign),
            _ => (1.0, 0.0, 0.0, 0.0),
        };
    }

    private (double X, double Y, double Z) Rotate(
        (double X, double Y, double Z) value, bool forward) => _upAxis switch
        {
            // A minus ninety degree turn about X takes stage Z up onto simulation Y up.
            UsdPhysicsExtractionUpAxis.Z => forward
                ? (value.X, value.Z, -value.Y)
                : (value.X, -value.Z, value.Y),

            // A ninety degree turn about Z takes stage X up onto simulation Y up.
            UsdPhysicsExtractionUpAxis.X => forward
                ? (-value.Y, value.X, value.Z)
                : (value.Y, -value.X, value.Z),

            _ => value,
        };
}
