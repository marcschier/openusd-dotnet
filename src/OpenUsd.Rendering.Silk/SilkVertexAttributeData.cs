// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// One authored vertex attribute retained from a mesh upsert, already resolved
/// onto the emitted triangle-list vertices.
/// </summary>
/// <remarks>
/// Constant-interpolation attributes keep a single element rather than being
/// expanded, so a per-mesh value costs one element instead of one per vertex.
/// Use <see cref="GetComponent"/>, which resolves the distinction, rather than
/// indexing <see cref="Data"/> by vertex.
/// </remarks>
public sealed class SilkVertexAttributeData
{
    private readonly float[] _data;

    internal SilkVertexAttributeData(
        string name,
        SilkAttributeSemantic semantic,
        SilkAttributeInterpolation interpolation,
        int componentCount,
        float[] data)
    {
        Name = name;
        Semantic = semantic;
        Interpolation = interpolation;
        ComponentCount = componentCount;
        _data = data;
    }

    /// <summary>Gets the authored primvar name.</summary>
    /// <remarks>
    /// Present for every attribute, not only custom ones: a mesh may carry
    /// several texture coordinate sets and a <c>UsdUVTexture</c> reader selects
    /// one of them by name, so the name is load bearing rather than decorative.
    /// </remarks>
    public string Name { get; }

    /// <summary>Gets the renderer-bound semantic.</summary>
    public SilkAttributeSemantic Semantic { get; }

    /// <summary>Gets the interpolation.</summary>
    public SilkAttributeInterpolation Interpolation { get; }

    /// <summary>Gets the component count, one to four.</summary>
    public int ComponentCount { get; }

    /// <summary>Gets the packed component data.</summary>
    public ReadOnlyMemory<float> Data => _data;

    /// <summary>Gets the number of stored elements.</summary>
    public int ElementCount =>
        ComponentCount == 0 ? 0 : _data.Length / ComponentCount;

    /// <summary>
    /// Gets one component for one emitted vertex, resolving constant
    /// interpolation to its single element.
    /// </summary>
    public float GetComponent(int vertexIndex, int component)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(vertexIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(component);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(component, ComponentCount);
        int element = Interpolation == SilkAttributeInterpolation.Constant
            ? 0
            : vertexIndex;
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(element, ElementCount);
        return _data[(element * ComponentCount) + component];
    }
}
