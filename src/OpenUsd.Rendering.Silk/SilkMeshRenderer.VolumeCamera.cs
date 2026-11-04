// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

public sealed partial class SilkMeshRenderer
{
    private bool IsVolumeCameraMesh(SilkMeshData mesh) =>
        ResolveMaterial(mesh) is { IsSupported: true, SurfaceKind: SilkSurfaceKind.VolumeDensity };

    private IReadOnlyList<(SilkMeshData Mesh, SilkMaterialData Material)> GetVolumeCameraFields()
    {
        List<(SilkMeshData Mesh, SilkMaterialData Material)>? fields = null;
        foreach (SilkMeshData mesh in Scene.MeshValues)
        {
            if (ResolveMaterial(mesh) is
                { IsSupported: true, SurfaceKind: SilkSurfaceKind.VolumeDensity } material)
            {
                SilkVolumeCameraValidation.ValidateMaterialChannels(material);
                if (SilkSurfaceUniformWriter.GetOpacity(material) > 0)
                {
                    (fields ??= []).Add((mesh, material));
                }
            }
        }
        if (fields is null)
        {
            return Array.Empty<(SilkMeshData Mesh, SilkMaterialData Material)>();
        }
        fields.Sort(static (left, right) =>
        {
            int path = StringComparer.Ordinal.Compare(left.Mesh.Path, right.Mesh.Path);
            return path != 0
                ? path
                : left.Mesh.InstanceIndex.CompareTo(right.Mesh.InstanceIndex);
        });
        return fields;
    }
}
