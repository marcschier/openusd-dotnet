// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;
using OpenUsd.Media;
using OpenUsd.Skel;
using OpenUsd.UI;

namespace OpenUsd.LiveAuthoring;

/// <summary>Applies data-only live updates through a public <see cref="UsdStageScheduler"/>.</summary>
public sealed class UsdStageBatchExecutor : ILiveAuthoringBatchExecutor
{
    /// <summary>
    /// The bounded, curated set of single-apply API schema tokens with an existing typed OpenUSD apply
    /// API. Schema removal has no underlying typed API yet and is always rejected explicitly.
    /// </summary>
    private static readonly Dictionary<string, Action<UsdPrim>> ApiSchemaApplyRegistry =
        new(StringComparer.Ordinal)
        {
            ["SkelBindingAPI"] = static prim => UsdSkelBinding.Apply(prim),
            ["AssetPreviewsAPI"] = static prim => UsdMediaAssetPreviews.Apply(prim),
            ["NodeGraphNodeAPI"] = static prim => UsdUINodeGraphNode.Apply(prim),
            ["SceneGraphPrimAPI"] = static prim => UsdUISceneGraphPrim.Apply(prim)
        };

    private readonly UsdStageScheduler _scheduler;

    /// <summary>Initializes a scheduler adapter that does not own the scheduler.</summary>
    public UsdStageBatchExecutor(
        UsdStageScheduler scheduler,
        LiveAuthoringEditLayer editLayer = LiveAuthoringEditLayer.Session)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if ((uint)editLayer > (uint)LiveAuthoringEditLayer.Root)
        {
            throw new ArgumentOutOfRangeException(nameof(editLayer));
        }
        _scheduler = scheduler;
        EditLayer = editLayer;
    }

    /// <summary>Gets the layer selected before every batch.</summary>
    public LiveAuthoringEditLayer EditLayer { get; }

    /// <summary>
    /// Gets the bounded, curated set of API schema tokens supported by
    /// <see cref="LiveApiSchemaOperation.Apply"/>.
    /// </summary>
    public static IReadOnlyCollection<string> SupportedApiSchemaTokens => ApiSchemaApplyRegistry.Keys;

    /// <inheritdoc/>
    public ValueTask<LiveAuthoringBatchResult> ExecuteAsync(
        LiveAuthoringBatch batch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        LiveAuthoringValidation.Validate(batch);
        return _scheduler.EditAsync(
            stage => Apply(stage, batch),
            batch.Invalidation,
            cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private LiveAuthoringBatchResult Apply(UsdStage stage, LiveAuthoringBatch batch)
    {
        SelectEditLayer(stage);
        ulong beforeSerial = stage.ChangeSerial;
        foreach (LiveStageUpdate update in batch.Updates)
        {
            Apply(stage, update);
        }
        return new LiveAuthoringBatchResult(
            batch.Sequence,
            batch.Sequence,
            1,
            batch.Updates.Count,
            batch.Invalidation,
            beforeSerial,
            stage.ChangeSerial,
            stage.EditTargetLayerIdentifier,
            batch.CorrelationId,
            batch.OriginId);
    }

    private void SelectEditLayer(UsdStage stage)
    {
        if (EditLayer == LiveAuthoringEditLayer.Session)
        {
            stage.SetEditTargetToSessionLayer();
        }
        else
        {
            stage.SetEditTargetToRootLayer();
        }
    }

    private static void Apply(UsdStage stage, LiveStageUpdate update)
    {
        switch (update)
        {
            case DefinePrimUpdate define:
                stage.DefinePrim(define.PrimPath, define.TypeName);
                break;
            case RemovePrimUpdate remove:
                stage.RemovePrim(remove.PrimPath);
                break;
            case SetAttributeUpdate attribute:
                ApplyAttribute(stage.GetPrim(attribute.PrimPath), attribute);
                break;
            case ClearUpdate clear:
                ApplyClear(stage.GetPrim(clear.PrimPath), clear);
                break;
            case SetRelationshipTargetsUpdate relationship:
                ApplyRelationship(stage.GetPrim(relationship.PrimPath), relationship);
                break;
            case SetReferenceUpdate reference:
                ApplyReference(stage.GetPrim(reference.PrimPath), reference);
                break;
            case SetPayloadUpdate payload:
                ApplyPayload(stage.GetPrim(payload.PrimPath), payload);
                break;
            case SetActiveUpdate active:
                stage.GetPrim(active.PrimPath).SetActive(active.Active);
                break;
            case SetInstanceableUpdate instanceable:
                stage.GetPrim(instanceable.PrimPath).SetInstanceable(instanceable.Instanceable);
                break;
            case SetVariantSelectionUpdate variant:
                ApplyVariant(stage.GetPrim(variant.PrimPath), variant);
                break;
            case SetMetadataUpdate metadata:
                ApplyMetadata(stage.GetPrim(metadata.PrimPath), metadata);
                break;
            case ApiSchemaUpdate apiSchema:
                ApplyApiSchema(stage.GetPrim(apiSchema.PrimPath), apiSchema);
                break;
            case SetPointInstancerOrientationsUpdate orientations:
                ApplyOrientations(stage.GetPrim(orientations.PrimPath), orientations);
                break;
            case ReplaceBridgeOverlayUpdate replace:
                ApplyBridgeOverlayReplacement(stage, replace);
                break;
            default:
                throw new NotSupportedException(
                    $"The live update type '{update.GetType().FullName}' is not supported.");
        }
    }

    private static void ApplyAttribute(UsdPrim prim, SetAttributeUpdate update)
    {
        string name = update.AttributeName;
        double? timeCode = update.TimeCode;
        LiveAttributeValue value = update.Value;
        switch (value.Kind)
        {
            case LiveAttributeKind.Boolean:
                SetScalar(timeCode, value.Boolean, v => prim.SetBool(name, v), (v, t) => prim.SetBool(name, v, t));
                break;
            case LiveAttributeKind.Int64:
                SetScalar(
                    timeCode,
                    value.Int64Value,
                    v => prim.SetInt64(name, v),
                    (v, t) => prim.SetInt64(name, v, t));
                break;
            case LiveAttributeKind.Double:
                SetScalar(
                    timeCode,
                    value.DoubleValue,
                    v => prim.SetDouble(name, v),
                    (v, t) => prim.SetDouble(name, v, t));
                break;
            case LiveAttributeKind.String:
                SetScalar(
                    timeCode,
                    value.StringValue,
                    v => prim.SetString(name, v),
                    (v, t) => prim.SetString(name, v, t));
                break;
            case LiveAttributeKind.Token:
                SetScalar(
                    timeCode,
                    value.TokenValue,
                    v => prim.SetToken(name, v),
                    (v, t) => prim.SetToken(name, v, t));
                break;
            case LiveAttributeKind.Vec3f:
                SetScalar(
                    timeCode,
                    value.Vec3f,
                    v => prim.SetVec3f(name, v),
                    (v, t) => prim.SetVec3f(name, v, t));
                break;
            case LiveAttributeKind.Matrix4d:
                SetScalar(
                    timeCode,
                    value.Matrix4d,
                    v => prim.SetMatrix4d(name, v),
                    (v, t) => prim.SetMatrix4d(name, v, t));
                break;
            case LiveAttributeKind.Int32Array:
                SetArray(
                    timeCode,
                    value.Int32Array.ToArray(),
                    a => prim.SetInt32Array(name, a),
                    (a, t) => prim.SetInt32Array(name, a, t));
                break;
            case LiveAttributeKind.FloatArray:
                SetArray(
                    timeCode,
                    value.FloatArray.ToArray(),
                    a => prim.SetFloatArray(name, a),
                    (a, t) => prim.SetFloatArray(name, a, t));
                break;
            case LiveAttributeKind.DoubleArray:
                SetArray(
                    timeCode,
                    value.DoubleArray.ToArray(),
                    a => prim.SetDoubleArray(name, a),
                    (a, t) => prim.SetDoubleArray(name, a, t));
                break;
            case LiveAttributeKind.Vec2fArray:
                SetArray(
                    timeCode,
                    value.Vec2fArray.ToArray(),
                    a => prim.SetVec2fArray(name, a),
                    (a, t) => prim.SetVec2fArray(name, a, t));
                break;
            case LiveAttributeKind.Vec3fArray:
                SetArray(
                    timeCode,
                    value.Vec3fArray.ToArray(),
                    a => prim.SetVec3fArray(name, a),
                    (a, t) => prim.SetVec3fArray(name, a, t));
                break;
            case LiveAttributeKind.Color3fArray:
                SetArray(
                    timeCode,
                    value.Color3fArray.ToArray(),
                    a => prim.SetColor3fArray(name, a),
                    (a, t) => prim.SetColor3fArray(name, a, t));
                break;
            case LiveAttributeKind.BooleanArray:
                SetArray(
                    timeCode,
                    value.BooleanArray.ToArray(),
                    a => prim.SetBoolArray(name, a),
                    (a, t) => prim.SetBoolArray(name, a, t));
                break;
            case LiveAttributeKind.TokenArray:
                SetArray(
                    timeCode,
                    value.TokenArray.ToArray(),
                    a => prim.SetTokenArray(name, a),
                    (a, t) => prim.SetTokenArray(name, a, t));
                break;
            case LiveAttributeKind.StringArray:
                SetArray(
                    timeCode,
                    value.StringArray.ToArray(),
                    a => prim.SetStringArray(name, a),
                    (a, t) => prim.SetStringArray(name, a, t));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(update), value.Kind, null);
        }
    }

    private static void SetScalar<T>(
        double? timeCode,
        T value,
        Action<T> setDefault,
        Action<T, double> setSample)
    {
        if (timeCode is { } sampledTime)
        {
            setSample(value, sampledTime);
        }
        else
        {
            setDefault(value);
        }
    }

    private static void SetArray<T>(
        double? timeCode,
        T[] values,
        Action<T[]> setDefault,
        Action<T[], double> setSample)
    {
        if (timeCode is { } sampledTime)
        {
            setSample(values, sampledTime);
        }
        else
        {
            setDefault(values);
        }
    }

    private static void ApplyClear(UsdPrim prim, ClearUpdate clear)
    {
        switch (clear.TargetKind)
        {
            case LiveClearTargetKind.AttributeValue:
                prim.GetAttribute(clear.Name!).ClearValue();
                break;
            case LiveClearTargetKind.RelationshipTargets:
                prim.ClearRelationshipTargets(clear.Name!);
                break;
            case LiveClearTargetKind.References:
                prim.ClearReferences();
                break;
            case LiveClearTargetKind.Payloads:
                prim.ClearPayloads();
                break;
            case LiveClearTargetKind.Metadata:
                prim.ClearMetadata(clear.Name!);
                break;
            default:
                throw new NotSupportedException(
                    $"The clear target kind '{clear.TargetKind}' is not supported.");
        }
    }

    private static void ApplyRelationship(
        UsdPrim prim,
        SetRelationshipTargetsUpdate update)
    {
        prim.CreateRelationship(update.RelationshipName);
        prim.SetRelationshipTargets(update.RelationshipName, update.Targets.ToArray());
    }

    private static void ApplyReference(UsdPrim prim, SetReferenceUpdate update)
    {
        prim.ClearReferences();
        prim.AddReference(update.AssetPath!, update.TargetPrimPath);
    }

    private static void ApplyPayload(UsdPrim prim, SetPayloadUpdate update)
    {
        prim.ClearPayloads();
        prim.AddPayload(update.AssetPath!, update.TargetPrimPath);
    }

    private static void ApplyVariant(UsdPrim prim, SetVariantSelectionUpdate update)
    {
        prim.AddVariantSet(update.VariantSetName);
        foreach (string variant in update.KnownVariants)
        {
            prim.AddVariant(update.VariantSetName, variant);
        }
        prim.SetVariantSelection(update.VariantSetName, update.Selection);
    }

    private static void ApplyMetadata(UsdPrim prim, SetMetadataUpdate update)
    {
        switch (update.Value.Kind)
        {
            case LiveMetadataKind.Boolean:
                prim.SetMetadata(update.Key, update.Value.Boolean);
                break;
            case LiveMetadataKind.Int64:
                prim.SetMetadata(update.Key, update.Value.Int64Value);
                break;
            case LiveMetadataKind.Double:
                prim.SetMetadata(update.Key, update.Value.DoubleValue);
                break;
            case LiveMetadataKind.String:
                prim.SetMetadata(update.Key, update.Value.StringValue);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(update), update.Value.Kind, null);
        }
    }

    private static void ApplyApiSchema(UsdPrim prim, ApiSchemaUpdate update)
    {
        if (update.Operation == LiveApiSchemaOperation.Remove)
        {
            throw new NotSupportedException(
                $"Removing the API schema '{update.SchemaToken}' is not yet supported. The " +
                "underlying OpenUSD typed API currently exposes schema application only, not generic " +
                "removal, for this bounded schema registry.");
        }

        if (!ApiSchemaApplyRegistry.TryGetValue(update.SchemaToken, out Action<UsdPrim>? apply))
        {
            throw new NotSupportedException(
                $"The API schema '{update.SchemaToken}' is not in the supported apply registry " +
                $"({string.Join(", ", ApiSchemaApplyRegistry.Keys)}).");
        }

        apply(prim);
    }

    private static void ApplyOrientations(UsdPrim prim, SetPointInstancerOrientationsUpdate update)
    {
        UsdGeomPointInstancer instancer = UsdGeomPointInstancer.Wrap(prim);
        instancer.SetOrientations(update.Orientations.ToArray());
    }

    /// <summary>
    /// Replaces the bridge-owned overlay inside the single scheduler edit that already owns the stage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Staged validation runs first and touches nothing: every nested update is re-checked against the
    /// bridge root scope before the existing overlay is removed, so a snapshot that reaches outside the
    /// bridge root is rejected while the previous overlay is still intact.
    /// </para>
    /// <para>
    /// Removal targets the current edit-target layer only, so a user-edit layer, a physics overlay, or
    /// the root layer keeps every opinion it holds outside the bridge root. If a nested update fails,
    /// the bridge root is removed again before the failure propagates: the overlay is then empty, which
    /// is a well-defined state the coordinator can replace from a newer snapshot, rather than a
    /// partially applied mixture of two snapshots.
    /// </para>
    /// </remarks>
    private static void ApplyBridgeOverlayReplacement(UsdStage stage, ReplaceBridgeOverlayUpdate update)
    {
        for (int index = 0; index < update.Updates.Count; index++)
        {
            LiveAuthoringValidation.ValidateBridgeScope(
                update.BridgeRootPath,
                update.Updates[index],
                $"{nameof(update)}.{nameof(update.Updates)}[{index}]");
        }

        RemoveBridgeRoot(stage, update.BridgeRootPath);
        try
        {
            stage.DefinePrim(update.BridgeRootPath);
            foreach (LiveStageUpdate nested in update.Updates)
            {
                Apply(stage, nested);
            }
        }
        catch
        {
            try
            {
                RemoveBridgeRoot(stage, update.BridgeRootPath);
            }
            catch (Exception rollbackFailure) when (rollbackFailure is not OutOfMemoryException)
            {
                // The original failure is the actionable one and must not be masked. A failed rollback
                // still leaves the session in ResyncRequired, so the next accepted snapshot replaces
                // whatever remains.
            }
            throw;
        }
    }

    private static void RemoveBridgeRoot(UsdStage stage, string bridgeRootPath)
    {
        if (stage.HasPrim(bridgeRootPath))
        {
            stage.RemovePrim(bridgeRootPath);
        }
    }
}
