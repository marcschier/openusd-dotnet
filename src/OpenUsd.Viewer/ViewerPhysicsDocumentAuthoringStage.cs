// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;

namespace OpenUsd.Viewer;

internal interface IViewerPhysicsHistorySource
{
    ViewerAuthoredEditController DocumentEditor { get; }
}

internal sealed class ViewerPhysicsDocumentAuthoringStage(ViewerAuthoredEditController editor)
    : IViewerPhysicsAuthoringStage, IViewerPhysicsHistorySource
{
    public ViewerAuthoredEditController DocumentEditor { get; } = editor;

    public async ValueTask<ViewerPhysicsAuthoringResult> ApplyAsync(
        ViewerPhysicsEditStep step, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Edits.Count == 0)
        {
            return new ViewerPhysicsAuthoringResult(0, 0, "The edit contains no property change.", []);
        }
        ArgumentOutOfRangeException.ThrowIfGreaterThan(step.Edits.Count, 256);
        var addresses = new UsdLayerEditAddress[step.Edits.Count];
        var changes = new UsdLayerEdit[step.Edits.Count];
        for (int index = 0; index < step.Edits.Count; index++)
        {
            ViewerPhysicsEdit edit = step.Edits[index];
            if (edit.After.IsAuthored &&
                ((edit.After.Kind == ViewerPhysicsValueKind.Number && !double.IsFinite(edit.After.NumberValue)) ||
                (edit.After.Kind == ViewerPhysicsValueKind.Vector3 &&
                (!float.IsFinite((float)edit.After.VectorValue.X) ||
                !float.IsFinite((float)edit.After.VectorValue.Y) ||
                !float.IsFinite((float)edit.After.VectorValue.Z)))))
            {
                return new ViewerPhysicsAuthoringResult(
                    0, step.Edits.Count, "Enter finite values within the declared storage range.", []);
            }
            addresses[index] = Address(edit.PrimPath, edit.Name);
            changes[index] = edit.After.IsAuthored
                ? UsdLayerEdit.Set(addresses[index], ToNative(edit.After), TypeName(edit.After.Kind))
                : UsdLayerEdit.Clear(addresses[index]);
        }
        ViewerAuthoredEditCapture before = await DocumentEditor.CaptureAsync(addresses, cancellationToken)
            .ConfigureAwait(false);
        ViewerAuthoredEditResult result = await DocumentEditor.ApplyAsync(
            before, changes, step.Description, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ToPhysicsResult(result, step.Edits.Count);
    }

    public async ValueTask<ViewerPhysicsValue> ReadAsync(
        string primPath, string name, ViewerPhysicsValueKind kind, CancellationToken cancellationToken)
    {
        ViewerAuthoredEditCapture capture = await DocumentEditor.CaptureAsync(
            [Address(primPath, name)], cancellationToken).ConfigureAwait(false);
        UsdLayerEditValue value = capture.Snapshot.Opinions[0].Value;
        if (value.Kind == UsdLayerEditValueKind.Absent)
        {
            return ViewerPhysicsValue.Unauthored(kind);
        }
        return kind switch
        {
            ViewerPhysicsValueKind.Bool when value.Kind == UsdLayerEditValueKind.Boolean =>
                ViewerPhysicsValue.FromBool(value.AsBoolean()),
            ViewerPhysicsValueKind.Number when value.Kind == UsdLayerEditValueKind.Double =>
                ViewerPhysicsValue.FromNumber(value.AsDouble()),
            ViewerPhysicsValueKind.Integer when value.Kind == UsdLayerEditValueKind.Int64 =>
                ViewerPhysicsValue.FromInteger(value.AsInt64()),
            ViewerPhysicsValueKind.Text when value.Kind == UsdLayerEditValueKind.String =>
                ViewerPhysicsValue.FromText(value.AsString()),
            ViewerPhysicsValueKind.Token when value.Kind == UsdLayerEditValueKind.Token =>
                ViewerPhysicsValue.FromToken(value.AsToken()),
            ViewerPhysicsValueKind.Vector3 when value.Kind == UsdLayerEditValueKind.Vec3f =>
                FromVector(value.AsVec3f()),
            _ => throw new NotSupportedException(
                "This target opinion cannot be represented by the physics scalar editor; it remains unchanged.")
        };
    }

    internal static ViewerPhysicsAuthoringResult ToPhysicsResult(
        ViewerAuthoredEditResult result, int count = 1) => new(
        result.Outcome == UsdLayerEditOutcome.Applied ? count : 0,
        result.Outcome == UsdLayerEditOutcome.Applied ? 0 : count,
        result.Message,
        result.AfterSerial > result.BeforeSerial
            ? [new ViewerPhysicsStageEdit(result.BeforeSerial, result.AfterSerial)] : []);

    private static UsdLayerEditAddress Address(string primPath, string name) =>
        new($"{primPath}.{name}", UsdLayerEditField.Default);

    private static ViewerPhysicsValue FromVector(UsdVec3f value) =>
        ViewerPhysicsValue.FromVector(new ViewerPhysicsVector3(value.X, value.Y, value.Z));

    private static UsdLayerEditValue ToNative(ViewerPhysicsValue value) => value.Kind switch
    {
        ViewerPhysicsValueKind.Bool => UsdLayerEditValue.FromBoolean(value.BoolValue),
        ViewerPhysicsValueKind.Number => UsdLayerEditValue.FromDouble(value.NumberValue),
        ViewerPhysicsValueKind.Integer => UsdLayerEditValue.FromInt64(value.IntegerValue),
        ViewerPhysicsValueKind.Text => UsdLayerEditValue.FromString(value.TextValue),
        ViewerPhysicsValueKind.Token => UsdLayerEditValue.FromToken(value.TextValue),
        ViewerPhysicsValueKind.Vector3 => UsdLayerEditValue.FromVec3f(new UsdVec3f(
            (float)value.VectorValue.X, (float)value.VectorValue.Y, (float)value.VectorValue.Z)),
        _ => throw new NotSupportedException("The physics value is outside the supported authored domain.")
    };

    private static string TypeName(ViewerPhysicsValueKind kind) => kind switch
    {
        ViewerPhysicsValueKind.Bool => "bool",
        ViewerPhysicsValueKind.Number => "double",
        ViewerPhysicsValueKind.Integer => "int64",
        ViewerPhysicsValueKind.Text => "string",
        ViewerPhysicsValueKind.Token => "token",
        ViewerPhysicsValueKind.Vector3 => "float3",
        _ => throw new NotSupportedException("The physics declaration is outside the supported authored domain.")
    };
}
