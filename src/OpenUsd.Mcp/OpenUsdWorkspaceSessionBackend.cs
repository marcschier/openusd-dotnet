// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenUsd.Rendering;
namespace OpenUsd.Mcp;

/// <summary>Creates scheduler-backed OpenUSD workspace sessions.</summary>
public sealed class OpenUsdWorkspaceSessionBackendFactory : IWorkspaceSessionBackendFactory
{
    /// <inheritdoc/>
    public async ValueTask<IWorkspaceSessionBackend> CreateAsync(
        WorkspaceSessionBackendContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        UsdStageScheduler scheduler = UsdStageScheduler.Create(context.OverlayPath);
        try
        {
            string sourceIdentifier = context.SourcePath.Replace('\\', '/');
            await scheduler.EditAsync(
                stage =>
                {
                    using UsdLayer root = stage.GetRootLayer();
                    root.AddSublayer(sourceIdentifier);
                    stage.SetEditTarget(root);
                    if (!stage.GetLayerStackIdentifiers().Any(
                            identifier => LayerIdentifierEquals(
                                identifier,
                                sourceIdentifier)))
                    {
                        throw new WorkspaceSourceCompositionException();
                    }
                    stage.Save();
                },
                UsdStageInvalidationKind.Composition,
                cancellationToken).ConfigureAwait(false);
            return new OpenUsdWorkspaceSessionBackend(context, scheduler);
        }
        catch
        {
            await scheduler.DisposeAsync().ConfigureAwait(false);
            throw;
        }

    }

    private static bool LayerIdentifierEquals(string left, string right)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            left.Replace('\\', '/'),
            right.Replace('\\', '/'),
            comparison);
    }
}

internal sealed class WorkspaceSourceCompositionException : Exception
{
    internal WorkspaceSourceCompositionException()
        : base(
            "The source layer could not be composed. Verify its USDA syntax and plugin " +
            "availability.")
    {
    }
}

internal sealed class OpenUsdWorkspaceSessionBackend : IWorkspaceSessionBackend
{
    private readonly WorkspaceSessionBackendContext _context;
    private readonly object _lifetimeGate = new();
    private readonly HashSet<UsdStageRenderSource> _renderSources =
        new(ReferenceEqualityComparer.Instance);
    private readonly SemaphoreSlim _renderSourceGate = new(1, 1);
    private readonly UsdStageScheduler _scheduler;
    private bool _closing;
    private bool _disposed;

    internal OpenUsdWorkspaceSessionBackend(
        WorkspaceSessionBackendContext context,
        UsdStageScheduler scheduler)
    {
        _context = context;
        _scheduler = scheduler;
    }

    public async ValueTask PersistOverlayAndExportAsync(
        string outputPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        WorkspacePathContainment.CreateContainedDirectory(
            _context.OutputDirectory,
            Path.GetDirectoryName(outputPath)!);
        WorkspacePathContainment.RejectReparsePoints(
            _context.OutputDirectory,
            outputPath);
        string temporaryPath = Path.Combine(
            Path.GetDirectoryName(outputPath)!,
            string.Concat(
                Path.GetFileNameWithoutExtension(outputPath),
                ".new",
                Path.GetExtension(outputPath)));
        try
        {
            File.Delete(temporaryPath);
            await _scheduler.InvokeAsync(
                stage =>
                {
                    stage.Save();
                    stage.Export(temporaryPath);
                },
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public ValueTask CreateCheckpointAsync(
        WorkspaceCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        cancellationToken.ThrowIfCancellationRequested();
        string checkpointPath = ResolveCheckpointPath(_context, checkpoint);
        WorkspacePathContainment.CreateContainedDirectory(
            _context.OutputDirectory,
            Path.GetDirectoryName(checkpointPath)!);
        File.Copy(_context.OverlayPath, checkpointPath, overwrite: false);
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteCheckpointAsync(
        WorkspaceCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        cancellationToken.ThrowIfCancellationRequested();
        string checkpointPath = ResolveCheckpointPath(_context, checkpoint);
        WorkspacePathContainment.RejectReparsePoints(
            _context.OutputDirectory,
            checkpointPath);
        File.Delete(checkpointPath);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<ulong> ApplyAsync(
        WorkspaceEditBatch batch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return await _scheduler.EditAsync(
            stage =>
            {
                foreach (WorkspaceEditOperation operation in batch.Operations)
                {
                    Apply(stage, operation);
                }

                stage.Save();
                return stage.ChangeSerial;
            },
            StrongestInvalidation(batch),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ulong> RollbackAsync(
        WorkspaceCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        string checkpointPath = ResolveCheckpointPath(_context, checkpoint);
        string temporaryPath = string.Concat(_context.OverlayPath, ".rollback");
        return await _scheduler.EditAsync(
            stage =>
            {
                try
                {
                    File.Copy(checkpointPath, temporaryPath, overwrite: true);
                    File.Move(temporaryPath, _context.OverlayPath, overwrite: true);
                    using UsdLayer root = stage.GetRootLayer();
                    if (!root.Reload(force: true))
                    {
                        throw new InvalidDataException(
                            "The restored overlay could not be reloaded.");
                    }
                    stage.Reload();
                    stage.SetEditTarget(root);
                    return stage.ChangeSerial;
                }
                finally
                {
                    File.Delete(temporaryPath);
                }
            },
            UsdStageInvalidationKind.Full,
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<ulong> GetStageRevisionAsync(CancellationToken cancellationToken) =>
        _scheduler.InvokeAsync(static stage => stage.ChangeSerial, cancellationToken);

    public ValueTask<WorkspaceSceneStatistics> InspectSceneAsync(
        CancellationToken cancellationToken) =>
        _scheduler.InvokeAsync(
            stage =>
            {
                StageStatisticsAnalysis statistics = StageStatisticsAnalyzer.Analyze(
                    stage,
                    StageStatisticsAnalysisLimits.Inspection,
                    cancellationToken);
                return new WorkspaceSceneStatistics(
                    statistics.DefaultPrimPath,
                    statistics.PrimCount,
                    statistics.MeshCount,
                    statistics.CurveVertexCount,
                    statistics.MeshVertexCount,
                    statistics.FaceCount,
                    statistics.RootPrimCount,
                    statistics.LeafPrimCount,
                    statistics.MaximumDepth);
            },
            cancellationToken);

    public async ValueTask<IReadOnlyList<CameraState>> CreatePreviewCamerasAsync(
        string? cameraPath,
        bool orbit,
        int width,
        int height,
        IReadOnlyList<double> timeCodes,
        CancellationToken cancellationToken) =>
        await _scheduler.InvokeAsync(
            stage =>
            {
                if (cameraPath is not null)
                {
                    return timeCodes
                        .Select(timeCode => CameraState.FromStageCamera(
                            stage,
                            cameraPath,
                            timeCode,
                            width,
                            height))
                        .ToArray();
                }

                if (!orbit)
                {
                    return Enumerable.Repeat(CameraState.Default, timeCodes.Count)
                        .ToArray();
                }

                const float verticalFieldOfView = MathF.PI / 4f;
                float aspectRatio = (float)width / height;
                var cameras = new CameraState[timeCodes.Count];
                for (int index = 0; index < cameras.Length; index++)
                {
                    UsdBounds3d bounds = stage.GetWorldBounds(timeCodes[index]);
                    if (!BoundsCameraFraming.TryCreate(
                            bounds,
                            verticalFieldOfView,
                            aspectRatio,
                            out BoundsCameraFraming framing))
                    {
                        cameras[index] = CameraState.Default;
                        continue;
                    }

                    float angle = (2f * MathF.PI * index) / cameras.Length;
                    Vector3 orbitDirection = Vector3.Normalize(new Vector3(
                        MathF.Sin(angle) * framing.Distance,
                        framing.Distance * 0.25f,
                        MathF.Cos(angle) * framing.Distance));
                    Vector3 position = framing.Target + (orbitDirection * framing.Distance);
                    cameras[index] = new CameraState(
                        Matrix4x4.CreateLookAt(position, framing.Target, Vector3.UnitY),
                        Matrix4x4.CreatePerspectiveFieldOfView(
                            verticalFieldOfView,
                            aspectRatio,
                            framing.NearPlane,
                            framing.FarPlane));
                }

                return cameras;
            },
            cancellationToken).ConfigureAwait(false);

    public async ValueTask<UsdStageRenderSource> AcquireRenderSourceAsync(
        CancellationToken cancellationToken = default)
    {
        await _renderSourceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_lifetimeGate)
            {
                ObjectDisposedException.ThrowIf(_closing, this);
            }

            UsdStageRenderSource source = await _scheduler
                .AcquireRenderSourceAsync(cancellationToken)
                .ConfigureAwait(false);
            lock (_lifetimeGate)
            {
                if (_closing)
                {
                    source.Dispose();
                    throw new ObjectDisposedException(nameof(OpenUsdWorkspaceSessionBackend));
                }

                try
                {
                    _renderSources.Add(source);
                    source.SetDisposeCallback(RemoveRenderSource);
                }
                catch
                {
                    _renderSources.Remove(source);
                    source.Dispose();
                    throw;
                }
            }

            return source;
        }
        finally
        {
            _renderSourceGate.Release();
        }
    }

    public async ValueTask PersistAsync(
        WorkspaceSessionManifest manifest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        string manifestPath = Path.Combine(_context.OutputDirectory, "manifest.json");
        string temporaryPath = string.Concat(manifestPath, ".new");
        try
        {
            File.Delete(temporaryPath);
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        manifest,
                        WorkspaceJournalJsonContext.Default.WorkspaceSessionManifest,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, manifestPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_lifetimeGate)
        {
            if (_disposed)
            {
                return;
            }

            _closing = true;
        }

        await _renderSourceGate.WaitAsync().ConfigureAwait(false);
        try
        {
            UsdStageRenderSource[] sources;
            lock (_lifetimeGate)
            {
                if (_disposed)
                {
                    return;
                }

                sources = _renderSources.ToArray();
                _renderSources.Clear();
            }

            foreach (UsdStageRenderSource source in sources)
            {
                source.Dispose();
            }

            await _scheduler.DisposeAsync().ConfigureAwait(false);
            lock (_lifetimeGate)
            {
                _disposed = true;
            }
        }
        finally
        {
            _renderSourceGate.Release();
        }
    }

    internal int TrackedRenderSourceCount
    {
        get
        {
            lock (_lifetimeGate)
            {
                return _renderSources.Count;
            }
        }
    }

    private static void Apply(UsdStage stage, WorkspaceEditOperation operation)
    {
        switch (operation)
        {
            case DefinePrimWorkspaceEdit define:
                _ = stage.DefinePrim(define.PrimPath, define.TypeName);
                break;
            case SetActiveWorkspaceEdit setActive:
                stage.GetPrim(setActive.PrimPath).SetActive(setActive.Active);
                break;
            case RemovePrimWorkspaceEdit deactivate:
                stage.GetPrim(deactivate.PrimPath).SetActive(false);
                break;
            case SetDoubleWorkspaceEdit setDouble:
                UsdPrim prim = stage.GetPrim(setDouble.PrimPath);
                if (setDouble.TimeCode is double timeCode)
                {
                    prim.SetDouble(setDouble.AttributeName, setDouble.Value, timeCode);
                }
                else
                {
                    prim.SetDouble(setDouble.AttributeName, setDouble.Value);
                }

                break;
            case SetBoolWorkspaceEdit setBool:
                prim = stage.GetPrim(setBool.PrimPath);
                if (setBool.TimeCode is double boolTimeCode)
                {
                    prim.SetBool(setBool.AttributeName, setBool.Value, boolTimeCode);
                }
                else
                {
                    prim.SetBool(setBool.AttributeName, setBool.Value);
                }

                break;
            case SetInt64WorkspaceEdit setInt64:
                prim = stage.GetPrim(setInt64.PrimPath);
                if (setInt64.TimeCode is double int64TimeCode)
                {
                    prim.SetInt64(setInt64.AttributeName, setInt64.Value, int64TimeCode);
                }
                else
                {
                    prim.SetInt64(setInt64.AttributeName, setInt64.Value);
                }

                break;
            case SetStringWorkspaceEdit setString:
                prim = stage.GetPrim(setString.PrimPath);
                if (setString.TimeCode is double stringTimeCode)
                {
                    prim.SetString(setString.AttributeName, setString.Value, stringTimeCode);
                }
                else
                {
                    prim.SetString(setString.AttributeName, setString.Value);
                }

                break;
            case SetTokenWorkspaceEdit setToken:
                prim = stage.GetPrim(setToken.PrimPath);
                if (setToken.TimeCode is double tokenTimeCode)
                {
                    prim.SetToken(setToken.AttributeName, setToken.Value, tokenTimeCode);
                }
                else
                {
                    prim.SetToken(setToken.AttributeName, setToken.Value);
                }

                break;
            case SetFloat3WorkspaceEdit setFloat3:
                prim = stage.GetPrim(setFloat3.PrimPath);
                var vector = new UsdVec3f(setFloat3.X, setFloat3.Y, setFloat3.Z);
                if (setFloat3.TimeCode is double float3TimeCode)
                {
                    prim.SetVec3f(setFloat3.AttributeName, vector, float3TimeCode);
                }
                else
                {
                    prim.SetVec3f(setFloat3.AttributeName, vector);
                }

                break;
            case SetColor3fWorkspaceEdit setColor3f:
                prim = stage.GetPrim(setColor3f.PrimPath);
                var color = new UsdVec3f(
                    setColor3f.Red,
                    setColor3f.Green,
                    setColor3f.Blue);
                if (setColor3f.TimeCode is double colorTimeCode)
                {
                    prim.SetColor3f(setColor3f.AttributeName, color, colorTimeCode);
                }
                else
                {
                    prim.SetColor3f(setColor3f.AttributeName, color);
                }

                break;
            case ClearOverlayAttributeWorkspaceEdit clear:
                stage.GetPrim(clear.PrimPath).GetAttribute(clear.AttributeName).ClearValue();
                break;
            case ClearAttributeWorkspaceEdit clear:
                stage.GetPrim(clear.PrimPath).GetAttribute(clear.AttributeName).ClearValue();
                break;
            default:
                throw new WorkspaceEditUnsupportedException(operation.Kind);
        }
    }

    private static UsdStageInvalidationKind StrongestInvalidation(WorkspaceEditBatch batch)
    {
        foreach (WorkspaceEditOperation operation in batch.Operations)
        {
            if (operation is DefinePrimWorkspaceEdit or
                SetActiveWorkspaceEdit or
                RemovePrimWorkspaceEdit)
            {
                return UsdStageInvalidationKind.Topology;
            }
        }

        return UsdStageInvalidationKind.Property;
    }

    internal static string ResolveCheckpointPath(
        WorkspaceSessionBackendContext context,
        WorkspaceCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!Guid.TryParseExact(checkpoint.CheckpointId, "N", out _))
        {
            throw new WorkspacePathContainmentException(
                "Checkpoint identifiers must use the canonical 32-digit format.");
        }

        string expectedFileName = Path.Combine(
            "checkpoints",
            string.Concat(checkpoint.CheckpointId, ".usda"));
        if (!string.Equals(
                checkpoint.FileName,
                expectedFileName,
                StringComparison.Ordinal))
        {
            throw new WorkspacePathContainmentException(
                "The checkpoint filename does not match its identifier.");
        }

        return WorkspacePathContainment.ResolveContainedPath(
            context.OutputDirectory,
            expectedFileName);
    }

    private void RemoveRenderSource(UsdStageRenderSource source)
    {
        lock (_lifetimeGate)
        {
            _renderSources.Remove(source);
        }
    }
}

[JsonSerializable(typeof(WorkspaceSessionManifest))]
internal sealed partial class WorkspaceJournalJsonContext : JsonSerializerContext;
