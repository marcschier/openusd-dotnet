// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenUsd;
using OpenUsd.Interop;
using OpenUsd.Physics.Extraction;

if (args.Length is not (2 or 3) || (args.Length == 3 && args[2] != "--mesh-data"))
{
    throw new ArgumentException("Usage: probe <plugin-directory> <root-usd> [--mesh-data]");
}
OpenUsdNativeRuntime.RegisterPlugins(args[0]);
bool includeMeshes = args.Length == 3;
var watch = Stopwatch.StartNew();
using UsdStage stage = UsdStage.Open(args[1]);
ulong serial = stage.ChangeSerial;
var report = new WarehouseAuditReport
{
    Root = stage.RootLayerIdentifier,
    OpenUsdVersion = OpenUsdNativeRuntime.Version,
    DataAbi = OpenUsdNativeRuntime.AbiVersion,
    IncludesColliderMeshes = includeMeshes,
    SourceChangeSerial = serial
};
try
{
    UsdHierarchySnapshot hierarchy = stage.GetHierarchySnapshot(UsdHierarchyLimits.Viewer);
    report.HierarchyEntries = hierarchy.Entries.Count;
    report.CompleteVariantMetadata = hierarchy.IsComplete;
    report.HierarchyTypes = hierarchy.Entries.GroupBy(static item => item.TypeName)
        .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
}
catch (OpenUsdNativeException exception)
{
    report.HierarchyError = exception.Message;
}

ulong traversals = UsdPhysicsStageExtractor.GetTraversalCount();
try
{
    UsdPhysicsExtractionPage page = UsdPhysicsStageExtractor.Extract(stage, new UsdPhysicsExtractionOptions
    {
        IncludeMeshData = includeMeshes,
        IncludeUnmapped = true,
        SkipGuide = false,
        SkipInvisible = false
    });
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
    for (int index = 0; index < page.ObjectCount; index++)
    {
        UsdPhysicsExtractionObject item = page.GetObject(index);
        add(report.PhysicsKinds, item.Kind.ToString());
        add(report.PhysicsGeometries, item.Geometry.ToString());
        if (!item.IsEnabled)
        {
            report.DisabledPhysicsObjects++;
        }
    }
    for (int index = 0; index < page.PropertyCount; index++)
    {
        UsdPhysicsExtractionProperty property = page.GetProperty(index);
        add(report.PhysicsProperties, property.Key.ToString());
        add(report.PhysicsAuthoredProperties, property.Name);
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
}
catch (Exception exception) when (exception is OpenUsdNativeException or UsdPhysicsExtractionException)
{
    report.PhysicsError = exception.Message;
}
report.SourceUnchanged = stage.ChangeSerial == serial;
report.ElapsedSeconds = watch.Elapsed.TotalSeconds;
using Process process = Process.GetCurrentProcess();
report.PeakWorkingSetBytes = process.PeakWorkingSet64;
Console.WriteLine(JsonSerializer.Serialize(report, WarehouseJsonContext.Default.WarehouseAuditReport));
return report.SourceUnchanged && report.HierarchyError is null &&
    report.PhysicsError is null && report.PhysicsTruncationFlags == 0
    ? 0 : 1;

static void add(Dictionary<string, int> counts, string name)
{
    counts.TryGetValue(name, out int count);
    counts[name] = count + 1;
}

internal sealed class WarehouseAuditReport
{
    public int SchemaVersion { get; } = 1;
    public string Scope { get; } =
        "Managed hierarchy and physics extraction only; not rendering or simulation readiness.";
    public string Root { get; init; } = string.Empty;
    public string OpenUsdVersion { get; init; } = string.Empty;
    public uint DataAbi { get; init; }
    public ulong SourceChangeSerial { get; init; }
    public bool SourceUnchanged { get; set; }
    public bool IncludesColliderMeshes { get; init; }
    public int HierarchyEntries { get; set; }
    public bool CompleteVariantMetadata { get; set; }
    public Dictionary<string, int> HierarchyTypes { get; set; } = new(StringComparer.Ordinal);
    public string? HierarchyError { get; set; }
    public string? PhysicsError { get; set; }
    public ulong PhysicsTraversalCount { get; set; }
    public ulong PhysicsVisitedPrims { get; set; }
    public int PhysicsPageBytes { get; set; }
    public int PhysicsObjects { get; set; }
    public int DisabledPhysicsObjects { get; set; }
    public uint PhysicsTruncationFlags { get; set; }
    public string PhysicsPageFlags { get; set; } = string.Empty;
    public double MetersPerUnit { get; set; }
    public double KilogramsPerUnit { get; set; }
    public string UpAxis { get; set; } = string.Empty;
    public int PointCount { get; set; }
    public int IndexCount { get; set; }
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

[JsonSerializable(typeof(WarehouseAuditReport))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class WarehouseJsonContext : JsonSerializerContext;
