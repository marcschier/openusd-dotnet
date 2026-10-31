// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenUsd;
using OpenUsd.Interop;
using OpenUsd.Physics;
using OpenUsd.Physics.Extraction;

if (args.Length == 2 && args[0] == "--build-page")
{
    return PreparedWorldProbe.Run(args[1]);
}

if (args.Length == 3 && args[0] == "--step-page")
{
    return PreparedWorldProbe.Run(args[1], args[2]);
}

if (args.Length is not (2 or 3)
    || (args.Length == 3
        && args[2] is not ("--mesh-data" or "--prepare-physics" or "--build-physics" or "--fingerprint-physics")))
{
    throw new ArgumentException(
        "Usage: probe <plugin-directory> <root-usd> " +
            "[--mesh-data|--prepare-physics|--build-physics|--fingerprint-physics], --build-page <page>, or " +
                "--step-page <page> <dynamic-prim>.");
}
OpenUsdNativeRuntime.RegisterPlugins(args[0]);
bool fingerprintOnly = args.Length == 3 && args[2] == "--fingerprint-physics";
bool includeMeshes = args.Length == 3 && !fingerprintOnly;
bool buildPhysics = args.Length == 3 && args[2] == "--build-physics";
bool prepare = args.Length == 3 && args[2] is "--prepare-physics" or "--build-physics";
var watch = Stopwatch.StartNew();
await using UsdStageScheduler scheduler = UsdStageScheduler.Open(args[1]);
WarehouseAuditReport report = await scheduler.InvokeAsync(stage =>
{
    var result = new WarehouseAuditReport
    {
        Root = stage.RootLayerIdentifier,
        OpenUsdVersion = OpenUsdNativeRuntime.Version,
        DataAbi = OpenUsdNativeRuntime.AbiVersion,
        IncludesColliderMeshes = includeMeshes,
        PreparedPhysics = prepare,
        SourceChangeSerial = stage.ChangeSerial
    };
    try
    {
        UsdHierarchySnapshot hierarchy = stage.GetHierarchySnapshot(UsdHierarchyLimits.Viewer);
        result.HierarchyEntries = hierarchy.Entries.Count;
        result.CompleteVariantMetadata = hierarchy.IsComplete;
        result.HierarchyTypes = hierarchy.Entries.GroupBy(static item => item.TypeName)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
    }
    catch (OpenUsdNativeException exception)
    {
        result.HierarchyError = exception.Message;
    }
    return result;
});

ulong traversals = UsdPhysicsStageExtractor.GetTraversalCount();
try
{
    var options = new UsdPhysicsExtractionOptions
    {
        IncludeMeshData = includeMeshes,
        FingerprintMeshData = fingerprintOnly,
        IncludeUnmapped = true,
        SkipGuide = false,
        SkipInvisible = false
    };
    if (prepare)
    {
        UsdPhysicsExtractionPage metadata = await UsdPhysicsStageExtractor.ExtractAsync(
            scheduler, options with { IncludeMeshData = false });
        report.SourceObjectCount = metadata.ObjectCount;
        for (int index = 0; index < metadata.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = metadata.GetObject(index);
            if (item.Kind == UsdPhysicsExtractionObjectKind.Collider)
            {
                report.SourceColliderCount++;
                if ((item.Flags & UsdPhysicsExtractionObjectTraits.UnmappedOpinions) != 0)
                {
                    report.UnmappedColliderCount++;
                }
            }
        }
        for (int index = 0; index < metadata.PropertyCount; index++)
        {
            UsdPhysicsExtractionProperty property = metadata.GetProperty(index);
            if (property.Key == UsdPhysicsExtractionKey.CollisionApproximation
                && property.IsText && property.ValueCount == 1)
            {
                add(report.SourceApproximations, metadata.GetText(property.ValueStart));
            }
        }
    }
    UsdPhysicsExtractionPage page = prepare
        ? await UsdPhysicsStageExtractor.PrepareAsync(scheduler, options, new PreparationProgress())
        : await UsdPhysicsStageExtractor.ExtractAsync(scheduler, options);
    report.PhysicsTraversalCount = UsdPhysicsStageExtractor.GetTraversalCount() - traversals;
    report.PhysicsVisitedPrims = UsdPhysicsStageExtractor.GetVisitedPrimCount();
    report.PhysicsPageBytes = page.ByteSize;
    report.PhysicsObjects = page.ObjectCount;
    report.PhysicsTruncationFlags = page.TruncationFlags;
    report.PhysicsPageFlags = page.Flags.ToString();
    report.MetersPerUnit = page.MetersPerUnit;
    report.KilogramsPerUnit = page.KilogramsPerUnit;
    report.UpAxis = page.UpAxis.ToString();
    report.PointCount = page.PointCount;
    report.IndexCount = page.IndexCount;
    report.PreparedHullCount = page.HullCount;
    report.SourceFingerprint = $"{page.FingerprintHigh:X16}{page.FingerprintLow:X16}";
    for (int index = 0; index < page.ObjectCount; index++)
    {
        UsdPhysicsExtractionObject item = page.GetObject(index);
        add(report.PhysicsKinds, item.Kind.ToString());
        add(report.PhysicsGeometries, item.Geometry.ToString());
        if (!item.IsEnabled)
        {
            report.DisabledPhysicsObjects++;
        }
        if (prepare && item.Kind == UsdPhysicsExtractionObjectKind.Collider)
        {
            report.Colliders.Add(new PreparedColliderRecord(item.Path, item.Id, item.PrototypeId,
                item.IsEnabled, item.Geometry.ToString(), item.PointCount, item.IndexCount,
                item.HullCount, item.Flags.ToString()));
        }
    }
    for (int index = 0; index < page.PropertyCount; index++)
    {
        UsdPhysicsExtractionProperty property = page.GetProperty(index);
        add(report.PhysicsProperties, property.Key.ToString());
        add(report.PhysicsAuthoredProperties, property.Name);
        if (property.Name.Contains("Convex", StringComparison.OrdinalIgnoreCase))
        {
            string value = property.IsText && property.ValueCount > 0
                ? page.GetText(property.ValueStart)
                : property.Scalar.ToString("R", CultureInfo.InvariantCulture);
            if (!report.CookingValues.TryGetValue(property.Name, out List<string>? values))
            {
                values = [];
                report.CookingValues.Add(property.Name, values);
            }
            if (values.Count < 32 && !values.Contains(value, StringComparer.Ordinal))
            {
                values.Add(value);
            }
        }
    }
    for (int index = 0; index < page.DiagnosticCount; index++)
    {
        UsdPhysicsExtractionDiagnostic diagnostic = page.GetDiagnostic(index);
        add(report.PhysicsDiagnosticCounts, $"{diagnostic.Severity}:{diagnostic.Code}");
        if (report.PhysicsDiagnostics.Count < 256)
        {
            report.PhysicsDiagnostics.Add(new WarehouseDiagnostic(
                diagnostic.Severity.ToString(), diagnostic.Code.ToString(), diagnostic.Message));
        }
    }
    report.PhysicsDiagnosticTotal = page.DiagnosticCount;
    if (prepare
        && Environment.GetEnvironmentVariable("OPENUSD_PHYSICS_PREPARED_PAGE_OUTPUT") is { Length: > 0 } destination)
    {
        if (!Path.IsPathFullyQualified(destination)
            || File.Exists(destination) || File.Exists(destination + ".writing"))
        {
            throw new InvalidOperationException("Use a new absolute path for the prepared physics page.");
        }
        byte[] bytes = page.ToArray();
        string temporary = destination + ".writing";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes);
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
        report.PreparedPagePath = destination;
        report.PreparedPageSha256 = Convert.ToHexString(SHA256.HashData(bytes));
    }
    if (buildPhysics)
    {
        await using UsdPhysicsTransport transport = await UsdPhysicsTransport.CreateAsync(scheduler);
        await transport.AttachExtractionAsync(page);
        await transport.BuildAsync();
        report.SolverState = transport.Status.State.ToString();
        report.SolverComplete = transport.Status.State == UsdPhysicsTransportState.Paused &&
            !transport.Diagnostics.Entries.Any(static entry =>
                entry.Severity == UsdPhysicsDiagnosticSeverity.Error ||
                entry.Code == "OPENUSD_PHYSICS_COMPOSITION_SKIPPED");
        report.SolverDiagnostics.AddRange(transport.Diagnostics.Entries.Select(static entry =>
            new WarehouseDiagnostic(entry.Severity.ToString(), entry.Code, entry.Message)));
    }
}
catch (Exception exception) when (exception is OpenUsdNativeException or UsdPhysicsExtractionException)
{
    report.PhysicsError = exception.Message;
}
report.SourceUnchanged = await scheduler.InvokeAsync(stage => stage.ChangeSerial == report.SourceChangeSerial);
report.ElapsedSeconds = watch.Elapsed.TotalSeconds;
using Process process = Process.GetCurrentProcess();
report.PeakWorkingSetBytes = process.PeakWorkingSet64;
Console.WriteLine(JsonSerializer.Serialize(report, WarehouseJsonContext.Default.WarehouseAuditReport));
return report.SourceUnchanged && report.HierarchyError is null &&
    report.PhysicsError is null && report.PhysicsTruncationFlags == 0 &&
    (!buildPhysics || report.SolverComplete)
    ? 0 : 1;

static void add(Dictionary<string, int> counts, string name)
{
    counts.TryGetValue(name, out int count);
    counts[name] = count + 1;
}

internal sealed class WarehouseAuditReport : IUsdDetachedResult
{
    public string Phase => PreparedPhysics ? "after-physics-preparation" : "after-physics-extraction";
    public int SchemaVersion { get; } = 1;
    public string Scope { get; } =
        "Managed hierarchy and physics extraction/preparation only; not rendering or simulation readiness.";
    public string Root { get; init; } = string.Empty;
    public string OpenUsdVersion { get; init; } = string.Empty;
    public uint DataAbi { get; init; }
    public ulong SourceChangeSerial { get; init; }
    public bool SourceUnchanged { get; set; }
    public bool IncludesColliderMeshes { get; init; }
    public bool PreparedPhysics { get; init; }
    public int HierarchyEntries { get; set; }
    public bool CompleteVariantMetadata { get; set; }
    public Dictionary<string, int> HierarchyTypes { get; set; } = new(StringComparer.Ordinal);
    public string? HierarchyError { get; set; }
    public string? PhysicsError { get; set; }
    public ulong PhysicsTraversalCount { get; set; }
    public ulong PhysicsVisitedPrims { get; set; }
    public int PhysicsPageBytes { get; set; }
    public int PhysicsObjects { get; set; }
    public int SourceObjectCount { get; set; }
    public int SourceColliderCount { get; set; }
    public int UnmappedColliderCount { get; set; }
    public Dictionary<string, int> SourceApproximations { get; } = new(StringComparer.Ordinal);
    public bool SolverComplete { get; set; }
    public string? SolverState { get; set; }
    public List<WarehouseDiagnostic> SolverDiagnostics { get; } = [];
    public int DisabledPhysicsObjects { get; set; }
    public uint PhysicsTruncationFlags { get; set; }
    public string PhysicsPageFlags { get; set; } = string.Empty;
    public double MetersPerUnit { get; set; }
    public double KilogramsPerUnit { get; set; }
    public string UpAxis { get; set; } = string.Empty;
    public int PointCount { get; set; }
    public int IndexCount { get; set; }
    public int PreparedHullCount { get; set; }
    public string SourceFingerprint { get; set; } = string.Empty;
    public string? PreparedPagePath { get; set; }
    public string? PreparedPageSha256 { get; set; }
    public List<PreparedColliderRecord> Colliders { get; } = [];
    public Dictionary<string, List<string>> CookingValues { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> PhysicsKinds { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> PhysicsGeometries { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> PhysicsProperties { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> PhysicsAuthoredProperties { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> PhysicsDiagnosticCounts { get; } = new(StringComparer.Ordinal);
    public int PhysicsDiagnosticTotal { get; set; }
    public List<WarehouseDiagnostic> PhysicsDiagnostics { get; } = [];
    public double ElapsedSeconds { get; set; }
    public long PeakWorkingSetBytes { get; set; }
}

internal sealed record WarehouseDiagnostic(string Severity, string Code, string Message);
internal sealed record PreparedColliderRecord(string Path, ulong Id, ulong PrototypeId,
    bool Enabled, string Geometry, int Points, int Indices, int Hulls, string Flags);

internal sealed class PreparationProgress : IProgress<double>
{
    public void Report(double value) =>
        Console.Error.WriteLine(FormattableString.Invariant($"Physics geometry prepared: {value:P1}"));
}

[JsonSerializable(typeof(WarehouseAuditReport))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class WarehouseJsonContext : JsonSerializerContext;
