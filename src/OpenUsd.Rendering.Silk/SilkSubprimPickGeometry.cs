// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Derives the exact primitives an edge or point pick pass rasterizes from one
/// retained mesh's ABI v22 subprim-identity tables.
/// </summary>
/// <remarks>
/// <para>
/// The pass draws one primitive per <em>emitted copy</em> of an authored
/// component and resolves every copy through the same authored index, so a pick
/// answers with the identity the scene authored wherever the component was
/// rasterized. Drawing only the first copy would silently lose the rest: a mesh
/// whose topology was expanded for a face-varying primvar emits one authored
/// edge as several distinct segments, and displacement or divergent authored
/// normals can place those copies at visibly different pixels.
/// </para>
/// <para>
/// Two rules make the mapping exact rather than approximate. A triangulation
/// diagonal is never drawn, because the scene authored no edge there: an edge
/// pick that lands on one must miss rather than name an edge no round trip
/// could resolve. And an authored component no emitted primitive covers is not
/// drawn either, because a degenerate stand-in would rasterize nothing on one
/// backend and a stray pixel on another.
/// </para>
/// <para>
/// Everything here is allocated against the size of the published tables, never
/// against the authored counts a record declares, so a malformed or hostile
/// record costs at most what its own bytes already cost.
/// </para>
/// <para>
/// Both the retained identity table and the renderer derive their order from
/// this one type, so the token a pass writes and the identity a readback
/// resolves cannot drift apart.
/// </para>
/// </remarks>
public static class SilkSubprimPickGeometry
{
    /// <summary>
    /// Resolves every emitted copy of every authored edge one mesh draws,
    /// together with the emitted vertex pair each copy is drawn from.
    /// </summary>
    /// <param name="mesh">The retained mesh.</param>
    /// <param name="authoredEdges">
    /// The authored edge index of every drawn line, in draw order. One authored
    /// index appears once per distinct emitted copy of that edge.
    /// </param>
    /// <param name="lineIndices">
    /// Two emitted vertex indices per drawn line, in the same order.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the mesh answers edge picks with authored
    /// identity. A mesh that refuses the target resolves to empty tables.
    /// </returns>
    public static bool TryResolveEdges(
        SilkMeshData mesh,
        out int[] authoredEdges,
        out uint[] lineIndices)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        int cornersPerPrimitive = CornersPerPrimitive(mesh.TopologyKind);
        int indicesPerPrimitive = IndicesPerPrimitive(mesh.TopologyKind);
        ReadOnlySpan<int> cornerEdges = mesh.CornerEdges.Span;
        ReadOnlySpan<uint> indices = mesh.Indices.Span;
        if (!mesh.SubprimIdentity.HasFlag(SilkSubprimIdentity.Edge) ||
            cornersPerPrimitive == 0 ||
            cornerEdges.Length == 0 ||
            indices.Length % indicesPerPrimitive != 0 ||
            cornerEdges.Length !=
                indices.Length / indicesPerPrimitive * cornersPerPrimitive)
        {
            authoredEdges = [];
            lineIndices = [];
            return false;
        }

        // Keyed by the authored edge and its canonical emitted vertex pair, so
        // the same segment reached twice is drawn once while two genuinely
        // different emitted copies of one authored edge are both drawn.
        var drawn = new List<EdgeDraw>(cornerEdges.Length);
        var seen = new HashSet<(int Authored, uint Low, uint High)>();
        int primitiveCount = indices.Length / indicesPerPrimitive;
        for (int primitive = 0; primitive < primitiveCount; primitive++)
        {
            int indexBase = primitive * indicesPerPrimitive;
            int cornerBase = primitive * cornersPerPrimitive;
            for (int corner = 0; corner < cornersPerPrimitive; corner++)
            {
                int authored = cornerEdges[cornerBase + corner];
                if (authored < 0)
                {
                    continue;
                }
                uint first = indices[indexBase + corner];
                uint second = cornersPerPrimitive == 1
                    ? indices[indexBase + 1]
                    : indices[indexBase + ((corner + 1) % cornersPerPrimitive)];
                if (first == second)
                {
                    continue;
                }
                if (!seen.Add((
                        authored,
                        Math.Min(first, second),
                        Math.Max(first, second))))
                {
                    continue;
                }
                drawn.Add(new EdgeDraw(authored, first, second));
            }
        }

        if (drawn.Count == 0)
        {
            authoredEdges = [];
            lineIndices = [];
            return false;
        }

        drawn.Sort(static (left, right) => left.CompareTo(right));
        authoredEdges = new int[drawn.Count];
        lineIndices = new uint[drawn.Count * 2];
        for (int index = 0; index < drawn.Count; index++)
        {
            authoredEdges[index] = drawn[index].Authored;
            lineIndices[index * 2] = drawn[index].First;
            lineIndices[(index * 2) + 1] = drawn[index].Second;
        }
        return true;
    }

    /// <summary>
    /// Resolves every emitted copy of every authored point one mesh draws,
    /// together with the emitted vertex each copy is drawn from.
    /// </summary>
    /// <param name="mesh">The retained mesh.</param>
    /// <param name="authoredPoints">
    /// The authored point index of every drawn point, in draw order. One
    /// authored index appears once per emitted vertex naming it.
    /// </param>
    /// <param name="pointIndices">
    /// One emitted vertex index per drawn point, in the same order.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the mesh answers point picks with authored
    /// identity. A mesh that refuses the target resolves to empty tables.
    /// </returns>
    /// <remarks>
    /// A face-varying mesh emits one vertex per corner, so one authored point
    /// arrives several times. Every copy is drawn and every copy resolves to the
    /// same authored index, so a point pick answers with one authored identity
    /// wherever the point was rasterized -- including when displacement or
    /// divergent authored normals moved the copies apart.
    /// </remarks>
    public static bool TryResolvePoints(
        SilkMeshData mesh,
        out int[] authoredPoints,
        out uint[] pointIndices)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ReadOnlySpan<int> origins = mesh.PointOrigins.Span;
        if (!mesh.SubprimIdentity.HasFlag(SilkSubprimIdentity.Point) ||
            origins.Length == 0 ||
            origins.Length != mesh.Points.Length / 3)
        {
            authoredPoints = [];
            pointIndices = [];
            return false;
        }

        int drawnCount = 0;
        for (int vertex = 0; vertex < origins.Length; vertex++)
        {
            if (origins[vertex] >= 0)
            {
                drawnCount++;
            }
        }
        if (drawnCount == 0)
        {
            authoredPoints = [];
            pointIndices = [];
            return false;
        }

        authoredPoints = new int[drawnCount];
        pointIndices = new uint[drawnCount];
        int drawn = 0;
        for (int vertex = 0; vertex < origins.Length; vertex++)
        {
            if (origins[vertex] < 0)
            {
                continue;
            }
            authoredPoints[drawn] = origins[vertex];
            pointIndices[drawn] = checked((uint)vertex);
            drawn++;
        }
        return true;
    }

    private static int CornersPerPrimitive(SilkTopologyKind topologyKind) =>
        topologyKind switch
        {
            SilkTopologyKind.TriangleList => 3,
            SilkTopologyKind.LineList => 1,
            _ => 0
        };

    private static int IndicesPerPrimitive(SilkTopologyKind topologyKind) =>
        topologyKind switch
        {
            SilkTopologyKind.TriangleList => 3,
            SilkTopologyKind.LineList => 2,
            _ => 1
        };

    private readonly record struct EdgeDraw(int Authored, uint First, uint Second)
        : IComparable<EdgeDraw>
    {
        public int CompareTo(EdgeDraw other)
        {
            int order = Authored.CompareTo(other.Authored);
            if (order != 0)
            {
                return order;
            }
            order = First.CompareTo(other.First);
            return order != 0 ? order : Second.CompareTo(other.Second);
        }
    }
}

/// <summary>
/// The derived edge and point draw tables of one prototype's emitted topology.
/// </summary>
/// <remarks>
/// The tables are a pure function of the emitted topology and the ABI v22
/// identity tables, all of which a lightweight instance shares with its
/// prototype rather than republishing. Deriving them once and sharing the
/// arrays is what keeps a thousand instances of a point cloud from costing a
/// thousand copies of the same table -- and a thousand traversals of the same
/// points to build them.
/// </remarks>
internal sealed class SilkSubprimTables
{
    internal SilkSubprimTables(
        bool hasEdges,
        int[] authoredEdges,
        uint[] lineIndices,
        bool hasPoints,
        int[] authoredPoints,
        uint[] pointIndices)
    {
        HasEdges = hasEdges;
        AuthoredEdges = authoredEdges;
        LineIndices = lineIndices;
        HasPoints = hasPoints;
        AuthoredPoints = authoredPoints;
        PointIndices = pointIndices;
    }

    internal bool HasEdges { get; }

    internal int[] AuthoredEdges { get; }

    internal uint[] LineIndices { get; }

    internal bool HasPoints { get; }

    internal int[] AuthoredPoints { get; }

    internal uint[] PointIndices { get; }
}

/// <summary>
/// Derives one prototype's subprim tables at most once and shares them with
/// every lightweight instance record of that prototype.
/// </summary>
/// <remarks>
/// The cache object, not the derived result, is what an instance shares, so the
/// derivation is still lazy: a scene whose pick target never reaches the edge or
/// point tables never pays for them at all.
/// </remarks>
internal sealed class SilkSubprimTableCache
{
    private volatile SilkSubprimTables? _tables;

    /// <summary>Gets the shared derived tables, deriving them on first use.</summary>
    /// <remarks>
    /// Two threads that race here both derive the same tables from the same
    /// immutable inputs and one publication wins, so the shared result is the
    /// same either way.
    /// </remarks>
    internal SilkSubprimTables Resolve(SilkMeshData mesh)
    {
        SilkSubprimTables? tables = _tables;
        if (tables is not null)
        {
            return tables;
        }
        bool hasEdges = SilkSubprimPickGeometry.TryResolveEdges(
            mesh,
            out int[] authoredEdges,
            out uint[] lineIndices);
        bool hasPoints = SilkSubprimPickGeometry.TryResolvePoints(
            mesh,
            out int[] authoredPoints,
            out uint[] pointIndices);
        tables = new SilkSubprimTables(
            hasEdges,
            authoredEdges,
            lineIndices,
            hasPoints,
            authoredPoints,
            pointIndices);
        _tables = tables;
        return tables;
    }
}
