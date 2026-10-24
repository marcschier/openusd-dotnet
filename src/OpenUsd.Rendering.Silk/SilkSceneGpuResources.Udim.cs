// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Runtime.InteropServices;

namespace OpenUsd.Rendering.Silk;

public sealed partial class SilkSceneGpuResources
{
    private static bool IsScalarTextureParameter(SilkMaterialParameter parameter) => parameter is
        SilkMaterialParameter.Roughness or SilkMaterialParameter.Metallic or
        SilkMaterialParameter.Opacity or SilkMaterialParameter.Occlusion or
        SilkMaterialParameter.Clearcoat or SilkMaterialParameter.ClearcoatRoughness or SilkMaterialParameter.Ior;

    private IReadOnlyList<SilkUdimTile> SelectReachableUdimTiles(
        SilkMaterialData material, SilkMaterialTexture texture, IReadOnlyList<SilkUdimTile> tiles)
    {
        var selected = new HashSet<uint>();
        var visited = new HashSet<SilkMeshGpuGeometryResource>();
        IReadOnlyList<float> uv = material.UvTransform;
        Vector3 row0 = new(uv[0], uv[1], uv[4]);
        Vector3 row1 = new(uv[2], uv[3], uv[5]);
        if (texture.Parameter == SilkMaterialParameter.DetailNormal)
        {
            ReadOnlySpan<float> detail0 = material.GetScalar(SilkMaterialParameter.DetailUvRow0);
            ReadOnlySpan<float> detail1 = material.GetScalar(SilkMaterialParameter.DetailUvRow1);
            row0 = detail0.Length == 3 ? new(detail0) : Vector3.UnitX;
            row1 = detail1.Length == 3 ? new(detail1) : Vector3.UnitY;
        }
        foreach (SilkMeshGpuResource resource in MeshValues)
        {
            if (resource.Mesh.MaterialPath != material.Path || !visited.Add(resource.Geometry))
            {
                continue;
            }
            SilkMeshData mesh = resource.Mesh;
            int primitiveSize = mesh.TopologyKind switch
            {
                SilkTopologyKind.TriangleList => 3,
                SilkTopologyKind.LineList => 2,
                SilkTopologyKind.PointList => 1,
                _ => 0
            };
            if (primitiveSize == 0)
            {
                return tiles;
            }
            string primvar = resource.Geometry.Key.UvPrimvar;
            SilkVertexAttributeData? coordinates = string.IsNullOrEmpty(primvar) ? null : mesh.FindTexCoord(primvar);
            for (int first = 0; first < mesh.Indices.Length; first += primitiveSize)
            {
                _materialCaptureRequest?.ThrowIfCancellationRequested();
                Vector2 minimum = new(float.PositiveInfinity);
                Vector2 maximum = new(float.NegativeInfinity);
                for (int corner = 0; corner < primitiveSize; corner++)
                {
                    int vertex = checked((int)mesh.Indices.Span[first + corner]);
                    Vector3 coordinate = coordinates is null ? new(0, 0, 1) :
                        new(coordinates.GetComponent(vertex, 0),
                            coordinates.ComponentCount > 1 ? coordinates.GetComponent(vertex, 1) : 0, 1);
                    Vector2 transformed = new(Vector3.Dot(row0, coordinate), Vector3.Dot(row1, coordinate));
                    // Enclose CPU/GPU affine rounding, including cancellation between large terms.
                    Vector3 absolute = Vector3.Abs(coordinate);
                    Vector2 error = new(
                        1e-5f * (1 + Vector3.Dot(Vector3.Abs(row0), absolute)),
                        1e-5f * (1 + Vector3.Dot(Vector3.Abs(row1), absolute)));
                    if (!float.IsFinite(transformed.X) || !float.IsFinite(transformed.Y) ||
                        !float.IsFinite(error.X) || !float.IsFinite(error.Y))
                    {
                        return tiles;
                    }
                    minimum = Vector2.Min(minimum, transformed - error);
                    maximum = Vector2.Max(maximum, transformed + error);
                }
                foreach (SilkUdimTile tile in tiles)
                {
                    int offset = checked((int)tile.Number - 1001);
                    int u = offset % 10;
                    int v = offset / 10;
                    if (maximum.X >= u && minimum.X < u + 1 && maximum.Y >= v && minimum.Y < v + 1)
                    {
                        selected.Add(tile.Number);
                    }
                }
                if (selected.Count == tiles.Count)
                {
                    return tiles;
                }
            }
        }
        // Direct texture consumers have no geometry; an empty selection retains the conservative path.
        return selected.Count == 0 ? tiles : tiles.Where(tile => selected.Contains(tile.Number)).ToArray();
    }

    private void CompactConstantUdimTiles(SilkDecodedImage[] images)
    {
        int componentBytes = checked((int)SilkTextureFormats.GetBytesPerPixel(images[0].Format));
        foreach (SilkDecodedImage image in images)
        {
            ReadOnlySpan<byte> first = image.Pixels.AsSpan(0, componentBytes);
            for (int offset = componentBytes; offset < image.Pixels.Length; offset += componentBytes)
            {
                if (!image.Pixels.AsSpan(offset, componentBytes).SequenceEqual(first))
                {
                    return;
                }
            }
        }
        for (int index = 0; index < images.Length; index++)
        {
            IDisposable? reservation = _cpuTextureBudget?.Reserve((ulong)componentBytes);
            try
            {
                var compact = new SilkDecodedImage(1, 1, images[index].Pixels.AsSpan(0, componentBytes).ToArray(),
                    images[index].Format);
                compact.OwnReservation(reservation);
                reservation = null;
                images[index].Dispose();
                images[index] = compact;
            }
            finally
            {
                reservation?.Dispose();
            }
        }
    }

    private static int SelectUdimColumns(SilkDecodedImage image, int count, int cells, uint maximumDimension)
    {
        long limit = maximumDimension == 0 ? int.MaxValue : maximumDimension;
        long bestBytes = long.MaxValue;
        long bestExtent = long.MaxValue;
        int best = 0;
        for (int columns = 1; columns <= count; columns++)
        {
            long width = columns * ((long)image.Width + 2);
            long metadataRows = (10 + cells * 2 + width - 1) / width;
            long height = metadataRows + ((count + columns - 1) / columns) * ((long)image.Height + 2);
            long pixels = checked(width * height);
            long extent = Math.Max(width, height);
            if (width <= limit && height <= limit &&
                (pixels < bestBytes || (pixels == bestBytes && extent < bestExtent)))
            {
                bestBytes = pixels;
                bestExtent = extent;
                best = columns;
            }
        }
        return best != 0 ? best : throw new NotSupportedException(
            $"Full-resolution UDIM tiles ({image.Width}x{image.Height}, {count} tiles) cannot fit " +
            $"the device's {maximumDimension}-texel atlas extent.");
    }

    private static void WritePackedUdimMetadata(UdimAtlasSource source, byte[] pixels, int firstRow)
    {
        int pixelBytes = checked((int)SilkTextureFormats.GetBytesPerPixel(source.Format));
        int start = checked(firstRow * source.Width);
        int end = Math.Min(start + pixels.Length / pixelBytes, 10 + source.Columns * source.Rows * 2);
        bool floatingPoint = source.Format is SilkTextureFormat.R32Float or SilkTextureFormat.Rgba32Float;
        for (int pixel = start; pixel < end; pixel++)
        {
            if (pixel == 1)
            {
                continue;
            }
            int value = pixel switch
            {
                0 => 255,
                2 => source.MinU,
                3 => source.MinV,
                4 => source.Columns,
                5 => source.Rows,
                6 => source.PhysicalColumns & 255,
                7 => source.PhysicalColumns >> 8,
                8 => source.Tiles.Count & 255,
                9 => source.Tiles.Count >> 8,
                _ => 0
            };
            if (pixel >= 10)
            {
                int cell = (pixel - 10) / 2;
                int tileNumber = 1001 + source.MinU + cell % source.Columns +
                    10 * (source.MinV + cell / source.Columns);
                for (int index = source.Tiles.Count - 1; index >= 0; index--)
                {
                    if (source.Tiles[index].Number == tileNumber)
                    {
                        value = (pixel & 1) == 0 ? (index + 1) & 255 : (index + 1) >> 8;
                        break;
                    }
                }
            }
            Span<byte> destination = pixels.AsSpan((pixel - start) * pixelBytes, pixelBytes);
            if (floatingPoint)
            {
                MemoryMarshal.Cast<byte, float>(destination)[0] = value / 255f;
            }
            else
            {
                destination[0] = checked((byte)value);
            }
        }
    }
}
