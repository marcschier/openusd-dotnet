// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Proves that texture coordinates and arbitrary primvars survive from the wire
/// into the retained scene. Without this the shading path has no way to sample a
/// texture, because a UsdUVTexture reader selects a UV set by authored name.
/// </summary>
public sealed class SilkVertexAttributeTests
{
    [Test]
    public async Task RetainsTexCoordAndCustomPrimvars()
    {
        byte[] page = CreateMeshWithAttributes();
        SilkSceneState state = new();
        _ = state.Apply(page, 1, 1);

        SilkMeshData mesh = state.MeshesByPath[("/World/Mesh", 0)];
        await Assert.That(mesh.Attributes.Count).IsEqualTo(3);

        SilkVertexAttributeData? st = mesh.FindTexCoord("st");
        await Assert.That(st).IsNotNull();
        await Assert.That(st!.ComponentCount).IsEqualTo(2);
        await Assert.That(st.ElementCount).IsEqualTo(3);
        await Assert.That(st.GetComponent(2, 1)).IsEqualTo(1f);

        // A second UV set must be selectable by name, which is the whole reason
        // the name travels for bound semantics and not only for custom ones.
        SilkVertexAttributeData? st1 = mesh.FindTexCoord("st1");
        await Assert.That(st1).IsNotNull();
        await Assert.That(st1!.GetComponent(0, 0)).IsEqualTo(0.5f);
        await Assert.That(mesh.FindTexCoord("missing")).IsNull();

        // Constant interpolation stores one element but must still answer for
        // every vertex, so a consumer never has to special-case it.
        SilkVertexAttributeData tint = mesh.Attributes.Single(
            static attribute => attribute.Name == "tint");
        await Assert.That(tint.Interpolation)
            .IsEqualTo(SilkAttributeInterpolation.Constant);
        await Assert.That(tint.ElementCount).IsEqualTo(1);
        await Assert.That(tint.GetComponent(0, 0)).IsEqualTo(0.25f);
        await Assert.That(tint.GetComponent(2, 0)).IsEqualTo(0.25f);
    }

    [Test]
    public async Task RejectsOutOfRangeAttributeAccess()
    {
        byte[] page = CreateMeshWithAttributes();
        SilkSceneState state = new();
        _ = state.Apply(page, 1, 1);
        SilkVertexAttributeData st = state
            .MeshesByPath[("/World/Mesh", 0)]
            .FindTexCoord("st")!;

        await Assert.That(() => st.GetComponent(0, 2))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => st.GetComponent(3, 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => st.GetComponent(-1, 0))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static byte[] CreateMeshWithAttributes()
    {
        const int pointCount = 3;
        byte[] path = Encoding.UTF8.GetBytes("/World/Mesh");
        (string Name, SilkAttributeSemantic Semantic,
            SilkAttributeInterpolation Interpolation, int Components, float[] Data)[]
            attributes =
            [
                ("st", SilkAttributeSemantic.TexCoord,
                    SilkAttributeInterpolation.Vertex, 2, [0f, 0f, 1f, 0f, 0f, 1f]),
                ("st1", SilkAttributeSemantic.TexCoord,
                    SilkAttributeInterpolation.Vertex, 2, [0.5f, 0f, 1f, 0.5f, 0f, 1f]),
                ("tint", SilkAttributeSemantic.Custom,
                    SilkAttributeInterpolation.Constant, 3, [0.25f, 0.5f, 0.75f]),
            ];

        List<byte> variable = [];
        variable.AddRange(path);
        for (int i = 0; i < pointCount * 3; i++)
        {
            variable.AddRange(BitConverter.GetBytes((float)i));
        }
        for (uint i = 0; i < 3; i++)
        {
            variable.AddRange(BitConverter.GetBytes(i));
        }
        variable.AddRange(BitConverter.GetBytes(0u));
        foreach ((string name, SilkAttributeSemantic semantic,
            SilkAttributeInterpolation interpolation, int components, float[] data)
            in attributes)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            variable.AddRange(BitConverter.GetBytes((uint)semantic));
            variable.AddRange(BitConverter.GetBytes((uint)components));
            variable.AddRange(BitConverter.GetBytes((uint)interpolation));
            variable.AddRange(BitConverter.GetBytes((uint)nameBytes.Length));
            variable.AddRange(BitConverter.GetBytes((uint)(data.Length / components)));
            variable.AddRange(nameBytes);
            foreach (float value in data)
            {
                variable.AddRange(BitConverter.GetBytes(value));
            }
        }

        byte[] bytes = new byte[224 + variable.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(0, 4), (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8, 8), SilkWireFormat.ComputeStableHash("/World/Mesh"));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28, 4), (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32, 8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44, 4),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48, 4), (uint)path.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52, 4), pointCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56, 4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60, 4), 1);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(76, 4), 1);
        for (int i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (i * 8), 8), i % 5 == 0 ? 1 : 0);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(220, 4), (uint)attributes.Length);
        variable.CopyTo(bytes, 224);
        return bytes;
    }
}
