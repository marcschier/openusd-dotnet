// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.ConformanceTests;

internal static class CoarseMeshNormalFixture
{
    internal const string SharedNormals = """
        normal3f[] normals = [
            (0,0.70710678,0.70710678), (0,0.70710678,0.70710678),
            (0,0,1), (0,1,0)
        ] (interpolation = "vertex")
        """;
    internal const string SeparateNormals = """
        normal3f[] normals = [
            (0,0,1), (0,0,1), (0,0,1),
            (0,1,0), (0,1,0), (0,1,0)
        ] (interpolation = "vertex")
        """;
    internal const string FlatCornerNormals = """
        normal3f[] normals = [
            (0,0,1), (0,0,1), (0,0,1),
            (0,1,0), (0,1,0), (0,1,0)
        ] (interpolation = "faceVarying")
        """;
    internal const string JoinedSeparateNormals = """
        normal3f[] normals = [
            (0,0.70710678,0.70710678), (0,0.70710678,0.70710678), (0,0,1),
            (0,0.70710678,0.70710678), (0,1,0), (0,0.70710678,0.70710678)
        ] (interpolation = "vertex")
        """;
    internal const string NgonNormals = """
        normal3f[] normals = [
            (0,0.70710678,0.70710678), (0,0.70710678,0.70710678),
            (0,0,1), (0,0,1), (0,1,0)
        ] (interpolation = "vertex")
        """;
    internal const string NgonTriangulatedNormals = """
        normal3f[] normals = [
            (0,0.4472136,0.8944272), (0,0.70710678,0.70710678),
            (0,0,1), (0,0,1), (0,1,0)
        ] (interpolation = "vertex")
        """;
    internal const string HalfHeightNgonTriangulatedNormals = """
        normal3f[] normals = [
            (0,0.242535625,0.9701425), (0,0.4472136,0.8944272),
            (0,0,1), (0,0,1), (0,1,0)
        ] (interpolation = "vertex")
        """;
    // Sum cross(next - current, previous - current) at each incident face corner.
    internal const string SkewedNgonCornerNormals = """
        normal3f[] normals = [
            (0,0.70710678,0.70710678), (0,0,1),
            (-0.40824829,-0.81649658,0.40824829),
            (-0.70710678,0,0.70710678), (0,1,0)
        ] (interpolation = "vertex")
        """;
    // Incorrect control: assign the quad's full area vector to every corner.
    internal const string SkewedNgonFaceAreaNormals = """
        normal3f[] normals = [
            (-0.31622777,0,0.9486833), (-0.31622777,0,0.9486833),
            (-0.26726124,-0.53452248,0.80178373),
            (-0.26726124,-0.53452248,0.80178373), (0,1,0)
        ] (interpolation = "vertex")
        """;
    // Incorrect control: one fan-summed normal for the entire original quad.
    internal const string SkewedNgonFlatNormals = """
        normal3f[] normals = [
            (-0.26726124,-0.53452248,0.80178373), (-0.26726124,-0.53452248,0.80178373),
            (-0.26726124,-0.53452248,0.80178373), (-0.26726124,-0.53452248,0.80178373),
            (0,1,0), (0,1,0), (0,1,0)
        ] (interpolation = "faceVarying")
        """;
    // HdMeshUtil at ee47c679 fans the quad into (0,1,2) and (0,2,3).
    internal const string SkewedNgonTriangulatedFlatNormals = """
        normal3f[] normals = [
            (0,-0.70710678,0.70710678), (0,-0.70710678,0.70710678), (0,-0.70710678,0.70710678),
            (-0.70710678,0,0.70710678), (-0.70710678,0,0.70710678), (-0.70710678,0,0.70710678),
            (0,1,0), (0,1,0), (0,1,0)
        ] (interpolation = "faceVarying")
        """;

    internal static string Mesh(
        string scheme, bool sharedPoints, bool faceVarying, string normals, bool holeSecondFace = false)
    {
        string points = sharedPoints
            ? "[(-0.6,-0.6,0), (0.6,-0.6,0), (-0.6,0.6,0), (-0.6,-0.6,1.2)]"
            : "[(-0.6,-0.6,0), (0.6,-0.6,0), (-0.6,0.6,0), " +
              "(-0.6,-0.6,0), (-0.6,-0.6,1.2), (0.6,-0.6,0)]";
        string indices = sharedPoints ? "[0,1,2,0,3,1]" : "[0,1,2,3,4,5]";
        string uv = faceVarying ? """
            texCoord2f[] primvars:st = [
                (0,0), (1,0), (0,1), (0.25,0.25), (0.5,1), (0.75,0.25)
            ] (interpolation = "faceVarying")
            """ : string.Empty;
        string holes = holeSecondFace ? "int[] holeIndices = [1]" : string.Empty;
        return $$"""
            def Mesh "Mesh" {
                uniform token subdivisionScheme = "{{scheme}}"
                point3f[] points = {{points}}
                int[] faceVertexCounts = [3,3]
                int[] faceVertexIndices = {{indices}}
                {{holes}}
                {{uv}}
                {{normals}}
            }
            def DistantLight "Key" {
                float inputs:intensity = 1
            }
            """;
    }

    internal static string NgonMesh(
        string scheme, bool faceVarying, string normals, bool skewed = false, bool triangulated = false,
        bool includeFaceVaryingColor = false)
    {
        string points = skewed
            ? "[(-0.8,-0.4,0), (0.8,-0.4,0), (0,0.4,0.8), (-0.8,0.4,0), (-0.8,-0.4,0.8)]"
            : "[(-0.6,-0.6,0), (0.6,-0.6,0), (0.6,0.6,0), (-0.6,0.6,0), (-0.6,-0.6,1.2)]";
        string counts = triangulated ? "[3,3,3]" : "[4,3]";
        string indices = triangulated ? "[0,1,2,0,2,3,0,4,1]" : "[0,1,2,3,0,4,1]";
        string uv = !faceVarying ? string.Empty : triangulated
            ? """
              texCoord2f[] primvars:st = [
                  (0,0), (1,0), (1,1), (0,0), (1,1), (0,1), (0.25,0.25), (0.5,1), (0.75,0.25)
              ] (interpolation = "faceVarying")
              """
            : """
              texCoord2f[] primvars:st = [
                  (0,0), (1,0), (1,1), (0,1), (0.25,0.25), (0.5,1), (0.75,0.25)
              ] (interpolation = "faceVarying")
              """;
        // The fallback material can filter unused st, so exercise face-varying
        // interpolation with a consumed displayColor primvar as well.
        string color = !faceVarying || !includeFaceVaryingColor ? string.Empty : triangulated
            ? """
              color3f[] primvars:displayColor = [
                  (0.8,0.8,0.8), (1,1,1), (0.9,0.9,0.9),
                  (0.8,0.8,0.8), (0.9,0.9,0.9), (1,1,1),
                  (0.2,0.2,0.2), (0.3,0.3,0.3), (0.4,0.4,0.4)
              ] (interpolation = "faceVarying")
              """
            : """
              color3f[] primvars:displayColor = [
                  (0.8,0.8,0.8), (1,1,1), (0.9,0.9,0.9), (1,1,1),
                  (0.2,0.2,0.2), (0.3,0.3,0.3), (0.4,0.4,0.4)
              ] (interpolation = "faceVarying")
              """;
        return $$"""
            def Mesh "Mesh" {
                uniform token subdivisionScheme = "{{scheme}}"
                point3f[] points = {{points}}
                int[] faceVertexCounts = {{counts}}
                int[] faceVertexIndices = {{indices}}
                {{uv}}
                {{color}}
                {{normals}}
            }
            def DistantLight "Key" {
                float inputs:intensity = 1
            }
            """;
    }
}
