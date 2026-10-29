// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerPropertyPageTests
{
    private static readonly string[] GroupedNames =
    [
        "xformOpOrder", "xformOp:rotateXYZ:local", "focalLength",
        "inputs:roughness", "outputs:surface", "custom"
    ];
    private static readonly string[] UngroupedNames =
    [
        "XformOp:translate", "xformOpOrderExtra", "FocalLength", "shutter:custom",
        "inputsExtra:roughness", "Outputs:surface", "material:binding", "primvars:", "Primvars:displayColor",
        "primvarsExtra:st", "collection:", "Collection:lights:includeRoot", "Visibility", "purposeExtra",
        "physics:", "Physics:mass", "physicsExtra:mass", "physxRigidBody:linearDamping",
        "skel:", "Skel:joints", "skelExtra:joints",
        "model:", "modelExtra:drawMode"
    ];
    private static readonly string[] NamespacedApiNames =
    [
        "primvars:displayColor", "primvars:st:indices", "inputs:file", "collection:lights:expansionRule",
        "collection:lights", "custom"
    ];
    private static readonly string[] ImageableAndPhysicsNames =
    [
        "physics:mass", "visibility", "custom", "purpose", "physics:rigidBodyEnabled"
    ];
    private static readonly string[] MeshNames =
    [
        "points", "faceVertexCounts", "pointsExtra", "Points", "faceVertexIndices", "extent", "doubleSided",
        "orientation", "normals", "velocities", "accelerations", "subdivisionScheme", "interpolateBoundary",
        "faceVaryingLinearInterpolation", "triangleSubdivisionRule", "holeIndices", "cornerIndices",
        "cornerSharpnesses", "creaseIndices", "creaseLengths", "creaseSharpnesses", "primvars:normals",
        "visibility"
    ];

    [Test]
    [Arguments(ViewerPropertyGroup.All, true,
        "xformOpOrder,xformOp:rotateXYZ:local,focalLength,inputs:roughness,outputs:surface,custom",
        "material:binding,material:binding:preview,link")]
    [Arguments(ViewerPropertyGroup.Transform, true, "xformOpOrder,xformOp:rotateXYZ:local", "")]
    [Arguments(ViewerPropertyGroup.Camera, true, "focalLength", "")]
    [Arguments(ViewerPropertyGroup.Camera, false, "", "")]
    [Arguments(ViewerPropertyGroup.ShaderInputs, true, "inputs:roughness", "")]
    [Arguments(ViewerPropertyGroup.ShaderOutputs, true, "outputs:surface", "")]
    [Arguments(ViewerPropertyGroup.MaterialBindings, true, "", "material:binding,material:binding:preview")]
    [Arguments(ViewerPropertyGroup.Other, true, "custom", "link")]
    [Arguments(ViewerPropertyGroup.Other, false, "focalLength,custom", "link")]
    internal async Task GroupsPartitionTheSnapshotWithoutReorderingOrReplacingProperties(
        ViewerPropertyGroup group, bool isCamera, string attributeNames, string relationshipNames)
    {
        ViewerAttributeSnapshot[] attributes = GroupedNames.Select(static name => new ViewerAttributeSnapshot(
            name, "float", true, false, 0, "<none>", "7")).ToArray();
        ViewerRelationshipSnapshot[] relationships =
        [
            new("material:binding", "/Looks/Main"),
            new("material:binding:preview", "/Looks/Preview"),
            new("link", "/World")
        ];

        ViewerPropertyPage page = ViewerPropertyPage.Create(
            attributes, relationships, null, 99, group: group, isCamera: isCamera);

        await Assert.That(string.Join(",", page.Attributes.Select(static item => item.Name)))
            .IsEqualTo(attributeNames);
        await Assert.That(string.Join(",", page.Relationships.Select(static item => item.Name)))
            .IsEqualTo(relationshipNames);
        await Assert.That(page.TotalCount).IsEqualTo(9);
        await Assert.That(page.MatchCount).IsEqualTo(page.Count);
        await Assert.That(page.PageIndex).IsEqualTo(0);
        foreach (ViewerAttributeSnapshot attribute in page.Attributes)
        {
            await Assert.That(attribute).IsSameReferenceAs(
                attributes.Single(original => original.Name == attribute.Name));
        }
        foreach (ViewerRelationshipSnapshot relationship in page.Relationships)
        {
            await Assert.That(relationship).IsSameReferenceAs(
                relationships.Single(original => original.Name == relationship.Name));
        }
    }

    [Test]
    public async Task GroupingUsesExactNamespacesAndPropertyKindsRatherThanNameOrTypeGuesses()
    {
        ViewerAttributeSnapshot[] attributes = UngroupedNames.Select(static name => new ViewerAttributeSnapshot(
            name, "float", true, false, 0, "<none>", "7")).ToArray();
        ViewerRelationshipSnapshot[] relationships =
        [
            new("material:bindingExtra", "/Looks/Main"),
            new("Material:binding", "/Looks/Main"),
            new("inputs:roughness", "/Graph.outputs:value"),
            new("xformOp:translate", "/World"),
            new("primvars:displayColor", "/World"),
            new("collection:", "/World"),
            new("proxyPrimExtra", "/Proxy"),
            new("physics:", "/Body"),
            new("skel:", "/Skel")
        ];
        ViewerPropertyPage other = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Other, isCamera: true);
        await Assert.That(other.Attributes).IsEquivalentTo(attributes);
        await Assert.That(other.Relationships).IsEquivalentTo(relationships);
        await Assert.That(other.MatchCount).IsEqualTo(32);
        await Assert.That(() => ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: (ViewerPropertyGroup)999))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task PrimvarAndCollectionGroupsUseTheirApiNamespacesAndPropertyKinds()
    {
        ViewerAttributeSnapshot[] attributes = NamespacedApiNames
            .Select(static name => new ViewerAttributeSnapshot(name, "float", true, false, 0, "<none>", "7"))
            .ToArray();
        ViewerRelationshipSnapshot[] relationships =
        [
            new("collection:lights:includes", "/World/Key"),
            new("material:binding:collection:lights", "/Looks/Main"),
            new("collection:lights:excludes", "/World/Fill")
        ];

        ViewerPropertyPage primvars = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Primvars);
        ViewerPropertyPage collections = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Collections);

        await Assert.That(string.Join(",", primvars.Attributes.Select(static item => item.Name)))
            .IsEqualTo("primvars:displayColor,primvars:st:indices");
        await Assert.That(primvars.Relationships).IsEmpty();
        await Assert.That(string.Join(",", collections.Attributes.Select(static item => item.Name)))
            .IsEqualTo("collection:lights:expansionRule,collection:lights");
        await Assert.That(string.Join(",", collections.Relationships.Select(static item => item.Name)))
            .IsEqualTo("collection:lights:includes,collection:lights:excludes");
        ViewerPropertyPage bindings = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.MaterialBindings);
        await Assert.That(bindings.Relationships.Single()).IsSameReferenceAs(relationships[1]);
    }

    [Test]
    public async Task ImageableAndPhysicsGroupsUseExactSchemaNamesAndTheirNamespace()
    {
        ViewerAttributeSnapshot[] attributes = ImageableAndPhysicsNames
            .Select(static name => new ViewerAttributeSnapshot(name, "token", true, false, 0, "<none>", "7"))
            .ToArray();
        ViewerRelationshipSnapshot[] relationships =
        [
            new("physics:body0", "/World/A"),
            new("proxyPrim", "/World/Proxy"),
            new("physics:filteredPairs", "/World/B")
        ];

        ViewerPropertyPage imageable = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Imageable, isCamera: true, isLight: true);
        ViewerPropertyPage physics = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Physics);

        await Assert.That(string.Join(",", imageable.Attributes.Select(static item => item.Name)))
            .IsEqualTo("visibility,purpose");
        await Assert.That(imageable.Relationships.Single()).IsSameReferenceAs(relationships[1]);
        await Assert.That(string.Join(",", physics.Attributes.Select(static item => item.Name)))
            .IsEqualTo("physics:mass,physics:rigidBodyEnabled");
        await Assert.That(string.Join(",", physics.Relationships.Select(static item => item.Name)))
            .IsEqualTo("physics:body0,physics:filteredPairs");
        ViewerPropertyPage other = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Other);
        await Assert.That(other.Attributes.Single().Name).IsEqualTo("custom");
        await Assert.That(other.Relationships).IsEmpty();
    }

    [Test]
    public async Task MeshGroupRequiresAMeshAndExactSchemaNames()
    {
        ViewerAttributeSnapshot[] attributes = MeshNames
            .Select(static name => new ViewerAttributeSnapshot(name, "int[]", true, false, 0, "<none>", "[]"))
            .ToArray();
        ViewerRelationshipSnapshot[] relationships = [new("points", "/World/A")];

        ViewerPropertyPage mesh = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Mesh, geometry: ViewerGeometrySchema.Mesh);

        string[] expected = MeshNames
            .Where(static name => name is not ("pointsExtra" or "Points" or "primvars:normals" or "visibility"))
            .ToArray();
        await Assert.That(string.Join(",", mesh.Attributes.Select(static item => item.Name)))
            .IsEqualTo(string.Join(",", expected));
        await Assert.That(mesh.Relationships).IsEmpty();
        ViewerPropertyPage other = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Other, geometry: ViewerGeometrySchema.Mesh);
        await Assert.That(string.Join(",", other.Attributes.Select(static item => item.Name)))
            .IsEqualTo("pointsExtra,Points");
        await Assert.That(other.Relationships.Single()).IsSameReferenceAs(relationships[0]);
        ViewerPropertyPage nonMesh = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Mesh, isCamera: true, isLight: true);
        await Assert.That(nonMesh.MatchCount).IsEqualTo(0);
        ViewerPropertyPage nonMeshOther = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Other);
        await Assert.That(nonMeshOther.Attributes.Count).IsEqualTo(expected.Length + 2);
    }

    [Test]
    public async Task InstancerGroupRequiresAPointInstancerAndExactSchemaNames()
    {
        string[] names =
        [
            "protoIndices", "ids", "positions", "orientations", "orientationsf", "scales", "velocities",
            "accelerations", "angularVelocities", "invisibleIds", "extent", "Positions", "positionsExtra",
            "primvars:displayColor", "points"
        ];
        ViewerAttributeSnapshot[] attributes = names
            .Select(static name => new ViewerAttributeSnapshot(name, "int[]", true, false, 0, "<none>", "[]"))
            .ToArray();
        ViewerRelationshipSnapshot[] relationships =
        [
            new("prototypes", "/World/Protos/A"),
            new("Prototypes", "/World/Protos/B"),
            new("prototypesExtra", "/World/Protos/C")
        ];

        ViewerPropertyPage instancer = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Instancer,
            geometry: ViewerGeometrySchema.PointInstancer);

        await Assert.That(string.Join(",", instancer.Attributes.Select(static item => item.Name)))
            .IsEqualTo(string.Join(",", names.Take(11)));
        await Assert.That(instancer.Relationships.Single()).IsSameReferenceAs(relationships[0]);
        ViewerPropertyPage other = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Other,
            geometry: ViewerGeometrySchema.PointInstancer);
        await Assert.That(string.Join(",", other.Attributes.Select(static item => item.Name)))
            .IsEqualTo("Positions,positionsExtra,points");
        await Assert.That(string.Join(",", other.Relationships.Select(static item => item.Name)))
            .IsEqualTo("Prototypes,prototypesExtra");
        ViewerPropertyPage notInstancer = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Instancer,
            geometry: ViewerGeometrySchema.Mesh);
        await Assert.That(notInstancer.MatchCount).IsEqualTo(0);
        ViewerPropertyPage notInstancerOther = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Other);
        await Assert.That(notInstancerOther.MatchCount).IsEqualTo(names.Length - 1 + relationships.Length);
    }

    [Test]
    public async Task PointsAndCurvesGroupFollowsTheSchemaOfThePrim()
    {
        string[] names =
        [
            "points", "widths", "ids", "curveVertexCounts", "type", "basis", "wrap", "order", "knots",
            "tangents", "Widths", "widthsExtra", "primvars:widths", "visibility"
        ];
        ViewerAttributeSnapshot[] attributes = names
            .Select(static name => new ViewerAttributeSnapshot(name, "int[]", true, false, 0, "<none>", "[]"))
            .ToArray();

        await Assert.That(groupNames(ViewerGeometrySchema.Points)).IsEqualTo("points,widths,ids");
        await Assert.That(groupNames(ViewerGeometrySchema.BasisCurves))
            .IsEqualTo("points,widths,curveVertexCounts,type,basis,wrap");
        await Assert.That(groupNames(ViewerGeometrySchema.NurbsCurves))
            .IsEqualTo("points,widths,curveVertexCounts,order,knots");
        await Assert.That(groupNames(ViewerGeometrySchema.HermiteCurves))
            .IsEqualTo("points,widths,curveVertexCounts,tangents");
        await Assert.That(groupNames(ViewerGeometrySchema.Mesh)).IsEmpty();
        await Assert.That(groupNames(ViewerGeometrySchema.None)).IsEmpty();
        ViewerPropertyPage other = ViewerPropertyPage.Create(
            attributes, [], null, 0, group: ViewerPropertyGroup.Other, geometry: ViewerGeometrySchema.Points);
        await Assert.That(string.Join(",", other.Attributes.Select(static item => item.Name)))
            .IsEqualTo("curveVertexCounts,type,basis,wrap,order,knots,tangents,Widths,widthsExtra");

        string groupNames(ViewerGeometrySchema geometry) => string.Join(",", ViewerPropertyPage.Create(
                attributes, [], null, 0, group: ViewerPropertyGroup.PointsAndCurves, geometry: geometry)
            .Attributes.Select(static item => item.Name));
    }

    [Test]
    public async Task VolumeGroupCoversFieldRelationshipsAndFieldAssetAttributes()
    {
        string[] names =
        [
            "filePath", "fieldName", "fieldIndex", "fieldDataType", "vectorDataRoleHint", "fieldClass",
            "fieldPurpose", "FilePath", "filePathExtra", "visibility"
        ];
        ViewerAttributeSnapshot[] attributes = names
            .Select(static name => new ViewerAttributeSnapshot(name, "token", true, false, 0, "<none>", "a"))
            .ToArray();
        ViewerRelationshipSnapshot[] relationships =
        [
            new("field:density", "/Other"),
            new("field:", "/Other"),
            new("fieldExtra", "/Other"),
            new("material:binding", "/Other"),
        ];

        await Assert.That(attributeNames(ViewerGeometrySchema.OpenVdbAsset))
            .IsEqualTo("filePath,fieldName,fieldIndex,fieldDataType,vectorDataRoleHint,fieldClass");
        await Assert.That(attributeNames(ViewerGeometrySchema.Field3DAsset))
            .IsEqualTo("filePath,fieldName,fieldIndex,fieldDataType,vectorDataRoleHint,fieldPurpose");
        await Assert.That(attributeNames(ViewerGeometrySchema.FieldAsset))
            .IsEqualTo("filePath,fieldName,fieldIndex,fieldDataType,vectorDataRoleHint");
        await Assert.That(attributeNames(ViewerGeometrySchema.Volume)).IsEmpty();
        await Assert.That(attributeNames(ViewerGeometrySchema.None)).IsEmpty();
        await Assert.That(relationshipNames(ViewerGeometrySchema.Volume)).IsEqualTo("field:density");
        await Assert.That(relationshipNames(ViewerGeometrySchema.None)).IsEmpty();
        ViewerPropertyPage other = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Other,
            geometry: ViewerGeometrySchema.Volume);
        await Assert.That(string.Join(",", other.Relationships.Select(static item => item.Name)))
            .IsEqualTo("field:,fieldExtra");

        string attributeNames(ViewerGeometrySchema geometry) => string.Join(",", ViewerPropertyPage.Create(
                attributes, [], null, 0, group: ViewerPropertyGroup.Volume, geometry: geometry)
            .Attributes.Select(static item => item.Name));

        string relationshipNames(ViewerGeometrySchema geometry) => string.Join(",", ViewerPropertyPage.Create(
                [], relationships, null, 0, group: ViewerPropertyGroup.Volume, geometry: geometry)
            .Relationships.Select(static item => item.Name));
    }

    [Test]
    public async Task ShapeGroupFollowsTheIntrinsicSchemaOfThePrim()
    {
        string[] names =
        [
            "radius", "size", "height", "axis", "width", "length", "extent", "doubleSided", "orientation",
            "Radius", "radiusExtra", "primvars:displayColor", "visibility"
        ];
        ViewerAttributeSnapshot[] attributes = names
            .Select(static name => new ViewerAttributeSnapshot(name, "double", true, false, 0, "<none>", "1"))
            .ToArray();

        await Assert.That(groupNames(ViewerGeometrySchema.Sphere)).IsEqualTo("radius,extent,doubleSided,orientation");
        await Assert.That(groupNames(ViewerGeometrySchema.Cube)).IsEqualTo("size,extent,doubleSided,orientation");
        foreach (ViewerGeometrySchema axial in new[]
        {
            ViewerGeometrySchema.Cylinder, ViewerGeometrySchema.Cone, ViewerGeometrySchema.Capsule
        })
        {
            await Assert.That(groupNames(axial))
                .IsEqualTo("radius,height,axis,extent,doubleSided,orientation");
        }
        await Assert.That(groupNames(ViewerGeometrySchema.Plane))
            .IsEqualTo("axis,width,length,extent,doubleSided,orientation");
        await Assert.That(groupNames(ViewerGeometrySchema.Mesh)).IsEmpty();
        await Assert.That(groupNames(ViewerGeometrySchema.None)).IsEmpty();
        ViewerPropertyPage other = ViewerPropertyPage.Create(
            attributes, [], null, 0, group: ViewerPropertyGroup.Other, geometry: ViewerGeometrySchema.Sphere);
        await Assert.That(string.Join(",", other.Attributes.Select(static item => item.Name)))
            .IsEqualTo("size,height,axis,width,length,Radius,radiusExtra");

        string groupNames(ViewerGeometrySchema geometry) => string.Join(",", ViewerPropertyPage.Create(
                attributes, [], null, 0, group: ViewerPropertyGroup.Shape, geometry: geometry)
            .Attributes.Select(static item => item.Name));
    }

    [Test]
    public async Task SkinningGroupUsesTheSkelNamespaceAndLeavesSkinningPrimvarsUnderPrimvars()
    {
        string[] names =
        [
            "skel:joints", "primvars:skel:jointIndices", "skel:blendShapes", "custom", "skel:skinningMethod"
        ];
        ViewerAttributeSnapshot[] attributes = names
            .Select(static name => new ViewerAttributeSnapshot(name, "token[]", true, false, 0, "<none>", "[]"))
            .ToArray();
        ViewerRelationshipSnapshot[] relationships =
        [
            new("skel:skeleton", "/Rig/Skel"),
            new("link", "/World"),
            new("skel:blendShapeTargets", "/Rig/Smile")
        ];

        ViewerPropertyPage skinning = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Skinning,
            geometry: ViewerGeometrySchema.Mesh);

        await Assert.That(string.Join(",", skinning.Attributes.Select(static item => item.Name)))
            .IsEqualTo("skel:joints,skel:blendShapes,skel:skinningMethod");
        await Assert.That(string.Join(",", skinning.Relationships.Select(static item => item.Name)))
            .IsEqualTo("skel:skeleton,skel:blendShapeTargets");
        ViewerPropertyPage primvars = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Primvars);
        await Assert.That(primvars.Attributes.Single().Name).IsEqualTo("primvars:skel:jointIndices");
        ViewerPropertyPage other = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Other);
        await Assert.That(other.Attributes.Single().Name).IsEqualTo("custom");
        await Assert.That(other.Relationships.Single()).IsSameReferenceAs(relationships[1]);
    }

    [Test]
    public async Task SkinningGroupHoldsTheAttributesOfSkeletonAnimationAndBlendShapePrims()
    {
        string[] names =
        [
            "joints", "jointNames", "bindTransforms", "restTransforms", "translations", "rotations", "scales",
            "blendShapes", "blendShapeWeights", "offsets", "normalOffsets", "pointIndices", "inbetweens:half",
            "inbetweens:", "Joints", "extent", "visibility"
        ];
        ViewerAttributeSnapshot[] attributes = names
            .Select(static name => new ViewerAttributeSnapshot(name, "token[]", true, false, 0, "<none>", "[]"))
            .ToArray();

        await Assert.That(groupNames(ViewerGeometrySchema.Skeleton))
            .IsEqualTo("joints,jointNames,bindTransforms,restTransforms");
        await Assert.That(groupNames(ViewerGeometrySchema.SkelAnimation))
            .IsEqualTo("joints,translations,rotations,scales,blendShapes,blendShapeWeights");
        await Assert.That(groupNames(ViewerGeometrySchema.BlendShape))
            .IsEqualTo("offsets,normalOffsets,pointIndices,inbetweens:half");
        await Assert.That(groupNames(ViewerGeometrySchema.Mesh)).IsEmpty();
        ViewerPropertyPage other = ViewerPropertyPage.Create(
            attributes, [], null, 0, group: ViewerPropertyGroup.Other, geometry: ViewerGeometrySchema.Skeleton);
        await Assert.That(string.Join(",", other.Attributes.Select(static item => item.Name))).IsEqualTo(
            "translations,rotations,scales,blendShapes,blendShapeWeights,offsets,normalOffsets,pointIndices," +
            "inbetweens:half,inbetweens:,Joints,extent");

        string groupNames(ViewerGeometrySchema geometry) => string.Join(",", ViewerPropertyPage.Create(
                attributes, [], null, 0, group: ViewerPropertyGroup.Skinning, geometry: geometry)
            .Attributes.Select(static item => item.Name));
    }

    [Test]
    public async Task RenderGroupHoldsTheSchemaPropertiesOfRenderSettingsProductVarAndPassPrims()
    {
        string[] names =
        [
            "resolution", "pixelAspectRatio", "aspectRatioConformPolicy", "dataWindowNDC", "instantaneousShutter",
            "disableMotionBlur", "disableDepthOfField", "includedPurposes", "materialBindingPurposes",
            "renderingColorSpace", "productType", "productName", "dataType", "sourceName", "sourceType",
            "passType", "command", "fileName", "denoise:enable", "Resolution", "visibility"
        ];
        string[] relationshipNames = ["camera", "products", "orderedVars", "renderSource", "inputPasses", "Camera"];
        ViewerAttributeSnapshot[] attributes = names
            .Select(static name => new ViewerAttributeSnapshot(name, "token", true, false, 0, "<none>", "raw"))
            .ToArray();
        ViewerRelationshipSnapshot[] relationships = relationshipNames
            .Select(static name => new ViewerRelationshipSnapshot(name, "/Render/Target"))
            .ToArray();
        const string settingsBase =
            "resolution,pixelAspectRatio,aspectRatioConformPolicy,dataWindowNDC,instantaneousShutter," +
            "disableMotionBlur,disableDepthOfField";

        await Assert.That(groupNames(ViewerGeometrySchema.RenderSettings)).IsEqualTo(
            settingsBase + ",includedPurposes,materialBindingPurposes,renderingColorSpace|camera,products");
        await Assert.That(groupNames(ViewerGeometrySchema.RenderProduct))
            .IsEqualTo(settingsBase + ",productType,productName|camera,orderedVars");
        await Assert.That(groupNames(ViewerGeometrySchema.RenderVar)).IsEqualTo("dataType,sourceName,sourceType|");
        await Assert.That(groupNames(ViewerGeometrySchema.RenderPass))
            .IsEqualTo("passType,command,fileName,denoise:enable|renderSource,inputPasses");
        await Assert.That(groupNames(ViewerGeometrySchema.Mesh)).IsEqualTo("|");
        ViewerPropertyPage other = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Other,
            geometry: ViewerGeometrySchema.RenderVar);
        await Assert.That(other.Attributes.Select(static item => item.Name)).Contains("resolution");
        await Assert.That(other.Attributes.Select(static item => item.Name)).DoesNotContain("sourceName");
        await Assert.That(other.Relationships.Select(static item => item.Name)).Contains("camera");

        string groupNames(ViewerGeometrySchema geometry)
        {
            ViewerPropertyPage page = ViewerPropertyPage.Create(
                attributes, relationships, null, 0, group: ViewerPropertyGroup.Render, geometry: geometry);
            return string.Join(",", page.Attributes.Select(static item => item.Name)) + "|" +
                string.Join(",", page.Relationships.Select(static item => item.Name));
        }
    }

    [Test]
    public async Task MeshGroupHoldsTheSchemaAttributesOfTetMeshNurbsPatchAndGeomSubsetPrims()
    {
        string[] names =
        [
            "points", "extent", "tetVertexIndices", "surfaceFaceVertexIndices", "uVertexCount", "vVertexCount",
            "uOrder", "vOrder", "uKnots", "vKnots", "uForm", "vForm", "uRange", "vRange", "pointWeights",
            "trimCurve:counts", "trimCurve:orders", "trimCurve:vertexCounts", "trimCurve:knots",
            "trimCurve:ranges", "trimCurve:points", "elementType", "indices", "familyName", "faceVertexCounts",
            "Indices", "trimCurve:extra", "visibility"
        ];
        ViewerAttributeSnapshot[] attributes = names
            .Select(static name => new ViewerAttributeSnapshot(name, "int[]", true, false, 0, "<none>", "[]"))
            .ToArray();
        ViewerRelationshipSnapshot[] relationships = [new("indices", "/World/A")];

        await Assert.That(groupNames(ViewerGeometrySchema.TetMesh))
            .IsEqualTo("points,extent,tetVertexIndices,surfaceFaceVertexIndices");
        await Assert.That(groupNames(ViewerGeometrySchema.NurbsPatch)).IsEqualTo(
            "points,extent,uVertexCount,vVertexCount,uOrder,vOrder,uKnots,vKnots,uForm,vForm,uRange,vRange," +
            "pointWeights,trimCurve:counts,trimCurve:orders,trimCurve:vertexCounts,trimCurve:knots," +
            "trimCurve:ranges,trimCurve:points");
        await Assert.That(groupNames(ViewerGeometrySchema.GeomSubset)).IsEqualTo("elementType,indices,familyName");
        await Assert.That(groupNames(ViewerGeometrySchema.None)).IsEmpty();
        ViewerPropertyPage subset = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Mesh,
            geometry: ViewerGeometrySchema.GeomSubset);
        await Assert.That(subset.Relationships).IsEmpty();
        ViewerPropertyPage other = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Other,
            geometry: ViewerGeometrySchema.GeomSubset);
        await Assert.That(other.Attributes.Select(static item => item.Name)).Contains("Indices");
        await Assert.That(other.Attributes.Select(static item => item.Name)).DoesNotContain("familyName");
        await Assert.That(other.Relationships.Single()).IsSameReferenceAs(relationships[0]);

        string groupNames(ViewerGeometrySchema geometry) => string.Join(
            ",",
            ViewerPropertyPage.Create(
                    attributes, relationships, null, 0, group: ViewerPropertyGroup.Mesh, geometry: geometry)
                .Attributes.Select(static item => item.Name));
    }

    [Test]
    public async Task ShaderIdentityGroupUsesTheInfoNamespaceAndLeavesShaderInputsUnderInputs()
    {
        string[] names =
        [
            "info:id", "info:implementationSource", "info:sourceAsset", "info:glslfx:sourceAsset",
            "info:sourceCode", "info:", "infos:id", "inputs:info:id", "inputs:roughness", "outputs:surface"
        ];
        ViewerAttributeSnapshot[] attributes = names
            .Select(static name => new ViewerAttributeSnapshot(name, "token", true, false, 0, "<none>", "raw"))
            .ToArray();

        ViewerPropertyPage identity = ViewerPropertyPage.Create(
            attributes, [], null, 0, group: ViewerPropertyGroup.ShaderIdentity);
        ViewerPropertyPage other = ViewerPropertyPage.Create(
            attributes, [], null, 0, group: ViewerPropertyGroup.Other);
        ViewerPropertyPage inputs = ViewerPropertyPage.Create(
            attributes, [], null, 0, group: ViewerPropertyGroup.ShaderInputs);

        await Assert.That(string.Join(",", identity.Attributes.Select(static item => item.Name))).IsEqualTo(
            "info:id,info:implementationSource,info:sourceAsset,info:glslfx:sourceAsset,info:sourceCode");
        await Assert.That(string.Join(",", other.Attributes.Select(static item => item.Name)))
            .IsEqualTo("info:,infos:id");
        await Assert.That(string.Join(",", inputs.Attributes.Select(static item => item.Name)))
            .IsEqualTo("inputs:info:id,inputs:roughness");
    }

    [Test]
    public async Task UserInterfaceGroupUsesTheUiNamespaceAndLeavesPrimvarsAndInputsInPlace()
    {
        string[] names =
        [
            "ui:nodegraph:node:pos", "ui:nodegraph:node:size", "ui:displayName", "ui:displayGroup",
            "ui:description", "ui:", "uix:pos", "inputs:ui:hint", "primvars:ui:tag", "info:id"
        ];
        ViewerAttributeSnapshot[] attributes = names
            .Select(static name => new ViewerAttributeSnapshot(name, "token", true, false, 0, "<none>", "raw"))
            .ToArray();

        ViewerPropertyPage ui = ViewerPropertyPage.Create(
            attributes, [], null, 0, group: ViewerPropertyGroup.UserInterface);
        ViewerPropertyPage other = ViewerPropertyPage.Create(
            attributes, [], null, 0, group: ViewerPropertyGroup.Other);
        ViewerPropertyPage inputs = ViewerPropertyPage.Create(
            attributes, [], null, 0, group: ViewerPropertyGroup.ShaderInputs);
        ViewerPropertyPage primvars = ViewerPropertyPage.Create(
            attributes, [], null, 0, group: ViewerPropertyGroup.Primvars);

        await Assert.That(string.Join(",", ui.Attributes.Select(static item => item.Name))).IsEqualTo(
            "ui:nodegraph:node:pos,ui:nodegraph:node:size,ui:displayName,ui:displayGroup,ui:description");
        await Assert.That(string.Join(",", other.Attributes.Select(static item => item.Name)))
            .IsEqualTo("ui:,uix:pos");
        await Assert.That(string.Join(",", inputs.Attributes.Select(static item => item.Name)))
            .IsEqualTo("inputs:ui:hint");
        await Assert.That(string.Join(",", primvars.Attributes.Select(static item => item.Name)))
            .IsEqualTo("primvars:ui:tag");
    }

    [Test]
    public async Task SemanticLabelAndLevelOfDetailGroupsUseTheirNamespacesAndAccessibilityJoinsUserInterface()
    {
        string[] names =
        [
            "semantics:labels:class", "semantics:labels:wordnet", "lod:default:index", "lod:override:mode",
            "lod:domain", "accessibility:default:label", "accessibility:alt:priority", "semantics:", "lod:",
            "lods:index", "primvars:semantics:labels:class", "thresholds"
        ];
        ViewerAttributeSnapshot[] attributes = names
            .Select(static name => new ViewerAttributeSnapshot(name, "token", true, false, 0, "<none>", "raw"))
            .ToArray();
        ViewerRelationshipSnapshot[] relationships =
            [new ViewerRelationshipSnapshot("lod:heuristics", "/Heuristic"),
            new ViewerRelationshipSnapshot("boundingVolume", "/Bounds")];

        static string joined(ViewerPropertyPage page) => string.Join(",",
            page.Attributes.Select(static item => item.Name).Concat(
                page.Relationships.Select(static item => item.Name)));

        await Assert.That(joined(ViewerPropertyPage.Create(
                attributes, relationships, null, 0, group: ViewerPropertyGroup.Semantics)))
            .IsEqualTo("semantics:labels:class,semantics:labels:wordnet");
        await Assert.That(joined(ViewerPropertyPage.Create(
                attributes, relationships, null, 0, group: ViewerPropertyGroup.LevelOfDetail)))
            .IsEqualTo("lod:default:index,lod:override:mode,lod:domain,lod:heuristics");
        await Assert.That(joined(ViewerPropertyPage.Create(
                attributes, relationships, null, 0, group: ViewerPropertyGroup.UserInterface)))
            .IsEqualTo("accessibility:default:label,accessibility:alt:priority");
        await Assert.That(joined(ViewerPropertyPage.Create(
                attributes, relationships, null, 0, group: ViewerPropertyGroup.Other)))
            .IsEqualTo("semantics:,lod:,lods:index,thresholds,boundingVolume");
        await Assert.That(joined(ViewerPropertyPage.Create(
                attributes, relationships, null, 0, group: ViewerPropertyGroup.Primvars)))
            .IsEqualTo("primvars:semantics:labels:class");
    }

    [Test]
    public async Task MotionAndCoordinateSystemGroupsUseTheirNamespaces()
    {
        string[] names =
        [
            "motion:blurScale", "motion:velocityScale", "motion:nonlinearSampleCount", "motion:",
            "motions:blurScale", "primvars:motion:blurScale", "velocities"
        ];
        ViewerAttributeSnapshot[] attributes = names
            .Select(static name => new ViewerAttributeSnapshot(name, "float", true, false, 0, "<none>", "raw"))
            .ToArray();
        ViewerRelationshipSnapshot[] relationships =
            [new ViewerRelationshipSnapshot("coordSys:worldSpace:binding", "/World/Space"),
            new ViewerRelationshipSnapshot("coordSys:", "/World/Space"),
            new ViewerRelationshipSnapshot("coordSystem:binding", "/World/Space")];

        static string joined(ViewerPropertyPage page) => string.Join(",",
            page.Attributes.Select(static item => item.Name).Concat(
                page.Relationships.Select(static item => item.Name)));

        await Assert.That(joined(ViewerPropertyPage.Create(
                attributes, relationships, null, 0, group: ViewerPropertyGroup.Motion)))
            .IsEqualTo("motion:blurScale,motion:velocityScale,motion:nonlinearSampleCount");
        await Assert.That(joined(ViewerPropertyPage.Create(
                attributes, relationships, null, 0, group: ViewerPropertyGroup.CoordinateSystems)))
            .IsEqualTo("coordSys:worldSpace:binding");
        await Assert.That(joined(ViewerPropertyPage.Create(
                attributes, relationships, null, 0, group: ViewerPropertyGroup.Other)))
            .IsEqualTo("motion:,motions:blurScale,velocities,coordSys:,coordSystem:binding");
    }

    [Test]
    public async Task PhysicsDrivesAndLimitsColorSpacesAndCameraBackPlatesJoinTheirGroups()
    {
        string[] names =
        [
            "drive:rotX:physics:stiffness", "limit:transX:physics:low", "drive:physics:stiffness",
            "drive:rotX:physx:stiffness", "drives:rotX:physics:stiffness", "colorSpace:name",
            "colorSpaceDefinition:acescg:redChroma", "colorSpace:", "backPlate:main:image",
            "backPlate:main:luma:gain"
        ];
        ViewerAttributeSnapshot[] attributes = names
            .Select(static name => new ViewerAttributeSnapshot(name, "float", true, false, 0, "<none>", "raw"))
            .ToArray();

        static string joined(ViewerPropertyPage page) =>
            string.Join(",", page.Attributes.Select(static item => item.Name));

        await Assert.That(joined(ViewerPropertyPage.Create(
                attributes, [], null, 0, group: ViewerPropertyGroup.Physics)))
            .IsEqualTo("drive:rotX:physics:stiffness,limit:transX:physics:low");
        await Assert.That(joined(ViewerPropertyPage.Create(
                attributes, [], null, 0, group: ViewerPropertyGroup.ColorSpace)))
            .IsEqualTo("colorSpace:name,colorSpaceDefinition:acescg:redChroma");
        await Assert.That(joined(ViewerPropertyPage.Create(
                attributes, [], null, 0, group: ViewerPropertyGroup.Camera, isCamera: true)))
            .IsEqualTo("backPlate:main:image,backPlate:main:luma:gain");
        await Assert.That(joined(ViewerPropertyPage.Create(
                attributes, [], null, 0, group: ViewerPropertyGroup.Other)))
            .IsEqualTo("drive:physics:stiffness,drive:rotX:physx:stiffness,drives:rotX:physics:stiffness," +
                "colorSpace:,backPlate:main:image,backPlate:main:luma:gain");
    }

    [Test]
    public async Task ParticleFieldsJoinVolumeAndLodHeuristicsJoinLevelOfDetailOnlyOnTheirSchemas()
    {
        string[] names =
        [
            "positions", "orientationsh", "scales", "opacities", "radiance:sphericalHarmonicsDegree",
            "sortingModeHint", "extent", "thresholds", "blendThresholds", "center", "projectionMethod", "radiance:"
        ];
        ViewerAttributeSnapshot[] attributes = names
            .Select(static name => new ViewerAttributeSnapshot(name, "float[]", true, false, 0, "<none>", "[]"))
            .ToArray();
        ViewerRelationshipSnapshot[] relationships = [new("boundingVolume", "/World/Bounds")];

        static string joined(ViewerPropertyPage page) =>
            string.Join(",", page.Attributes.Select(static item => item.Name).Concat(
                page.Relationships.Select(static item => item.Name)));

        await Assert.That(joined(ViewerPropertyPage.Create(attributes, relationships, null, 0,
                group: ViewerPropertyGroup.Volume, geometry: ViewerGeometrySchema.ParticleField)))
            .IsEqualTo("positions,orientationsh,scales,opacities,radiance:sphericalHarmonicsDegree," +
                "sortingModeHint,extent");
        await Assert.That(joined(ViewerPropertyPage.Create(attributes, relationships, null, 0,
                group: ViewerPropertyGroup.LevelOfDetail, geometry: ViewerGeometrySchema.LodHeuristic)))
            .IsEqualTo("extent,thresholds,blendThresholds,center,projectionMethod,boundingVolume");
        await Assert.That(joined(ViewerPropertyPage.Create(attributes, relationships, null, 0,
                group: ViewerPropertyGroup.Other, geometry: ViewerGeometrySchema.None)))
            .IsEqualTo(string.Join(",", names) + ",boundingVolume");
    }

    [Test]
    public async Task ModelGroupUsesTheModelNamespaceAndLeavesModelPrimvarsUnderPrimvars()
    {
        string[] names =
        [
            "model:drawMode", "primvars:model:tint", "model:cardGeometry", "custom", "model:drawModeColor",
            "Model:drawMode"
        ];
        ViewerAttributeSnapshot[] attributes = names
            .Select(static name => new ViewerAttributeSnapshot(name, "token", true, false, 0, "<none>", "cards"))
            .ToArray();
        ViewerRelationshipSnapshot[] relationships =
        [
            new("model:customTarget", "/Cards"),
            new("link", "/World")
        ];

        ViewerPropertyPage model = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Model, geometry: ViewerGeometrySchema.Mesh);

        await Assert.That(string.Join(",", model.Attributes.Select(static item => item.Name)))
            .IsEqualTo("model:drawMode,model:cardGeometry,model:drawModeColor");
        await Assert.That(model.Relationships.Single()).IsSameReferenceAs(relationships[0]);
        ViewerPropertyPage primvars = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Primvars);
        await Assert.That(primvars.Attributes.Single().Name).IsEqualTo("primvars:model:tint");
        ViewerPropertyPage other = ViewerPropertyPage.Create(
            attributes, relationships, null, 0, group: ViewerPropertyGroup.Other);
        await Assert.That(string.Join(",", other.Attributes.Select(static item => item.Name)))
            .IsEqualTo("custom,Model:drawMode");
        await Assert.That(other.Relationships.Single()).IsSameReferenceAs(relationships[1]);
    }

    [Test]
    [Arguments("exposure")]
    [Arguments("exposure:iso")]
    [Arguments("exposure:time")]
    [Arguments("exposure:fStop")]
    [Arguments("exposure:responsivity")]
    public async Task CameraExposureGroupingRequiresExactNamesAndACamera(string name)
    {
        var exposure = new ViewerAttributeSnapshot(name, "float", false, false, 0, "<none>", "0");
        var lookalike = new ViewerAttributeSnapshot($"{name}:extra", "float", true, false, 0, "<none>", "7");
        ViewerAttributeSnapshot[] attributes = [exposure, lookalike];
        ViewerPropertyPage camera = ViewerPropertyPage.Create(
            attributes, [], null, 0, group: ViewerPropertyGroup.Camera, isCamera: true);
        await Assert.That(camera.Attributes.Count).IsEqualTo(1);
        await Assert.That(camera.Attributes.Single()).IsSameReferenceAs(exposure);
        ViewerPropertyPage other = ViewerPropertyPage.Create(
            attributes, [], null, 0, group: ViewerPropertyGroup.Other, isCamera: true);
        await Assert.That(other.Attributes.Count).IsEqualTo(1);
        await Assert.That(other.Attributes.Single()).IsSameReferenceAs(lookalike);
        ViewerPropertyPage nonCamera = ViewerPropertyPage.Create(
            attributes, [], null, 0, group: ViewerPropertyGroup.Camera, isCamera: false);
        await Assert.That(nonCamera.Attributes).IsEmpty();
        await Assert.That(nonCamera.MatchCount).IsEqualTo(0);
    }

    [Test]
    public async Task CommonLightControlsHaveAnExplicitSchemaGroupWithoutHidingOtherShaderInputs()
    {
        string[] attributeNames =
        [
            "inputs:intensity",
            "inputs:exposure",
            "inputs:diffuse",
            "inputs:specular",
            "inputs:colorTemperature",
            "inputs:enableColorTemperature",
            "inputs:normalize",
            "inputs:color",
            "inputs:width",
            "inputs:height",
            "inputs:radius",
            "inputs:length",
            "inputs:angle",
            "inputs:shaping:focus",
            "inputs:shaping:cone:angle",
            "inputs:shaping:cone:softness",
            "inputs:shapeSpecific",
            "inputs:intensityExtra",
            "inputs:color:red"
        ];
        ViewerAttributeSnapshot[] attributes = attributeNames
            .Select(static name => new ViewerAttributeSnapshot(name, "float", true, false, 0, "<none>", "7"))
            .ToArray();

        ViewerPropertyPage lightControls = ViewerPropertyPage.Create(
            attributes, [], null, 0, group: ViewerPropertyGroup.Light, isLight: true);
        await Assert.That(string.Join(",", lightControls.Attributes.Select(static item => item.Name)))
            .IsEqualTo(
                "inputs:intensity,inputs:exposure,inputs:diffuse,inputs:specular," +
                "inputs:colorTemperature,inputs:enableColorTemperature,inputs:normalize,inputs:color");

        ViewerPropertyPage shaderInputs = ViewerPropertyPage.Create(
            attributes, [], null, 0, group: ViewerPropertyGroup.ShaderInputs, isLight: true);
        await Assert.That(string.Join(",", shaderInputs.Attributes.Select(static item => item.Name)))
            .IsEqualTo(string.Join(",", attributeNames.Where(
                static name => name.StartsWith("inputs:", StringComparison.Ordinal))));

        ViewerPropertyPage nonLightInputs = ViewerPropertyPage.Create(
            attributes, [], null, 0, group: ViewerPropertyGroup.ShaderInputs);
        await Assert.That(nonLightInputs.Attributes).IsEquivalentTo(attributes.Where(
            static item => item.Name.StartsWith("inputs:", StringComparison.Ordinal)));

        ViewerPropertyPage nonLightControls = ViewerPropertyPage.Create(
            attributes, [], null, 0, group: ViewerPropertyGroup.Light);
        await Assert.That(nonLightControls.Attributes).IsEmpty();
    }

    [Test]
    [Arguments("inputs:width", true)]
    [Arguments("inputs:height", true)]
    [Arguments("inputs:radius", true)]
    [Arguments("inputs:length", true)]
    [Arguments("inputs:angle", true)]
    [Arguments("inputs:shaping:focus", true)]
    [Arguments("inputs:shaping:cone:angle", true)]
    [Arguments("inputs:shaping:cone:softness", true)]
    [Arguments("inputs:width", false)]
    [Arguments("inputs:height", false)]
    [Arguments("inputs:radius", false)]
    [Arguments("inputs:length", false)]
    [Arguments("inputs:angle", false)]
    [Arguments("inputs:shaping:focus", false)]
    [Arguments("inputs:shaping:cone:angle", false)]
    [Arguments("inputs:shaping:cone:softness", false)]
    public async Task ShapeAndShapingGroupingRequiresValidatedMembershipAndPreservesGenericAccess(
        string name, bool isLight)
    {
        var supported = new ViewerAttributeSnapshot(name, "float", true, false, 1, "5", "2");
        var unproven = new ViewerAttributeSnapshot("inputs:unsupportedShape", "float", true, false, 1, "5", "3");
        ViewerAttributeSnapshot[] attributes =
        [
            supported, unproven,
            new(name + ":extra", "float", true, false, 1, "5", "4"),
            new(name.ToUpperInvariant(), "float", true, false, 1, "5", "5")
        ];
        var relationship = new ViewerRelationshipSnapshot(name, "/World");
        ViewerPropertyPage validated = ViewerPropertyPage.Create(
            attributes, [relationship], null, int.MaxValue,
            ViewerPropertyFilter.TimeSampledAttributes, ViewerPropertyGroup.Light,
            isLight: isLight, lightSchemaProperties: [name]);
        await Assert.That(validated.MatchCount).IsEqualTo(isLight ? 1 : 0);
        await Assert.That(validated.PageIndex).IsEqualTo(0);
        await Assert.That(validated.Relationships).IsEmpty();
        if (isLight)
        {
            await Assert.That(validated.Attributes.Single()).IsSameReferenceAs(supported);
        }
        else
        {
            await Assert.That(validated.Attributes).IsEmpty();
        }
        ViewerPropertyPage unvalidated = ViewerPropertyPage.Create(
            attributes, [relationship], null, 0, group: ViewerPropertyGroup.Light, isLight: isLight);
        await Assert.That(unvalidated.Attributes).IsEmpty();
        ViewerPropertyPage shaderInputs = ViewerPropertyPage.Create(
            attributes, [relationship], null, 0, group: ViewerPropertyGroup.ShaderInputs,
            isLight: isLight, lightSchemaProperties: [name]);
        await Assert.That(shaderInputs.Attributes).IsEquivalentTo(attributes.Take(3));
        await Assert.That(shaderInputs.Relationships).IsEmpty();
        await Assert.That(supported.Value).IsEqualTo("2");
        await Assert.That(supported.TimeSampleCount).IsEqualTo(1);
    }

    [Test]
    [Arguments("inputs:width")]
    [Arguments("inputs:shaping:focus")]
    [Arguments("inputs:texture:file")]
    [Arguments("inputs:texture:format")]
    public async Task RemovingSchemaMembershipReclassifiesWithoutReplacingOrChangingTheProperty(string name)
    {
        var property = new ViewerAttributeSnapshot(name, "float", true, false, 1, "5", "2");
        ViewerPropertyPage before = ViewerPropertyPage.Create(
            [property], [], null, 0, group: ViewerPropertyGroup.Light,
            isLight: true, lightSchemaProperties: [name]);
        ViewerPropertyPage after = ViewerPropertyPage.Create(
            [property], [], null, 0, group: ViewerPropertyGroup.Light,
            isLight: true, lightSchemaProperties: []);
        ViewerPropertyPage generic = ViewerPropertyPage.Create(
            [property], [], null, 0, group: ViewerPropertyGroup.ShaderInputs,
            isLight: true, lightSchemaProperties: []);
        await Assert.That(before.Attributes.Single()).IsSameReferenceAs(property);
        await Assert.That(after.Attributes).IsEmpty();
        await Assert.That(after.TotalCount).IsEqualTo(1);
        await Assert.That(generic.Attributes.Single()).IsSameReferenceAs(property);
        await Assert.That(before.Attributes.Single().Value).IsEqualTo("2");
        await Assert.That(property.TimeSampleCount).IsEqualTo(1);
    }

    [Test]
    [Arguments("inputs:texture:file", "asset", true)]
    [Arguments("inputs:texture:file", "asset", false)]
    [Arguments("inputs:texture:format", "token", true)]
    [Arguments("inputs:texture:format", "token", false)]
    public async Task LightTextureGroupingPreservesExactPropertiesAndGenericShaderAccess(
        string name, string typeName, bool isLight)
    {
        var texture = new ViewerAttributeSnapshot(name, typeName, true, false, 1, "5", "@original.hdr@");
        ViewerAttributeSnapshot[] attributes =
        [
            texture,
            new($"{name}:extra", typeName, true, false, 0, "<none>", "lookalike"),
            new(name.ToUpperInvariant(), typeName, true, false, 0, "<none>", "lookalike"),
            new("inputs:roughness", "float", true, false, 0, "<none>", "0.5")
        ];
        var relationship = new ViewerRelationshipSnapshot(name, "/World/Texture");
        ViewerPropertyPage lights = ViewerPropertyPage.Create(
            attributes, [relationship], "texture", int.MaxValue,
            ViewerPropertyFilter.TimeSampledAttributes, ViewerPropertyGroup.Light,
            isLight: isLight, lightSchemaProperties: [name]);
        await Assert.That(lights.TotalCount).IsEqualTo(5);
        await Assert.That(lights.MatchCount).IsEqualTo(isLight ? 1 : 0);
        await Assert.That(lights.PageIndex).IsEqualTo(0);
        await Assert.That(lights.Relationships).IsEmpty();
        if (isLight)
        {
            await Assert.That(lights.Attributes.Single()).IsSameReferenceAs(texture);
        }
        else
        {
            await Assert.That(lights.Attributes).IsEmpty();
        }
        ViewerPropertyPage unfiltered = ViewerPropertyPage.Create(
            attributes, [relationship], null, 0, group: ViewerPropertyGroup.Light, isLight: isLight);
        await Assert.That(unfiltered.MatchCount).IsEqualTo(0);
        await Assert.That(unfiltered.Relationships).IsEmpty();
        ViewerPropertyPage shaderInputs = ViewerPropertyPage.Create(
            attributes, [relationship], null, 0, group: ViewerPropertyGroup.ShaderInputs, isLight: isLight);
        await Assert.That(shaderInputs.MatchCount).IsEqualTo(3);
        await Assert.That(shaderInputs.Attributes[0]).IsSameReferenceAs(texture);
        await Assert.That(shaderInputs.Attributes[1]).IsSameReferenceAs(attributes[1]);
        await Assert.That(shaderInputs.Attributes[2]).IsSameReferenceAs(attributes[3]);
        await Assert.That(shaderInputs.Relationships).IsEmpty();
        ViewerPropertyPage other = ViewerPropertyPage.Create(
            attributes, [relationship], null, 0, group: ViewerPropertyGroup.Other, isLight: isLight);
        await Assert.That(other.Attributes.Single()).IsSameReferenceAs(attributes[2]);
        await Assert.That(other.Relationships.Single()).IsSameReferenceAs(relationship);
        await Assert.That(texture.TimeSampleCount).IsEqualTo(1);
        await Assert.That(texture.Value).IsEqualTo("@original.hdr@");
    }

    [Test]
    public async Task GroupStateAndSearchIntersectBeforeClampingAndBoundedPaging()
    {
        ViewerAttributeSnapshot[] attributes = Enumerable.Range(0, 200)
            .Select(index => new ViewerAttributeSnapshot(
                index < 100 ? $"inputs:weight{index:D3}" : $"other:weight{index:D3}",
                "double", index % 2 == 0, false, 0, "<none>", "7")).ToArray();

        ViewerPropertyPage page = ViewerPropertyPage.Create(
            attributes, [new("inputs:link", "/World")], "WEIGHT", int.MaxValue,
            ViewerPropertyFilter.AuthoredAttributes, ViewerPropertyGroup.ShaderInputs);

        await Assert.That(page.TotalCount).IsEqualTo(201);
        await Assert.That(page.MatchCount).IsEqualTo(50);
        await Assert.That(page.PageIndex).IsEqualTo(1);
        await Assert.That(page.Count).IsEqualTo(18);
        await Assert.That(page.Attributes[0].Name).IsEqualTo("inputs:weight064");
        await Assert.That(page.Attributes[^1].Name).IsEqualTo("inputs:weight098");
        await Assert.That(page.Relationships).IsEmpty();
        await Assert.That(page.HasPrevious).IsTrue();
        await Assert.That(page.HasNext).IsFalse();
        ViewerPropertyPage empty = ViewerPropertyPage.Create(
            attributes, [], null, 9, group: ViewerPropertyGroup.Camera);
        await Assert.That(empty.MatchCount).IsEqualTo(0);
        await Assert.That(empty.PageIndex).IsEqualTo(0);
        await Assert.That(empty.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(ViewerPropertyFilter.All, "authored,blocked,animated,fallback,unknown", "link")]
    [Arguments(ViewerPropertyFilter.AuthoredAttributes, "authored,blocked,animated", "")]
    [Arguments(ViewerPropertyFilter.UnauthoredAttributes, "fallback", "")]
    [Arguments(ViewerPropertyFilter.BlockedAttributes, "blocked", "")]
    [Arguments(ViewerPropertyFilter.TimeSampledAttributes, "animated", "")]
    [Arguments(ViewerPropertyFilter.Relationships, "", "link")]
    [Arguments(ViewerPropertyFilter.UnknownAuthoredState, "unknown", "")]
    internal async Task StateFiltersPreserveUnknownMetadataAndExactSnapshotIdentity(
        ViewerPropertyFilter filter, string attributeNames, string relationshipNames)
    {
        ViewerAttributeSnapshot[] attributes =
        [
            new("authored", "double", true, false, 0, "<none>", "7"),
            new("blocked", "double", true, true, 0, "<none>", "<blocked>"),
            new("animated", "double", true, false, 2, "0, 1", "5"),
            new("fallback", "token", false, false, 0, "<none>", "inherited"),
            new("unknown", "double", null, false, null, "<unproven>", "<deferred>")
        ];
        ViewerRelationshipSnapshot relationship = new("link", "/World");

        ViewerPropertyPage page = ViewerPropertyPage.Create(attributes, [relationship], null, 99, filter);

        await Assert.That(string.Join(",", page.Attributes.Select(static value => value.Name)))
            .IsEqualTo(attributeNames);
        await Assert.That(string.Join(",", page.Relationships.Select(static value => value.Name)))
            .IsEqualTo(relationshipNames);
        await Assert.That(page.TotalCount).IsEqualTo(6);
        await Assert.That(page.MatchCount).IsEqualTo(page.Count);
        await Assert.That(page.PageIndex).IsEqualTo(0);
        foreach (ViewerAttributeSnapshot attribute in page.Attributes)
        {
            await Assert.That(attribute).IsSameReferenceAs(
                attributes.Single(original => original.Name == attribute.Name));
        }
        if (page.Relationships.Count != 0)
        {
            await Assert.That(page.Relationships.Single()).IsSameReferenceAs(relationship);
        }
        await Assert.That(attributes[^1].HasAuthoredValue).IsNull();
        await Assert.That(attributes[^1].TimeSampleCount).IsNull();
    }

    [Test]
    public async Task StateAndTextFiltersComposeBeforePagingAndRejectInvalidCategories()
    {
        ViewerAttributeSnapshot[] attributes = Enumerable.Range(0, 100)
            .Select(index => new ViewerAttributeSnapshot(
                $"value{index:D3}", "double", index % 2 == 0, false, 0, "<none>", "7"))
            .ToArray();
        ViewerPropertyPage page = ViewerPropertyPage.Create(
            attributes, [new("valueLink", "/World")], " DOUBLE ", 1, ViewerPropertyFilter.AuthoredAttributes);
        await Assert.That(page.TotalCount).IsEqualTo(101);
        await Assert.That(page.MatchCount).IsEqualTo(50);
        await Assert.That(page.StartIndex).IsEqualTo(32);
        await Assert.That(page.Attributes.Count).IsEqualTo(18);
        await Assert.That(page.Attributes[0].Name).IsEqualTo("value064");
        await Assert.That(page.Attributes[^1].Name).IsEqualTo("value098");
        await Assert.That(page.Relationships).IsEmpty();
        await Assert.That(page.HasPrevious).IsTrue();
        await Assert.That(page.HasNext).IsFalse();
        ViewerPropertyPage empty = ViewerPropertyPage.Create(
            attributes, [], "missing", 1, ViewerPropertyFilter.AuthoredAttributes);
        await Assert.That(empty.MatchCount).IsEqualTo(0);
        await Assert.That(empty.PageIndex).IsEqualTo(0);
        await Assert.That(empty.Count).IsEqualTo(0);
        await Assert.That(() => ViewerPropertyPage.Create(attributes, [], null, 0, (ViewerPropertyFilter)999))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(ViewerPropertyFilter.All)]
    [Arguments(ViewerPropertyFilter.AuthoredAttributes)]
    [Arguments(ViewerPropertyFilter.UnauthoredAttributes)]
    [Arguments(ViewerPropertyFilter.BlockedAttributes)]
    [Arguments(ViewerPropertyFilter.TimeSampledAttributes)]
    [Arguments(ViewerPropertyFilter.Relationships)]
    [Arguments(ViewerPropertyFilter.UnknownAuthoredState)]
    internal async Task EveryStateFilterHandlesAnEmptyPrimWithoutPhantomResults(ViewerPropertyFilter filter)
    {
        ViewerPropertyPage page = ViewerPropertyPage.Create([], [], null, int.MaxValue, filter);
        await Assert.That(page.TotalCount).IsEqualTo(0);
        await Assert.That(page.MatchCount).IsEqualTo(0);
        await Assert.That(page.Count).IsEqualTo(0);
        await Assert.That(page.PageIndex).IsEqualTo(0);
        await Assert.That(page.HasPrevious).IsFalse();
        await Assert.That(page.HasNext).IsFalse();
    }

    [Test]
    public async Task EveryAttributeAndRelationshipIsReachableWithoutMaterializingAnUnboundedPage()
    {
        ViewerAttributeSnapshot[] attributes = Enumerable.Range(0, 65)
            .Select(index => new ViewerAttributeSnapshot(
                $"value{index:D2}", "double", true, false, 0, "<none>", "7"))
            .ToArray();
        ViewerRelationshipSnapshot[] relationships =
        [
            new("first", "/World/A"),
            new("second", "/World/B"),
            new("third", "/World/C"),
            new("fourth", "/World/D")
        ];

        ViewerPropertyPage first = ViewerPropertyPage.Create(attributes, relationships, null, 0);
        ViewerPropertyPage middle = ViewerPropertyPage.Create(attributes, relationships, null, 1);
        ViewerPropertyPage last = ViewerPropertyPage.Create(attributes, relationships, null, 2);

        await Assert.That(first.TotalCount).IsEqualTo(69);
        await Assert.That(first.MatchCount).IsEqualTo(69);
        await Assert.That(first.Attributes.Count).IsEqualTo(32);
        await Assert.That(first.Relationships.Count).IsEqualTo(0);
        await Assert.That(first.Attributes[0].Name).IsEqualTo("value00");
        await Assert.That(first.Attributes[^1].Name).IsEqualTo("value31");
        await Assert.That(first.HasPrevious).IsFalse();
        await Assert.That(first.HasNext).IsTrue();
        await Assert.That(middle.Attributes[0].Name).IsEqualTo("value32");
        await Assert.That(middle.Attributes[^1].Name).IsEqualTo("value63");
        await Assert.That(middle.StartIndex).IsEqualTo(32);
        await Assert.That(last.Attributes.Single().Name).IsEqualTo("value64");
        await Assert.That(last.Relationships.Select(item => item.Name))
            .IsEquivalentTo(["first", "second", "third", "fourth"]);
        await Assert.That(last.Count).IsEqualTo(5);
        await Assert.That(last.StartIndex).IsEqualTo(64);
        await Assert.That(last.HasPrevious).IsTrue();
        await Assert.That(last.HasNext).IsFalse();
    }

    [Test]
    public async Task SearchMatchesNamesAndTypesWithoutChangingAuthoredSnapshots()
    {
        ViewerAttributeSnapshot authored = new("inputs:roughness", "float", true, false, 2, "0, 1", "0.5");
        ViewerAttributeSnapshot blocked = new("inputs:color", "color3f", true, true, 0, "<none>", "<blocked>");
        ViewerAttributeSnapshot fallback = new("visibility", "token", false, false, 0, "<none>", "inherited");
        ViewerAttributeSnapshot[] attributes = [authored, blocked, fallback];
        ViewerRelationshipSnapshot relationship = new("material:binding", "/Looks/Material");
        ViewerRelationshipSnapshot[] relationships = [relationship];

        ViewerPropertyPage byName = ViewerPropertyPage.Create(attributes, relationships, "  INPUTS:  ", 0);
        ViewerPropertyPage byType = ViewerPropertyPage.Create(attributes, relationships, "COLOR3F", 0);
        ViewerPropertyPage links = ViewerPropertyPage.Create(attributes, relationships, "relationship", 0);
        ViewerPropertyPage empty = ViewerPropertyPage.Create(attributes, relationships, "not-a-property", 9);

        await Assert.That(byName.Attributes).IsEquivalentTo([authored, blocked]);
        await Assert.That(byName.MatchCount).IsEqualTo(2);
        await Assert.That(byName.TotalCount).IsEqualTo(4);
        await Assert.That(byType.Attributes.Single()).IsSameReferenceAs(blocked);
        await Assert.That(links.Relationships.Single()).IsSameReferenceAs(relationship);
        await Assert.That(empty.Count).IsEqualTo(0);
        await Assert.That(empty.PageIndex).IsEqualTo(0);
        await Assert.That(empty.HasNext).IsFalse();
        await Assert.That(empty.HasPrevious).IsFalse();
        await Assert.That(attributes).IsEquivalentTo([authored, blocked, fallback]);
    }

    [Test]
    public async Task RefreshClampsThePageAndDetachesItsBoundedCollection()
    {
        ViewerAttributeSnapshot original = new("weight", "double", true, false, 0, "<none>", "7");
        ViewerAttributeSnapshot[] attributes = [original];
        ViewerPropertyPage page = ViewerPropertyPage.Create(attributes, [], null, int.MaxValue);
        attributes[0] = original with { Value = "99" };

        await Assert.That(page.PageIndex).IsEqualTo(0);
        await Assert.That(page.StartIndex).IsEqualTo(0);
        await Assert.That(page.Count).IsEqualTo(1);
        await Assert.That(page.Attributes.Single().Value).IsEqualTo("7");
        await Assert.That(page.HasPrevious).IsFalse();
        await Assert.That(page.HasNext).IsFalse();
        await Assert.That(() => ((IList<ViewerAttributeSnapshot>)page.Attributes).Add(original))
            .Throws<NotSupportedException>();
        await Assert.That(() => ViewerPropertyPage.Create(attributes, [], null, -1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ACompletelyEmptyPrimHasNoNavigationOrPhantomFirstResult()
    {
        ViewerPropertyPage page = ViewerPropertyPage.Create([], [], " ", 3);

        await Assert.That(page.TotalCount).IsEqualTo(0);
        await Assert.That(page.MatchCount).IsEqualTo(0);
        await Assert.That(page.PageIndex).IsEqualTo(0);
        await Assert.That(page.StartIndex).IsEqualTo(0);
        await Assert.That(page.Count).IsEqualTo(0);
        await Assert.That(page.Attributes).IsEmpty();
        await Assert.That(page.Relationships).IsEmpty();
        await Assert.That(page.HasPrevious).IsFalse();
        await Assert.That(page.HasNext).IsFalse();
    }

    [Test]
    public async Task APageFromTenThousandPropertiesCopiesOnlyItsBoundedResults()
    {
        ViewerAttributeSnapshot[] attributes = Enumerable.Range(0, 10_000)
            .Select(index => new ViewerAttributeSnapshot(
                $"weight{index:D5}", "double", true, false, 0, "<none>", "7"))
            .ToArray();

        long before = GC.GetAllocatedBytesForCurrentThread();
        ViewerPropertyPage page = ViewerPropertyPage.Create(
            attributes, [], "weight", 312, group: ViewerPropertyGroup.Other);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(page.MatchCount).IsEqualTo(10_000);
        await Assert.That(page.Attributes.Count).IsEqualTo(16);
        await Assert.That(page.Attributes[0].Name).IsEqualTo("weight09984");
        await Assert.That(page.Attributes[^1].Name).IsEqualTo("weight09999");
        await Assert.That(page.HasPrevious).IsTrue();
        await Assert.That(page.HasNext).IsFalse();
        await Assert.That(allocated).IsLessThan(32L * 1024);
    }
}
