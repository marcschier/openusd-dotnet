// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Viewer;

/// <summary>Reports what applying one authoring step did.</summary>
/// <param name="Applied">The property edits that reached the stage.</param>
/// <param name="Rejected">The property edits the stage refused.</param>
/// <param name="Message">A sentence describing the outcome, always non-empty.</param>
/// <param name="Edits">
/// The change the whole step produced, identified by the serials that bracket it.
/// </param>
internal readonly record struct ViewerPhysicsAuthoringResult(
    int Applied,
    int Rejected,
    string Message,
    IReadOnlyList<ViewerPhysicsStageEdit> Edits)
{
    /// <summary>Gets a value indicating whether every edit reached the stage.</summary>
    internal bool Succeeded => Rejected == 0 && Applied > 0;
}

/// <summary>Authors physics property opinions through the stage owner.</summary>
/// <remarks>
/// The seam exists so the inspector, the undo history, and the toolbar can be driven
/// deterministically without a stage, and so the one implementation that does touch a stage stays
/// the only place that knows about edit targets and change serials.
/// </remarks>
internal interface IViewerPhysicsAuthoringStage
{
    /// <summary>Applies one authoring step as a single transaction.</summary>
    /// <param name="step">The step to author.</param>
    /// <param name="cancellationToken">Cancels the step before it runs.</param>
    /// <returns>What the step did.</returns>
    ValueTask<ViewerPhysicsAuthoringResult> ApplyAsync(
        ViewerPhysicsEditStep step,
        CancellationToken cancellationToken);

    /// <summary>Reads the current value of one property, so an edit can record what it replaced.</summary>
    /// <param name="primPath">The prim the property is authored on.</param>
    /// <param name="name">The authored property name.</param>
    /// <param name="kind">The value the property carries.</param>
    /// <param name="cancellationToken">Cancels the read before it runs.</param>
    /// <returns>The current value, or the unauthored value when the prim carries no opinion.</returns>
    ValueTask<ViewerPhysicsValue> ReadAsync(
        string primPath,
        string name,
        ViewerPhysicsValueKind kind,
        CancellationToken cancellationToken);
}

/// <summary>
/// Authors physics property opinions into the session overlay's user-edit layer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every edit goes through the scheduler.</b> The stage is owned by one thread; authoring from
/// the UI thread would race the physics extraction, the render source, and the preview applier.
/// One <see cref="UsdStageScheduler.EditAsync{T}"/> call carries the whole step, so a step that
/// authors several properties produces exactly one observed change instead of one per property.
/// </para>
/// <para>
/// <b>Edits land in the user layer, never in the root layer.</b> The stage's session edit target is
/// redirected into the overlay's user layer whenever a session overlay is active, which is the
/// project's convention: simulation results compose above user edits, and user edits compose above
/// the authored file, so nothing the inspector writes can ever modify the file on disk by accident.
/// The previous edit target is restored before the call returns.
/// </para>
/// <para>
/// <b>The step reports its own change serials.</b> The controller suppresses exactly the change it
/// authored, so the pair that brackets the whole step is carried out with the result rather than
/// being guessed from timing.
/// </para>
/// </remarks>
internal sealed class ViewerPhysicsSchedulerAuthoringStage(UsdStageScheduler scheduler)
    : IViewerPhysicsAuthoringStage
{
    /// <inheritdoc/>
    public async ValueTask<ViewerPhysicsAuthoringResult> ApplyAsync(
        ViewerPhysicsEditStep step,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(step);
        if (step.Edits.Count == 0)
        {
            return new ViewerPhysicsAuthoringResult(
                0, 0, "The authoring step carried no property edit.", []);
        }

        IReadOnlyList<ViewerPhysicsEdit> edits = step.Edits;
        try
        {
            (int applied, int rejected, string failure, ulong before, ulong after) =
                await scheduler.EditAsync(
                    stage => AuthorStep(stage, edits),
                    UsdStageInvalidationKind.Property,
                    cancellationToken).ConfigureAwait(false);

            ViewerPhysicsStageEdit[] changes = after > before
                ? [new ViewerPhysicsStageEdit(before, after)]
                : [];
            string message = rejected == 0
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Authored {applied} physics propert{(applied == 1 ? "y" : "ies")}.")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Authored {applied} of {edits.Count}; {rejected} refused: {failure}");
            return new ViewerPhysicsAuthoringResult(applied, rejected, message, changes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw ViewerPhysicsTransportAdapter.Translate(exception);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<ViewerPhysicsValue> ReadAsync(
        string primPath,
        string name,
        ViewerPhysicsValueKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(primPath);
        ArgumentNullException.ThrowIfNull(name);
        try
        {
            return await scheduler.InvokeAsync(
                stage => Read(stage, primPath, name, kind),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw ViewerPhysicsTransportAdapter.Translate(exception);
        }
    }

    private static (int Applied, int Rejected, string Failure, ulong Before, ulong After)
        AuthorStep(UsdStage stage, IReadOnlyList<ViewerPhysicsEdit> edits)
    {
        ulong before = stage.ChangeSerial;
        string previousTarget = stage.EditTargetLayerIdentifier;
        var applied = 0;
        var rejected = 0;
        string failure = string.Empty;
        try
        {
            // While a session overlay is active this redirects into the overlay's user layer, so
            // the inspector never authors into the file the stage was opened from.
            stage.SetEditTargetToSessionLayer();
            for (int index = 0; index < edits.Count; index++)
            {
                ViewerPhysicsEdit edit = edits[index];
                if (TryAuthor(stage, edit, out string? why))
                {
                    applied++;
                    continue;
                }

                rejected++;
                if (failure.Length == 0)
                {
                    failure = why ?? "The stage refused the property edit.";
                }
            }
        }
        finally
        {
            RestoreEditTarget(stage, previousTarget);
        }

        return (applied, rejected, failure, before, stage.ChangeSerial);
    }

    private static void RestoreEditTarget(UsdStage stage, string previousTarget)
    {
        if (string.Equals(previousTarget, stage.RootLayerIdentifier, StringComparison.Ordinal))
        {
            stage.SetEditTargetToRootLayer();
            return;
        }

        stage.SetEditTargetToSessionLayer();
    }

    private static bool TryAuthor(UsdStage stage, ViewerPhysicsEdit edit, out string? failure)
    {
        failure = null;
        if (!stage.HasPrim(edit.PrimPath))
        {
            failure = string.Create(
                CultureInfo.InvariantCulture,
                $"The stage no longer carries {edit.PrimPath}.");
            return false;
        }

        UsdPrim prim = stage.GetPrim(edit.PrimPath);
        ViewerPhysicsValue value = edit.After;
        try
        {
            if (!value.IsAuthored)
            {
                // Clearing is how an undo removes an opinion the edit created. A blocked value
                // would keep an opinion that says "nothing composes here", which is not the same
                // as the prim having had no opinion at all. A property that was never created is
                // already in the state the clear is asking for.
                if (HasAttribute(prim, edit.Name))
                {
                    prim.GetAttribute(edit.Name).ClearValue();
                }

                return true;
            }

            switch (value.Kind)
            {
                case ViewerPhysicsValueKind.Bool:
                    prim.SetBool(edit.Name, value.BoolValue);
                    return true;
                case ViewerPhysicsValueKind.Number:
                    prim.SetDouble(edit.Name, value.NumberValue);
                    return true;
                case ViewerPhysicsValueKind.Integer:
                    prim.SetInt64(edit.Name, value.IntegerValue);
                    return true;
                case ViewerPhysicsValueKind.Text:
                    prim.SetString(edit.Name, value.TextValue);
                    return true;
                case ViewerPhysicsValueKind.Token:
                    prim.SetToken(edit.Name, value.TextValue);
                    return true;
                case ViewerPhysicsValueKind.Vector3:
                    prim.SetVec3f(
                        edit.Name,
                        new UsdVec3f(
                            (float)value.VectorValue.X,
                            (float)value.VectorValue.Y,
                            (float)value.VectorValue.Z));
                    return true;
                default:
                    failure = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{edit.Name} carries a value the inspector cannot author.");
                    return false;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A refused property is a diagnostic, not a crash: the rest of the step still applies
            // and the caller is told exactly which property the stage would not take.
            failure = string.Create(
                CultureInfo.InvariantCulture,
                $"{edit.Name}: {exception.Message}");
            return false;
        }
    }

    private static bool HasAttribute(UsdPrim prim, string name)
    {
        string[] names = prim.GetAttributeNames();
        for (int index = 0; index < names.Length; index++)
        {
            if (string.Equals(names[index], name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static ViewerPhysicsValue Read(
        UsdStage stage,
        string primPath,
        string name,
        ViewerPhysicsValueKind kind)
    {
        if (!stage.HasPrim(primPath))
        {
            return ViewerPhysicsValue.Unauthored(kind);
        }

        UsdPrim prim = stage.GetPrim(primPath);
        if (!HasAttribute(prim, name))
        {
            // A property the prim never declared is unauthored, which is exactly what an editor
            // has to record so that undoing an edit can remove the opinion it created. Reading it
            // through the attribute accessor would throw instead of reporting that.
            return ViewerPhysicsValue.Unauthored(kind);
        }

        UsdAttribute attribute = prim.GetAttribute(name);
        if (!attribute.IsAuthored || !attribute.TryGetValue(out UsdScalarValue scalar))
        {
            return ViewerPhysicsValue.Unauthored(kind);
        }

        return scalar.Kind switch
        {
            UsdScalarKind.Boolean => ViewerPhysicsValue.FromBool(scalar.BoolValue),
            UsdScalarKind.Number => ViewerPhysicsValue.FromNumber(scalar.DoubleValue),
            UsdScalarKind.Signed64 => ViewerPhysicsValue.FromInteger(scalar.Int64Value),
            UsdScalarKind.Text => ViewerPhysicsValue.FromText(scalar.StringValue),
            UsdScalarKind.Token => ViewerPhysicsValue.FromToken(scalar.TokenValue),
            UsdScalarKind.Vector3 or UsdScalarKind.Color3 => ViewerPhysicsValue.FromVector(
                new ViewerPhysicsVector3(
                    scalar.Vec3fValue.X,
                    scalar.Vec3fValue.Y,
                    scalar.Vec3fValue.Z)),
            _ => ViewerPhysicsValue.Unauthored(kind),
        };
    }
}
