// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Analytic gates for hdSilk's UsdPreviewSurface <c>displacement</c> input.
/// </summary>
/// <remarks>
/// <para>
/// Displacement in hdSilk is a geometry operation, not a shading one: an authored
/// constant, or a per-vertex sample of an authored height field, moves the point
/// along the shading normal in the one retained vertex buffer the colour pass, the
/// raster shadow depth pass, the pick pass and the selection outline all draw. The
/// cases here pin that arithmetic exactly, against a closed-form oracle rather than
/// against a second implementation, and pin the bounded subset: every input this
/// renderer cannot represent exactly is named in a diagnostic and leaves the surface
/// undisplaced.
/// </para>
/// <para>
/// Every equality case carries its own non-vacuity proof, because "the displaced
/// vertices equal the expected vertices" is trivially true of a renderer that
/// displaced nothing and an expectation that expected nothing.
/// </para>
/// </remarks>
internal sealed class SilkDisplacementTests
{
    private const string MeshPath = "/World/Quad";
    private const string MaterialPath = "/World/Materials/Displaced";
    private const string SecondMaterialPath = "/World/Materials/Shallow";
    private const string HeightAsset = "height.png";

    /// <summary>
    /// A constant displacement moves every point by exactly the authored amount
    /// along the normal that point is shaded with, and moves nothing else.
    /// </summary>
    [Test]
    public async Task AConstantDisplacementMovesEveryPointAlongItsShadingNormal()
    {
        float[] points = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        float[] normals = [0, 0, 1, 0, 1, 0, 1, 0, 0];
        SilkMeshData mesh = BuildMesh(points, normals, TexCoords);

        SilkMeshGeometry displaced = SilkMeshGeometryBuilder.Build(
            mesh,
            uvPrimvar: string.Empty,
            requireTangents: false,
            displacementAmounts: [0.5f, 0.5f, 0.5f]);
        SilkMeshGeometry flat = SilkMeshGeometryBuilder.Build(mesh);

        await Assert.That(displaced.Displaced).IsTrue();
        await Assert.That(displaced.MaximumDisplacement).IsEqualTo(0.5f);
        for (int point = 0; point < 3; point++)
        {
            int stride = point * 6;
            for (int axis = 0; axis < 3; axis++)
            {
                await Assert.That(displaced.Vertices[stride + axis])
                    .IsEqualTo(points[(point * 3) + axis] + (0.5f * normals[(point * 3) + axis]))
                    .Because("the point must move by the authored amount along its own normal");
                // The shading frame is preserved exactly: hdSilk claims deform then
                // displace, and a re-derived normal would make that claim untestable
                // against the CPU deformation oracle.
                await Assert.That(displaced.Vertices[stride + 3 + axis])
                    .IsEqualTo(flat.Vertices[stride + 3 + axis])
                    .Because("displacement must not re-derive the shading normal");
            }
        }

        // Non-vacuity: an implementation that displaced nothing would satisfy the
        // normal comparisons above, so the positions have to differ.
        await Assert.That(displaced.Vertices.AsSpan().SequenceEqual(flat.Vertices))
            .IsFalse()
            .Because("a displaced build must not equal the undisplaced build");
    }

    /// <summary>
    /// A per-vertex height sample equals the closed-form bilinear sampler rule the
    /// fragment stage applies to the same decoded image.
    /// </summary>
    [Test]
    [Arguments(0.25f, 0.25f, 128f / 255f)]
    [Arguments(0.75f, 0.25f, 1f)]
    [Arguments(0.25f, 0.75f, 0f)]
    [Arguments(0.75f, 0.75f, 64f / 255f)]
    public async Task ATexturedFieldSamplesTheAuthoredTexelAtItsCentre(
        float u,
        float v,
        float expected)
    {
        SilkDisplacementField field = CreateHeightField();
        await Assert.That(field.Sample(u, v)).IsEqualTo(expected).Within(1e-6f);
        // Non-vacuity: an image whose texels were all one value would satisfy every
        // row of this case, so the four expectations must not be the same number.
        await Assert.That(field.Sample(0.25f, 0.25f)).IsNotEqualTo(field.Sample(0.75f, 0.25f));
    }

    /// <summary>
    /// A sample between texel centres is the bilinear blend of its four neighbours,
    /// which is exactly what a linear-filtered sampler computes.
    /// </summary>
    [Test]
    public async Task ATexturedFieldFiltersBilinearlyBetweenTexelCentres()
    {
        SilkDisplacementField field = CreateHeightField();
        float expected = ((128f / 255f) + 1f + 0f + (64f / 255f)) / 4f;
        await Assert.That(field.Sample(0.5f, 0.5f)).IsEqualTo(expected).Within(1e-6f);
        // A nearest-neighbour reader would return one of the four corners here, so
        // the blend has to differ from each of them.
        await Assert.That(field.Sample(0.5f, 0.5f)).IsNotEqualTo(field.Sample(0.25f, 0.25f));
        await Assert.That(field.Sample(0.5f, 0.5f)).IsNotEqualTo(field.Sample(0.75f, 0.75f));
    }

    /// <summary>
    /// Every authored wrap mode addresses a coordinate outside the unit range the
    /// way USD defines it, and <c>black</c> and <c>useMetadata</c> are a true
    /// transparent-black border rather than a clamp.
    /// </summary>
    [Test]
    public async Task WrapModesAddressOutsideTheUnitRange()
    {
        // A one-dimensional ramp so the addressed texel is readable from the value.
        float[] texels = [0.25f, 1f];
        SilkDisplacementField repeat = SilkDisplacementField.Textured(
            texels, 2, 1, SilkTextureWrap.Repeat, SilkTextureWrap.Clamp, Identity, 1, 0, "st", 1);
        SilkDisplacementField clamp = SilkDisplacementField.Textured(
            texels, 2, 1, SilkTextureWrap.Clamp, SilkTextureWrap.Clamp, Identity, 1, 0, "st", 2);
        SilkDisplacementField black = SilkDisplacementField.Textured(
            texels, 2, 1, SilkTextureWrap.Black, SilkTextureWrap.Clamp, Identity, 1, 0, "st", 3);
        SilkDisplacementField metadata = SilkDisplacementField.Textured(
            texels, 2, 1, SilkTextureWrap.UseMetadata, SilkTextureWrap.Clamp, Identity, 1, 0, "st", 4);
        SilkDisplacementField mirror = SilkDisplacementField.Textured(
            texels, 2, 1, SilkTextureWrap.Mirror, SilkTextureWrap.Clamp, Identity, 1, 0, "st", 5);

        // u = 1.25 addresses texel index 2, which is index 0 under repeat, index 1
        // under clamp, index 1 under mirror (the second period runs backwards),
        // and the border under the two border modes.
        await Assert.That(repeat.Sample(1.25f, 0.5f)).IsEqualTo(0.25f).Within(1e-6f);
        await Assert.That(clamp.Sample(1.25f, 0.5f)).IsEqualTo(1f).Within(1e-6f);
        await Assert.That(mirror.Sample(1.25f, 0.5f)).IsEqualTo(1f).Within(1e-6f);
        await Assert.That(black.Sample(1.25f, 0.5f)).IsEqualTo(0f).Within(1e-6f);
        await Assert.That(metadata.Sample(1.25f, 0.5f)).IsEqualTo(0f).Within(1e-6f);

        // Non-vacuity: inside the unit range every mode has to agree, so the
        // differences above are the addressing rule and not a broken sampler.
        await Assert.That(repeat.Sample(0.25f, 0.5f)).IsEqualTo(clamp.Sample(0.25f, 0.5f));
        await Assert.That(mirror.Sample(0.25f, 0.5f)).IsEqualTo(clamp.Sample(0.25f, 0.5f));
        await Assert.That(black.Sample(0.25f, 0.5f)).IsEqualTo(clamp.Sample(0.25f, 0.5f));
        await Assert.That(metadata.Sample(0.25f, 0.5f)).IsEqualTo(clamp.Sample(0.25f, 0.5f));
        await Assert.That(repeat.Sample(1.25f, 0.5f)).IsNotEqualTo(clamp.Sample(1.25f, 0.5f));
    }

    /// <summary>
    /// A border mode contributes its zero to a bilinear blend that straddles the
    /// image edge, which is what a border-addressed sampler computes and what a
    /// clamp cannot reproduce.
    /// </summary>
    [Test]
    public async Task ABorderModeContributesZeroToABilinearBlendAtTheEdge()
    {
        float[] texels = [0.25f, 1f];
        SilkDisplacementField black = SilkDisplacementField.Textured(
            texels, 2, 1, SilkTextureWrap.Black, SilkTextureWrap.Clamp, Identity, 1, 0, "st", 1);
        SilkDisplacementField clamp = SilkDisplacementField.Textured(
            texels, 2, 1, SilkTextureWrap.Clamp, SilkTextureWrap.Clamp, Identity, 1, 0, "st", 2);

        // u = 0 places the sample half a texel outside the first texel centre, so
        // a border sampler blends the border with texel 0 in equal parts.
        await Assert.That(black.Sample(0f, 0.5f)).IsEqualTo(0.125f).Within(1e-6f);
        // A clamped sampler blends texel 0 with itself and returns it whole,
        // which is exactly the value the border rule must not produce.
        await Assert.That(clamp.Sample(0f, 0.5f)).IsEqualTo(0.25f).Within(1e-6f);

        // And at the far edge: u = 1 blends texel 1 with the border.
        await Assert.That(black.Sample(1f, 0.5f)).IsEqualTo(0.5f).Within(1e-6f);
        await Assert.That(clamp.Sample(1f, 0.5f)).IsEqualTo(1f).Within(1e-6f);
        // Non-vacuity: a border implemented as clamp would make every comparison
        // above an identity.
        await Assert.That(black.Sample(0f, 0.5f)).IsNotEqualTo(clamp.Sample(0f, 0.5f));
    }

    /// <summary>
    /// The material's single folded texture-coordinate affine transforms the
    /// authored coordinate before the height field is addressed, exactly as the
    /// checked fragment permutation transforms it.
    /// </summary>
    [Test]
    public async Task TheMaterialAffineTransformsTheSampledCoordinate()
    {
        float[] texels = [0f, 1f];
        // Halve u and shift it by 0.5, so an authored u of 1.5 addresses 1.25.
        float[] affine = [0.5f, 0, 0, 1, 0.5f, 0];
        SilkDisplacementField transformed = SilkDisplacementField.Textured(
            texels, 2, 1, SilkTextureWrap.Clamp, SilkTextureWrap.Clamp, affine, 1, 0, "st", 1);
        SilkDisplacementField untransformed = SilkDisplacementField.Textured(
            texels, 2, 1, SilkTextureWrap.Clamp, SilkTextureWrap.Clamp, Identity, 1, 0, "st", 2);

        await Assert.That(transformed.Sample(1.5f, 0.5f))
            .IsEqualTo(untransformed.Sample(1.25f, 0.5f))
            .Within(1e-6f);
        // Non-vacuity: an ignored affine would make the two fields identical.
        await Assert.That(transformed.Sample(0f, 0.5f))
            .IsNotEqualTo(untransformed.Sample(0f, 0.5f));
    }

    /// <summary>
    /// A displacement whose amounts are all zero resolves to no amounts at all, so
    /// it shares the retained geometry of a material that authors none.
    /// </summary>
    [Test]
    public async Task AFieldThatMovesNothingResolvesNoAmounts()
    {
        SilkDisplacementField zero = SilkDisplacementField.Constant(0, 7);
        await Assert.That(zero.TryResolveAmounts(3, uv: null, out float[] none)).IsFalse();
        await Assert.That(none.Length).IsEqualTo(0);

        SilkDisplacementField moving = SilkDisplacementField.Constant(0.25f, 8);
        await Assert.That(moving.TryResolveAmounts(3, uv: null, out float[] amounts)).IsTrue();
        await Assert.That(amounts.Length).IsEqualTo(3);
        foreach (float amount in amounts)
        {
            await Assert.That(amount).IsEqualTo(0.25f);
        }
    }

    /// <summary>
    /// A textured field resolves one amount per emitted point from that point's own
    /// authored coordinate.
    /// </summary>
    [Test]
    public async Task ATexturedFieldResolvesOneAmountPerEmittedPoint()
    {
        SilkMeshData mesh = BuildMesh(FlatPoints, FlatNormals, TexCoords);
        SilkDisplacementField field = CreateHeightField();
        await Assert
            .That(field.TryResolveAmounts(3, mesh.FindTexCoord("st"), out float[] amounts))
            .IsTrue();
        await Assert.That(amounts.Length).IsEqualTo(3);
        await Assert.That(amounts[0]).IsEqualTo(128f / 255f).Within(1e-6f);
        await Assert.That(amounts[1]).IsEqualTo(1f).Within(1e-6f);
        await Assert.That(amounts[2]).IsEqualTo(0f).Within(1e-6f);
        // Non-vacuity: a field read through one shared coordinate would give every
        // point the same amount.
        await Assert.That(amounts[0]).IsNotEqualTo(amounts[1]);
    }

    /// <summary>
    /// An authored constant reaches the retained vertex buffer the colour and shadow
    /// passes draw, rather than stopping at the geometry builder.
    /// </summary>
    [Test]
    public async Task AnAuthoredConstantReachesTheRetainedVertexBuffer()
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, MissingDecoder);
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(scalarAmount: 0.5f),
            CreateMeshUpsert(FlatPoints, FlatNormals));

        float[] vertices = ReadVertices(resources, pointCount: 3, strideFloats: 6);
        for (int point = 0; point < 3; point++)
        {
            await Assert.That(vertices[(point * 6) + 2])
                .IsEqualTo(0.5f)
                .Within(1e-6f)
                .Because("the retained vertex buffer must carry the displaced position");
            await Assert.That(vertices[(point * 6) + 5]).IsEqualTo(1f).Within(1e-6f);
        }

        // Non-vacuity: the same scene without the displacement input must leave the
        // very same points at z = 0, so the z above is the displacement and not the
        // authored geometry.
        using var flatDevice = new DisplacementDevice();
        using var flatResources = new SilkSceneGpuResources(flatDevice, MissingDecoder);
        var flatScene = new SilkSceneState();
        ApplyPage(
            flatScene,
            flatResources,
            1,
            CreateMaterialUpsert(scalarAmount: null),
            CreateMeshUpsert(FlatPoints, FlatNormals));
        float[] flat = ReadVertices(flatResources, pointCount: 3, strideFloats: 6);
        await Assert.That(flat[2]).IsEqualTo(0f);
    }

    /// <summary>
    /// A displacement image reaches the retained vertex buffer per vertex, and the
    /// decoded height field is retained inside its byte budget.
    /// </summary>
    [Test]
    public async Task AnAuthoredHeightFieldReachesTheRetainedVertexBufferPerVertex()
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, HeightDecoder);
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(textureAsset: HeightAsset),
            CreateMeshUpsert(FlatPoints, FlatNormals));

        // The material publishes a texture-coordinate primvar, so the emitted vertex
        // carries one and the stride is eight floats.
        float[] vertices = ReadVertices(resources, pointCount: 3, strideFloats: 8);
        await Assert.That(vertices[2]).IsEqualTo(128f / 255f).Within(1e-6f);
        await Assert.That(vertices[10]).IsEqualTo(1f).Within(1e-6f);
        await Assert.That(vertices[18]).IsEqualTo(0f).Within(1e-6f);
        // Non-vacuity: a field sampled through one shared coordinate would move
        // every vertex by the same amount.
        await Assert.That(vertices[2]).IsNotEqualTo(vertices[10]);

        await Assert.That(resources.DisplacementImageCount).IsEqualTo(1);
        await Assert.That(resources.DisplacementImageBytes).IsEqualTo(4UL * sizeof(float));
        await Assert
            .That(Diagnostics(resources, SilkRenderDiagnosticCodes.DisplacementApplied).Count)
            .IsEqualTo(1);
    }

    /// <summary>
    /// Republishing an identical material reuses the retained displaced geometry and
    /// the decoded height field, and changing the authored amount rebuilds both.
    /// </summary>
    [Test]
    public async Task ChangingTheDisplacementRebuildsTheGeometryAndRepeatingItReusesIt()
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, HeightDecoder);
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(scalarAmount: 0.5f),
            CreateMeshUpsert(FlatPoints, FlatNormals));
        ulong afterFirst = resources.Statistics.GeometryBuilds;
        await Assert.That(afterFirst).IsEqualTo(1UL);

        // Republishing the identical material is what a transform edit does to a
        // displaced prim, and it must not rebuild the displaced vertices.
        ApplyPage(scene, resources, 2, CreateMaterialUpsert(scalarAmount: 0.5f));
        await Assert.That(resources.Statistics.GeometryBuilds).IsEqualTo(afterFirst);

        // A changed amount is a different height field, so it must not reuse the
        // vertices the previous amount displaced.
        ApplyPage(scene, resources, 3, CreateMaterialUpsert(scalarAmount: 0.75f));
        await Assert.That(resources.Statistics.GeometryBuilds).IsEqualTo(afterFirst + 1);
        float[] vertices = ReadVertices(resources, pointCount: 3, strideFloats: 6);
        await Assert.That(vertices[2]).IsEqualTo(0.75f).Within(1e-6f);

        // And removing the displacement returns the surface to where it started.
        ApplyPage(scene, resources, 4, CreateMaterialUpsert(scalarAmount: null));
        float[] flat = ReadVertices(resources, pointCount: 3, strideFloats: 6);
        await Assert.That(flat[2]).IsEqualTo(0f);
    }

    /// <summary>
    /// A displacement authored at exactly zero is the schema default: it moves
    /// nothing, reports nothing, and shares the undisplaced retained geometry.
    /// </summary>
    [Test]
    public async Task AZeroConstantDisplacementIsSilentAndSharesTheUndisplacedGeometry()
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, MissingDecoder);
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(scalarAmount: null),
            CreateMeshUpsert(FlatPoints, FlatNormals));
        ulong afterFlat = resources.Statistics.GeometryBuilds;

        ApplyPage(scene, resources, 2, CreateMaterialUpsert(scalarAmount: 0f));
        await Assert.That(resources.Statistics.GeometryBuilds)
            .IsEqualTo(afterFlat)
            .Because("an authored zero displaces nothing and must reuse the flat geometry");
        await Assert.That(resources.Diagnostics.Entries.Count).IsEqualTo(0);

        // Non-vacuity: a non-zero amount over the same scene does rebuild, so the
        // reuse above is the zero and not a cache that never invalidates.
        ApplyPage(scene, resources, 3, CreateMaterialUpsert(scalarAmount: 0.5f));
        await Assert.That(resources.Statistics.GeometryBuilds).IsEqualTo(afterFlat + 1);
    }

    /// <summary>
    /// Every displacement hdSilk cannot represent exactly is named, and leaves the
    /// surface undisplaced rather than flat-and-silent.
    /// </summary>
    [Test]
    [Arguments("udim", SilkRenderDiagnosticCodes.DisplacementUnsupported, "UDIM tile set")]
    [Arguments("missing", SilkRenderDiagnosticCodes.DisplacementUnsupported, "could not be found")]
    [Arguments("uvset", SilkRenderDiagnosticCodes.DisplacementUnsupported, "does not carry")]
    [Arguments("composite", SilkRenderDiagnosticCodes.DisplacementUnsupported, "composite operand")]
    [Arguments("nonfinite", SilkRenderDiagnosticCodes.DisplacementUnsupported, "not finite")]
    public async Task AnUnrepresentableDisplacementIsNamedAndLeavesTheSurfaceUndisplaced(
        string variant,
        string expectedCode,
        string expectedDetail)
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, HeightDecoder);
        var scene = new SilkSceneState();

        byte[] material = variant switch
        {
            "udim" => CreateMaterialUpsert(textureAsset: "height.<UDIM>.png"),
            "missing" => CreateMaterialUpsert(textureAsset: "absent.png"),
            "uvset" => CreateMaterialUpsert(textureAsset: HeightAsset, uvPrimvar: "st_other"),
            "composite" => CreateMaterialUpsert(
                textureAsset: HeightAsset,
                compositeOperator: SilkCompositeOperator.Multiply),
            _ => CreateMaterialUpsert(scalarAmount: float.PositiveInfinity)
        };
        ApplyPage(scene, resources, 1, material, CreateMeshUpsert(FlatPoints, FlatNormals));

        IReadOnlyList<RenderDiagnostic> reported = Diagnostics(resources, expectedCode);
        await Assert.That(reported.Count)
            .IsEqualTo(1)
            .Because($"'{variant}' must report exactly one displacement diagnostic");
        await Assert.That(reported[0].Message).Contains(expectedDetail);
        await Assert.That(reported[0].Severity).IsEqualTo(RenderDiagnosticSeverity.Warning);

        // Silent success is the failure this case exists to prevent, so the surface
        // has to be exactly where it was authored.
        int stride = variant is "udim" or "missing" or "composite" ? 8 : 6;
        float[] vertices = ReadVertices(resources, pointCount: 3, strideFloats: stride);
        await Assert.That(vertices[2]).IsEqualTo(0f);
        await Assert
            .That(Diagnostics(resources, SilkRenderDiagnosticCodes.DisplacementApplied).Count)
            .IsEqualTo(0);
    }

    /// <summary>
    /// A topology with no surface to displace along is named rather than translated
    /// along the canonical normal fallback.
    /// </summary>
    [Test]
    public async Task ALineListDisplacementIsNamedAndLeavesThePointsWhereTheyWere()
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, MissingDecoder);
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(scalarAmount: 0.5f),
            CreateMeshUpsert(
                FlatPoints,
                FlatNormals,
                topology: SilkTopologyKind.LineList,
                indices: [0, 1]));

        IReadOnlyList<RenderDiagnostic> reported = Diagnostics(
            resources,
            SilkRenderDiagnosticCodes.DisplacementUnsupported);
        await Assert.That(reported.Count).IsEqualTo(1);
        await Assert.That(reported[0].Message).Contains("not a triangle list");
        float[] vertices = ReadVertices(resources, pointCount: 3, strideFloats: 6);
        await Assert.That(vertices[2]).IsEqualTo(0f);
    }

    /// <summary>
    /// Both displacement budgets refuse before an amount array or a decoded height
    /// field is allocated, and name which bound was exceeded.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ADisplacementOutsideItsBudgetIsRefusedBeforeAllocation(bool vertexBudget)
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, HeightDecoder);
        resources.SetDisplacementBudgetsForTesting(
            maximumPoints: vertexBudget ? 2 : 1024,
            maximumTexels: vertexBudget ? 1024 : 3);
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            vertexBudget
                ? CreateMaterialUpsert(scalarAmount: 0.5f)
                : CreateMaterialUpsert(textureAsset: HeightAsset),
            CreateMeshUpsert(FlatPoints, FlatNormals));

        IReadOnlyList<RenderDiagnostic> reported = Diagnostics(
            resources,
            SilkRenderDiagnosticCodes.DisplacementBudgetExceeded);
        await Assert.That(reported.Count).IsEqualTo(1);
        await Assert.That(reported[0].Message)
            .Contains(vertexBudget ? "more than 2 points" : "more than 3 texels");
        await Assert.That(resources.DisplacementImageCount)
            .IsEqualTo(0)
            .Because("a refused displacement must retain no decoded height field");

        float[] vertices = ReadVertices(
            resources,
            pointCount: 3,
            strideFloats: vertexBudget ? 6 : 8);
        await Assert.That(vertices[2]).IsEqualTo(0f);

        // Non-vacuity: the identical scene inside the budget does displace, so the
        // refusal above is the bound and not a path that never displaces.
        using var allowedDevice = new DisplacementDevice();
        using var allowed = new SilkSceneGpuResources(allowedDevice, HeightDecoder);
        var allowedScene = new SilkSceneState();
        ApplyPage(
            allowedScene,
            allowed,
            1,
            vertexBudget
                ? CreateMaterialUpsert(scalarAmount: 0.5f)
                : CreateMaterialUpsert(textureAsset: HeightAsset),
            CreateMeshUpsert(FlatPoints, FlatNormals));
        float[] displaced = ReadVertices(
            allowed,
            pointCount: 3,
            strideFloats: vertexBudget ? 6 : 8);
        await Assert.That(displaced[2]).IsNotEqualTo(0f);
    }

    /// <summary>
    /// Disposing the retained resources releases the decoded height fields as well
    /// as the buffers they displaced.
    /// </summary>
    [Test]
    public async Task DisposalReleasesTheDecodedHeightFields()
    {
        using var device = new DisplacementDevice();
        var resources = new SilkSceneGpuResources(device, HeightDecoder);
        var scene = new SilkSceneState();
        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(textureAsset: HeightAsset),
            CreateMeshUpsert(FlatPoints, FlatNormals));
        await Assert.That(resources.DisplacementImageCount).IsEqualTo(1);
        await Assert.That(device.LiveBufferCount).IsGreaterThan(0);

        resources.Dispose();
        await Assert.That(resources.DisplacementImageCount).IsEqualTo(0);
        await Assert.That(resources.DisplacementImageBytes).IsEqualTo(0UL);
        await Assert.That(device.LiveBufferCount).IsEqualTo(0);
    }

    /// <summary>
    /// The applied diagnostic names the emitted vertex density the height field was
    /// sampled at, because the wire carries no refinement level and complexity Low
    /// emits the control cage.
    /// </summary>
    [Test]
    public async Task TheAppliedDiagnosticNamesTheEmittedVertexDensity()
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, MissingDecoder);
        var scene = new SilkSceneState();
        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(scalarAmount: 0.5f),
            CreateMeshUpsert(FlatPoints, FlatNormals));

        IReadOnlyList<RenderDiagnostic> reported = Diagnostics(
            resources,
            SilkRenderDiagnosticCodes.DisplacementApplied);
        await Assert.That(reported.Count).IsEqualTo(1);
        await Assert.That(reported[0].Severity)
            .IsEqualTo(RenderDiagnosticSeverity.Information);
        await Assert.That(reported[0].Message).Contains("3 emitted vertices");
        await Assert.That(reported[0].Message).Contains("0.5 scene units");
    }

    /// <summary>
    /// A height field keeps single-precision values through the authored
    /// <c>scale</c> and <c>bias</c>, including values below zero and above one
    /// that an unsigned-normalized requantization would have clamped away.
    /// </summary>
    /// <remarks>
    /// The oracle is independent of the renderer: the authored affine is applied
    /// to the byte-to-unit conversion by hand, in the case itself, and the
    /// expected vertex position is formed from it. The two extreme rows are the
    /// point -- an eight-bit round trip would pin them at exactly zero and one.
    /// </remarks>
    [Test]
    public async Task SignedAndOverUnitHeightsSurviveAsFloats()
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, HeightDecoder);
        var scene = new SilkSceneState();

        // scale = 4, bias = -2 maps the four authored bytes to
        // -2, -0.996..., 0.007..., 2 -- two of them outside the unit range.
        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(
                textureAsset: HeightAsset,
                textureScale: 4f,
                textureBias: -2f),
            CreateMeshUpsert(FlatPoints, FlatNormals));

        float[] vertices = ReadVertices(resources, pointCount: 3, strideFloats: 8);
        await Assert.That(vertices[2]).IsEqualTo(height(128) * 4f - 2f).Within(1e-6f);
        await Assert.That(vertices[10]).IsEqualTo(height(255) * 4f - 2f).Within(1e-6f);
        await Assert.That(vertices[18]).IsEqualTo(height(0) * 4f - 2f).Within(1e-6f);

        // The signed and the over-unit height both survive, which is what an
        // eight-bit clamp would have destroyed in opposite directions.
        await Assert.That(vertices[18]).IsEqualTo(-2f).Within(1e-6f);
        await Assert.That(vertices[10]).IsEqualTo(2f).Within(1e-6f);
        await Assert.That(vertices[18]).IsLessThan(0f);
        await Assert.That(vertices[10]).IsGreaterThan(1f);

        static float height(byte texel) => texel / 255f;
    }

    /// <summary>
    /// An unreadable height field displaces by the authored <c>fallback</c>, read
    /// through the same output channel and the same affine a texel would have
    /// been, and says so.
    /// </summary>
    [Test]
    public async Task AnUnreadableHeightFieldDisplacesByTheAuthoredFallback()
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, MissingDecoder);
        var scene = new SilkSceneState();

        // fallback 0.5 through scale 3 and bias -0.25 is 1.25.
        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(
                textureAsset: "absent.png",
                textureScale: 3f,
                textureBias: -0.25f,
                textureFallback: 0.5f),
            CreateMeshUpsert(FlatPoints, FlatNormals));

        float[] vertices = ReadVertices(resources, pointCount: 3, strideFloats: 8);
        await Assert.That(vertices[2]).IsEqualTo(1.25f).Within(1e-6f);
        await Assert.That(vertices[10]).IsEqualTo(1.25f).Within(1e-6f);

        IReadOnlyList<RenderDiagnostic> reported = Diagnostics(
            resources,
            SilkRenderDiagnosticCodes.DisplacementUnsupported);
        await Assert.That(reported.Count).IsEqualTo(1);
        await Assert.That(reported[0].Message).Contains("authored fallback");
        await Assert.That(reported[0].Message).Contains("could not be found or decoded");

        // Non-vacuity: a renderer that left the surface flat on a missing file
        // would put these vertices at zero.
        await Assert.That(vertices[2]).IsNotEqualTo(0f);
    }

    /// <summary>
    /// An image whose header alone claims more texels than the budget retains is
    /// refused without being decoded.
    /// </summary>
    [Test]
    public async Task AHostileImageHeaderIsRefusedBeforeItIsDecoded()
    {
        bool decoded = false;
        using var device = new DisplacementDevice();

        // Four billion by four billion: the texel product overflows 32 bits and
        // the byte product overflows it several times over.
        var hostile = new SilkImageDescription(
            4_000_000_000,
            4_000_000_000,
            SilkTextureFormat.Rgba8Unorm);
        using var resources = new SilkSceneGpuResources(
            device,
            (asset, srgb) =>
            {
                decoded = true;
                throw new InvalidOperationException(
                    "A refused displacement image must never reach a decoder.");
            },
            udimResolver: null,
            residencyOptions: null,
            imageDescriber: _ => hostile);
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(textureAsset: HeightAsset),
            CreateMeshUpsert(FlatPoints, FlatNormals));

        await Assert.That(decoded)
            .IsFalse()
            .Because("the bound must be decided from the header, before any allocation");
        IReadOnlyList<RenderDiagnostic> reported = Diagnostics(
            resources,
            SilkRenderDiagnosticCodes.DisplacementBudgetExceeded);
        await Assert.That(reported.Count).IsEqualTo(1);
        await Assert.That(reported[0].Message).Contains("texels");
        await Assert.That(resources.DisplacementImageCount).IsEqualTo(0);
        float[] vertices = ReadVertices(resources, pointCount: 3, strideFloats: 8);
        await Assert.That(vertices[2]).IsEqualTo(0f);
    }

    /// <summary>
    /// A retained geometry answers a repeated resolution, a second instance and a
    /// republished page without decoding an image, sampling a point or assembling
    /// a vertex.
    /// </summary>
    [Test]
    public async Task ACacheHitCostsNoDecodeSamplingOrBuild()
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, HeightDecoder);
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(textureAsset: HeightAsset),
            CreateMeshUpsert(FlatPoints, FlatNormals),
            CreateMeshUpsert(FlatPoints, FlatNormals, instanceIndex: 1, primId: 2));

        // Two instances of one prototype: one build, one decode, one sampling.
        await Assert.That(resources.Statistics.GeometryBuilds).IsEqualTo(1UL);
        await Assert.That(resources.DisplacementImageDecodes).IsEqualTo(1UL);
        await Assert.That(resources.DisplacementResolves).IsEqualTo(1UL);
        await Assert.That(resources.DisplacementSampledPoints).IsEqualTo(3UL);
        await Assert.That(resources.GeometryCacheHits)
            .IsGreaterThanOrEqualTo(1UL)
            .Because("the second instance must be answered by the retained geometry");

        // Republishing the identical page costs nothing at all.
        ulong hits = resources.GeometryCacheHits;
        ApplyPage(
            scene,
            resources,
            2,
            CreateMaterialUpsert(textureAsset: HeightAsset),
            CreateMeshUpsert(FlatPoints, FlatNormals),
            CreateMeshUpsert(FlatPoints, FlatNormals, instanceIndex: 1, primId: 2));
        await Assert.That(resources.Statistics.GeometryBuilds).IsEqualTo(1UL);
        await Assert.That(resources.DisplacementImageDecodes).IsEqualTo(1UL);
        await Assert.That(resources.DisplacementResolves).IsEqualTo(1UL);
        await Assert.That(resources.GeometryCacheHits).IsGreaterThan(hits);

        // Non-vacuity: a changed height field does all of that work again, so the
        // counters above are a cache and not a path that never runs.
        ApplyPage(
            scene,
            resources,
            3,
            CreateMaterialUpsert(textureAsset: HeightAsset, textureScale: 0.5f),
            CreateMeshUpsert(FlatPoints, FlatNormals),
            CreateMeshUpsert(FlatPoints, FlatNormals, instanceIndex: 1, primId: 2));
        await Assert.That(resources.Statistics.GeometryBuilds).IsEqualTo(2UL);
        await Assert.That(resources.DisplacementImageDecodes).IsEqualTo(2UL);
        await Assert.That(resources.DisplacementResolves).IsEqualTo(2UL);
    }

    /// <summary>
    /// Rebinding a prim to a different material rebuilds its geometry, even when
    /// nothing about the mesh record moved.
    /// </summary>
    [Test]
    public async Task RebindingAMaterialRebuildsTheDisplacedGeometry()
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, MissingDecoder);
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(scalarAmount: 0.5f),
            CreateMaterialUpsert(scalarAmount: 0.25f, materialPath: SecondMaterialPath),
            CreateMeshUpsert(FlatPoints, FlatNormals));
        float[] first = ReadVertices(resources, pointCount: 3, strideFloats: 6);
        await Assert.That(first[2]).IsEqualTo(0.5f).Within(1e-6f);

        // The same points, the same normals, the same everything except which
        // material the prim binds.
        ApplyPage(
            scene,
            resources,
            2,
            CreateMeshUpsert(FlatPoints, FlatNormals, materialPath: SecondMaterialPath));
        float[] rebound = ReadVertices(resources, pointCount: 3, strideFloats: 6);
        await Assert.That(rebound[2])
            .IsEqualTo(0.25f)
            .Within(1e-6f)
            .Because("a rebinding must resolve the new material's displacement");
    }

    /// <summary>
    /// Editing the texture-coordinate values a displacement samples through
    /// resamples it, even when the points and the primvar's name are unchanged.
    /// </summary>
    [Test]
    public async Task EditingBoundUvDataResamplesTheDisplacement()
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, HeightDecoder);
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(textureAsset: HeightAsset),
            CreateMeshUpsert(FlatPoints, FlatNormals));
        float[] before = ReadVertices(resources, pointCount: 3, strideFloats: 8);
        await Assert.That(before[2]).IsEqualTo(128f / 255f).Within(1e-6f);

        // Every coordinate moves to the texel the first one used to read, so a
        // renderer that kept the previous samples would draw the previous heights.
        ApplyPage(
            scene,
            resources,
            2,
            CreateMeshUpsert(
                FlatPoints,
                FlatNormals,
                texCoords: [0.75f, 0.25f, 0.75f, 0.25f, 0.75f, 0.25f]));
        float[] after = ReadVertices(resources, pointCount: 3, strideFloats: 8);
        await Assert.That(after[2]).IsEqualTo(1f).Within(1e-6f);
        await Assert.That(after[10]).IsEqualTo(1f).Within(1e-6f);
        await Assert.That(after[18]).IsEqualTo(1f).Within(1e-6f);
        await Assert.That(resources.Statistics.GeometryBuilds).IsEqualTo(2UL);
        // Non-vacuity: the emitted coordinates moved too, so the resample is not
        // hiding a geometry that simply never changed.
        await Assert.That(after[6]).IsNotEqualTo(before[6]);
    }

    /// <summary>
    /// The shadow-bounds verdict follows the published shadow table: it appears
    /// when shadows are enabled after a prim is displaced, and clears when they
    /// retire, without the prim's geometry or material changing at all.
    /// </summary>
    [Test]
    public async Task TheShadowBoundsVerdictFollowsTheShadowTable()
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, MissingDecoder);
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(scalarAmount: 0.5f),
            CreateMeshUpsert(FlatPoints, FlatNormals));
        await Assert
            .That(Diagnostics(
                resources,
                SilkRenderDiagnosticCodes.DisplacementShadowBoundsUnverified).Count)
            .IsEqualTo(0)
            .Because("a scene with no shadow map has no light frustum to be clipped by");

        // Shadows are enabled after the displaced geometry already exists.
        ApplyPage(scene, resources, 2, CreateShadowCommand(descriptorCount: 1));
        IReadOnlyList<RenderDiagnostic> raised = Diagnostics(
            resources,
            SilkRenderDiagnosticCodes.DisplacementShadowBoundsUnverified);
        await Assert.That(raised.Count).IsEqualTo(1);
        await Assert.That(raised[0].Message).Contains("0.5 scene units");
        await Assert.That(raised[0].Severity).IsEqualTo(RenderDiagnosticSeverity.Information);

        // And retiring them clears it again.
        ApplyPage(scene, resources, 3, CreateShadowCommand(descriptorCount: 0));
        await Assert
            .That(Diagnostics(
                resources,
                SilkRenderDiagnosticCodes.DisplacementShadowBoundsUnverified).Count)
            .IsEqualTo(0)
            .Because("a retired shadow table leaves no frustum to report against");
    }

    /// <summary>
    /// An authored <c>auto</c> colour space is resolved from what the image
    /// library observed about the file, so an untagged one-channel height map is
    /// read raw rather than linearized as if it were an sRGB colour.
    /// </summary>
    [Test]
    [Arguments(1u, SilkImageColorSpaceObservation.Raw, false)]
    [Arguments(3u, SilkImageColorSpaceObservation.Srgb, true)]
    public async Task AutoColourSpaceUsesTheObservedImageColourSpace(
        uint channelCount,
        SilkImageColorSpaceObservation observed,
        bool expectedLinearization)
    {
        bool? requestedLinearization = null;
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (asset, srgb) =>
            {
                requestedLinearization = srgb;
                return HeightDecoder(asset, srgb);
            },
            udimResolver: null,
            residencyOptions: null,
            imageDescriber: _ => new SilkImageDescription(
                2,
                2,
                SilkTextureFormat.Rgba8Unorm,
                channelCount,
                SilkImageObservation.Queried |
                    SilkImageObservation.ChannelCount |
                    SilkImageObservation.ColorSpace,
                observed));
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(
                textureAsset: HeightAsset,
                colorSpace: SilkColorSpace.Auto),
            CreateMeshUpsert(FlatPoints, FlatNormals));

        await Assert.That(requestedLinearization)
            .IsEqualTo(expectedLinearization)
            .Because("auto must resolve from what the image library observed");

        // Non-vacuity: an authored raw never linearizes, whatever the file says,
        // so the answer above is the auto rule and not a constant.
        requestedLinearization = null;
        ApplyPage(
            scene,
            resources,
            2,
            CreateMaterialUpsert(
                textureAsset: HeightAsset,
                colorSpace: SilkColorSpace.Raw),
            CreateMeshUpsert(FlatPoints, FlatNormals));
        await Assert.That(requestedLinearization).IsFalse();
    }

    /// <summary>
    /// An input that defers to image metadata this renderer did not observe is
    /// refused by name rather than resolved from a guess.
    /// </summary>
    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task AnUnobservedDeferredInputIsRefusedByName(bool deferColorSpace, bool deferWrap)
    {
        using var device = new DisplacementDevice();
        // The decoder-backed describer observes only the shape, which is exactly
        // the state a consumer that supplied no describer is in.
        using var resources = new SilkSceneGpuResources(device, HeightDecoder);
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(
                textureAsset: HeightAsset,
                colorSpace: deferColorSpace ? SilkColorSpace.Auto : SilkColorSpace.Raw,
                wrap: deferWrap ? SilkTextureWrap.UseMetadata : SilkTextureWrap.Clamp),
            CreateMeshUpsert(FlatPoints, FlatNormals));

        IReadOnlyList<RenderDiagnostic> reported = Diagnostics(
            resources,
            SilkRenderDiagnosticCodes.DisplacementUnsupported);
        await Assert.That(reported.Count).IsEqualTo(1);
        await Assert.That(reported[0].Message).Contains("did not observe");
        float[] vertices = ReadVertices(resources, pointCount: 3, strideFloats: 8);
        await Assert.That(vertices[2]).IsEqualTo(0f);
    }

    /// <summary>
    /// <c>useMetadata</c> honours the wrap the image file states, resolves an
    /// image that carries none to black, and refuses a mode the wire cannot
    /// carry.
    /// </summary>
    [Test]
    [Arguments(true, SilkImageAddressObservation.Repeat, 128f / 255f, false)]
    [Arguments(true, SilkImageAddressObservation.ClampToEdge, 1f, false)]
    [Arguments(true, SilkImageAddressObservation.ClampToBorder, 0f, false)]
    [Arguments(false, SilkImageAddressObservation.ClampToEdge, 0f, false)]
    [Arguments(true, SilkImageAddressObservation.MirrorClampToEdge, 0f, true)]
    public async Task UseMetadataHonoursObservedImageWrapping(
        bool observedWrap,
        SilkImageAddressObservation address,
        float expectedOutsideSample,
        bool expectRefusal)
    {
        SilkImageObservation observed = SilkImageObservation.Queried |
            SilkImageObservation.ColorSpace;
        if (observedWrap)
        {
            observed |= SilkImageObservation.AddressU | SilkImageObservation.AddressV;
        }
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            HeightDecoder,
            udimResolver: null,
            residencyOptions: null,
            imageDescriber: _ => new SilkImageDescription(
                2,
                2,
                SilkTextureFormat.Rgba8Unorm,
                1,
                observed,
                SilkImageColorSpaceObservation.Raw,
                address,
                address));
        var scene = new SilkSceneState();

        // The first coordinate is pushed a whole tile outside the unit range, so
        // the resolved addressing decides what it reads.
        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(
                textureAsset: HeightAsset,
                wrap: SilkTextureWrap.UseMetadata),
            CreateMeshUpsert(
                FlatPoints,
                FlatNormals,
                texCoords: [1.25f, 0.25f, 0.75f, 0.25f, 0.25f, 0.75f]));

        IReadOnlyList<RenderDiagnostic> reported = Diagnostics(
            resources,
            SilkRenderDiagnosticCodes.DisplacementUnsupported);
        if (expectRefusal)
        {
            await Assert.That(reported.Count).IsEqualTo(1);
            await Assert.That(reported[0].Message).Contains("the wire cannot carry");
            return;
        }

        await Assert.That(reported.Count).IsEqualTo(0);
        float[] vertices = ReadVertices(resources, pointCount: 3, strideFloats: 8);
        await Assert.That(vertices[2]).IsEqualTo(expectedOutsideSample).Within(1e-6f);
    }

    /// <summary>
    /// A transparent-black border sample carries the authored bias, because
    /// UsdUVTexture applies the affine after sampling rather than to the texels.
    /// </summary>
    [Test]
    public async Task ABorderSampleReceivesTheAuthoredBias()
    {
        float[] texels = [0.25f, 1f];
        SilkDisplacementField black = SilkDisplacementField.Textured(
            texels,
            2,
            1,
            SilkTextureWrap.Black,
            SilkTextureWrap.Clamp,
            Identity,
            2f,
            0.5f,
            "st",
            1);

        // Fully outside: the sample is the border zero, so the authored result is
        // the bias alone. A renderer that folded the affine into its texels would
        // return zero here.
        await Assert.That(black.Sample(1.75f, 0.5f)).IsEqualTo(0.5f).Within(1e-6f);
        // Half a texel outside the first centre: the filtered sample is half of
        // texel zero, and the affine applies once to that blend.
        await Assert.That(black.Sample(0f, 0.5f))
            .IsEqualTo(((0.25f * 0.5f) * 2f) + 0.5f)
            .Within(1e-6f);
        // Fully inside is the ordinary affine, which is what makes the two above
        // a border rule rather than a broken sampler.
        await Assert.That(black.Sample(0.25f, 0.5f))
            .IsEqualTo((0.25f * 2f) + 0.5f)
            .Within(1e-6f);
        // Non-vacuity: an affine folded into the texels would put the border at
        // zero and the edge blend at half the inside value.
        await Assert.That(black.Sample(1.75f, 0.5f)).IsNotEqualTo(0f);
        await Assert.That(black.Sample(0f, 0.5f))
            .IsNotEqualTo(black.Sample(0.25f, 0.5f) * 0.5f);
    }

    /// <summary>
    /// Repairing an unreadable height field and retrying resolves it, even though
    /// the authored material and the mesh record never changed.
    /// </summary>
    [Test]
    public async Task RepairingAnUnreadableHeightFieldResolvesOnRetry()
    {
        bool repaired = false;
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (asset, srgb) => repaired
                ? HeightDecoder(HeightAsset, srgb)
                : throw new FileNotFoundException($"Texture '{asset}' is absent.", asset),
            udimResolver: null,
            residencyOptions: null,
            imageDescriber: _ => repaired
                ? new SilkImageDescription(
                    2,
                    2,
                    SilkTextureFormat.Rgba8Unorm,
                    1,
                    SilkImageObservation.Queried |
                        SilkImageObservation.ChannelCount |
                        SilkImageObservation.ColorSpace,
                    SilkImageColorSpaceObservation.Raw)
                : throw new FileNotFoundException("absent", HeightAsset));
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(textureAsset: HeightAsset, textureFallback: 0.125f),
            CreateMeshUpsert(FlatPoints, FlatNormals));
        float[] broken = ReadVertices(resources, pointCount: 3, strideFloats: 8);
        await Assert.That(broken[2]).IsEqualTo(0.125f).Within(1e-6f);
        await Assert
            .That(Diagnostics(
                resources,
                SilkRenderDiagnosticCodes.DisplacementUnsupported).Count)
            .IsEqualTo(1);

        // The file is repaired outside the scene: nothing about the material or
        // the mesh record changes, so only the retry can pick it up.
        repaired = true;
        resources.RetryFailedTextures(scene);

        float[] fixedUp = ReadVertices(resources, pointCount: 3, strideFloats: 8);
        await Assert.That(fixedUp[2]).IsEqualTo(128f / 255f).Within(1e-6f);
        await Assert.That(fixedUp[10]).IsEqualTo(1f).Within(1e-6f);
        await Assert
            .That(Diagnostics(
                resources,
                SilkRenderDiagnosticCodes.DisplacementUnsupported).Count)
            .IsEqualTo(0)
            .Because("a repaired asset must stop reporting the substitution");
        await Assert
            .That(Diagnostics(resources, SilkRenderDiagnosticCodes.DisplacementApplied).Count)
            .IsEqualTo(1);
    }

    /// <summary>
    /// Changing a working displacement into a refused one clears the applied and
    /// shadow-bounds verdicts and publishes the named refusal.
    /// </summary>
    [Test]
    public async Task ARefusedDisplacementClearsTheAppliedAndShadowVerdicts()
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, HeightDecoder);
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateShadowCommand(descriptorCount: 1),
            CreateMaterialUpsert(scalarAmount: 0.5f),
            CreateMeshUpsert(FlatPoints, FlatNormals));
        await Assert
            .That(Diagnostics(resources, SilkRenderDiagnosticCodes.DisplacementApplied).Count)
            .IsEqualTo(1);
        await Assert
            .That(Diagnostics(
                resources,
                SilkRenderDiagnosticCodes.DisplacementShadowBoundsUnverified).Count)
            .IsEqualTo(1);

        ApplyPage(
            scene,
            resources,
            2,
            CreateMaterialUpsert(textureAsset: "height.<UDIM>.png"));
        await Assert
            .That(Diagnostics(resources, SilkRenderDiagnosticCodes.DisplacementApplied).Count)
            .IsEqualTo(0)
            .Because("a refused displacement must stop claiming it was applied");
        await Assert
            .That(Diagnostics(
                resources,
                SilkRenderDiagnosticCodes.DisplacementShadowBoundsUnverified).Count)
            .IsEqualTo(0)
            .Because("a surface that no longer moves cannot exceed a light frustum");
        IReadOnlyList<RenderDiagnostic> refusal = Diagnostics(
            resources,
            SilkRenderDiagnosticCodes.DisplacementUnsupported);
        await Assert.That(refusal.Count).IsEqualTo(1);
        await Assert.That(refusal[0].Message).Contains("UDIM tile set");
    }

    /// <summary>
    /// Retiring one instance of a displaced prototype leaves its siblings'
    /// verdicts alone, and the surviving instances still report once for the
    /// prototype's path.
    /// </summary>
    [Test]
    public async Task RetiringOneInstanceKeepsTheSurvivingInstanceVerdict()
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, MissingDecoder);
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(scalarAmount: 0.5f),
            CreateMeshUpsert(FlatPoints, FlatNormals, instanceIndex: 0, primId: 1),
            CreateMeshUpsert(FlatPoints, FlatNormals, instanceIndex: 1, primId: 2),
            CreateMeshUpsert(FlatPoints, FlatNormals, instanceIndex: 2, primId: 3));
        await Assert.That(resources.DisplacementVerdictCount).IsEqualTo(3);
        // One prototype path is one diagnostic, whatever its instance count: a
        // per-instance report would exhaust the bounded snapshot on a crowd.
        await Assert
            .That(Diagnostics(resources, SilkRenderDiagnosticCodes.DisplacementApplied).Count)
            .IsEqualTo(1);

        ApplyPage(scene, resources, 2, CreateMeshRemove(instanceIndex: 1));
        await Assert.That(resources.DisplacementVerdictCount)
            .IsEqualTo(2)
            .Because("only the retired instance's verdict may be dropped");
        await Assert
            .That(Diagnostics(resources, SilkRenderDiagnosticCodes.DisplacementApplied).Count)
            .IsEqualTo(1)
            .Because("the surviving instances still earn the prototype's report");

        ApplyPage(
            scene,
            resources,
            3,
            CreateMeshRemove(instanceIndex: 0),
            CreateMeshRemove(instanceIndex: 2));
        await Assert.That(resources.DisplacementVerdictCount).IsEqualTo(0);
        await Assert
            .That(Diagnostics(resources, SilkRenderDiagnosticCodes.DisplacementApplied).Count)
            .IsEqualTo(0)
            .Because("a prototype nobody draws reports nothing");
    }

    /// <summary>
    /// Retrying without a scene discards only what the next resolution can put
    /// back. The retained displaced geometry and its verdict survive, because
    /// this overload cannot rebuild either and a renderer must not draw vertices
    /// whose verdict it has thrown away.
    /// </summary>
    [Test]
    public async Task RetryingWithoutASceneKeepsTheStateItCannotRebuild()
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, HeightDecoder);
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateShadowCommand(descriptorCount: 1),
            CreateMaterialUpsert(textureAsset: HeightAsset),
            CreateMeshUpsert(FlatPoints, FlatNormals));
        float[] before = ReadVertices(resources, pointCount: 3, strideFloats: 8);
        await Assert.That(before[2]).IsEqualTo(128f / 255f).Within(1e-6f);
        await Assert.That(resources.DisplacementVerdictCount).IsEqualTo(1);

        resources.RetryFailedTextures();

        // The vertices are still the displaced ones, and they are still claimed.
        float[] after = ReadVertices(resources, pointCount: 3, strideFloats: 8);
        await Assert.That(after[2]).IsEqualTo(before[2]).Within(1e-6f);
        await Assert
            .That(resources.DisplacementVerdictCount)
            .IsEqualTo(1)
            .Because("a verdict this overload cannot restate must not be dropped");
        await Assert
            .That(Diagnostics(resources, SilkRenderDiagnosticCodes.DisplacementApplied).Count)
            .IsEqualTo(1);
        await Assert
            .That(Diagnostics(
                resources,
                SilkRenderDiagnosticCodes.DisplacementShadowBoundsUnverified).Count)
            .IsEqualTo(1);
    }

    /// <summary>
    /// A scene-scoped retry publishes every replacement and advances both
    /// revisions before anything is disposed, so a retained selection or shadow
    /// key cannot still validate while the resource it names is gone.
    /// </summary>
    [Test]
    public async Task RepairingWithASceneAdvancesEveryRetainedConsumerRevision()
    {
        bool repaired = false;
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (asset, srgb) => repaired
                ? HeightDecoder(HeightAsset, srgb)
                : throw new FileNotFoundException($"Texture '{asset}' is absent.", asset),
            udimResolver: null,
            residencyOptions: null,
            imageDescriber: _ => repaired
                ? new SilkImageDescription(
                    2,
                    2,
                    SilkTextureFormat.Rgba8Unorm,
                    1,
                    SilkImageObservation.Queried |
                        SilkImageObservation.ChannelCount |
                        SilkImageObservation.ColorSpace,
                    SilkImageColorSpaceObservation.Raw)
                : throw new FileNotFoundException("absent", HeightAsset));
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateShadowCommand(descriptorCount: 1),
            CreateMaterialUpsert(textureAsset: HeightAsset, textureFallback: 0.125f),
            CreateMeshUpsert(FlatPoints, FlatNormals));
        SilkMeshGpuResource stale = resources.Meshes.Values.Single();
        ulong resourceRevision = resources.Revision;
        ulong geometryRevision = scene.GeometryRevision;
        int liveBuffers = device.LiveBufferCount;

        repaired = true;
        resources.RetryFailedTextures(scene);

        SilkMeshGpuResource repairedMesh = resources.Meshes.Values.Single();
        await Assert
            .That(ReferenceEquals(repairedMesh, stale))
            .IsFalse()
            .Because("the repaired height field has to reach a rebuilt geometry");
        await Assert
            .That(resources.Revision)
            .IsGreaterThan(resourceRevision)
            .Because("a resolved selection is only revalidated when this advances");
        await Assert
            .That(scene.GeometryRevision)
            .IsGreaterThan(geometryRevision)
            .Because("the retained shadow atlas is only rebuilt when this advances");
        // The retired resource was released, not leaked, and the live count is
        // back where it started: the replacement took its place before the
        // disposal, never the other way round.
        await Assert.That(device.LiveBufferCount).IsEqualTo(liveBuffers);
        float[] fixedUp = ReadVertices(resources, pointCount: 3, strideFloats: 8);
        await Assert.That(fixedUp[2]).IsEqualTo(128f / 255f).Within(1e-6f);
    }

    /// <summary>
    /// A GPU-deformed geometry never moves under a displacement, but it still
    /// carries the verdict: two rigs refused for different reasons, or one rig
    /// whose reason changes, must not share a retained resource.
    /// </summary>
    [Test]
    public async Task ARefusalReasonReachesTheGpuDeformedGeometryKey()
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, MissingDecoder);
        var scene = new SilkSceneState();

        // The three states a rig can be bound to: nothing authored, a UDIM tile
        // set, and an unreadable file. None of them displaces the vertices, so
        // only the identity distinguishes them.
        ApplyPage(scene, resources, 1, CreateMeshUpsert(FlatPoints, FlatNormals));
        ulong notAuthored = displacementIdentityOf(resources);

        ApplyPage(
            scene,
            resources,
            2,
            CreateMaterialUpsert(textureAsset: "height.<UDIM>.png"),
            CreateMeshUpsert(FlatPoints, FlatNormals));
        ulong udim = displacementIdentityOf(resources);

        ApplyPage(
            scene,
            resources,
            3,
            CreateMaterialUpsert(textureAsset: HeightAsset),
            CreateMeshUpsert(FlatPoints, FlatNormals));
        ulong unreadable = displacementIdentityOf(resources);

        await Assert.That(notAuthored).IsEqualTo(0UL);
        await Assert.That(udim).IsNotEqualTo(notAuthored);
        await Assert.That(unreadable).IsNotEqualTo(notAuthored);
        await Assert
            .That(unreadable)
            .IsNotEqualTo(udim)
            .Because("two refusals are two verdicts, not one undisplaced surface");

        // The kernel's own key carries the same three identities apart, so a rig
        // that changes refusal reason rebuilds rather than reusing the first one.
        SilkMeshData mesh = BuildMesh(FlatPoints, FlatNormals, TexCoords);
        SilkMeshDeformationData deformation = SilkDeformationRigFixture.Build(
            FlatPoints,
            FlatNormals,
            influencesPerPoint: 1,
            [0, 0, 0],
            [1f, 1f, 1f],
            [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1],
            [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
        var keys = new HashSet<SilkMeshGpuGeometryKey>
        {
            SilkMeshGpuGeometryKey.CreateGpuDeformed(
                mesh, deformation, "st", false, MaterialPath, 3, notAuthored),
            SilkMeshGpuGeometryKey.CreateGpuDeformed(
                mesh, deformation, "st", false, MaterialPath, 3, udim),
            SilkMeshGpuGeometryKey.CreateGpuDeformed(
                mesh, deformation, "st", false, MaterialPath, 3, unreadable)
        };
        await Assert.That(keys.Count).IsEqualTo(3);

        static ulong displacementIdentityOf(SilkSceneGpuResources resources) =>
            resources.Meshes.Values.Single().Geometry.Key.DisplacementIdentity;
    }

    /// <summary>
    /// A displacement is resolved on its own merits, so editing the coordinates
    /// it samples through resamples it even when the bound surface is a network
    /// hdSilk cannot shade at all.
    /// </summary>
    [Test]
    public async Task EditingDisplacementUvDataResamplesUnderAnUnshadeableSurface()
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, HeightDecoder);
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(
                textureAsset: HeightAsset,
                surfaceKind: SilkSurfaceKind.Unsupported),
            CreateMeshUpsert(FlatPoints, FlatNormals));
        // The bound surface is a network hdSilk cannot shade, so no surface
        // texture names the emitted coordinate stream and the vertex carries
        // position and normal only. The height field is unaffected: it samples
        // the mesh's own primvar.
        float[] before = ReadVertices(resources, pointCount: 3, strideFloats: 6);
        await Assert
            .That(before[2])
            .IsEqualTo(128f / 255f)
            .Within(1e-6f)
            .Because("an unshadeable surface does not make a height field unusable");

        ApplyPage(
            scene,
            resources,
            2,
            CreateMeshUpsert(
                FlatPoints,
                FlatNormals,
                texCoords: [0.75f, 0.25f, 0.75f, 0.25f, 0.75f, 0.25f]));
        float[] after = ReadVertices(resources, pointCount: 3, strideFloats: 6);
        await Assert
            .That(after[2])
            .IsEqualTo(1f)
            .Within(1e-6f)
            .Because("the fast path must compare the displacement's own UV data");
        await Assert.That(after[8]).IsEqualTo(1f).Within(1e-6f);
        await Assert.That(resources.Statistics.GeometryBuilds).IsEqualTo(2UL);
    }

    /// <summary>
    /// A retry that fails part-way through leaves every retained value exactly as
    /// it found it, and does not spend the state a later retry needs.
    /// </summary>
    /// <remarks>
    /// The destructive half of a retry -- dropping the failed-texture entries, the
    /// texture diagnostics, the decoded height fields, the verdicts and the
    /// published geometry keys -- has to happen before the rebuild, because the
    /// rebuild is what re-resolves them. If that half were committed and the
    /// rebuild then failed, the renderer would be left holding meshes whose
    /// verdicts it had thrown away, a selection and a shadow atlas naming
    /// resources of an aborted generation, and a failed-texture cache it could not
    /// restate. The whole call is therefore one transaction.
    /// </remarks>
    [Test]
    public async Task AFailedRetryLeavesEveryRetainedValueExactlyAsItWas()
    {
        const string missingAsset = "missing-height.png";
        bool repaired = false;
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (asset, srgb) =>
                string.Equals(asset, HeightAsset, StringComparison.Ordinal) || repaired
                    ? HeightDecoder(HeightAsset, srgb)
                    : throw new FileNotFoundException($"Texture '{asset}' is absent.", asset),
            udimResolver: null,
            residencyOptions: null,
            imageDescriber: asset =>
                string.Equals(asset, HeightAsset, StringComparison.Ordinal) || repaired
                    ? new SilkImageDescription(
                        2,
                        2,
                        SilkTextureFormat.Rgba8Unorm,
                        1,
                        SilkImageObservation.Queried |
                            SilkImageObservation.ChannelCount |
                            SilkImageObservation.ColorSpace,
                        SilkImageColorSpaceObservation.Raw)
                    : throw new FileNotFoundException("absent", asset));
        var scene = new SilkSceneState();

        // One prim whose height field reads, and one whose file is missing. The
        // first is what makes the retained image cache non-empty, so a rollback
        // that dropped it would be visible.
        ApplyPage(
            scene,
            resources,
            1,
            CreateShadowCommand(descriptorCount: 1),
            CreateMaterialUpsert(textureAsset: HeightAsset),
            CreateMaterialUpsert(
                textureAsset: missingAsset,
                textureFallback: 0.125f,
                materialPath: SecondMaterialPath),
            CreateMeshUpsert(FlatPoints, FlatNormals, instanceIndex: 0, primId: 1),
            CreateMeshUpsert(
                FlatPoints,
                FlatNormals,
                instanceIndex: 1,
                primId: 2,
                materialPath: SecondMaterialPath));

        ulong resourceRevision = resources.Revision;
        ulong geometryRevision = scene.GeometryRevision;
        int verdicts = resources.DisplacementVerdictCount;
        int liveBuffers = device.LiveBufferCount;
        int geometryResources = resources.GeometryResourceCount;
        int imageCount = resources.DisplacementImageCount;
        ulong imageBytes = resources.DisplacementImageBytes;
        SilkMeshGpuResource[] meshes = [.. resources.Meshes.Values];
        string[] diagnostics = DiagnosticSignature(resources);
        float[][] before =
            [.. meshes.Select(mesh => ReadVertices(resources, mesh, 3, strideFloats: 8))];
        await Assert.That(imageCount)
            .IsEqualTo(1)
            .Because("the readable height field must be retained for the rollback to protect");
        await Assert.That(verdicts).IsEqualTo(2);

        // The file is repaired, so both retries below would otherwise succeed.
        repaired = true;

        // First refusal: the device refuses before any replacement exists, which
        // is the window in which the destructive half has run and the rebuilding
        // half has not.
        device.FailAllocationAfter = 0;
        await Assert
            .That(() => resources.RetryFailedTextures(scene))
            .Throws<InvalidOperationException>();
        await assertUnchanged();

        // Second refusal: the device refuses once the first replacement is already
        // built, so the rollback must also release exactly the partial work.
        device.FailAllocationAfter = 3;
        await Assert
            .That(() => resources.RetryFailedTextures(scene))
            .Throws<InvalidOperationException>();
        await assertUnchanged();

        // And the state a later retry needs was not spent: the same call, with the
        // device no longer refusing, still repairs the scene.
        resources.RetryFailedTextures(scene);
        await Assert.That(resources.Revision).IsGreaterThan(resourceRevision);
        await Assert.That(scene.GeometryRevision).IsGreaterThan(geometryRevision);
        await Assert
            .That(resources.Meshes.Values.Select(mesh =>
                ReadVertices(resources, mesh, 3, strideFloats: 8)[2])
                .All(static height => Math.Abs(height - (128f / 255f)) < 1e-6f))
            .IsTrue()
            .Because("the repaired file must reach every prim that was waiting for it");

        async Task assertUnchanged()
        {
            await Assert.That(resources.Revision)
                .IsEqualTo(resourceRevision)
                .Because("a failed retry must not advance the revision a selection is keyed by");
            await Assert.That(scene.GeometryRevision)
                .IsEqualTo(geometryRevision)
                .Because("a failed retry must not advance the shadow atlas revision");
            await Assert.That(resources.DisplacementVerdictCount).IsEqualTo(verdicts);
            await Assert.That(resources.GeometryResourceCount).IsEqualTo(geometryResources);
            await Assert.That(resources.DisplacementImageCount)
                .IsEqualTo(imageCount)
                .Because("a failed retry must leave the retained height fields as it found them");
            await Assert.That(resources.DisplacementImageBytes).IsEqualTo(imageBytes);
            await Assert.That(device.LiveBufferCount)
                .IsEqualTo(liveBuffers)
                .Because("partial work must be released and nothing else disposed");
            await Assert.That(DiagnosticSignature(resources)).IsEquivalentTo(diagnostics);
            await Assert.That(resources.Meshes.Values.SequenceEqual(meshes))
                .IsTrue()
                .Because("every prior mesh resource must still be the one the renderer holds");
            for (int index = 0; index < meshes.Length; index++)
            {
                float[] now = ReadVertices(resources, meshes[index], 3, strideFloats: 8);
                await Assert.That(now.AsSpan().SequenceEqual(before[index]))
                    .IsTrue()
                    .Because("a failed retry must leave the drawn vertices untouched");
            }
        }
    }

    /// <summary>
    /// A displacement refused because the mesh carries no such coordinate set
    /// recovers the moment that coordinate set is added, even though the points,
    /// the material and the surface stream are all unchanged.
    /// </summary>
    /// <remarks>
    /// The refusal is a statement about an attribute that is <em>absent</em>, so
    /// the retained geometry has to record which attribute it was and that it was
    /// missing. Recording nothing -- which is what a refusal carrying no texture
    /// does by default -- makes the fast path compare "no displacement coordinate
    /// set" against "no displacement coordinate set" and accept the flat vertices
    /// forever. The surface here is a material this renderer cannot shade, so the
    /// surface stream is empty and cannot mask the comparison.
    /// </remarks>
    [Test]
    public async Task AddingTheMissingDisplacementUvSetRecoversFromTheRefusal()
    {
        using var device = new DisplacementDevice();
        using var resources = new SilkSceneGpuResources(device, HeightDecoder);
        var scene = new SilkSceneState();

        ApplyPage(
            scene,
            resources,
            1,
            CreateMaterialUpsert(
                textureAsset: HeightAsset,
                uvPrimvar: "st2",
                surfaceKind: SilkSurfaceKind.Unsupported),
            CreateMeshUpsert(FlatPoints, FlatNormals));
        float[] refused = ReadVertices(resources, pointCount: 3, strideFloats: 6);
        await Assert.That(refused[2]).IsEqualTo(0f).Within(1e-6f);
        IReadOnlyList<RenderDiagnostic> named = Diagnostics(
            resources,
            SilkRenderDiagnosticCodes.DisplacementUnsupported);
        await Assert.That(named.Count).IsEqualTo(1);
        await Assert.That(named[0].Message).Contains("coordinate set");

        // Nothing about the points, the normals, the material or the surface
        // stream changes: the mesh simply starts carrying the primvar the height
        // field always asked for.
        ApplyPage(
            scene,
            resources,
            2,
            CreateMeshUpsert(
                FlatPoints,
                FlatNormals,
                secondaryTexCoords: [0.25f, 0.25f, 0.75f, 0.25f, 0.25f, 0.75f]));
        float[] recovered = ReadVertices(resources, pointCount: 3, strideFloats: 6);
        await Assert
            .That(recovered[2])
            .IsEqualTo(128f / 255f)
            .Within(1e-6f)
            .Because("adding the requested coordinate set must invalidate the refusal");
        await Assert.That(recovered[8]).IsEqualTo(1f).Within(1e-6f);
        await Assert
            .That(Diagnostics(
                resources,
                SilkRenderDiagnosticCodes.DisplacementUnsupported).Count)
            .IsEqualTo(0)
            .Because("a refusal that no longer holds must stop being reported");
        await Assert
            .That(Diagnostics(resources, SilkRenderDiagnosticCodes.DisplacementApplied).Count)
            .IsEqualTo(1);
    }

    private static readonly float[] Identity = [1, 0, 0, 1, 0, 0];

    private static readonly float[] FlatPoints = [0, 0, 0, 1, 0, 0, 0, 1, 0];

    private static readonly float[] FlatNormals = [0, 0, 1, 0, 0, 1, 0, 0, 1];

    // Three coordinates addressing three different texel centres of the 2x2 field.
    private static readonly float[] TexCoords = [0.25f, 0.25f, 0.75f, 0.25f, 0.25f, 0.75f];

    /// <summary>
    /// The 2x2 height field the textured cases read, after the shared decode path
    /// has flipped its rows: (0, 0) is 128/255, (1, 0) is 1, (0, 1) is 0 and (1, 1)
    /// is 64/255.
    /// </summary>
    private static SilkDisplacementField CreateHeightField() =>
        SilkDisplacementField.Textured(
            [128f / 255f, 1f, 0f, 64f / 255f],
            2,
            2,
            SilkTextureWrap.Clamp,
            SilkTextureWrap.Clamp,
            Identity,
            1,
            0,
            "st",
            42);

    /// <summary>
    /// A decoder that answers the height asset with the unflipped source of
    /// <see cref="CreateHeightField"/>; the shared decode path flips its rows.
    /// </summary>
    private static SilkDecodedImage HeightDecoder(string asset, bool srgb)
    {
        if (!string.Equals(asset, HeightAsset, StringComparison.Ordinal))
        {
            throw new FileNotFoundException($"Texture '{asset}' is absent.", asset);
        }
        byte[] pixels = new byte[16];
        byte[] rows = [0, 64, 128, 255];
        for (int texel = 0; texel < 4; texel++)
        {
            pixels[texel * 4] = rows[texel];
            pixels[(texel * 4) + 1] = rows[texel];
            pixels[(texel * 4) + 2] = rows[texel];
            pixels[(texel * 4) + 3] = 255;
        }
        return new SilkDecodedImage(2, 2, pixels);
    }

    /// <summary>
    /// Builds a MESH_REMOVE for one instance of the prototype.
    /// </summary>
    private static byte[] CreateMeshRemove(int instanceIndex)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(MeshPath);
        byte[] bytes = new byte[24 + pathBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshRemove);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(MeshPath));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), instanceIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), (uint)pathBytes.Length);
        pathBytes.CopyTo(bytes, 24);
        return bytes;
    }

    private static SilkDecodedImage MissingDecoder(string asset, bool srgb) =>
        throw new FileNotFoundException($"Texture '{asset}' is absent.", asset);

    /// <summary>The 2x2 height source in the requested decoded format.</summary>
    private static SilkDecodedImage FormattedHeightImage(SilkTextureFormat format)
    {
        byte[] rows = [0, 64, 128, 255];
        if (format == SilkTextureFormat.Rgba32Float)
        {
            float[] values = new float[16];
            for (int texel = 0; texel < 4; texel++)
            {
                for (int component = 0; component < 3; component++)
                {
                    values[(texel * 4) + component] = rows[texel] / 255f;
                }
                values[(texel * 4) + 3] = 1f;
            }
            return new SilkDecodedImage(
                2,
                2,
                MemoryMarshal.AsBytes(values.AsSpan()).ToArray(),
                SilkTextureFormat.Rgba32Float);
        }
        return HeightDecoder(HeightAsset, false);
    }

    /// <summary>
    /// Builds an ABI v19 shadow table command, with no descriptors when the table
    /// retires.
    /// </summary>
    private static byte[] CreateShadowCommand(uint descriptorCount)
    {
        const int descriptorSize = 288;
        byte[] bytes = new byte[24 + (descriptorCount * descriptorSize)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Shadow);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), descriptorCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), descriptorCount == 0 ? 0u : 1u);
        for (uint descriptor = 0; descriptor < descriptorCount; descriptor++)
        {
            int start = 24 + (int)(descriptor * descriptorSize);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(start), descriptor);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(start + 4), descriptor);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(start + 8), 1024u);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(start + 12), 1u);
            for (int element = 0; element < 16; element++)
            {
                double value = element % 5 == 0 ? 1 : 0;
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(start + 16 + (element * 8)),
                    value);
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(start + 144 + (element * 8)),
                    element == 10 ? -1 : value);
            }
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(start + 272), 0.001f);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(start + 276), 0.001f);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(start + 280), 1f);
        }
        return bytes;
    }

    private static IReadOnlyList<RenderDiagnostic> Diagnostics(
        SilkSceneGpuResources resources,
        string code) =>
        [.. resources.Diagnostics.Entries.Where(
            diagnostic => string.Equals(diagnostic.Code, code, StringComparison.Ordinal))];

    private static string[] DiagnosticSignature(SilkSceneGpuResources resources) =>
        [.. resources.Diagnostics.Entries
            .Select(static diagnostic => $"{diagnostic.Code}|{diagnostic.Message}")
            .OrderBy(static entry => entry, StringComparer.Ordinal)];

    private static float[] ReadVertices(
        SilkSceneGpuResources resources,
        int pointCount,
        int strideFloats) =>
        ReadVertices(resources, resources.Meshes.Values.Single(), pointCount, strideFloats);

    private static float[] ReadVertices(
        SilkSceneGpuResources resources,
        SilkMeshGpuResource mesh,
        int pointCount,
        int strideFloats)
    {
        ArgumentNullException.ThrowIfNull(resources);
        // The retained vertex buffer of a CPU geometry is uploadable rather than
        // storage, so the fake device's own retained bytes are read directly: this
        // is the very buffer the colour, shadow and pick passes bind.
        var buffer = (DisplacementBuffer)mesh.VertexBuffer;
        float[] vertices = new float[pointCount * strideFloats];
        for (int index = 0; index < vertices.Length; index++)
        {
            vertices[index] = BinaryPrimitives.ReadSingleLittleEndian(
                buffer.Bytes.Slice(index * sizeof(float)));
        }
        return vertices;
    }

    private static void ApplyPage(
        SilkSceneState scene,
        SilkSceneGpuResources resources,
        ulong revision,
        params byte[][] commands)
    {
        int length = 0;
        foreach (byte[] command in commands)
        {
            length += command.Length;
        }
        byte[] page = new byte[length];
        int offset = 0;
        foreach (byte[] command in commands)
        {
            command.CopyTo(page, offset);
            offset += command.Length;
        }
        SilkSceneDelta delta = scene.Apply(page, checked((uint)commands.Length), revision);
        resources.Apply(scene, delta);
    }

    private static byte[] CreateMaterialUpsert(
        float? scalarAmount = null,
        string? textureAsset = null,
        string uvPrimvar = "st",
        SilkCompositeOperator compositeOperator = SilkCompositeOperator.None,
        float textureScale = 1f,
        float textureBias = 0f,
        float textureFallback = 0f,
        SilkColorSpace colorSpace = SilkColorSpace.Raw,
        SilkTextureWrap wrap = SilkTextureWrap.Clamp,
        string materialPath = MaterialPath,
        SilkSurfaceKind surfaceKind = SilkSurfaceKind.PreviewSurface)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(materialPath);
        List<byte> payload = [];
        int scalarCount = scalarAmount is null ? 0 : 1;
        int textureCount = textureAsset is null
            ? 0
            : compositeOperator == SilkCompositeOperator.None ? 1 : 2;
        payload.AddRange(BitConverter.GetBytes(SilkWireFormat.ComputeStableHash(materialPath)));
        payload.AddRange(BitConverter.GetBytes((uint)pathBytes.Length));
        payload.AddRange(BitConverter.GetBytes((uint)surfaceKind));
        payload.AddRange(BitConverter.GetBytes((uint)scalarCount));
        payload.AddRange(BitConverter.GetBytes((uint)textureCount));
        payload.AddRange(pathBytes);

        if (scalarAmount is { } amount)
        {
            payload.AddRange(BitConverter.GetBytes((uint)SilkMaterialParameter.Displacement));
            payload.AddRange(BitConverter.GetBytes(1u));
            payload.AddRange(BitConverter.GetBytes(amount));
        }

        if (textureAsset is not null)
        {
            WriteTexture(
                payload,
                textureAsset,
                uvPrimvar,
                SilkCompositeOperator.None,
                textureScale,
                textureBias,
                textureFallback,
                colorSpace,
                wrap);
            if (compositeOperator != SilkCompositeOperator.None)
            {
                WriteTexture(
                    payload,
                    textureAsset,
                    uvPrimvar,
                    compositeOperator,
                    textureScale,
                    textureBias,
                    textureFallback,
                    colorSpace,
                    wrap);
            }
        }

        payload.AddRange(BitConverter.GetBytes(0u));
        payload.AddRange(BitConverter.GetBytes(0u));
        foreach (float element in Identity)
        {
            payload.AddRange(BitConverter.GetBytes(element));
        }
        return CreateCommand(SilkCommandType.MaterialUpsert, payload);
    }

    private static void WriteTexture(
        List<byte> payload,
        string asset,
        string uvPrimvar,
        SilkCompositeOperator compositeOperator,
        float scale,
        float bias,
        float fallback,
        SilkColorSpace colorSpace,
        SilkTextureWrap wrap)
    {
        byte[] assetBytes = Encoding.UTF8.GetBytes(asset);
        byte[] uvBytes = Encoding.UTF8.GetBytes(uvPrimvar);
        payload.AddRange(BitConverter.GetBytes((uint)SilkMaterialParameter.Displacement));
        payload.AddRange(BitConverter.GetBytes((uint)wrap));
        payload.AddRange(BitConverter.GetBytes((uint)wrap));
        payload.AddRange(BitConverter.GetBytes((uint)colorSpace));
        payload.AddRange(BitConverter.GetBytes((uint)assetBytes.Length));
        payload.AddRange(BitConverter.GetBytes((uint)uvBytes.Length));
        payload.AddRange(BitConverter.GetBytes(1u));
        for (int component = 0; component < 4; component++)
        {
            payload.AddRange(BitConverter.GetBytes(scale));
        }
        for (int component = 0; component < 4; component++)
        {
            payload.AddRange(BitConverter.GetBytes(bias));
        }
        for (int component = 0; component < 4; component++)
        {
            payload.AddRange(BitConverter.GetBytes(component == 3 ? 1f : fallback));
        }
        payload.AddRange(BitConverter.GetBytes((uint)SilkTextureChannel.R));
        payload.AddRange(BitConverter.GetBytes((uint)compositeOperator));
        payload.AddRange(BitConverter.GetBytes(0f));
        payload.AddRange(assetBytes);
        payload.AddRange(uvBytes);
    }

    private static byte[] CreateCommand(SilkCommandType type, List<byte> payload)
    {
        byte[] bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)type);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    private static SilkMeshData BuildMesh(float[] points, float[] normals, float[] texCoords)
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshUpsert(points, normals, texCoords: texCoords), 1, 1);
        return scene.MeshesByPath[(MeshPath, 0)];
    }

    private static byte[] CreateMeshUpsert(
        float[] points,
        float[] normals,
        SilkTopologyKind topology = SilkTopologyKind.TriangleList,
        uint[]? indices = null,
        float[]? texCoords = null,
        float[]? secondaryTexCoords = null,
        int instanceIndex = 0,
        int primId = 1,
        string materialPath = MaterialPath)
    {
        indices ??= [0, 1, 2];
        texCoords ??= TexCoords;
        int indicesPerPrimitive = topology switch
        {
            SilkTopologyKind.TriangleList => 3,
            SilkTopologyKind.LineList => 2,
            _ => 1
        };
        int primitiveCount = indices.Length / indicesPerPrimitive;
        byte[] pathBytes = Encoding.UTF8.GetBytes(MeshPath);
        byte[] materialBytes = Encoding.UTF8.GetBytes(materialPath);
        byte[] normalName = Encoding.UTF8.GetBytes("normals");
        byte[] uvName = Encoding.UTF8.GetBytes("st");
        byte[] secondaryUvName = Encoding.UTF8.GetBytes("st2");

        List<byte> variable = [];
        variable.AddRange(pathBytes);
        foreach (float value in points)
        {
            variable.AddRange(BitConverter.GetBytes(value));
        }
        foreach (uint value in indices)
        {
            variable.AddRange(BitConverter.GetBytes(value));
        }
        for (int primitive = 0; primitive < primitiveCount; primitive++)
        {
            variable.AddRange(BitConverter.GetBytes(0u));
        }
        variable.AddRange(materialBytes);
        WriteAttribute(
            variable,
            SilkAttributeSemantic.Normal,
            3,
            normalName,
            normals);
        WriteAttribute(
            variable,
            SilkAttributeSemantic.TexCoord,
            2,
            uvName,
            texCoords);
        if (secondaryTexCoords is not null)
        {
            WriteAttribute(
                variable,
                SilkAttributeSemantic.TexCoord,
                2,
                secondaryUvName,
                secondaryTexCoords);
        }

        byte[] bytes = new byte[268 + variable.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(MeshPath));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), primId);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), instanceIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), (uint)topology);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)pathBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), (uint)(points.Length / 3));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), (uint)indices.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), (uint)primitiveCount);
        for (int index = 0; index < 4; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64 + (index * 4)), 1);
        }
        for (int index = 0; index < 16; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (index * 8)),
                index % 5 == 0 ? 1 : 0);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(216), (uint)materialBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(220),
            secondaryTexCoords is null ? 2u : 3u);
        variable.CopyTo(bytes, 268);
        return bytes;
    }

    private static void WriteAttribute(
        List<byte> variable,
        SilkAttributeSemantic semantic,
        int components,
        byte[] name,
        float[] data)
    {
        variable.AddRange(BitConverter.GetBytes((uint)semantic));
        variable.AddRange(BitConverter.GetBytes((uint)components));
        variable.AddRange(
            BitConverter.GetBytes((uint)SilkAttributeInterpolation.Vertex));
        variable.AddRange(BitConverter.GetBytes((uint)name.Length));
        variable.AddRange(BitConverter.GetBytes((uint)(data.Length / components)));
        variable.AddRange(name);
        foreach (float value in data)
        {
            variable.AddRange(BitConverter.GetBytes(value));
        }
    }

    /// <summary>A device that retains what was written so a vertex buffer can be read back.</summary>
    private sealed class DisplacementDevice : ISilkGraphicsDevice
    {
        private int _created;
        private int _disposedBuffers;

        internal int LiveBufferCount => _created - _disposedBuffers;

        /// <summary>
        /// Refuses the allocation this many buffers from now, then stops refusing.
        /// A retry has to survive a device that runs out part-way through.
        /// </summary>
        internal int FailAllocationAfter { get; set; } = -1;

        public SilkGraphicsBackend Backend => SilkGraphicsBackend.Vulkan;

        public SilkGraphicsCapabilities Capabilities { get; } =
            new("Displacement test", "1", SupportsCompute: true, IsSoftware: true);

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage)
        {
            if (FailAllocationAfter == 0)
            {
                FailAllocationAfter = -1;
                throw new InvalidOperationException("The device refused the allocation.");
            }
            if (FailAllocationAfter > 0)
            {
                FailAllocationAfter--;
            }
            _created++;
            return new DisplacementBuffer(size, usage, () => _disposedBuffers++);
        }

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            throw new NotSupportedException();

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsShaderModule CreateShaderModule(
            SilkShaderModuleDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsBindingLayout CreateBindingLayout(
            SilkBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsShaderProgram CreateShaderProgram(
            SilkShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsPipeline CreateGraphicsPipeline(
            SilkGraphicsPipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeBindingLayout CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeShaderProgram CreateComputeShaderProgram(
            SilkComputeShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputePipeline CreateComputePipeline(
            SilkComputePipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsCommandList CreateCommandList() =>
            throw new NotSupportedException();

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) =>
            throw new NotSupportedException();

        public void WaitIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class DisplacementBuffer(nuint size, SilkBufferUsage usage, Action disposed)
        : SilkGraphicsBufferBase(size, usage)
    {
        private readonly Action _disposed = disposed;

        private byte[] Data { get; } = new byte[checked((int)size)];

        internal ReadOnlySpan<byte> Bytes => Data;

        public override void Write(ReadOnlySpan<byte> data, nuint offset = 0)
        {
            _ = ValidateWrite(data.Length, offset);
            data.CopyTo(Data.AsSpan(checked((int)offset)));
        }

        public override void ReadbackForTesting(Span<byte> destination)
        {
            _ = ValidateReadback(destination.Length);
            Data.CopyTo(destination);
        }

        protected override void ReleaseNative() => _disposed();
    }
}
