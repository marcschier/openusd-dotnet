// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

public sealed partial class SilkPhysicsTransformOverrides
{
    private enum TargetFailure { None, Unresolved, Capacity }

    private sealed class TargetGroup(PhysicsRenderObjectId id, PhysicsRenderBinding binding)
    {
        internal PhysicsRenderObjectId Id { get; } = id;
        internal PhysicsRenderBinding Binding { get; } = binding;
        internal SilkMeshData[] Meshes { get; set; } = [];
        internal TargetFailure Failure { get; set; }
    }

    private SilkSceneState? _targetScene;
    private PhysicsRenderBindingTable? _targetBindings;
    private ulong _targetGeometryRevision;
    private ulong _targetRollbackGeneration;
    private ulong _targetBindingRevision;
    private TargetGroup[] _targetGroups = [];

    private int RefreshSubtrees(
        SilkSceneState scene, PhysicsRenderBindingTable bindings, PhysicsRenderOverrideView overrides)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(overrides.Count, Capacity, nameof(overrides));
        ReadOnlySpan<PhysicsRenderTransformOverride> items = overrides.Items;
        EnsureSubtreeTargets(scene, bindings, items);
        _slots.Clear();
        ResolvedOverrideCount = 0;
        for (int index = 0; index < items.Length; index++)
        {
            TargetGroup group = _targetGroups[index];
            if (group.Failure != TargetFailure.None)
            {
                if (group.Failure == TargetFailure.Capacity)
                {
                    _droppedOverrides++;
                }
                else
                {
                    _unresolvedOverrides++;
                }
                continue;
            }
            foreach (SilkMeshData mesh in group.Meshes)
            {
                int slot = _slots.Count;
                _slots.Add(mesh.Id, slot);
                Span<double> destination = _transforms.AsSpan(slot * PhysicsRenderTransforms.ElementCount,
                    PhysicsRenderTransforms.ElementCount);
                if (group.Binding.IncludesDescendants)
                {
                    ComposeSubtree(items[index], group.Binding, mesh.Transform.Span, destination);
                }
                else
                {
                    PhysicsRenderTransforms.Compose(items[index], mesh.Transform.Span, destination);
                }
            }
            ResolvedOverrideCount++;
        }
        Revision++;
        SourceRevision = overrides.Revision;
        return Count;
    }

    private void EnsureSubtreeTargets(
        SilkSceneState scene, PhysicsRenderBindingTable bindings,
        ReadOnlySpan<PhysicsRenderTransformOverride> items)
    {
        bool current = ReferenceEquals(scene, _targetScene) && ReferenceEquals(bindings, _targetBindings) &&
            scene.GeometryRevision == _targetGeometryRevision && bindings.Revision == _targetBindingRevision &&
            scene.RollbackGeneration == _targetRollbackGeneration &&
            items.Length == _targetGroups.Length;
        for (int index = 0; current && index < items.Length; index++)
        {
            current = items[index].Id == _targetGroups[index].Id;
        }
        if (current)
        {
            return;
        }

        var groups = new TargetGroup[items.Length];
        var owners = new Dictionary<ulong, int>(Capacity);
        var candidates = new List<SilkMeshData>(Math.Min(Capacity, 256));
        for (int index = 0; index < items.Length; index++)
        {
            bool bound = bindings.TryResolve(items[index].Id, out PhysicsRenderBinding binding);
            var group = new TargetGroup(items[index].Id, binding);
            groups[index] = group;
            candidates.Clear();
            if (!bound)
            {
                group.Failure = TargetFailure.Unresolved;
                continue;
            }
            if (binding.IncludesDescendants)
            {
                foreach (var pair in scene.MeshesByPath)
                {
                    string path = pair.Key.Path;
                    bool matches = path == binding.PrimPath ||
                        (path.StartsWith(binding.PrimPath, StringComparison.Ordinal) &&
                            path.Length > binding.PrimPath.Length && path[binding.PrimPath.Length] == '/');
                    if (!matches)
                    {
                        continue;
                    }
                    if (pair.Key.InstanceIndex != 0 || pair.Value.InstancerPath.Length != 0)
                    {
                        group.Failure = TargetFailure.Unresolved;
                        break;
                    }
                    if (candidates.Count >= Capacity - owners.Count)
                    {
                        group.Failure = TargetFailure.Capacity;
                        break;
                    }
                    candidates.Add(pair.Value);
                }
            }
            else if (scene.MeshesByPath.TryGetValue((binding.PrimPath, binding.InstanceIndex), out SilkMeshData? mesh))
            {
                if (owners.Count >= Capacity)
                {
                    group.Failure = TargetFailure.Capacity;
                }
                else
                {
                    candidates.Add(mesh);
                }
            }
            if (group.Failure != TargetFailure.None)
            {
                continue;
            }
            if (candidates.Count == 0)
            {
                group.Failure = TargetFailure.Unresolved;
                continue;
            }
            foreach (SilkMeshData mesh in candidates)
            {
                if (owners.TryGetValue(mesh.Id, out int owner))
                {
                    groups[owner].Failure = TargetFailure.Unresolved;
                    group.Failure = TargetFailure.Unresolved;
                }
            }
            if (group.Failure != TargetFailure.None)
            {
                continue;
            }
            group.Meshes = candidates.ToArray();
            foreach (SilkMeshData mesh in candidates)
            {
                owners.Add(mesh.Id, index);
            }
        }
        _targetGroups = groups;
        _targetScene = scene;
        _targetBindings = bindings;
        _targetGeometryRevision = scene.GeometryRevision;
        _targetRollbackGeneration = scene.RollbackGeneration;
        _targetBindingRevision = bindings.Revision;
    }

    private static void ComposeSubtree(
        in PhysicsRenderTransformOverride value, in PhysicsRenderBinding binding,
        ReadOnlySpan<double> authored, Span<double> destination)
    {
        double orientationLength = value.Orientation.X * value.Orientation.X +
            value.Orientation.Y * value.Orientation.Y + value.Orientation.Z * value.Orientation.Z +
            value.Orientation.W * value.Orientation.W;
        if (!double.IsFinite(value.Position.X) || !double.IsFinite(value.Position.Y) ||
            !double.IsFinite(value.Position.Z) || !value.Orientation.IsFinite ||
            !double.IsFinite(orientationLength) || orientationLength <= 0)
        {
            throw new ArgumentException("A subtree override requires a finite pose.", nameof(value));
        }
        Span<double> reference = stackalloc double[16];
        Span<double> current = stackalloc double[16];
        Span<double> inverse = stackalloc double[16];
        Span<double> delta = stackalloc double[16];
        PhysicsRenderTransforms.Compose(binding.ReferencePosition, binding.ReferenceOrientation, [], reference);
        PhysicsRenderTransforms.Compose(value, [], current);
        inverse.Clear();
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                inverse[row * 4 + column] = reference[column * 4 + row];
            }
        }
        for (int column = 0; column < 3; column++)
        {
            inverse[12 + column] = -(reference[12] * inverse[column] +
                reference[13] * inverse[4 + column] + reference[14] * inverse[8 + column]);
        }
        inverse[15] = 1;
        Multiply(inverse, current, delta);
        Multiply(authored, delta, destination);
        foreach (double valueElement in destination)
        {
            if (!double.IsFinite(valueElement))
            {
                throw new InvalidOperationException("A subtree transform exceeded the finite matrix range.");
            }
        }
    }

    private static void Multiply(ReadOnlySpan<double> left, ReadOnlySpan<double> right, Span<double> result)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                double sum = 0;
                for (int index = 0; index < 4; index++)
                {
                    sum += left[row * 4 + index] * right[index * 4 + column];
                }
                result[row * 4 + column] = sum;
            }
        }
    }

    private void ClearSubtreeCache()
    {
        _targetGroups = [];
        _targetScene = null;
        _targetBindings = null;
    }
}
