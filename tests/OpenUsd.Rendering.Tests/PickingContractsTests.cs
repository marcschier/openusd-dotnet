// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;

namespace OpenUsd.Rendering.Tests;

public sealed class PickingContractsTests
{
    private const int AllocationIterations = 1000;

    [Test]
    public async Task RequestCapturesPhysicalTopLeftPixelAndRevisionBinding()
    {
        var request = new RenderPickRequest(
            x: 1919,
            y: 1079,
            new ViewportDimensions(1920, 1080),
            requestedStateRevision: 42,
            requestedSceneRevision: 7,
            RenderPickTarget.Face,
            RenderPickOptions.CullBackFaces);

        await Assert.That(request.X).IsEqualTo(1919);
        await Assert.That(request.Y).IsEqualTo(1079);
        await Assert.That(request.Width).IsEqualTo(1);
        await Assert.That(request.Height).IsEqualTo(1);
        await Assert.That(request.Viewport).IsEqualTo(new ViewportDimensions(1920, 1080));
        await Assert.That(request.RequestedStateRevision).IsEqualTo(42ul);
        await Assert.That(request.RequestedSceneRevision).IsEqualTo(7ul);
        await Assert.That(request.Target).IsEqualTo(RenderPickTarget.Face);
        await Assert.That(request.Flags).IsEqualTo(RenderPickOptions.CullBackFaces);
        await Assert.That(request.IsStale(42, 7)).IsFalse();
        await Assert.That(request.IsStale(43, 7)).IsTrue();
        await Assert.That(request.IsStale(42, 8)).IsTrue();
        await Assert.That(request.InferStaleReasons(42, 7))
            .IsEqualTo(RenderPickStaleReason.None);
        await Assert.That(request.InferStaleReasons(43, 8))
            .IsEqualTo(
                RenderPickStaleReason.StateRevision |
                RenderPickStaleReason.SceneRevision);
    }

    [Test]
    public async Task RequestAcceptsEveryBoundaryPixel()
    {
        var viewport = new ViewportDimensions(2, 2);

        var topLeft = new RenderPickRequest(0, 0, viewport, requestedStateRevision: 0);
        var bottomRight = new RenderPickRequest(1, 1, viewport, requestedStateRevision: ulong.MaxValue);

        await Assert.That(topLeft.X).IsEqualTo(0);
        await Assert.That(topLeft.Y).IsEqualTo(0);
        await Assert.That(bottomRight.X).IsEqualTo(1);
        await Assert.That(bottomRight.Y).IsEqualTo(1);
        await Assert.That(bottomRight.RequestedStateRevision).IsEqualTo(ulong.MaxValue);
    }

    [Test]
    public void RequestRejectsInvalidCoordinatesDimensionsTargetsAndFlags()
    {
        var viewport = new ViewportDimensions(2, 2);

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new RenderPickRequest(-1, 0, viewport, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new RenderPickRequest(0, -1, viewport, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new RenderPickRequest(2, 0, viewport, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new RenderPickRequest(0, 2, viewport, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new RenderPickRequest(0, 0, ViewportDimensions.Empty, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new RenderPickRequest(0, 0, 0, 1, viewport, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new RenderPickRequest(0, 0, 2, 1, viewport, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new RenderPickRequest(0, 0, 1, 0, viewport, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new RenderPickRequest(0, 0, 1, 2, viewport, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new RenderPickRequest(
                0,
                0,
                viewport,
                0,
                target: (RenderPickTarget)99));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new RenderPickRequest(
                0,
                0,
                viewport,
                0,
                flags: (RenderPickOptions)2));
    }

    [Test]
    public async Task SelectionItemValidatesAbsolutePathsAndIndexRelationships()
    {
        var item = new SelectionItem(
            "/World/Prototype",
            "/World/Instancer",
            instanceIndex: 3,
            elementIndex: 8,
            elementKind: SelectionElementKind.Face);

        await Assert.That(item.PrimPath).IsEqualTo("/World/Prototype");
        await Assert.That(item.InstancerPath).IsEqualTo("/World/Instancer");
        await Assert.That(item.InstanceIndex).IsEqualTo(3);
        await Assert.That(item.ElementIndex).IsEqualTo(8);

        _ = Assert.Throws<ArgumentException>(() => _ = new SelectionItem("World/Cube"));
        _ = Assert.Throws<ArgumentException>(() => _ = new SelectionItem("/"));
        _ = Assert.Throws<ArgumentException>(() => _ = new SelectionItem("/World/"));
        _ = Assert.Throws<ArgumentException>(() => _ = new SelectionItem("/World//Cube"));
        _ = Assert.Throws<ArgumentException>(() => _ = new SelectionItem("/World/Cube.visibility"));
        _ = Assert.Throws<ArgumentException>(() => _ = new SelectionItem("/World/Cube", instanceIndex: 0));
        _ = Assert.Throws<ArgumentException>(
            () => _ = new SelectionItem("/World/Cube", "/World/Instancer"));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new SelectionItem("/World/Cube", "/World/Instancer", -1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new SelectionItem("/World/Cube", elementIndex: -1));
    }

    /// <summary>
    /// The four-parameter constructor states no element kind, and a hit
    /// resolves that unstated kind against the request's own target.
    /// </summary>
    /// <remarks>
    /// The old shape predates the kind, so classifying its index as a face
    /// turned an edge or point selection built by a legacy producer into a face
    /// selection with no diagnostic at all -- the exact ambiguity the kind was
    /// added to remove. The index is preserved instead and named by the only
    /// thing that knows: the request that was answered.
    /// </remarks>
    [Test]
    [Arguments(RenderPickTarget.Face, SelectionElementKind.Face)]
    [Arguments(RenderPickTarget.Edge, SelectionElementKind.Edge)]
    [Arguments(RenderPickTarget.Point, SelectionElementKind.Point)]
    public async Task ALegacyElementItemIsResolvedAgainstTheRequestedTarget(
        RenderPickTarget target,
        SelectionElementKind expected)
    {
        var legacy = new SelectionItem(
            "/World/Prototype",
            "/World/Instancer",
            instanceIndex: 2,
            elementIndex: 9);

        await Assert.That(legacy.ElementKind)
            .IsEqualTo(SelectionElementKind.Unspecified);
        await Assert.That(legacy.ElementIndex).IsEqualTo(9);

        var request = new RenderPickRequest(
            1,
            1,
            new ViewportDimensions(8, 8),
            requestedStateRevision: 4,
            requestedSceneRevision: null,
            target);
        RenderPickResult hit = RenderPickResult.Hit(request, 4, null, legacy);

        await Assert.That(hit.Item!.Value.ElementKind).IsEqualTo(expected);
        await Assert.That(hit.Item!.Value.ElementIndex).IsEqualTo(9);
        await Assert.That(hit.ElementIndex).IsEqualTo(9);
        await Assert.That(hit.Item!.Value.PrimPath).IsEqualTo("/World/Prototype");
        await Assert.That(hit.Item!.Value.InstancerPath).IsEqualTo("/World/Instancer");
        await Assert.That(hit.Item!.Value.InstanceIndex).IsEqualTo(2);

        // The normalized item is what the result publishes: it is a complete,
        // self-describing identity, not the unstated one that went in.
        await Assert.That(hit.Item!.Value).IsNotEqualTo(legacy);
        await Assert.That(hit.Item!.Value).IsEqualTo(new SelectionItem(
            "/World/Prototype",
            "/World/Instancer",
            instanceIndex: 2,
            elementIndex: 9,
            elementKind: expected));
    }

    /// <summary>
    /// An unstated kind has no meaning for a prim request, and an explicitly
    /// stated kind that disagrees with the target still fails.
    /// </summary>
    [Test]
    public async Task AnUnstatedKindIsRefusedForAPrimitiveRequestAndMismatchesStillThrow()
    {
        var primitiveRequest = new RenderPickRequest(
            1,
            1,
            new ViewportDimensions(8, 8),
            requestedStateRevision: 4);
        var edgeRequest = new RenderPickRequest(
            1,
            1,
            new ViewportDimensions(8, 8),
            requestedStateRevision: 4,
            requestedSceneRevision: null,
            RenderPickTarget.Edge);
        var legacy = new SelectionItem("/World/Cube", null, null, elementIndex: 9);
        var statedFace = new SelectionItem(
            "/World/Cube",
            instancerPath: null,
            instanceIndex: null,
            elementIndex: 9,
            elementKind: SelectionElementKind.Face);

        _ = Assert.Throws<ArgumentException>(
            () => RenderPickResult.Hit(primitiveRequest, 4, null, legacy));
        _ = Assert.Throws<ArgumentException>(
            () => RenderPickResult.Hit(edgeRequest, 4, null, statedFace));

        // Unspecified and Face are different identities, so a selection set
        // keeps both rather than collapsing one into the other.
        await Assert.That(legacy).IsNotEqualTo(statedFace);
        await Assert.That(new SelectionState([legacy, statedFace]).Items.Count)
            .IsEqualTo(2);
    }

    [Test]
    public async Task LegacySelectionConstructorAndPrimPathsRemainCompatible()
    {
        var paths = new List<string> { "/World/Cube", "/World/Sphere" };

        var selection = new SelectionState(paths);
        paths.Clear();

        await Assert.That(selection.PrimPaths).Count().IsEqualTo(2);
        await Assert.That(selection.PrimPaths[0]).IsEqualTo("/World/Cube");
        await Assert.That(selection.PrimPaths[1]).IsEqualTo("/World/Sphere");
        await Assert.That(selection.Items).Count().IsEqualTo(2);
        await Assert.That(selection.Items[0]).IsEqualTo(new SelectionItem("/World/Cube"));
        await Assert.That(selection.Items[1]).IsEqualTo(new SelectionItem("/World/Sphere"));
        await Assert.That(selection)
            .IsEqualTo(new SelectionState(
                new SelectionItem[]
                {
                    new("/World/Cube"),
                    new("/World/Sphere")
                }));
        _ = Assert.Throws<ArgumentException>(
            () => _ = new SelectionState(new List<string> { "/World/Cube", "/World/Cube" }));
    }

    [Test]
    public async Task SelectionPreservesOrderAndDistinctInstanceSubprimIdentity()
    {
        var first = new SelectionItem(
            "/World/Prototype",
            "/World/Instancer",
            0,
            4,
            SelectionElementKind.Face);
        var second = new SelectionItem(
            "/World/Prototype",
            "/World/Instancer",
            1,
            4,
            SelectionElementKind.Face);
        var items = new List<SelectionItem> { first, second };

        var selection = new SelectionState(items);
        items.Clear();

        await Assert.That(selection.Items).Count().IsEqualTo(2);
        await Assert.That(selection.Items[0]).IsEqualTo(first);
        await Assert.That(selection.Items[1]).IsEqualTo(second);
        await Assert.That(selection.PrimPaths[0]).IsEqualTo("/World/Prototype");
        await Assert.That(selection.PrimPaths[1]).IsEqualTo("/World/Prototype");
        _ = Assert.Throws<ArgumentException>(
            () => _ = new SelectionState(new SelectionItem[] { first, first }));
    }

    [Test]
    public async Task SelectionUsesCompleteOrderedIdentityForEqualityAndHashing()
    {
        var first = new SelectionState(
            new SelectionItem[]
            {
                new(
                    "/World/Prototype",
                    "/World/Instancer",
                    2,
                    9,
                    SelectionElementKind.Face)
            });
        var equal = new SelectionState(
            new SelectionItem[]
            {
                new(
                    "/World/Prototype",
                    "/World/Instancer",
                    2,
                    9,
                    SelectionElementKind.Face)
            });
        var differentInstance = new SelectionState(
            new SelectionItem[]
            {
                new(
                    "/World/Prototype",
                    "/World/Instancer",
                    3,
                    9,
                    SelectionElementKind.Face)
            });
        var differentElement = new SelectionState(
            new SelectionItem[]
            {
                new(
                    "/World/Prototype",
                    "/World/Instancer",
                    2,
                    10,
                    SelectionElementKind.Face)
            });

        await Assert.That(first).IsEqualTo(equal);
        await Assert.That(first.GetHashCode()).IsEqualTo(equal.GetHashCode());
        await Assert.That(first).IsNotEqualTo(differentInstance);
        await Assert.That(first).IsNotEqualTo(differentElement);
    }

    [Test]
    public async Task FullGeometryHitRemainsSourceCompatibleAndCarriesDiagnosticBackendToken()
    {
        var request = new RenderPickRequest(
            10,
            20,
            new ViewportDimensions(100, 50),
            requestedStateRevision: 12,
            requestedSceneRevision: 5,
            RenderPickTarget.Face);
        var item = new SelectionItem(
            "/World/Prototype",
            "/World/Instancer",
            instanceIndex: 6,
            elementIndex: 14,
            elementKind: SelectionElementKind.Face);
        var worldPosition = new Vector3(1, 2, 3);
        var worldNormal = Vector3.UnitY;

        RenderPickResult result = RenderPickResult.Hit(
            request,
            stateRevision: 12,
            sceneRevision: 5,
            item,
            worldPosition,
            worldNormal,
            normalizedDepth: 0.25f,
            RenderBackendKind.Vulkan,
            backendToken: 123);

        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(result.Request).IsEqualTo(request);
        await Assert.That(result.RequestedStateRevision).IsEqualTo(12ul);
        await Assert.That(result.StateRevision).IsEqualTo(12ul);
        await Assert.That(result.RequestedSceneRevision).IsEqualTo(5ul);
        await Assert.That(result.SceneRevision).IsEqualTo(5ul);
        await Assert.That(result.Item).IsEqualTo(item);
        await Assert.That(result.PrimPath).IsEqualTo("/World/Prototype");
        await Assert.That(result.InstancerPath).IsEqualTo("/World/Instancer");
        await Assert.That(result.InstanceIndex).IsEqualTo(6);
        await Assert.That(result.ElementIndex).IsEqualTo(14);
        await Assert.That(result.WorldPosition).IsEqualTo(worldPosition);
        await Assert.That(result.WorldNormal).IsEqualTo(worldNormal);
        await Assert.That(result.NormalizedDepth).IsEqualTo(0.25f);
        await Assert.That(result.BackendKind).IsEqualTo(RenderBackendKind.Vulkan);
        await Assert.That(result.BackendToken).IsEqualTo(123u);
        await Assert.That(result.StaleReasons).IsEqualTo(RenderPickStaleReason.None);
    }

    [Test]
    public async Task HitFactoryHasOneUnambiguousOptionalGeometrySignature()
    {
        System.Reflection.MethodInfo[] hitFactories = typeof(RenderPickResult)
            .GetMethods()
            .Where(method => method.Name == nameof(RenderPickResult.Hit))
            .ToArray();

        await Assert.That(hitFactories).Count().IsEqualTo(1);

        System.Reflection.ParameterInfo[] parameters = hitFactories[0].GetParameters();
        await Assert.That(parameters[3].ParameterType).IsEqualTo(typeof(SelectionItem).MakeByRefType());
        await Assert.That(parameters[3].IsOptional).IsFalse();
        await Assert.That(parameters[4].ParameterType).IsEqualTo(typeof(Vector3?));
        await Assert.That(parameters[4].IsOptional).IsTrue();
        await Assert.That(parameters[4].DefaultValue).IsNull();
        await Assert.That(parameters[5].ParameterType).IsEqualTo(typeof(Vector3?));
        await Assert.That(parameters[5].IsOptional).IsTrue();
        await Assert.That(parameters[5].DefaultValue).IsNull();
        await Assert.That(parameters[6].ParameterType).IsEqualTo(typeof(float?));
        await Assert.That(parameters[6].IsOptional).IsTrue();
        await Assert.That(parameters[6].DefaultValue).IsNull();
    }

    [Test]
    public async Task HitAcceptsIdOnlyAndIndependentlyOptionalGeometry()
    {
        var request = new RenderPickRequest(
            0,
            0,
            new ViewportDimensions(1, 1),
            requestedStateRevision: 1);
        var item = new SelectionItem("/World/Cube");
        var worldPosition = new Vector3(1, 2, 3);
        var worldNormal = Vector3.UnitZ;

        RenderPickResult idOnly = RenderPickResult.Hit(request, 1, sceneRevision: null, item);
        RenderPickResult positionOnly = RenderPickResult.Hit(
            request,
            1,
            sceneRevision: null,
            item,
            worldPosition);
        RenderPickResult depthOnly = RenderPickResult.Hit(
            request,
            1,
            sceneRevision: null,
            item,
            normalizedDepth: 0.5f);
        RenderPickResult normalAndDepth = RenderPickResult.Hit(
            request,
            1,
            sceneRevision: null,
            item,
            worldNormal: worldNormal,
            normalizedDepth: 0.75f);

        await Assert.That(idOnly.Item).IsEqualTo(item);
        await Assert.That(idOnly.WorldPosition).IsNull();
        await Assert.That(idOnly.WorldNormal).IsNull();
        await Assert.That(idOnly.NormalizedDepth).IsNull();

        await Assert.That(positionOnly.WorldPosition).IsEqualTo(worldPosition);
        await Assert.That(positionOnly.WorldNormal).IsNull();
        await Assert.That(positionOnly.NormalizedDepth).IsNull();

        await Assert.That(depthOnly.WorldPosition).IsNull();
        await Assert.That(depthOnly.WorldNormal).IsNull();
        await Assert.That(depthOnly.NormalizedDepth).IsEqualTo(0.5f);

        await Assert.That(normalAndDepth.WorldPosition).IsNull();
        await Assert.That(normalAndDepth.WorldNormal).IsEqualTo(worldNormal);
        await Assert.That(normalAndDepth.NormalizedDepth).IsEqualTo(0.75f);
    }

    [Test]
    public void HitValidatesIdentitySubprimOptionalGeometryDepthAndDiagnosticToken()
    {
        var primitiveRequest = new RenderPickRequest(
            0,
            0,
            new ViewportDimensions(1, 1),
            1);
        var faceRequest = new RenderPickRequest(
            0,
            0,
            new ViewportDimensions(1, 1),
            1,
            target: RenderPickTarget.Face);
        var item = new SelectionItem("/World/Cube");

        _ = RenderPickResult.Hit(primitiveRequest, 1, sceneRevision: null, item);
        _ = Assert.Throws<ArgumentException>(
            () => RenderPickResult.Hit(
                primitiveRequest,
                1,
                sceneRevision: null,
                default(SelectionItem)));
        _ = Assert.Throws<ArgumentException>(
            () => RenderPickResult.Hit(
                faceRequest,
                1,
                sceneRevision: null,
                item));
        _ = Assert.Throws<ArgumentException>(
            () => RenderPickResult.Hit(
                primitiveRequest,
                1,
                sceneRevision: null,
                item,
                worldPosition: new Vector3(float.NaN, 0, 0)));
        _ = Assert.Throws<ArgumentException>(
            () => RenderPickResult.Hit(
                primitiveRequest,
                1,
                sceneRevision: null,
                item,
                worldPosition: new Vector3(0, float.NegativeInfinity, 0)));
        _ = Assert.Throws<ArgumentException>(
            () => RenderPickResult.Hit(
                primitiveRequest,
                1,
                sceneRevision: null,
                item,
                worldNormal: new Vector3(0, float.PositiveInfinity, 0)));
        _ = Assert.Throws<ArgumentException>(
            () => RenderPickResult.Hit(
                primitiveRequest,
                1,
                sceneRevision: null,
                item,
                worldNormal: new Vector3(0, 0, float.NaN)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderPickResult.Hit(
                primitiveRequest,
                1,
                sceneRevision: null,
                item,
                normalizedDepth: -0.01f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderPickResult.Hit(
                primitiveRequest,
                1,
                sceneRevision: null,
                item,
                normalizedDepth: 1.01f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderPickResult.Hit(
                primitiveRequest,
                1,
                sceneRevision: null,
                item,
                normalizedDepth: float.NaN));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderPickResult.Hit(
                primitiveRequest,
                1,
                sceneRevision: null,
                item,
                normalizedDepth: float.PositiveInfinity));
        _ = Assert.Throws<ArgumentException>(
            () => RenderPickResult.Hit(
                primitiveRequest,
                1,
                sceneRevision: null,
                item,
                backendToken: 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderPickResult.Hit(
                primitiveRequest,
                1,
                sceneRevision: null,
                item,
                backendKind: RenderBackendKind.Storm,
                backendToken: 0));
    }

    [Test]
    public async Task ResultFactoriesEnforceCurrentAndStaleRevisionBindings()
    {
        var request = new RenderPickRequest(
            0,
            0,
            new ViewportDimensions(1, 1),
            requestedStateRevision: 8,
            requestedSceneRevision: 3);
        var item = new SelectionItem("/World/Cube");

        _ = Assert.Throws<ArgumentException>(
            () => RenderPickResult.Hit(
                request,
                stateRevision: 9,
                sceneRevision: 3,
                item,
                Vector3.Zero,
                Vector3.UnitZ,
                normalizedDepth: 0.5f));
        _ = Assert.Throws<ArgumentException>(
            () => RenderPickResult.Miss(request, stateRevision: 8, sceneRevision: 4));
        _ = Assert.Throws<ArgumentException>(
            () => RenderPickResult.Unsupported(request, stateRevision: 9, sceneRevision: 3));
        _ = Assert.Throws<ArgumentException>(
            () => RenderPickResult.Stale(request, stateRevision: 8, sceneRevision: 3));
        _ = Assert.Throws<ArgumentException>(
            () => RenderPickResult.Miss(request, stateRevision: 8, sceneRevision: null));

        RenderPickResult staleState = RenderPickResult.Stale(
            request,
            stateRevision: 9,
            sceneRevision: 3);
        RenderPickResult staleScene = RenderPickResult.Stale(
            request,
            stateRevision: 8,
            sceneRevision: 4);
        RenderPickResult staleMissingScene = RenderPickResult.Stale(
            request,
            stateRevision: 8,
            sceneRevision: null);
        RenderPickResult staleCamera = RenderPickResult.Stale(
            request,
            stateRevision: 8,
            sceneRevision: 3,
            RenderPickStaleReason.Camera);
        RenderPickResult staleCombined = RenderPickResult.Stale(
            request,
            stateRevision: 9,
            sceneRevision: 3,
            RenderPickStaleReason.Viewport |
            RenderPickStaleReason.ContextGeneration);

        await Assert.That(staleState.Status).IsEqualTo(RenderPickStatus.Stale);
        await Assert.That(staleState.StateRevision).IsEqualTo(9ul);
        await Assert.That(staleState.StaleReasons)
            .IsEqualTo(RenderPickStaleReason.StateRevision);
        await Assert.That(staleScene.Status).IsEqualTo(RenderPickStatus.Stale);
        await Assert.That(staleScene.SceneRevision).IsEqualTo(4ul);
        await Assert.That(staleScene.StaleReasons)
            .IsEqualTo(RenderPickStaleReason.SceneRevision);
        await Assert.That(staleMissingScene.Status).IsEqualTo(RenderPickStatus.Stale);
        await Assert.That(staleMissingScene.SceneRevision).IsNull();
        await Assert.That(staleMissingScene.StaleReasons)
            .IsEqualTo(RenderPickStaleReason.SceneRevision);
        await Assert.That(staleCamera.StaleReasons)
            .IsEqualTo(RenderPickStaleReason.Camera);
        await Assert.That(staleCombined.StaleReasons)
            .IsEqualTo(
                RenderPickStaleReason.StateRevision |
                RenderPickStaleReason.Viewport |
                RenderPickStaleReason.ContextGeneration);

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderPickResult.Stale(
                request,
                stateRevision: 8,
                sceneRevision: 3,
                (RenderPickStaleReason)(1 << 7)));
    }

    [Test]
    public async Task StaleReasonContractIsFlagsBasedAndFactoryRemainsSourceCompatible()
    {
        await Assert.That(typeof(RenderPickStaleReason).IsDefined(typeof(FlagsAttribute), false))
            .IsTrue();
        await Assert.That(Enum.GetValues<RenderPickStaleReason>())
            .IsEquivalentTo(
            [
                RenderPickStaleReason.None,
                RenderPickStaleReason.StateRevision,
                RenderPickStaleReason.SceneRevision,
                RenderPickStaleReason.Camera,
                RenderPickStaleReason.Viewport,
                RenderPickStaleReason.Time,
                RenderPickStaleReason.ContextGeneration,
                RenderPickStaleReason.BackendState
            ]);

        System.Reflection.MethodInfo[] staleFactories = typeof(RenderPickResult)
            .GetMethods()
            .Where(method => method.Name == nameof(RenderPickResult.Stale))
            .OrderBy(method => method.GetParameters().Length)
            .ToArray();
        System.Reflection.ParameterInfo[] compatibleParameters =
            staleFactories[0].GetParameters();
        System.Reflection.ParameterInfo[] reasonParameters =
            staleFactories[1].GetParameters();

        await Assert.That(staleFactories).Count().IsEqualTo(2);
        await Assert.That(compatibleParameters).Count().IsEqualTo(3);
        await Assert.That(reasonParameters).Count().IsEqualTo(4);
        await Assert.That(reasonParameters[3].ParameterType)
            .IsEqualTo(typeof(RenderPickStaleReason));
        await Assert.That(reasonParameters[3].IsOptional).IsFalse();

        var request = new RenderPickRequest(
            0,
            0,
            new ViewportDimensions(1, 1),
            requestedStateRevision: 1);
        RenderPickResult sourceCompatible = RenderPickResult.Stale(
            request,
            stateRevision: 2,
            sceneRevision: null);

        await Assert.That(sourceCompatible.StaleReasons)
            .IsEqualTo(RenderPickStaleReason.StateRevision);
    }

    [Test]
    public async Task UnboundSceneRevisionAcceptsTruthfulActualSceneRevision()
    {
        var request = new RenderPickRequest(
            0,
            0,
            new ViewportDimensions(1, 1),
            requestedStateRevision: 8);

        RenderPickResult miss = RenderPickResult.Miss(
            request,
            stateRevision: 8,
            sceneRevision: 99);

        await Assert.That(request.IsStale(8, 99)).IsFalse();
        await Assert.That(miss.RequestedSceneRevision).IsNull();
        await Assert.That(miss.SceneRevision).IsEqualTo(99ul);
    }

    [Test]
    public async Task NonHitResultsHaveDeterministicEmptyIdentity()
    {
        var request = new RenderPickRequest(
            0,
            0,
            new ViewportDimensions(1, 1),
            requestedStateRevision: 8,
            requestedSceneRevision: 3);
        RenderPickResult[] results =
        [
            RenderPickResult.Miss(request, 8, 3),
            RenderPickResult.Stale(request, 9, 3),
            RenderPickResult.Stale(
                request,
                8,
                3,
                RenderPickStaleReason.Camera |
                RenderPickStaleReason.Time |
                RenderPickStaleReason.BackendState),
            RenderPickResult.Unsupported(request, 8, 3)
        ];

        foreach (RenderPickResult result in results)
        {
            await Assert.That(result.Item).IsNull();
            await Assert.That(result.PrimPath).IsEqualTo(string.Empty);
            await Assert.That(result.InstancerPath).IsNull();
            await Assert.That(result.InstanceIndex).IsNull();
            await Assert.That(result.ElementIndex).IsNull();
            await Assert.That(result.WorldPosition).IsNull();
            await Assert.That(result.WorldNormal).IsNull();
            await Assert.That(result.NormalizedDepth).IsNull();
            await Assert.That(result.BackendKind).IsNull();
            await Assert.That(result.BackendToken).IsNull();
            if (result.Status == RenderPickStatus.Stale)
            {
                await Assert.That(result.StaleReasons)
                    .IsNotEqualTo(RenderPickStaleReason.None);
            }
            else
            {
                await Assert.That(result.StaleReasons)
                    .IsEqualTo(RenderPickStaleReason.None);
            }
        }
    }

    [Test]
    public async Task RequestsResultsAndItemsUseValueEquality()
    {
        var request = new RenderPickRequest(
            3,
            4,
            new ViewportDimensions(10, 10),
            requestedStateRevision: 2,
            requestedSceneRevision: 1,
            RenderPickTarget.Face);
        var equalRequest = new RenderPickRequest(
            3,
            4,
            new ViewportDimensions(10, 10),
            requestedStateRevision: 2,
            requestedSceneRevision: 1,
            RenderPickTarget.Face);
        var item = new SelectionItem(
            "/World/Cube",
            instancerPath: null,
            instanceIndex: null,
            elementIndex: 4,
            elementKind: SelectionElementKind.Face);
        var equalItem = new SelectionItem(
            "/World/Cube",
            instancerPath: null,
            instanceIndex: null,
            elementIndex: 4,
            elementKind: SelectionElementKind.Face);
        RenderPickResult result = RenderPickResult.Hit(
            request,
            2,
            1,
            item,
            Vector3.One,
            Vector3.UnitZ,
            0.5f);
        RenderPickResult equalResult = RenderPickResult.Hit(
            equalRequest,
            2,
            1,
            equalItem,
            Vector3.One,
            Vector3.UnitZ,
            0.5f);

        await Assert.That(request).IsEqualTo(equalRequest);
        await Assert.That(request.GetHashCode()).IsEqualTo(equalRequest.GetHashCode());
        await Assert.That(item).IsEqualTo(equalItem);
        await Assert.That(item.GetHashCode()).IsEqualTo(equalItem.GetHashCode());
        await Assert.That(result).IsEqualTo(equalResult);
        await Assert.That(result.GetHashCode()).IsEqualTo(equalResult.GetHashCode());
    }

    [Test]
    public async Task RichSelectionUpdatePreservesCompleteStageState()
    {
        StageRenderState original = StageRenderState.Create(new StageIdentity("stage.usda"))
            .WithCamera(new CameraState(
                Matrix4x4.CreateTranslation(1, 2, 3),
                Matrix4x4.CreatePerspectiveFieldOfView(1, 1, 0.1f, 100)))
            .WithTime(new StageTime(24))
            .WithViewport(new ViewportDimensions(640, 480));
        var item = new SelectionItem(
            "/World/Prototype",
            "/World/Instancer",
            instanceIndex: 2,
            elementIndex: 7,
            elementKind: SelectionElementKind.Face);

        StageRenderState selected = original.WithSelection(
            new SelectionState(new SelectionItem[] { item }));

        await Assert.That(selected.Revision).IsEqualTo(original.Revision + 1);
        await Assert.That(selected.Stage).IsSameReferenceAs(original.Stage);
        await Assert.That(selected.Camera).IsEqualTo(original.Camera);
        await Assert.That(selected.Time).IsEqualTo(original.Time);
        await Assert.That(selected.Display).IsEqualTo(original.Display);
        await Assert.That(selected.Viewport).IsEqualTo(original.Viewport);
        await Assert.That(selected.RenderSettings).IsEqualTo(original.RenderSettings);
        await Assert.That(selected.Diagnostics).IsSameReferenceAs(original.Diagnostics);
        await Assert.That(selected.Selection.Items[0]).IsEqualTo(item);
    }

    [Test]
    public async Task OptionalBackendCapabilityAndDiagnosticContractsExposePicking()
    {
        var request = new RenderPickRequest(
            0,
            0,
            new ViewportDimensions(1, 1),
            requestedStateRevision: 4);
        IRenderPickingBackend backend = new TestPickingBackend();
        var capabilities = new RenderBackendCapabilities(
            RenderBackendCapability.Picking,
            maxSamplesPerPixel: 1,
            isSoftware: false);

        RenderPickResult result = await backend.PickAsync(request);
        RenderBackendDiagnosticCategory pickingCategory = RenderBackendDiagnosticCategory.Picking;

        await Assert.That(capabilities.Supports(RenderBackendCapability.Picking)).IsTrue();
        await Assert.That(pickingCategory)
            .IsNotEqualTo(RenderBackendDiagnosticCategory.Rendering);
        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Miss);
        await Assert.That(result.Request).IsEqualTo(request);
    }

    [Test]
    public async Task ValueOnlyHitAndStaleCreationDoesNotAllocateAfterWarmup()
    {
        var viewport = new ViewportDimensions(640, 480);
        var item = new SelectionItem(
            "/World/Prototype",
            "/World/Instancer",
            instanceIndex: 1,
            elementIndex: 2,
            elementKind: SelectionElementKind.Face);
        var worldPosition = new Vector3(1, 2, 3);
        var worldNormal = Vector3.UnitY;
        ulong checksum = 0;

        for (int index = 0; index < 64; index++)
        {
            var warmupRequest = new RenderPickRequest(
                index,
                index,
                viewport,
                (ulong)index,
                requestedSceneRevision: (ulong)index,
                RenderPickTarget.Face);
            RenderPickResult warmupResult = RenderPickResult.Hit(
                warmupRequest,
                (ulong)index,
                (ulong)index,
                item,
                worldPosition,
                worldNormal,
                0.5f,
                RenderBackendKind.Vulkan,
                backendToken: 1);
            RenderPickResult idOnlyWarmupResult = RenderPickResult.Hit(
                warmupRequest,
                (ulong)index,
                (ulong)index,
                item);
            RenderPickResult staleWarmupResult = RenderPickResult.Stale(
                warmupRequest,
                (ulong)index,
                (ulong)index,
                RenderPickStaleReason.Camera |
                RenderPickStaleReason.ContextGeneration);
            checksum +=
                warmupResult.StateRevision +
                idOnlyWarmupResult.StateRevision +
                (ulong)staleWarmupResult.StaleReasons;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < AllocationIterations; index++)
        {
            var request = new RenderPickRequest(
                index % viewport.Width,
                index % viewport.Height,
                viewport,
                (ulong)index,
                requestedSceneRevision: (ulong)index,
                RenderPickTarget.Face);
            RenderPickResult result = RenderPickResult.Hit(
                request,
                (ulong)index,
                (ulong)index,
                item,
                worldPosition,
                worldNormal,
                0.5f,
                RenderBackendKind.Vulkan,
                backendToken: 1);
            RenderPickResult idOnlyResult = RenderPickResult.Hit(
                request,
                (ulong)index,
                (ulong)index,
                item);
            RenderPickResult staleResult = RenderPickResult.Stale(
                request,
                (ulong)index,
                (ulong)index,
                RenderPickStaleReason.Viewport |
                RenderPickStaleReason.Time);
            checksum +=
                result.StateRevision +
                idOnlyResult.StateRevision +
                (ulong)staleResult.StaleReasons;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
        await Assert.That(checksum).IsGreaterThan(0ul);
    }

    private sealed class TestPickingBackend : IRenderPickingBackend
    {
        public ValueTask<RenderPickResult> PickAsync(
            RenderPickRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(RenderPickResult.Miss(
                request,
                request.RequestedStateRevision,
                request.RequestedSceneRevision));
        }
    }
}
