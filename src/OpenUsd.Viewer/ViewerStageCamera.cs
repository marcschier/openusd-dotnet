// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using OpenUsd.Geom;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal readonly record struct ViewerStageCameraSnapshot : IUsdDetachedResult
{
    internal ViewerStageCameraSnapshot(
        string primPath,
        double timeCode,
        UsdMatrix4d localToWorld,
        UsdMatrix4d worldToView,
        UsdGeomCameraState optics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeCode),
                "The stage-camera time code must be finite.");
        }
        if (!ViewerStageCameraMatrixConversion.IsFinite(localToWorld))
        {
            throw new ArgumentException(
                "The stage-camera local-to-world matrix must contain only finite values.",
                nameof(localToWorld));
        }
        if (!ViewerStageCameraMatrixConversion.IsFinite(worldToView))
        {
            throw new ArgumentException(
                "The stage-camera world-to-view matrix must contain only finite values.",
                nameof(worldToView));
        }
        ValidateOptics(optics);

        PrimPath = primPath;
        TimeCode = timeCode;
        LocalToWorld = localToWorld;
        WorldToView = worldToView;
        Optics = optics;
    }

    internal string PrimPath { get; }

    internal double TimeCode { get; }

    internal UsdMatrix4d LocalToWorld { get; }

    internal UsdMatrix4d WorldToView { get; }

    internal UsdGeomCameraState Optics { get; }

    internal UsdGeomCameraProjection Projection => Optics.Projection;

    private static void ValidateOptics(UsdGeomCameraState optics)
    {
        _ = new UsdGeomCameraState(
            optics.Projection,
            optics.WindowLeft,
            optics.WindowRight,
            optics.WindowBottom,
            optics.WindowTop,
            optics.ClippingNear,
            optics.ClippingFar,
            optics.FocalLength,
            optics.HorizontalAperture,
            optics.VerticalAperture,
            optics.HorizontalApertureOffset,
            optics.VerticalApertureOffset,
            optics.FocusDistance,
            optics.FStop);
    }
}

internal readonly record struct ViewerStageCameraRequest
{
    internal ViewerStageCameraRequest(string primPath, double timeCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeCode),
                "The stage-camera time code must be finite.");
        }

        PrimPath = primPath;
        TimeCode = timeCode;
    }

    internal string PrimPath { get; }

    internal double TimeCode { get; }
}

internal static class ViewerStageCameraSnapshotFactory
{
    internal static ViewerStageCameraSnapshot Create(
        string primPath,
        double timeCode,
        UsdMatrix4d localToWorld,
        UsdGeomCameraState optics)
    {
        if (!localToWorld.TryInvert(out UsdMatrix4d worldToView))
        {
            throw new InvalidOperationException(
                $"Camera '{primPath}' has a non-finite or non-invertible " +
                $"world transform at time {timeCode}.");
        }

        return new ViewerStageCameraSnapshot(
            primPath,
            timeCode,
            localToWorld,
            worldToView,
            optics);
    }
}


internal readonly record struct ViewerStageCameraMenuEntry(string Path, string Name);

internal static class ViewerStageCameraDiscovery
{
    private const string PrimaryCameraPrimMetadata = "primaryCameraPrim";

    internal static ViewerStageCameraMenuEntry[] ListCameras(UsdStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return stage.Traverse()
            .Where(static prim => UsdGeomCamera.TryWrap(prim, out _))
            .Select(static prim => new ViewerStageCameraMenuEntry(
                prim.Path,
                GetPrimName(prim.Path)))
            .ToArray();
    }

    internal static string? GetPrimaryCameraPath(UsdStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        string? authored = TryGetPrimaryCameraFromRootLayer(stage.RootLayerIdentifier);
        return NormalizePrimaryCameraPath(authored);
    }

    internal static string? NormalizePrimaryCameraPath(string? authored)
    {
        if (string.IsNullOrWhiteSpace(authored))
        {
            return null;
        }
        string value = authored.Trim();
        if (value.Length >= 2 && value[0] == '<' && value[^1] == '>')
        {
            value = value[1..^1].Trim();
        }
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1].Trim();
        }
        return value.Length > 0 &&
            value[0] == '/' &&
            !value.Contains('\\')
                ? value
                : null;
    }

    private static string? TryGetPrimaryCameraFromRootLayer(string rootLayerIdentifier)
    {
        if (string.IsNullOrWhiteSpace(rootLayerIdentifier) ||
            !File.Exists(rootLayerIdentifier))
        {
            return null;
        }
        try
        {
            foreach (string line in File.ReadLines(rootLayerIdentifier))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith(
                        PrimaryCameraPrimMetadata,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                int equals = trimmed.IndexOf('=', StringComparison.Ordinal);
                if (equals < 0)
                {
                    continue;
                }
                return trimmed[(equals + 1)..].Trim();
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        return null;
    }

    private static string GetPrimName(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash >= 0 && slash + 1 < path.Length ? path[(slash + 1)..] : path;
    }
}

internal enum ViewerStageCameraQueryOutcome
{
    Ready,
    NoSelection,
    MissingPrim,
    NotCamera,
    InvalidCamera,
}

internal readonly record struct ViewerStageCameraQueryResult : IUsdDetachedResult
{
    private ViewerStageCameraQueryResult(
        ViewerStageCameraQueryOutcome outcome,
        string? primPath,
        ViewerStageCameraSnapshot snapshot,
        string? error)
    {
        Outcome = outcome;
        PrimPath = primPath;
        Snapshot = snapshot;
        Error = error;
    }

    internal ViewerStageCameraQueryOutcome Outcome { get; }

    internal string? PrimPath { get; }

    internal ViewerStageCameraSnapshot Snapshot { get; }

    internal string? Error { get; }

    internal static ViewerStageCameraQueryResult Ready(
        ViewerStageCameraSnapshot snapshot) =>
        new(
            ViewerStageCameraQueryOutcome.Ready,
            snapshot.PrimPath,
            snapshot,
            error: null);

    internal static ViewerStageCameraQueryResult NoSelection() =>
        new(
            ViewerStageCameraQueryOutcome.NoSelection,
            primPath: null,
            default,
            "Select a UsdGeomCamera prim before using the selected camera.");

    internal static ViewerStageCameraQueryResult Missing(string primPath) =>
        new(
            ViewerStageCameraQueryOutcome.MissingPrim,
            primPath,
            default,
            $"The selected camera prim '{primPath}' no longer exists.");

    internal static ViewerStageCameraQueryResult NotCamera(string primPath) =>
        new(
            ViewerStageCameraQueryOutcome.NotCamera,
            primPath,
            default,
            $"The selected prim '{primPath}' is not a UsdGeomCamera.");

    internal static ViewerStageCameraQueryResult Invalid(
        string primPath,
        string error) =>
        new(
            ViewerStageCameraQueryOutcome.InvalidCamera,
            primPath,
            default,
            error);
}

internal interface IViewerStageCameraSource
{
    ValueTask<ViewerStageCameraQueryResult> QueryAsync(
        ViewerStageCameraRequest request,
        CancellationToken cancellationToken);
}

internal sealed class ViewerSchedulerStageCameraSource : IViewerStageCameraSource
{
    private readonly UsdStageScheduler _scheduler;

    internal ViewerSchedulerStageCameraSource(UsdStageScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
    }

    public ValueTask<ViewerStageCameraQueryResult> QueryAsync(
        ViewerStageCameraRequest request,
        CancellationToken cancellationToken) =>
        _scheduler.InvokeAsync(
            stage => QueryStage(stage, request),
            cancellationToken);

    private static ViewerStageCameraQueryResult QueryStage(
        UsdStage stage,
        in ViewerStageCameraRequest request)
    {
        if (!stage.HasPrim(request.PrimPath))
        {
            return ViewerStageCameraQueryResult.Missing(request.PrimPath);
        }

        UsdPrim prim = stage.GetPrim(request.PrimPath);
        if (!UsdGeomCamera.TryWrap(prim, out UsdGeomCamera camera))
        {
            return ViewerStageCameraQueryResult.NotCamera(request.PrimPath);
        }

        try
        {
            UsdMatrix4d localToWorld = camera.Xformable.GetWorldTransform(request.TimeCode);
            UsdGeomCameraState optics = camera.GetState(request.TimeCode);
            ViewerStageCameraSnapshot snapshot = ViewerStageCameraSnapshotFactory.Create(
                request.PrimPath,
                request.TimeCode,
                localToWorld,
                optics);
            return ViewerStageCameraQueryResult.Ready(snapshot);
        }
        catch (Exception exception)
        {
            return ViewerStageCameraQueryResult.Invalid(
                request.PrimPath,
                $"Camera '{request.PrimPath}' is invalid at time {request.TimeCode}: " +
                exception.Message);
        }
    }
}

internal static class ViewerStageCameraQuery
{
    internal static ValueTask<ViewerStageCameraQueryResult> QueryAsync(
        IViewerStageCameraSource source,
        string? selectedPrimPath,
        StageTime time,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(selectedPrimPath))
        {
            return ValueTask.FromResult(ViewerStageCameraQueryResult.NoSelection());
        }

        return source.QueryAsync(
            new ViewerStageCameraRequest(selectedPrimPath, time.TimeCode),
            cancellationToken);
    }
}

internal static class ViewerStageCameraSmokeContract
{
    internal const string ScenarioName = "stage-camera-backend-smoke";
    internal const string SourceName = nameof(ViewerSchedulerStageCameraSource);
    internal const double InitialTimeCode = 0d;
    internal const double SampledTimeCode = 24d;

    internal static ViewerStageCameraSnapshot RequireReady(
        in ViewerStageCameraQueryResult result,
        string expectedPrimPath,
        double expectedTimeCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPrimPath);
        if (!double.IsFinite(expectedTimeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(expectedTimeCode));
        }
        if (result.Outcome != ViewerStageCameraQueryOutcome.Ready)
        {
            throw new InvalidOperationException(
                result.Error ??
                $"Camera '{expectedPrimPath}' was not ready at time {expectedTimeCode}.");
        }
        if (!string.Equals(
                result.Snapshot.PrimPath,
                expectedPrimPath,
                StringComparison.Ordinal) ||
            result.Snapshot.TimeCode != expectedTimeCode)
        {
            throw new InvalidDataException(
                "The stage-camera source returned a stale path or time sample.");
        }
        return result.Snapshot;
    }

    internal static StageRenderState ApplyCamera(
        StageRenderState current,
        StageRenderState expected,
        string primPath,
        double timeCode,
        in CameraState camera)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        if (!ReferenceEquals(current, expected))
        {
            throw new InvalidOperationException(
                "The stage-camera query completed against a stale Viewer render state.");
        }
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(timeCode));
        }
        if (camera.Mode != CameraMode.Matrices)
        {
            throw new InvalidDataException(
                "An authored stage camera must produce explicit view/projection matrices.");
        }

        return current
            .WithSelection(new SelectionState([primPath]))
            .WithTime(new StageTime(timeCode))
            .WithCamera(camera);
    }

    internal static StageRenderState ApplyAutomatic(
        StageRenderState current,
        StageRenderState expected,
        string primPath,
        double timeCode)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        if (!ReferenceEquals(current, expected))
        {
            throw new InvalidOperationException(
                "The automatic reset targeted a stale Viewer render state.");
        }
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(timeCode));
        }
        return current
            .WithSelection(new SelectionState([primPath]))
            .WithTime(new StageTime(timeCode))
            .WithCamera(CameraState.Default);
    }

    internal static string ComputeSnapshotSha256(
        in ViewerStageCameraSnapshot snapshot)
    {
        using var stream = new MemoryStream(512);
        using (var writer = new BinaryWriter(
            stream,
            Encoding.UTF8,
            leaveOpen: true))
        {
            writer.Write(1);
            writer.Write(snapshot.PrimPath);
            writer.Write(snapshot.TimeCode);
            WriteMatrix(writer, snapshot.LocalToWorld);
            WriteMatrix(writer, snapshot.WorldToView);
            writer.Write((int)snapshot.Projection);
            writer.Write(snapshot.Optics.WindowLeft);
            writer.Write(snapshot.Optics.WindowRight);
            writer.Write(snapshot.Optics.WindowBottom);
            writer.Write(snapshot.Optics.WindowTop);
            writer.Write(snapshot.Optics.ClippingNear);
            writer.Write(snapshot.Optics.ClippingFar);
            writer.Write(snapshot.Optics.FocalLength);
            writer.Write(snapshot.Optics.HorizontalAperture);
            writer.Write(snapshot.Optics.VerticalAperture);
            writer.Write(snapshot.Optics.HorizontalApertureOffset);
            writer.Write(snapshot.Optics.VerticalApertureOffset);
            writer.Write(snapshot.Optics.FocusDistance);
            writer.Write(snapshot.Optics.FStop);
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteMatrix(BinaryWriter writer, in UsdMatrix4d value)
    {
        writer.Write(value.M00);
        writer.Write(value.M01);
        writer.Write(value.M02);
        writer.Write(value.M03);
        writer.Write(value.M10);
        writer.Write(value.M11);
        writer.Write(value.M12);
        writer.Write(value.M13);
        writer.Write(value.M20);
        writer.Write(value.M21);
        writer.Write(value.M22);
        writer.Write(value.M23);
        writer.Write(value.M30);
        writer.Write(value.M31);
        writer.Write(value.M32);
        writer.Write(value.M33);
    }
}

internal readonly record struct ViewerStageCameraApertureWindow(
    double Left,
    double Right,
    double Bottom,
    double Top)
{
    internal double Width => Right - Left;

    internal double Height => Top - Bottom;

    internal double CenterX => (Left / 2d) + (Right / 2d);

    internal double CenterY => (Bottom / 2d) + (Top / 2d);
}

internal static class ViewerStageCameraProjectionMath
{
    internal static CameraState CreateCameraState(
        in ViewerStageCameraSnapshot snapshot,
        ViewportDimensions viewport) =>
        new(
            ViewerStageCameraMatrixConversion.ToMatrix4x4(snapshot.WorldToView),
            CreateProjectionMatrix(snapshot, viewport));

    internal static Matrix4x4 CreateProjectionMatrix(
        in ViewerStageCameraSnapshot snapshot,
        ViewportDimensions viewport)
    {
        ViewerStageCameraApertureWindow window = ConformWindow(
            snapshot.Optics.WindowLeft,
            snapshot.Optics.WindowRight,
            snapshot.Optics.WindowBottom,
            snapshot.Optics.WindowTop,
            viewport);
        double windowWidth = window.Width;
        double windowHeight = window.Height;
        double near = snapshot.Optics.ClippingNear;
        double far = snapshot.Optics.ClippingFar;
        double depthRange = far - near;
        if (snapshot.Projection == UsdGeomCameraProjection.Perspective)
        {
            return new Matrix4x4(
                ToFiniteFloat(2d / windowWidth), 0f, 0f, 0f,
                0f, ToFiniteFloat(2d / windowHeight), 0f, 0f,
                ToFiniteFloat((window.Right + window.Left) / windowWidth),
                ToFiniteFloat((window.Top + window.Bottom) / windowHeight),
                ToFiniteFloat(-((far + near) / depthRange)),
                -1f,
                0f, 0f, ToFiniteFloat(-(2d * near * far / depthRange)), 0f);
        }

        return new Matrix4x4(
            ToFiniteFloat(2d / windowWidth), 0f, 0f, 0f,
            0f, ToFiniteFloat(2d / windowHeight), 0f, 0f,
            0f, 0f, ToFiniteFloat(-2d / depthRange), 0f,
            ToFiniteFloat(-((window.Right + window.Left) / windowWidth)),
            ToFiniteFloat(-((window.Top + window.Bottom) / windowHeight)),
            ToFiniteFloat(-((far + near) / depthRange)),
            1f);
    }

    internal static ViewerStageCameraApertureWindow ConformWindow(
        double left,
        double right,
        double bottom,
        double top,
        ViewportDimensions viewport)
    {
        ViewerStageCameraApertureWindow authored = CreateWindow(
            left,
            right,
            bottom,
            top);
        double authoredAspect = authored.Width / authored.Height;
        double viewportAspect = viewport.Width == 0 || viewport.Height == 0
            ? authoredAspect
            : (double)viewport.Width / viewport.Height;
        if (!double.IsFinite(viewportAspect) || viewportAspect <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(viewport),
                "The stage-camera viewport aspect must be finite and positive.");
        }

        if (viewportAspect >= authoredAspect)
        {
            double targetWidth = authored.Height * viewportAspect;
            double expansion = (targetWidth - authored.Width) / 2d;
            return CreateWindow(
                authored.Left - expansion,
                authored.Right + expansion,
                authored.Bottom,
                authored.Top);
        }

        double targetHeight = authored.Width / viewportAspect;
        double verticalExpansion = (targetHeight - authored.Height) / 2d;
        return CreateWindow(
            authored.Left,
            authored.Right,
            authored.Bottom - verticalExpansion,
            authored.Top + verticalExpansion);
    }

    private static float ToFiniteFloat(double value)
    {
        if (!double.IsFinite(value) ||
            value < -float.MaxValue ||
            value > float.MaxValue)
        {
            throw new InvalidOperationException(
                "The stage-camera projection cannot be represented by finite float matrices.");
        }

        float converted = (float)value;
        if (!float.IsFinite(converted) || (converted == 0f && value != 0d))
        {
            throw new InvalidOperationException(
                "The stage-camera projection is outside the finite float range.");
        }
        return converted;
    }

    private static ViewerStageCameraApertureWindow CreateWindow(
        double left,
        double right,
        double bottom,
        double top)
    {
        var window = new ViewerStageCameraApertureWindow(
            left,
            right,
            bottom,
            top);
        if (!double.IsFinite(left) ||
            !double.IsFinite(right) ||
            !double.IsFinite(bottom) ||
            !double.IsFinite(top) ||
            left >= right ||
            bottom >= top ||
            !double.IsFinite(window.Width) ||
            !double.IsFinite(window.Height) ||
            !double.IsFinite(window.CenterX) ||
            !double.IsFinite(window.CenterY))
        {
            throw new ArgumentOutOfRangeException(
                nameof(right),
                "The stage-camera frustum window must be finite and ordered.");
        }
        return window;
    }
}

internal static class ViewerStageCameraMatrixConversion
{
    internal static bool IsFinite(in UsdMatrix4d value) =>
        double.IsFinite(value.M00) &&
        double.IsFinite(value.M01) &&
        double.IsFinite(value.M02) &&
        double.IsFinite(value.M03) &&
        double.IsFinite(value.M10) &&
        double.IsFinite(value.M11) &&
        double.IsFinite(value.M12) &&
        double.IsFinite(value.M13) &&
        double.IsFinite(value.M20) &&
        double.IsFinite(value.M21) &&
        double.IsFinite(value.M22) &&
        double.IsFinite(value.M23) &&
        double.IsFinite(value.M30) &&
        double.IsFinite(value.M31) &&
        double.IsFinite(value.M32) &&
        double.IsFinite(value.M33);

    internal static Matrix4x4 ToMatrix4x4(in UsdMatrix4d value)
    {
        if (!IsFinite(value))
        {
            throw new InvalidOperationException(
                "The stage-camera matrix must contain only finite values.");
        }

        return new Matrix4x4(
            ToFiniteFloat(value.M00), ToFiniteFloat(value.M01),
            ToFiniteFloat(value.M02), ToFiniteFloat(value.M03),
            ToFiniteFloat(value.M10), ToFiniteFloat(value.M11),
            ToFiniteFloat(value.M12), ToFiniteFloat(value.M13),
            ToFiniteFloat(value.M20), ToFiniteFloat(value.M21),
            ToFiniteFloat(value.M22), ToFiniteFloat(value.M23),
            ToFiniteFloat(value.M30), ToFiniteFloat(value.M31),
            ToFiniteFloat(value.M32), ToFiniteFloat(value.M33));
    }

    private static float ToFiniteFloat(double value)
    {
        if (value < -float.MaxValue || value > float.MaxValue)
        {
            throw new InvalidOperationException(
                "The stage-camera view matrix is outside the finite float range.");
        }

        float converted = (float)value;
        if (!float.IsFinite(converted) || (converted == 0f && value != 0d))
        {
            throw new InvalidOperationException(
                "The stage-camera view matrix cannot be represented by finite floats.");
        }
        return converted;
    }
}

internal readonly record struct ViewerStageCameraActivation(
    long Generation,
    string PrimPath,
    double TimeCode);

internal readonly record struct ViewerStageCameraRefreshRequest(
    long Generation,
    string PrimPath,
    double TimeCode,
    bool ApplyTime);

internal readonly record struct ViewerStageCameraModeView(
    bool IsActive,
    bool ForcesAutomatic,
    string? PrimPath,
    UsdGeomCameraProjection Projection);

internal sealed class ViewerStageCameraModeState
{
    private readonly object _gate = new();
    private ViewportDimensions _viewport;
    private ViewerStageCameraSnapshot _snapshot;
    private CameraState _camera;
    private string? _primPath;
    private long _generation;
    private bool _active;
    private bool _forcesAutomatic;

    internal ViewerStageCameraModeState(ViewportDimensions viewport)
    {
        _viewport = viewport;
        _forcesAutomatic = true;
    }

    internal ViewerStageCameraActivation CaptureActivation(
        string primPath,
        double timeCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(timeCode));
        }

        lock (_gate)
        {
            return new ViewerStageCameraActivation(
                _generation,
                primPath,
                timeCode);
        }
    }

    internal bool TryActivate(
        in ViewerStageCameraActivation activation,
        in ViewerStageCameraSnapshot snapshot,
        out CameraState camera)
    {
        lock (_gate)
        {
            if (activation.Generation != _generation ||
                !string.Equals(
                    activation.PrimPath,
                    snapshot.PrimPath,
                    StringComparison.Ordinal) ||
                activation.TimeCode != snapshot.TimeCode)
            {
                camera = default;
                return false;
            }

            camera = ViewerStageCameraProjectionMath.CreateCameraState(
                snapshot,
                _viewport);
            _generation++;
            _snapshot = snapshot;
            _camera = camera;
            _primPath = snapshot.PrimPath;
            _active = true;
            _forcesAutomatic = false;
            return true;
        }
    }

    internal bool TryFallbackFromActivation(
        in ViewerStageCameraActivation activation,
        out long fallbackGeneration)
    {
        lock (_gate)
        {
            if (activation.Generation != _generation)
            {
                fallbackGeneration = 0;
                return false;
            }

            fallbackGeneration = SetAutomaticFallback();
            return true;
        }
    }

    internal bool TryCreateRefreshRequest(
        double timeCode,
        bool applyTime,
        out ViewerStageCameraRefreshRequest request)
    {
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(timeCode));
        }

        lock (_gate)
        {
            if (!_active)
            {
                request = default;
                return false;
            }

            request = new ViewerStageCameraRefreshRequest(
                _generation,
                _primPath!,
                timeCode,
                applyTime);
            return true;
        }
    }

    internal bool TryRefresh(
        in ViewerStageCameraRefreshRequest request,
        in ViewerStageCameraSnapshot snapshot,
        out CameraState camera)
    {
        lock (_gate)
        {
            if (!IsActiveRequest(request) ||
                !string.Equals(
                    snapshot.PrimPath,
                    request.PrimPath,
                    StringComparison.Ordinal) ||
                snapshot.TimeCode != request.TimeCode)
            {
                camera = default;
                return false;
            }

            camera = ViewerStageCameraProjectionMath.CreateCameraState(
                snapshot,
                _viewport);
            _snapshot = snapshot;
            _camera = camera;
            return true;
        }
    }

    internal bool TryFallback(
        in ViewerStageCameraRefreshRequest request,
        out long fallbackGeneration)
    {
        lock (_gate)
        {
            if (!IsActiveRequest(request))
            {
                fallbackGeneration = 0;
                return false;
            }

            fallbackGeneration = SetAutomaticFallback();
            return true;
        }
    }

    internal bool TryGetCamera(out CameraState camera)
    {
        lock (_gate)
        {
            if (_active)
            {
                camera = _camera;
                return true;
            }
            if (_forcesAutomatic)
            {
                camera = CameraState.Default;
                return true;
            }

            camera = default;
            return false;
        }
    }

    internal ViewerStageCameraModeView GetView()
    {
        lock (_gate)
        {
            return new ViewerStageCameraModeView(
                _active,
                _forcesAutomatic,
                _primPath,
                _snapshot.Projection);
        }
    }

    internal void Resize(ViewportDimensions viewport)
    {
        lock (_gate)
        {
            CameraState camera = _camera;
            if (_active)
            {
                camera = ViewerStageCameraProjectionMath.CreateCameraState(
                    _snapshot,
                    viewport);
            }

            _viewport = viewport;
            if (_active)
            {
                _camera = camera;
            }
        }
    }

    internal bool ExitForNavigation(out bool resetOrbitToAutomatic)
    {
        lock (_gate)
        {
            bool exited = _active || _forcesAutomatic;
            resetOrbitToAutomatic = _forcesAutomatic;
            _generation++;
            _active = false;
            _forcesAutomatic = false;
            _primPath = null;
            return exited;
        }
    }

    internal bool ResetToAutomatic()
    {
        lock (_gate)
        {
            bool changed = _active || !_forcesAutomatic;
            _generation++;
            _active = false;
            _forcesAutomatic = true;
            _primPath = null;
            return changed;
        }
    }

    internal bool IsActive(
        long generation,
        string primPath)
    {
        lock (_gate)
        {
            return _active &&
                _generation == generation &&
                string.Equals(_primPath, primPath, StringComparison.Ordinal);
        }
    }

    internal bool TryGetActiveCamera(
        long generation,
        string primPath,
        out CameraState camera)
    {
        lock (_gate)
        {
            if (_active &&
                _generation == generation &&
                string.Equals(_primPath, primPath, StringComparison.Ordinal))
            {
                camera = _camera;
                return true;
            }

            camera = default;
            return false;
        }
    }

    internal bool IsAutomaticFallback(long generation)
    {
        lock (_gate)
        {
            return !_active &&
                _forcesAutomatic &&
                _generation == generation;
        }
    }

    private bool IsActiveRequest(in ViewerStageCameraRefreshRequest request) =>
        _active &&
        _generation == request.Generation &&
        string.Equals(_primPath, request.PrimPath, StringComparison.Ordinal);

    private long SetAutomaticFallback()
    {
        _generation++;
        _active = false;
        _forcesAutomatic = true;
        _primPath = null;
        return _generation;
    }
}

internal enum ViewerStageCameraRefreshOutcome
{
    Ready,
    FallbackAutomatic,
    TimeOnly,
}

internal readonly record struct ViewerStageCameraRefreshApplication(
    ViewerStageCameraRefreshRequest Request,
    ViewerStageCameraRefreshOutcome Outcome,
    long Generation,
    CameraState Camera,
    string? Error);

internal sealed class ViewerStageCameraRefreshPump : IAsyncDisposable
{
    private readonly IViewerStageCameraSource _source;
    private readonly ViewerStageCameraModeState _mode;
    private readonly Func<
        ViewerStageCameraRefreshApplication,
        CancellationToken,
        ValueTask> _applyAsync;
    private readonly Action<Exception> _reportFailure;
    private readonly CancellationTokenSource _lifetime;
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly object _gate = new();
    private readonly Task _worker;
    private ViewerStageCameraRefreshRequest _pending;
    private long _latestSequence;
    private bool _hasPending;
    private bool _disposed;
    private bool _accepting = true;

    internal ViewerStageCameraRefreshPump(
        IViewerStageCameraSource source,
        ViewerStageCameraModeState mode,
        Func<
            ViewerStageCameraRefreshApplication,
            CancellationToken,
            ValueTask> applyAsync,
        Action<Exception> reportFailure,
        CancellationToken documentToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(applyAsync);
        ArgumentNullException.ThrowIfNull(reportFailure);
        _source = source;
        _mode = mode;
        _applyAsync = applyAsync;
        _reportFailure = reportFailure;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(documentToken);
        _worker = RunAsync(_lifetime.Token);
    }

    internal bool IsAccepting
    {
        get
        {
            lock (_gate)
            {
                return _accepting && !_lifetime.IsCancellationRequested;
            }
        }
    }

    internal bool TryPost(in ViewerStageCameraRefreshRequest request)
    {
        lock (_gate)
        {
            if (!_accepting || _lifetime.IsCancellationRequested)
            {
                return false;
            }

            _pending = request;
            _hasPending = true;
            _latestSequence++;
            if (_signal.CurrentCount == 0)
            {
                _signal.Release();
            }
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _accepting = false;
        }

        _lifetime.Cancel();
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        _signal.Dispose();
        _lifetime.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
                ViewerStageCameraRefreshRequest request;
                long sequence;
                lock (_gate)
                {
                    if (!_hasPending)
                    {
                        continue;
                    }
                    request = _pending;
                    sequence = _latestSequence;
                    _hasPending = false;
                }

                ViewerStageCameraQueryResult result;
                try
                {
                    result = await _source.QueryAsync(
                        new ViewerStageCameraRequest(
                            request.PrimPath,
                            request.TimeCode),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    result = ViewerStageCameraQueryResult.Invalid(
                        request.PrimPath,
                        $"Camera '{request.PrimPath}' could not be refreshed: " +
                        exception.Message);
                }

                lock (_gate)
                {
                    if (sequence != _latestSequence)
                    {
                        continue;
                    }
                }

                if (result.Outcome == ViewerStageCameraQueryOutcome.Ready)
                {
                    try
                    {
                        if (_mode.TryRefresh(request, result.Snapshot, out CameraState camera))
                        {
                            await _applyAsync(
                                new ViewerStageCameraRefreshApplication(
                                    request,
                                    ViewerStageCameraRefreshOutcome.Ready,
                                    request.Generation,
                                    camera,
                                    Error: null),
                                cancellationToken).ConfigureAwait(false);
                        }
                        else if (request.ApplyTime)
                        {
                            await ApplyTimeOnlyAsync(request, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        continue;
                    }
                    catch (OperationCanceledException) when (
                        cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        result = ViewerStageCameraQueryResult.Invalid(
                            request.PrimPath,
                            $"Camera '{request.PrimPath}' cannot be converted for rendering: " +
                            exception.Message);
                    }
                }

                if (_mode.TryFallback(request, out long fallbackGeneration))
                {
                    await _applyAsync(
                        new ViewerStageCameraRefreshApplication(
                            request,
                            ViewerStageCameraRefreshOutcome.FallbackAutomatic,
                            fallbackGeneration,
                            CameraState.Default,
                            result.Error ??
                                $"Camera '{request.PrimPath}' is no longer usable."),
                        cancellationToken).ConfigureAwait(false);
                }
                else if (request.ApplyTime)
                {
                    await ApplyTimeOnlyAsync(request, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _reportFailure(exception);
        }
        finally
        {
            lock (_gate)
            {
                _accepting = false;
            }
        }
    }

    private ValueTask ApplyTimeOnlyAsync(
        ViewerStageCameraRefreshRequest request,
        CancellationToken cancellationToken) =>
        _applyAsync(
            new ViewerStageCameraRefreshApplication(
                request,
                ViewerStageCameraRefreshOutcome.TimeOnly,
                request.Generation,
                CameraState.Default,
                Error: null),
            cancellationToken);
}
