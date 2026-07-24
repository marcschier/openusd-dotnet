// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.LiveAuthoring;

/// <summary>Applies data-only live updates through a public <see cref="UsdStageScheduler"/>.</summary>
public sealed class UsdStageBatchExecutor : ILiveAuthoringBatchExecutor
{
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
            stage.EditTargetLayerIdentifier);
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
            case SetScalarUpdate scalar:
                ApplyScalar(stage.GetPrim(scalar.PrimPath), scalar);
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
            default:
                throw new NotSupportedException(
                    $"The live update type '{update.GetType().FullName}' is not supported.");
        }
    }

    private static void ApplyScalar(UsdPrim prim, SetScalarUpdate update)
    {
        switch (update.Value.Kind)
        {
            case LiveScalarKind.Boolean:
                Set(
                    update,
                    value => prim.SetBool(update.AttributeName, value),
                    (value, time) => prim.SetBool(update.AttributeName, value, time),
                    update.Value.Boolean);
                break;
            case LiveScalarKind.Int64:
                Set(
                    update,
                    value => prim.SetInt64(update.AttributeName, value),
                    (value, time) => prim.SetInt64(update.AttributeName, value, time),
                    update.Value.Int64Value);
                break;
            case LiveScalarKind.Double:
                Set(
                    update,
                    value => prim.SetDouble(update.AttributeName, value),
                    (value, time) => prim.SetDouble(update.AttributeName, value, time),
                    update.Value.DoubleValue);
                break;
            case LiveScalarKind.String:
                Set(
                    update,
                    value => prim.SetString(update.AttributeName, value),
                    (value, time) => prim.SetString(update.AttributeName, value, time),
                    update.Value.Text!);
                break;
            case LiveScalarKind.Token:
                Set(
                    update,
                    value => prim.SetToken(update.AttributeName, value),
                    (value, time) => prim.SetToken(update.AttributeName, value, time),
                    update.Value.Text!);
                break;
            case LiveScalarKind.Vec3f:
                Set(
                    update,
                    value => prim.SetVec3f(update.AttributeName, value),
                    (value, time) => prim.SetVec3f(update.AttributeName, value, time),
                    update.Value.Vec3f);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(update), update.Value.Kind, null);
        }
    }

    private static void Set<T>(
        SetScalarUpdate update,
        Action<T> setDefault,
        Action<T, double> setSample,
        T value)
    {
        if (update.TimeCode is { } timeCode)
        {
            setSample(value, timeCode);
        }
        else
        {
            setDefault(value);
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
}
