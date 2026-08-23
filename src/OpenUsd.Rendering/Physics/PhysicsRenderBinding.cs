// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering;

/// <summary>
/// Names the renderable entity one simulated object drives.
/// </summary>
/// <param name="PrimPath">The absolute authored prim path of the renderable entity.</param>
/// <param name="InstanceIndex">The zero-based instance ordinal of the renderable entity.</param>
public readonly record struct PhysicsRenderBinding(string PrimPath, int InstanceIndex);

/// <summary>
/// Maps stable simulation identities onto the renderable entities they drive.
/// </summary>
/// <remarks>
/// The table is the only place a simulation identity meets an authored path. It stores no stage,
/// prim, or solver handle, so a backend can resolve overrides to its own retained entities without
/// reading USD during a render update. Bindings are bounded: a table that is full refuses further
/// bindings and counts them instead of growing without limit.
/// </remarks>
public sealed class PhysicsRenderBindingTable
{
    private readonly Dictionary<PhysicsRenderObjectId, PhysicsRenderBinding> _bindings;
    private readonly Dictionary<ulong, PhysicsRenderBinding> _byIdentity;
    private readonly HashSet<ulong> _ambiguous = [];
    private long _refusedBindings;

    /// <summary>Initializes a bounded binding table.</summary>
    /// <param name="capacity">The number of bindings the table can hold.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
    public PhysicsRenderBindingTable(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        Capacity = capacity;
        _bindings = new Dictionary<PhysicsRenderObjectId, PhysicsRenderBinding>(capacity);
        _byIdentity = new Dictionary<ulong, PhysicsRenderBinding>(capacity);
    }

    /// <summary>Gets the number of bindings the table can hold.</summary>
    public int Capacity { get; }

    /// <summary>Gets the number of bindings the table holds.</summary>
    public int Count => _bindings.Count;

    /// <summary>Gets the monotonic revision advanced by every change to the table.</summary>
    public ulong Revision { get; private set; }

    /// <summary>Gets the number of bindings refused because the table was full.</summary>
    public long RefusedBindings => _refusedBindings;

    /// <summary>Binds one simulated object to the renderable entity it drives.</summary>
    /// <param name="id">The stable simulation identity.</param>
    /// <param name="primPath">The absolute authored prim path of the renderable entity.</param>
    /// <param name="instanceIndex">The zero-based instance ordinal.</param>
    /// <returns>
    /// <see langword="true"/> when the binding was stored; <see langword="false"/> when the table
    /// was full and the binding was refused and counted instead.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="primPath"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="primPath"/> is not an absolute prim path, or <paramref name="id"/> is the
    /// identity that addresses no object.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="instanceIndex"/> is negative.
    /// </exception>
    public bool TryBind(PhysicsRenderObjectId id, string primPath, int instanceIndex = 0)
    {
        SelectionPathValidation.ValidateAbsolutePrimPath(primPath, nameof(primPath));
        ArgumentOutOfRangeException.ThrowIfNegative(instanceIndex);
        if (id.IsNone)
        {
            throw new ArgumentException(
                "A binding requires an identity that addresses an object.",
                nameof(id));
        }

        var binding = new PhysicsRenderBinding(primPath, instanceIndex);
        if (_bindings.TryGetValue(id, out PhysicsRenderBinding existing))
        {
            if (existing == binding)
            {
                return true;
            }

            _bindings[id] = binding;
            TrackIdentity(id.Value, binding);
            Revision++;
            return true;
        }

        if (_bindings.Count >= Capacity)
        {
            _refusedBindings++;
            return false;
        }

        _bindings.Add(id, binding);
        TrackIdentity(id.Value, binding);
        Revision++;
        return true;
    }

    /// <summary>Removes the binding of one simulated object.</summary>
    /// <param name="id">The stable simulation identity.</param>
    /// <returns><see langword="true"/> when a binding was removed.</returns>
    public bool Unbind(PhysicsRenderObjectId id)
    {
        if (!_bindings.Remove(id))
        {
            return false;
        }

        _ = _byIdentity.Remove(id.Value);
        _ = _ambiguous.Remove(id.Value);
        Revision++;
        return true;
    }

    /// <summary>Resolves the renderable entity one simulated object drives.</summary>
    /// <param name="id">The stable simulation identity.</param>
    /// <param name="binding">The resolved renderable entity.</param>
    /// <returns><see langword="true"/> when the identity is bound.</returns>
    /// <remarks>
    /// An identity that misses on the exact key is still resolved by its stable 64-bit value when
    /// that value names exactly one renderable entity. The value alone already addresses the
    /// authored object, while the kind carried alongside it is a classification the producer of a
    /// pose and the producer of a binding can legitimately describe differently; dropping such an
    /// override would silently freeze a simulated prim rather than draw it.
    /// </remarks>
    public bool TryResolve(PhysicsRenderObjectId id, out PhysicsRenderBinding binding)
    {
        if (_bindings.TryGetValue(id, out binding))
        {
            return true;
        }

        return !_ambiguous.Contains(id.Value) && _byIdentity.TryGetValue(id.Value, out binding);
    }

    /// <summary>Removes every binding.</summary>
    public void Clear()
    {
        if (_bindings.Count == 0)
        {
            return;
        }

        _bindings.Clear();
        _byIdentity.Clear();
        _ambiguous.Clear();
        Revision++;
    }

    private void TrackIdentity(ulong value, PhysicsRenderBinding binding)
    {
        if (_byIdentity.TryGetValue(value, out PhysicsRenderBinding existing))
        {
            if (existing != binding)
            {
                _ = _ambiguous.Add(value);
            }

            return;
        }

        _byIdentity[value] = binding;
    }
}

/// <summary>
/// Composes renderer-neutral world transforms from physics poses and authored transforms.
/// </summary>
/// <remarks>
/// <para>
/// The composed matrix is row-major with the translation in the last row, which is the layout the
/// renderer-neutral scene state and every render backend already consume. The authored transform
/// only contributes its scale and shear: a simulated body owns its world rotation and translation,
/// and preserving the authored stretch keeps a scaled or sheared prim the shape its author gave it
/// without authoring anything back into USD.
/// </para>
/// <para>
/// The authored basis is split by a left polar decomposition <c>A = S * Q</c>, where <c>S</c> is
/// the symmetric positive semi-definite stretch that carries every authored scale and shear and
/// <c>Q</c> is the authored rotation. Because the row-major layout applies the basis as
/// <c>v * A</c>, composing <c>S * R</c> with the simulated rotation <c>R</c> keeps the authored
/// stretch in the body's own frame and replaces only the authored rotation. Retaining the row
/// lengths alone would discard authored shear, so the stretch is recovered as the symmetric square
/// root of <c>A * A^T</c> through a fixed-sweep Jacobi diagonalization.
/// </para>
/// <para>
/// Degenerate input never produces a non-finite transform. An authored basis whose elements are
/// not finite, or whose decomposition overflows, falls back to the unstretched simulated pose, so
/// the prim renders at unit scale rather than disappearing. A singular or near-singular authored
/// basis keeps its collapsed axes collapsed, because negative and denormal eigenvalues are clamped
/// to zero before the square root is taken; that is the authored shape and it stays finite.
/// </para>
/// </remarks>
public static class PhysicsRenderTransforms
{
    /// <summary>Gets the number of elements in a composed transform.</summary>
    public const int ElementCount = 16;

    private const int BasisElementCount = 9;
    private const int MaximumJacobiSweeps = 16;

    /// <summary>Composes the world transform of one override.</summary>
    /// <param name="value">The override the transform is composed from.</param>
    /// <param name="authored">
    /// The authored row-major local-to-world transform whose scale and shear are preserved, or an empty
    /// span to compose an unstretched transform.
    /// </param>
    /// <param name="destination">The composed row-major transform.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="authored"/> is neither empty nor <see cref="ElementCount"/> elements long,
    /// or <paramref name="destination"/> is not <see cref="ElementCount"/> elements long.
    /// </exception>
    public static void Compose(
        in PhysicsRenderTransformOverride value,
        ReadOnlySpan<double> authored,
        Span<double> destination) =>
        Compose(value.Position, value.Orientation, authored, destination);

    /// <summary>Composes a world transform from a position and orientation.</summary>
    /// <param name="position">The world-space position, in stage units.</param>
    /// <param name="orientation">The world-space orientation.</param>
    /// <param name="authored">
    /// The authored row-major local-to-world transform whose scale and shear are preserved, or an empty
    /// span to compose an unstretched transform.
    /// </param>
    /// <param name="destination">The composed row-major transform.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="authored"/> is neither empty nor <see cref="ElementCount"/> elements long,
    /// or <paramref name="destination"/> is not <see cref="ElementCount"/> elements long.
    /// </exception>
    public static void Compose(
        UsdVec3d position,
        PhysicsRenderOrientation orientation,
        ReadOnlySpan<double> authored,
        Span<double> destination)
    {
        if (destination.Length != ElementCount)
        {
            throw new ArgumentException(
                $"A composed transform must contain exactly {ElementCount} elements.",
                nameof(destination));
        }
        if (!authored.IsEmpty && authored.Length != ElementCount)
        {
            throw new ArgumentException(
                $"An authored transform must contain exactly {ElementCount} elements.",
                nameof(authored));
        }

        PhysicsRenderOrientation unit = orientation.Normalized();
        double x = unit.X;
        double y = unit.Y;
        double z = unit.Z;
        double w = unit.W;

        destination[0] = 1 - (2 * ((y * y) + (z * z)));
        destination[1] = 2 * ((x * y) + (z * w));
        destination[2] = 2 * ((x * z) - (y * w));
        destination[3] = 0;
        destination[4] = 2 * ((x * y) - (z * w));
        destination[5] = 1 - (2 * ((x * x) + (z * z)));
        destination[6] = 2 * ((y * z) + (x * w));
        destination[7] = 0;
        destination[8] = 2 * ((x * z) + (y * w));
        destination[9] = 2 * ((y * z) - (x * w));
        destination[10] = 1 - (2 * ((x * x) + (y * y)));
        destination[11] = 0;
        destination[12] = position.X;
        destination[13] = position.Y;
        destination[14] = position.Z;
        destination[15] = 1;

        if (authored.IsEmpty)
        {
            return;
        }

        Span<double> stretch = stackalloc double[BasisElementCount];
        if (!TryDecomposeStretch(authored, stretch))
        {
            return;
        }

        Span<double> rotation = stackalloc double[BasisElementCount];
        for (int row = 0; row < 3; row++)
        {
            int source = row * 4;
            int target = row * 3;
            rotation[target] = destination[source];
            rotation[target + 1] = destination[source + 1];
            rotation[target + 2] = destination[source + 2];
        }

        Span<double> composed = stackalloc double[BasisElementCount];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                double sum = 0;
                for (int inner = 0; inner < 3; inner++)
                {
                    sum += stretch[(row * 3) + inner] * rotation[(inner * 3) + column];
                }

                if (!double.IsFinite(sum))
                {
                    return;
                }

                composed[(row * 3) + column] = sum;
            }
        }

        for (int row = 0; row < 3; row++)
        {
            int source = row * 3;
            int target = row * 4;
            destination[target] = composed[source];
            destination[target + 1] = composed[source + 1];
            destination[target + 2] = composed[source + 2];
        }
    }

    /// <summary>
    /// Recovers the symmetric scale and shear of an authored basis by left polar decomposition.
    /// </summary>
    /// <param name="authored">The authored row-major transform.</param>
    /// <param name="stretch">The recovered symmetric row-major 3x3 stretch.</param>
    /// <returns>
    /// <see langword="false"/> when the authored basis is not finite or the decomposition
    /// overflows, in which case the caller keeps the unstretched simulated pose.
    /// </returns>
    private static bool TryDecomposeStretch(ReadOnlySpan<double> authored, Span<double> stretch)
    {
        Span<double> basis = stackalloc double[BasisElementCount];
        for (int row = 0; row < 3; row++)
        {
            int source = row * 4;
            for (int column = 0; column < 3; column++)
            {
                double element = authored[source + column];
                if (!double.IsFinite(element))
                {
                    return false;
                }

                basis[(row * 3) + column] = element;
            }
        }

        // A * A^T is symmetric positive semi-definite and equals S^2, so diagonalizing it yields
        // the stretch without ever forming the authored rotation the simulated pose replaces.
        Span<double> gram = stackalloc double[BasisElementCount];
        for (int row = 0; row < 3; row++)
        {
            for (int column = row; column < 3; column++)
            {
                double sum = 0;
                for (int inner = 0; inner < 3; inner++)
                {
                    sum += basis[(row * 3) + inner] * basis[(column * 3) + inner];
                }

                if (!double.IsFinite(sum))
                {
                    return false;
                }

                gram[(row * 3) + column] = sum;
                gram[(column * 3) + row] = sum;
            }
        }

        Span<double> axes = stackalloc double[BasisElementCount];
        Diagonalize(gram, axes);

        for (int row = 0; row < 3; row++)
        {
            for (int column = row; column < 3; column++)
            {
                double sum = 0;
                for (int axis = 0; axis < 3; axis++)
                {
                    double eigenvalue = gram[(axis * 3) + axis];
                    double length = eigenvalue > 0 ? Math.Sqrt(eigenvalue) : 0;
                    sum += axes[(row * 3) + axis] * length * axes[(column * 3) + axis];
                }

                if (!double.IsFinite(sum))
                {
                    return false;
                }

                stretch[(row * 3) + column] = sum;
                stretch[(column * 3) + row] = sum;
            }
        }

        return true;
    }

    /// <summary>Diagonalizes a symmetric 3x3 matrix with a fixed-sweep cyclic Jacobi rotation.</summary>
    /// <param name="symmetric">
    /// The symmetric row-major 3x3 matrix, replaced by its eigenvalues on the diagonal.
    /// </param>
    /// <param name="axes">The row-major eigenvectors, one per column.</param>
    private static void Diagonalize(Span<double> symmetric, Span<double> axes)
    {
        axes.Clear();
        axes[0] = 1;
        axes[4] = 1;
        axes[8] = 1;

        double magnitude = 0;
        for (int index = 0; index < BasisElementCount; index++)
        {
            magnitude = Math.Max(magnitude, Math.Abs(symmetric[index]));
        }

        if (magnitude == 0)
        {
            return;
        }

        // Doubles carry about sixteen digits, so an off-diagonal energy this far below the matrix
        // scale is already at the noise floor and further sweeps only churn.
        double threshold = magnitude * magnitude * 1e-30;
        for (int sweep = 0; sweep < MaximumJacobiSweeps; sweep++)
        {
            double off = (symmetric[1] * symmetric[1]) +
                (symmetric[2] * symmetric[2]) +
                (symmetric[5] * symmetric[5]);
            if (off <= threshold)
            {
                return;
            }

            Annihilate(symmetric, axes, 0, 1);
            Annihilate(symmetric, axes, 0, 2);
            Annihilate(symmetric, axes, 1, 2);
        }
    }

    /// <summary>Zeroes one off-diagonal pair of a symmetric 3x3 matrix.</summary>
    /// <param name="symmetric">The symmetric row-major 3x3 matrix.</param>
    /// <param name="axes">The accumulated row-major eigenvectors.</param>
    /// <param name="first">The lower index of the annihilated pair.</param>
    /// <param name="second">The higher index of the annihilated pair.</param>
    private static void Annihilate(
        Span<double> symmetric,
        Span<double> axes,
        int first,
        int second)
    {
        double pivot = symmetric[(first * 3) + second];
        if (pivot == 0)
        {
            return;
        }

        double lower = symmetric[(first * 3) + first];
        double upper = symmetric[(second * 3) + second];
        double theta = (upper - lower) / (2 * pivot);
        double tangent = (theta < 0 ? -1 : 1) /
            (Math.Abs(theta) + Math.Sqrt((theta * theta) + 1));
        double cosine = 1 / Math.Sqrt((tangent * tangent) + 1);
        double sine = tangent * cosine;
        int other = 3 - first - second;
        double otherLower = symmetric[(other * 3) + first];
        double otherUpper = symmetric[(other * 3) + second];

        symmetric[(first * 3) + first] = lower - (tangent * pivot);
        symmetric[(second * 3) + second] = upper + (tangent * pivot);
        symmetric[(first * 3) + second] = 0;
        symmetric[(second * 3) + first] = 0;

        double rotatedLower = (cosine * otherLower) - (sine * otherUpper);
        double rotatedUpper = (sine * otherLower) + (cosine * otherUpper);
        symmetric[(other * 3) + first] = rotatedLower;
        symmetric[(first * 3) + other] = rotatedLower;
        symmetric[(other * 3) + second] = rotatedUpper;
        symmetric[(second * 3) + other] = rotatedUpper;

        for (int row = 0; row < 3; row++)
        {
            double axisLower = axes[(row * 3) + first];
            double axisUpper = axes[(row * 3) + second];
            axes[(row * 3) + first] = (cosine * axisLower) - (sine * axisUpper);
            axes[(row * 3) + second] = (sine * axisLower) + (cosine * axisUpper);
        }
    }
}
