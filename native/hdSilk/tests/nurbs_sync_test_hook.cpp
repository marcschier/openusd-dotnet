// Copyright (c) marcschier. Licensed under the MIT License.

#include "hdsilk_test_hooks.h"

#include "geometry_sync_page.h"
#include "renderDelegate.h"
#include "nurbsSceneIndexPlugin.h"

#include "pxr/base/gf/vec2d.h"
#include "pxr/base/gf/vec2f.h"
#include "pxr/base/gf/vec3d.h"
#include "pxr/base/gf/vec4f.h"
#include "pxr/base/plug/registry.h"
#include "pxr/base/tf/diagnosticMgr.h"
#include "pxr/base/tf/weakPtr.h"
#include "pxr/base/tf/warning.h"
#include "pxr/imaging/hd/basisCurvesSchema.h"
#include "pxr/imaging/hd/basisCurvesTopologySchema.h"
#include "pxr/imaging/hd/changeTracker.h"
#include "pxr/imaging/hd/filteringSceneIndex.h"
#include "pxr/imaging/hd/meshSchema.h"
#include "pxr/imaging/hd/meshTopologySchema.h"
#include "pxr/imaging/hd/overlayContainerDataSource.h"
#include "pxr/imaging/hd/primvarSchema.h"
#include "pxr/imaging/hd/primvarsSchema.h"
#include "pxr/imaging/hd/renderIndex.h"
#include "pxr/imaging/hd/retainedDataSource.h"
#include "pxr/imaging/hd/rprim.h"
#include "pxr/imaging/hd/sceneDelegate.h"
#include "pxr/imaging/hd/sceneIndexPluginRegistry.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/usd/sdf/layer.h"
#include "pxr/usd/sdf/types.h"
#include "pxr/usd/usd/attribute.h"
#include "pxr/usd/usd/stage.h"
#include "pxr/usd/usd/timeCode.h"
#include "pxr/usd/usdGeom/primvarsAPI.h"
#include "pxr/usd/usdGeom/tokens.h"
#include "pxr/usdImaging/usdImaging/stageSceneIndex.h"

#include <atomic>
#include <cmath>
#include <memory>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>
#include <vector>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
const SdfPath CurvePath("/Curve");
const SdfPath PatchPath("/Patch");

const char* const AuthoredNurbs = R"usd(#usda 1.0
def NurbsCurves "Curve"
{
    int[] curveVertexCounts = [3]
    int[] order = [3]
    double[] knots = [0, 0, 0, 1, 1, 1]
    double2[] ranges = [(0, 1)]
    point3f[] points = [(1, 0, 0), (1, 1, 0), (0, 1, 0)]
    double[] pointWeights = [1, 0.7071067811865476, 1]
    float[] widths = [0.1] (interpolation = "constant")
}
def NurbsPatch "Patch"
{
    int uVertexCount = 2
    int vVertexCount = 2
    int uOrder = 2
    int vOrder = 2
    double[] uKnots = [0, 0, 1, 1]
    double[] vKnots = [0, 0, 1, 1]
    double2 uRange = (0, 1)
    double2 vRange = (0, 1)
    token uForm = "open"
    token vForm = "open"
    token orientation = "rightHanded"
    point3f[] points = [(0, 0, 0), (0, 2, 0), (2, 0, 0), (2, 2, 0)]
    double[] pointWeights = [1, 1, 2, 2]
}
def Mesh "OrdinaryMesh"
{
    point3f[] points = [(0, 0, 0), (1, 0, 0), (0, 1, 0)]
    int[] faceVertexCounts = [3]
    int[] faceVertexIndices = [0, 1, 2]
    uniform token subdivisionScheme = "none"
}
def BasisCurves "OrdinaryCurve"
{
    uniform token type = "linear"
    uniform token wrap = "nonperiodic"
    int[] curveVertexCounts = [2]
    point3f[] points = [(0, 0, 0), (1, 0, 0)]
    float[] widths = [0.1] (interpolation = "constant")
}
)usd";

class CountedPoints final : public HdVec3fArrayDataSource
{
public:
    CountedPoints(HdVec3fArrayDataSourceHandle source, std::shared_ptr<size_t> reads)
        : _source(std::move(source)), _reads(std::move(reads))
    {
    }

    VtValue GetValue(Time time) override
    {
        ++*_reads;
        return _source->GetValue(time);
    }

    VtVec3fArray GetTypedValue(Time time) override
    {
        ++*_reads;
        return _source->GetTypedValue(time);
    }

    bool GetContributingSampleTimesForInterval(
        Time start, Time end, std::vector<Time>* times) override
    {
        return _source->GetContributingSampleTimesForInterval(start, end, times);
    }

private:
    HdVec3fArrayDataSourceHandle _source;
    std::shared_ptr<size_t> _reads;
};

// Count point values, not GetPrim or descriptor enumeration.
class CountPointReads final : public HdSingleInputFilteringSceneIndexBase
{
public:
    static HdSceneIndexBaseRefPtr New(
        const HdSceneIndexBaseRefPtr& input, const SdfPath& path,
        const std::shared_ptr<size_t>& reads,
        const HdContainerDataSourceHandle& metadata = nullptr)
    {
        return TfCreateRefPtr(new CountPointReads(input, path, reads, metadata));
    }

    HdSceneIndexPrim GetPrim(const SdfPath& path) const override
    {
        HdSceneIndexPrim prim = _GetInputSceneIndex()->GetPrim(path);
        if (path != _path)
        {
            return prim;
        }
        if (_metadata)
        {
            prim.dataSource = HdOverlayContainerDataSource::OverlayedContainerDataSources(
                HdRetainedContainerDataSource::New(
                    HdPrimvarsSchemaTokens->primvars, _metadata),
                prim.dataSource);
        }
        const auto value = HdVec3fArrayDataSource::Cast(
            HdPrimvarsSchema::GetFromParent(prim.dataSource)
                .GetPrimvar(HdPrimvarsSchemaTokens->points).GetPrimvarValue());
        if (!value)
        {
            return prim;
        }
        const auto points = HdPrimvarSchema::Builder()
            .SetPrimvarValue(std::make_shared<CountedPoints>(value, _reads))
            .SetInterpolation(HdPrimvarSchema::BuildInterpolationDataSource(
                HdPrimvarSchemaTokens->vertex))
            .SetRole(HdPrimvarSchema::BuildRoleDataSource(HdPrimvarSchemaTokens->point))
            .Build();
        const auto overlay = HdRetainedContainerDataSource::New(
            HdPrimvarsSchemaTokens->primvars,
            HdRetainedContainerDataSource::New(HdPrimvarsSchemaTokens->points, points));
        prim.dataSource = HdOverlayContainerDataSource::OverlayedContainerDataSources(
            overlay, prim.dataSource);
        return prim;
    }

    SdfPathVector GetChildPrimPaths(const SdfPath& path) const override
    {
        return _GetInputSceneIndex()->GetChildPrimPaths(path);
    }

private:
    CountPointReads(
        const HdSceneIndexBaseRefPtr& input, SdfPath path,
        std::shared_ptr<size_t> reads, HdContainerDataSourceHandle metadata)
        : HdSingleInputFilteringSceneIndexBase(input),
          _path(std::move(path)), _reads(std::move(reads)), _metadata(std::move(metadata))
    {
    }

    void _PrimsAdded(
        const HdSceneIndexBase&, const HdSceneIndexObserver::AddedPrimEntries& entries) override
    {
        _SendPrimsAdded(entries);
    }
    void _PrimsRemoved(
        const HdSceneIndexBase&, const HdSceneIndexObserver::RemovedPrimEntries& entries) override
    {
        _SendPrimsRemoved(entries);
    }
    void _PrimsDirtied(
        const HdSceneIndexBase&, const HdSceneIndexObserver::DirtiedPrimEntries& entries) override
    {
        _SendPrimsDirtied(entries);
    }

    SdfPath _path;
    std::shared_ptr<size_t> _reads;
    HdContainerDataSourceHandle _metadata;
};

class DirtyObserver final : public HdSceneIndexObserver
{
public:
    explicit DirtyObserver(SdfPath path) : _path(std::move(path)) {}

    void PrimsAdded(const HdSceneIndexBase&, const AddedPrimEntries&) override {}
    void PrimsRemoved(const HdSceneIndexBase&, const RemovedPrimEntries&) override {}
    void PrimsRenamed(const HdSceneIndexBase&, const RenamedPrimEntries&) override {}
    void PrimsDirtied(const HdSceneIndexBase&, const DirtiedPrimEntries& entries) override
    {
        for (const auto& entry : entries)
        {
            if (entry.primPath == _path &&
                entry.dirtyLocators == HdDataSourceLocatorSet::UniversalSet())
            {
                ++universalDirties;
            }
        }
    }

    size_t universalDirties = 0;

private:
    SdfPath _path;
};

class TrimDiagnostic final : public TfDiagnosticMgr::Delegate
{
public:
    TrimDiagnostic() { TfDiagnosticMgr::GetInstance().AddDelegate(this); }
    ~TrimDiagnostic() override { TfDiagnosticMgr::GetInstance().RemoveDelegate(this); }

    void IssueStatus(const TfStatus&) override {}
    void IssueError(const TfError&) override { unexpected = true; }
    void IssueFatalError(const TfCallContext&, const std::string&) override
    {
        unexpected = true;
        _UnhandledAbort();
    }
    void IssueWarning(const TfWarning& warning) override
    {
        const std::string text = warning.GetCommentary();
        if (text.find("/Patch") != std::string::npos &&
            text.find("trimmed NURBS patches are not supported.") != std::string::npos)
        {
            ++trimWarnings;
        }
        else
        {
            unexpected = true;
        }
    }

    size_t trimWarnings = 0;
    bool unexpected = false;
};

class NoDiagnostic final : public TfDiagnosticMgr::Delegate
{
public:
    NoDiagnostic() { TfDiagnosticMgr::GetInstance().AddDelegate(this); }
    ~NoDiagnostic() override { TfDiagnosticMgr::GetInstance().RemoveDelegate(this); }

    void IssueStatus(const TfStatus&) override {}
    void IssueError(const TfError&) override { unexpected = true; }
    void IssueFatalError(const TfCallContext&, const std::string&) override
    {
        unexpected = true;
        _UnhandledAbort();
    }
    void IssueWarning(const TfWarning&) override { unexpected = true; }

    bool unexpected = false;
};

class PointWeightsDiagnostic final : public TfDiagnosticMgr::Delegate
{
public:
    PointWeightsDiagnostic(SdfPath path, std::string expectedMessage)
        : _path(std::move(path)), _expectedMessage(std::move(expectedMessage))
    {
        TfDiagnosticMgr::GetInstance().AddDelegate(this);
    }

    ~PointWeightsDiagnostic() override
    {
        TfDiagnosticMgr::GetInstance().RemoveDelegate(this);
    }

    void IssueStatus(const TfStatus&) override {}
    void IssueError(const TfError&) override { unexpected = true; }
    void IssueFatalError(const TfCallContext&, const std::string&) override
    {
        unexpected = true;
        _UnhandledAbort();
    }
    void IssueWarning(const TfWarning& warning) override
    {
        const std::string text = warning.GetCommentary();
        if (text.find(_path.GetString()) != std::string::npos &&
            text.find(_expectedMessage) != std::string::npos)
        {
            ++rejectionWarnings;
        }
        else
        {
            unexpected = true;
        }
    }

    size_t rejectionWarnings = 0;
    bool unexpected = false;

private:
    SdfPath _path;
    std::string _expectedMessage;
};

class NurbsFailureSequenceDiagnostic final : public TfDiagnosticMgr::Delegate
{
public:
    NurbsFailureSequenceDiagnostic(
        SdfPath path,
        std::string firstMessage,
        std::string secondMessage)
        : _path(std::move(path)),
          _firstMessage(std::move(firstMessage)),
          _secondMessage(std::move(secondMessage))
    {
        TfDiagnosticMgr::GetInstance().AddDelegate(this);
    }

    ~NurbsFailureSequenceDiagnostic() override
    {
        TfDiagnosticMgr::GetInstance().RemoveDelegate(this);
    }

    void IssueStatus(const TfStatus&) override {}
    void IssueError(const TfError&) override { unexpected = true; }
    void IssueFatalError(const TfCallContext&, const std::string&) override
    {
        unexpected = true;
        _UnhandledAbort();
    }
    void IssueWarning(const TfWarning& warning) override
    {
        const std::string text = warning.GetCommentary();
        if (text.find(_path.GetString()) == std::string::npos)
        {
            unexpected = true;
        }
        else if (text.find(_firstMessage) != std::string::npos)
        {
            ++firstWarnings;
        }
        else if (text.find(_secondMessage) != std::string::npos)
        {
            ++secondWarnings;
        }
        else
        {
            unexpected = true;
        }
    }

    size_t firstWarnings = 0;
    size_t secondWarnings = 0;
    bool unexpected = false;

private:
    SdfPath _path;
    std::string _firstMessage;
    std::string _secondMessage;
};

class DiscardedPrimvarDiagnostic final : public TfDiagnosticMgr::Delegate
{
public:
    DiscardedPrimvarDiagnostic() { TfDiagnosticMgr::GetInstance().AddDelegate(this); }
    ~DiscardedPrimvarDiagnostic() override
    {
        TfDiagnosticMgr::GetInstance().RemoveDelegate(this);
    }

    void IssueStatus(const TfStatus&) override {}
    void IssueError(const TfError&) override { unexpected = true; }
    void IssueFatalError(const TfCallContext&, const std::string&) override
    {
        unexpected = true;
        _UnhandledAbort();
    }
    void IssueWarning(const TfWarning& warning) override
    {
        const std::string text = warning.GetCommentary();
        if (text.find("/Curve") != std::string::npos &&
            text.find("discarded authored NURBS primvar 'uniformTint'") !=
                std::string::npos &&
            text.find("'uniform' interpolation; only constant and vertex primvars are preserved.") !=
                std::string::npos)
        {
            ++discardWarnings;
        }
        else
        {
            unexpected = true;
        }
    }

    size_t discardWarnings = 0;
    bool unexpected = false;
};

struct NurbsScene
{
    UsdStageRefPtr stage;
    UsdImagingStageSceneIndexRefPtr source;
    HdSceneIndexBaseRefPtr converted;

    bool Open(
        const SdfPath& countedPath = SdfPath(),
        const std::shared_ptr<size_t>& authoredReads = nullptr)
    {
        stage = UsdStage::CreateInMemory();
        if (!stage || !stage->GetRootLayer()->ImportFromString(AuthoredNurbs))
        {
            return false;
        }
        source = UsdImagingStageSceneIndex::New();
        source->SetStage(stage);
        source->SetTime(UsdTimeCode(0.0));
        const HdSceneIndexBaseRefPtr input = authoredReads
            ? CountPointReads::New(source, countedPath, authoredReads)
            : HdSceneIndexBaseRefPtr(source);
        converted = HdSceneIndexPluginRegistry::GetInstance().AppendSceneIndex(
            TfToken("HdSilk_NurbsSceneIndexPlugin"), input, nullptr);
        return converted && converted != input &&
            source->GetPrim(CurvePath).primType == TfToken("nurbsCurves") &&
            source->GetPrim(PatchPath).primType == TfToken("nurbsPatch");
    }

    bool Set(const SdfPath& path, const char* attribute, const VtValue& value)
    {
        if (!stage->GetPrimAtPath(path).GetAttribute(TfToken(attribute)).Set(value))
        {
            return false;
        }
        source->ApplyPendingUpdates();
        return true;
    }

    bool SetAtTime(
        const SdfPath& path,
        const char* attribute,
        const VtValue& value,
        double time)
    {
        if (!stage->GetPrimAtPath(path).GetAttribute(TfToken(attribute))
                 .Set(value, UsdTimeCode(time)))
        {
            return false;
        }
        source->ApplyPendingUpdates();
        return true;
    }

    bool Clear(const SdfPath& path, const char* attribute)
    {
        if (!stage->GetPrimAtPath(path).GetAttribute(TfToken(attribute)).Clear())
        {
            return false;
        }
        source->ApplyPendingUpdates();
        return true;
    }
};

VtVec3fArray GeneratedPoints(const HdSceneIndexPrim& prim, const TfToken& name)
{
    const auto value = HdVec3fArrayDataSource::Cast(
        HdPrimvarsSchema::GetFromParent(prim.dataSource).GetPrimvar(name).GetPrimvarValue());
    return value ? value->GetTypedValue(0.0f) : VtVec3fArray{};
}

bool VerifyAuthoredPointWeights(const HdSceneIndexPrim& prim, const VtDoubleArray& expected)
{
    const HdPrimvarSchema weights =
        HdPrimvarsSchema::GetFromParent(prim.dataSource)
            .GetPrimvar(TfToken("pointWeights"));
    const HdSampledDataSourceHandle valueSource = weights.GetPrimvarValue();
    if (!valueSource)
    {
        return false;
    }
    const VtValue value = valueSource->GetValue(0.0f);
    return value.IsHolding<VtDoubleArray>() &&
        value.UncheckedGet<VtDoubleArray>() == expected;
}

bool Near(const GfVec3f& actual, double x, double y, double z)
{
    return std::abs(actual[0] - x) < 2e-6 &&
        std::abs(actual[1] - y) < 2e-6 && std::abs(actual[2] - z) < 2e-6;
}

bool VerifyGeneratedPrimvars(const HdSceneIndexPrim& prim, bool patch)
{
    const auto primvars = HdPrimvarsSchema::GetFromParent(prim.dataSource);
    const auto marker = HdBoolDataSource::Cast(
        primvars.GetPrimvar(TfToken("silkNurbsGeneratedGeometry")).GetPrimvarValue());
    const auto points = primvars.GetPrimvar(HdPrimvarsSchemaTokens->points);
    return marker && marker->GetTypedValue(0.0f) && points.GetInterpolation() &&
        points.GetInterpolation()->GetTypedValue(0.0f) == HdPrimvarSchemaTokens->vertex &&
        !primvars.GetPrimvar(TfToken("pointWeights")).IsDefined() &&
        prim.primType == (patch ? HdPrimTypeTokens->mesh : HdPrimTypeTokens->basisCurves);
}

bool VerifyRationalCurveOutput(const HdSceneIndexPrim& prim, double middleWeight)
{
    if (!VerifyGeneratedPrimvars(prim, false))
    {
        return false;
    }
    const auto topology = HdBasisCurvesSchema::GetFromParent(prim.dataSource).GetTopology();
    if (!topology.GetCurveVertexCounts() || !topology.GetType() || !topology.GetWrap() ||
        topology.GetCurveVertexCounts()->GetTypedValue(0.0f) != VtIntArray{9} ||
        topology.GetType()->GetTypedValue(0.0f) != HdTokens->linear ||
        topology.GetWrap()->GetTypedValue(0.0f) != HdTokens->nonperiodic)
    {
        return false;
    }
    const auto points = GeneratedPoints(prim, HdPrimvarsSchemaTokens->points);
    if (points.size() != 9)
    {
        return false;
    }
    for (size_t i = 0; i < points.size(); ++i)
    {
        const double t = static_cast<double>(i) / 8.0;
        const double a = (1.0 - t) * (1.0 - t);
        const double b = 2.0 * middleWeight * t * (1.0 - t);
        const double c = t * t;
        if (!Near(points[i], (a + b) / (a + b + c), (b + c) / (a + b + c), 0.0))
        {
            return false;
        }
    }
    return true;
}

bool VerifyUnitWeightCurveOutput(const HdSceneIndexPrim& prim)
{
    if (!VerifyRationalCurveOutput(prim, 1.0))
    {
        return false;
    }
    const auto points = GeneratedPoints(prim, HdPrimvarsSchemaTokens->points);
    for (size_t i = 0; i < points.size(); ++i)
    {
        const double t = static_cast<double>(i) / 8.0;
        if (!Near(points[i], 1.0 - t * t, 2.0 * t - t * t, 0.0))
        {
            return false;
        }
    }
    return true;
}

bool VerifyRationalPatchOutput(const HdSceneIndexPrim& prim, double rightWeight)
{
    if (!VerifyGeneratedPrimvars(prim, true))
    {
        return false;
    }
    const auto mesh = HdMeshSchema::GetFromParent(prim.dataSource);
    const auto topology = mesh.GetTopology();
    if (!topology.GetFaceVertexCounts() || !topology.GetFaceVertexIndices() ||
        !mesh.GetSubdivisionScheme() ||
        mesh.GetSubdivisionScheme()->GetTypedValue(0.0f) != TfToken("none") ||
        topology.GetFaceVertexCounts()->GetTypedValue(0.0f) != VtIntArray(64, 4))
    {
        return false;
    }
    const auto indices = topology.GetFaceVertexIndices()->GetTypedValue(0.0f);
    const auto points = GeneratedPoints(prim, HdPrimvarsSchemaTokens->points);
    const auto normals = GeneratedPoints(prim, HdPrimvarsSchemaTokens->normals);
    if (indices.size() != 256 || points.size() != 81 || normals.size() != 81)
    {
        return false;
    }
    for (size_t v = 0; v < 9; ++v)
    {
        for (size_t u = 0; u < 9; ++u)
        {
            const double t = static_cast<double>(u) / 8.0;
            if (!Near(points[v * 9 + u],
                    2.0 * rightWeight * t / (1.0 - t + rightWeight * t),
                    static_cast<double>(v) / 4.0, 0.0) ||
                !Near(normals[v * 9 + u], 0.0, 0.0, 1.0))
            {
                return false;
            }
            if (u < 8 && v < 8)
            {
                const size_t face = (v * 8 + u) * 4;
                const int lower = static_cast<int>(v * 9 + u);
                if (indices[face] != lower || indices[face + 1] != lower + 1 ||
                    indices[face + 2] != lower + 10 || indices[face + 3] != lower + 9)
                {
                    return false;
                }
            }
        }
    }
    return true;
}

bool VerifyUnitWeightPatchOutput(const HdSceneIndexPrim& prim)
{
    if (!VerifyRationalPatchOutput(prim, 1.0))
    {
        return false;
    }
    const auto points = GeneratedPoints(prim, HdPrimvarsSchemaTokens->points);
    for (size_t v = 0; v < 9; ++v)
    {
        for (size_t u = 0; u < 9; ++u)
        {
            if (!Near(points[v * 9 + u],
                    2.0 * static_cast<double>(u) / 8.0,
                    2.0 * static_cast<double>(v) / 8.0, 0.0))
            {
                return false;
            }
        }
    }
    return true;
}

bool VerifyRejectedPatchOutput(const HdSceneIndexPrim& prim)
{
    if (!VerifyGeneratedPrimvars(prim, true))
    {
        return false;
    }
    const auto primvars = HdPrimvarsSchema::GetFromParent(prim.dataSource);
    const auto points = HdVec3fArrayDataSource::Cast(
        primvars.GetPrimvar(HdPrimvarsSchemaTokens->points).GetPrimvarValue());
    const auto normals = HdVec3fArrayDataSource::Cast(
        primvars.GetPrimvar(HdPrimvarsSchemaTokens->normals).GetPrimvarValue());
    const auto topology = HdMeshSchema::GetFromParent(prim.dataSource).GetTopology();
    const auto faceCounts = topology.GetFaceVertexCounts();
    const auto faceIndices = topology.GetFaceVertexIndices();
    return points && normals && faceCounts && faceIndices &&
        points->GetTypedValue(0.0f).empty() &&
        normals->GetTypedValue(0.0f).empty() &&
        faceCounts->GetTypedValue(0.0f).empty() &&
        faceIndices->GetTypedValue(0.0f).empty();
}

bool VerifyRejectedCurveOutput(const HdSceneIndexPrim& prim)
{
    if (!VerifyGeneratedPrimvars(prim, false))
    {
        return false;
    }
    const auto points = GeneratedPoints(prim, HdPrimvarsSchemaTokens->points);
    const auto topology = HdBasisCurvesSchema::GetFromParent(prim.dataSource).GetTopology();
    return points.empty() && topology.GetCurveVertexCounts() &&
        topology.GetCurveVertexCounts()->GetTypedValue(0.0f).empty();
}

struct SyncTarget
{
    HdSilkRenderDelegate renderDelegate;
    std::unique_ptr<HdRenderIndex> index;
    HdRprim* rprim = nullptr;
    HdSceneDelegate* adapter = nullptr;

    bool Insert(const HdSceneIndexBaseRefPtr& input, const SdfPath& path)
    {
        index.reset(HdRenderIndex::New(&renderDelegate, {}));
        if (!index)
        {
            return false;
        }
        index->InsertSceneIndex(input, SdfPath::AbsoluteRootPath(), false);
        rprim = const_cast<HdRprim*>(index->GetRprim(path));
        adapter = index->GetSceneDelegateForRprim(path);
        return rprim && adapter;
    }

    bool Sync(HdDirtyBits dirty)
    {
        rprim->Sync(adapter, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
        return dirty == HdChangeTracker::Clean;
    }

    bool VerifyPublished(const SdfPath& path, const VtVec3fArray& points)
    {
        uint32_t commands = 0;
        const auto page = renderDelegate.GetSceneState()->BuildPage(nullptr, &commands);
        VtVec3fArray emitted = points;
        if (path == CurvePath)
        {
            emitted.clear();
            for (size_t i = 1; i < points.size(); ++i)
            {
                emitted.push_back(points[i - 1]);
                emitted.push_back(points[i]);
            }
        }
        return commands != 0 && renderDelegate.GetSceneState()->HasMeshPath(path.GetString()) &&
            HdSilkTest::HasMeshPoints(page, path.GetString(), emitted);
    }
};

bool VerifyPublishedVertexAttribute(
    const std::vector<uint8_t>& page, const SdfPath& path,
    const std::string& name, uint32_t semantic, uint32_t components,
    const std::vector<float>& expected)
{
    size_t offset = 0;
    uint32_t size = 0;
    uint32_t pathLength = 0, points = 0, indices = 0, primitives = 0;
    uint32_t materialLength = 0, attributes = 0;
    if (!HdSilkTest::FindMeshCommand(
            page, OPENUSD_SILK_COMMAND_MESH_UPSERT, path.GetString(), &offset, &size) ||
        !HdSilkTest::ReadPageValue(page, offset + 48, &pathLength) ||
        !HdSilkTest::ReadPageValue(page, offset + 52, &points) ||
        !HdSilkTest::ReadPageValue(page, offset + 56, &indices) ||
        !HdSilkTest::ReadPageValue(page, offset + 60, &primitives) ||
        !HdSilkTest::ReadPageValue(page, offset + 216, &materialLength) ||
        !HdSilkTest::ReadPageValue(page, offset + 220, &attributes))
    {
        return false;
    }
    size_t cursor = offset + 276 + pathLength +
        static_cast<size_t>(points) * 3 * sizeof(float) +
        static_cast<size_t>(indices + primitives) * sizeof(uint32_t) + materialLength;
    const size_t end = offset + size;
    for (uint32_t index = 0; index < attributes; ++index)
    {
        uint32_t actualSemantic = 0, actualComponents = 0, interpolation = 0;
        uint32_t nameLength = 0, elements = 0;
        if (cursor > end || end - cursor < 20 ||
            !HdSilkTest::ReadPageValue(page, cursor, &actualSemantic) ||
            !HdSilkTest::ReadPageValue(page, cursor + 4, &actualComponents) ||
            !HdSilkTest::ReadPageValue(page, cursor + 8, &interpolation) ||
            !HdSilkTest::ReadPageValue(page, cursor + 12, &nameLength) ||
            !HdSilkTest::ReadPageValue(page, cursor + 16, &elements))
        {
            return false;
        }
        cursor += 20;
        const uint64_t bytes = static_cast<uint64_t>(actualComponents) * elements * sizeof(float);
        if (nameLength > end - cursor || bytes > end - cursor - nameLength)
        {
            return false;
        }
        if (nameLength == name.size() &&
            std::memcmp(page.data() + cursor, name.data(), nameLength) == 0)
        {
            if (actualSemantic != semantic || actualComponents != components ||
                interpolation != OPENUSD_SILK_INTERPOLATION_VERTEX ||
                static_cast<uint64_t>(elements) * components != expected.size())
            {
                return false;
            }
            for (size_t value = 0; value < expected.size(); ++value)
            {
                float actual = 0;
                if (!HdSilkTest::ReadPageValue(
                        page, cursor + nameLength + value * sizeof(float), &actual) ||
                    std::abs(actual - expected[value]) > 1e-5f)
                {
                    return false;
                }
            }
            return true;
        }
        cursor += nameLength + static_cast<size_t>(bytes);
    }
    return false;
}

int32_t VerifyUntrustedNurbsMetadata(bool patch)
{
    const SdfPath path(patch ? "/OrdinaryMesh" : "/OrdinaryCurve");
    for (int markerKind = 0; markerKind < 3; ++markerKind)
    {
        NurbsScene scene;
        if (!scene.Open())
        {
            return 1;
        }
        UsdGeomPrimvarsAPI primvars(scene.stage->GetPrimAtPath(path));
        const auto marker = primvars.CreatePrimvar(
            HdSilkNurbsGeneratedGeometryPrimvar(),
            markerKind == 2 ? SdfValueTypeNames->String : SdfValueTypeNames->Bool,
            UsdGeomTokens->constant);
        const auto plan = primvars.CreatePrimvar(
            HdSilkNurbsGeneratedGeometryPlanPrimvar(),
            SdfValueTypeNames->UInt64Array, UsdGeomTokens->constant);
        const auto provenance = primvars.CreatePrimvar(
            TfToken("silkNurbsAdapterProvenance"),
            SdfValueTypeNames->String, UsdGeomTokens->constant);
        if (!marker.Set(markerKind == 2 ? VtValue(std::string("true")) : VtValue(markerKind != 0)) ||
            !provenance.Set(std::string("authored")) ||
            (markerKind != 0 && !plan.Set(markerKind == 1
                ? VtUInt64Array{0, 0, 0, 0} : VtUInt64Array{1})))
        {
            return 2;
        }
        scene.source->ApplyPendingUpdates();
        const auto reads = std::make_shared<size_t>(0);
        SyncTarget target;
        if (!target.Insert(CountPointReads::New(scene.converted, path, reads), path))
        {
            return 3;
        }
        HdSilkNurbsGeneratedGeometryPlan generated;
        if (HdSilkHasNurbsGeneratedGeometry(target.adapter, path) ||
            HdSilkGetNurbsGeneratedGeometryPlan(target.adapter, path, &generated))
        {
            return 4;
        }
        const auto& state = target.renderDelegate.GetSceneState();
        const auto& budget = state->MeshPreparationBudget();
        budget->Configure(1);
        if (!target.Sync(target.rprim->GetInitialDirtyBitsMask()) ||
            !budget->Refused() || *reads == 0 || state->HasMeshPath(path.GetString()))
        {
            return 5;
        }
    }
    return 0;
}

int32_t VerifyTrustedNurbsPlanValidation(bool patch)
{
    const SdfPath& path = patch ? PatchPath : CurvePath;
    for (int mode = 0; mode < 4; ++mode)
    {
        NurbsScene scene;
        if (!scene.Open())
        {
            return 1;
        }
        HdSampledDataSourceHandle value;
        if (mode == 1)
        {
            value = HdRetainedTypedSampledDataSource<bool>::New(true);
        }
        else if (mode == 2)
        {
            value = HdRetainedTypedSampledDataSource<VtUInt64Array>::New(
                VtUInt64Array{0, 0, 0, 0});
        }
        HdDataSourceBaseHandle planMetadata;
        if (mode == 0)
        {
            planMetadata = HdBlockDataSource::New();
        }
        else
        {
            planMetadata = HdPrimvarSchema::Builder()
                .SetPrimvarValue(value)
                .SetInterpolation(HdPrimvarSchema::BuildInterpolationDataSource(
                    HdPrimvarSchemaTokens->constant)).Build();
        }
        const auto metadata = HdRetainedContainerDataSource::New(
            HdSilkNurbsGeneratedGeometryPlanPrimvar(), planMetadata);
        if (mode == 3 && !scene.Set(
                path, patch ? "uOrder" : "order",
                patch ? VtValue(0) : VtValue(VtIntArray{0})))
        {
            return 2;
        }
        const auto reads = std::make_shared<size_t>(0);
        SyncTarget target;
        if (!target.Insert(CountPointReads::New(
                scene.converted, path, reads, mode == 3 ? nullptr : metadata), path))
        {
            return 3;
        }
        if (!HdSilkHasNurbsGeneratedGeometry(target.adapter, path))
        {
            return 4;
        }
        try
        {
            HdSilkNurbsGeneratedGeometryPlan plan;
            HdSilkGetNurbsGeneratedGeometryPlan(target.adapter, path, &plan);
            return 5;
        }
        catch (const std::invalid_argument& error)
        {
            const std::string expected = mode == 3
                ? "topology was refused" : "metadata is missing or malformed";
            if (std::string(error.what()).find(expected) == std::string::npos || *reads != 0)
            {
                return 6;
            }
        }
        const auto& budget = target.renderDelegate.GetSceneState()->MeshPreparationBudget();
        budget->Configure(64ull * 1024 * 1024);
        if (!target.Sync(target.rprim->GetInitialDirtyBitsMask()) ||
            !budget->Refused() || *reads != 0 || budget->Peak() != 0)
        {
            return 7;
        }
    }
    return 0;
}

int32_t VerifyRationalSync(bool patch)
{
    NurbsScene scene;
    if (!scene.Open())
    {
        return 1;
    }
    const SdfPath& path = patch ? PatchPath : CurvePath;
    const VtDoubleArray expectedWeights = patch
        ? VtDoubleArray{1, 1, 2, 2}
        : VtDoubleArray{1, 0.7071067811865476, 1};
    if (!VerifyAuthoredPointWeights(scene.source->GetPrim(path), expectedWeights))
    {
        return 2;
    }
    const auto prim = scene.converted->GetPrim(path);
    if (patch ? !VerifyRationalPatchOutput(prim, 2.0)
              : !VerifyRationalCurveOutput(prim, std::sqrt(0.5)))
    {
        return 3;
    }
    SyncTarget target;
    if (!target.Insert(scene.converted, path) ||
        !target.Sync(target.rprim->GetInitialDirtyBitsMask()) ||
        !target.VerifyPublished(path, GeneratedPoints(prim, HdPrimvarsSchemaTokens->points)))
    {
        return 4;
    }
    return 0;
}

int32_t VerifyConcurrentNurbsSampling()
{
    NurbsScene scene;
    if (!scene.Open())
    {
        return 1;
    }

    VtVec3fArray basePoints = {
        GfVec3f(1, 0, 0), GfVec3f(1, 1, 0), GfVec3f(0, 1, 0)};
    VtVec3fArray shiftedPoints = basePoints;
    for (GfVec3f& point : shiftedPoints)
    {
        point[0] += 100.0f;
    }
    if (!scene.SetAtTime(CurvePath, "points", VtValue(basePoints), 0.0) ||
        !scene.SetAtTime(CurvePath, "points", VtValue(shiftedPoints), 1.0))
    {
        return 2;
    }

    const HdSceneIndexPrim prim = scene.converted->GetPrim(CurvePath);
    const auto points = HdVec3fArrayDataSource::Cast(
        HdPrimvarsSchema::GetFromParent(prim.dataSource)
            .GetPrimvar(HdPrimvarsSchemaTokens->points).GetPrimvarValue());
    const auto counts = HdIntArrayDataSource::Cast(
        HdBasisCurvesSchema::GetFromParent(prim.dataSource)
            .GetTopology().GetCurveVertexCounts());
    if (!points || !counts)
    {
        return 3;
    }

    constexpr size_t threadCount = 8;
    constexpr size_t iterations = 1000;
    std::atomic<size_t> ready{0};
    std::atomic<bool> start{false};
    std::atomic<bool> valid{true};
    std::vector<std::thread> readers;
    readers.reserve(threadCount);
    for (size_t reader = 0; reader < threadCount; ++reader)
    {
        const bool shifted = reader % 2 != 0;
        readers.emplace_back([&, shifted] {
            const HdSampledDataSource::Time time = shifted ? 1.0f : 0.0f;
            const float expectedX = shifted ? 101.0f : 1.0f;
            ++ready;
            while (!start.load())
            {
                std::this_thread::yield();
            }
            for (size_t iteration = 0; iteration < iterations; ++iteration)
            {
                const VtVec3fArray sampledPoints = points->GetTypedValue(time);
                if (sampledPoints.size() != 9 ||
                    std::abs(sampledPoints.front()[0] - expectedX) > 1e-6f)
                {
                    valid = false;
                    return;
                }
                if (iteration % 8 == 0 &&
                    counts->GetTypedValue(time) != VtIntArray{9})
                {
                    valid = false;
                    return;
                }
            }
        });
    }
    while (ready.load() != threadCount)
    {
        std::this_thread::yield();
    }
    start = true;
    for (std::thread& reader : readers)
    {
        reader.join();
    }
    if (!valid)
    {
        return 4;
    }

    VtVec3fArray basePatchPoints = {
        GfVec3f(0, 0, 0), GfVec3f(0, 2, 0),
        GfVec3f(2, 0, 0), GfVec3f(2, 2, 0)};
    VtVec3fArray shiftedPatchPoints = basePatchPoints;
    for (GfVec3f& point : shiftedPatchPoints)
    {
        point[0] += 100.0f;
    }
    if (!scene.SetAtTime(PatchPath, "points", VtValue(basePatchPoints), 0.0) ||
        !scene.SetAtTime(PatchPath, "points", VtValue(shiftedPatchPoints), 1.0))
    {
        return 5;
    }

    const HdSceneIndexPrim patchPrim = scene.converted->GetPrim(PatchPath);
    const HdPrimvarsSchema patchPrimvars =
        HdPrimvarsSchema::GetFromParent(patchPrim.dataSource);
    const auto patchPoints = HdVec3fArrayDataSource::Cast(
        patchPrimvars.GetPrimvar(HdPrimvarsSchemaTokens->points).GetPrimvarValue());
    const auto normals = HdVec3fArrayDataSource::Cast(
        patchPrimvars.GetPrimvar(HdPrimvarsSchemaTokens->normals).GetPrimvarValue());
    const HdMeshTopologySchema topology =
        HdMeshSchema::GetFromParent(patchPrim.dataSource).GetTopology();
    const auto faceCounts =
        HdIntArrayDataSource::Cast(topology.GetFaceVertexCounts());
    const auto faceIndices =
        HdIntArrayDataSource::Cast(topology.GetFaceVertexIndices());
    if (!patchPoints || !normals || !faceCounts || !faceIndices)
    {
        return 6;
    }

    ready = 0;
    start = false;
    valid = true;
    readers.clear();
    for (size_t reader = 0; reader < threadCount; ++reader)
    {
        const bool shifted = reader % 2 != 0;
        readers.emplace_back([&, shifted] {
            const HdSampledDataSource::Time time = shifted ? 1.0f : 0.0f;
            const float expectedX = shifted ? 100.0f : 0.0f;
            ++ready;
            while (!start.load())
            {
                std::this_thread::yield();
            }
            for (size_t iteration = 0; iteration < iterations; ++iteration)
            {
                const VtVec3fArray sampledPoints = patchPoints->GetTypedValue(time);
                const VtVec3fArray sampledNormals = normals->GetTypedValue(time);
                if (sampledPoints.size() != 81 ||
                    std::abs(sampledPoints.front()[0] - expectedX) > 1e-6f ||
                    sampledNormals.size() != 81 ||
                    std::abs(sampledNormals[40][2] - 1.0f) > 1e-6f ||
                    faceCounts->GetTypedValue(time) != VtIntArray(64, 4))
                {
                    valid = false;
                    return;
                }
                if (iteration % 8 == 0)
                {
                    const VtIntArray sampledIndices = faceIndices->GetTypedValue(time);
                    if (sampledIndices.size() != 256 ||
                        sampledIndices[0] != 0 ||
                        sampledIndices[1] != 1 ||
                        sampledIndices[2] != 10 ||
                        sampledIndices[3] != 9)
                    {
                        valid = false;
                        return;
                    }
                }
            }
        });
    }
    while (ready.load() != threadCount)
    {
        std::this_thread::yield();
    }
    start = true;
    for (std::thread& reader : readers)
    {
        reader.join();
    }
    return valid ? 0 : 7;
}

int32_t VerifyPrimvarRetention()
{
    NurbsScene scene;
    if (!scene.Open())
    {
        return 1;
    }

    UsdGeomPrimvarsAPI primvars(scene.stage->GetPrimAtPath(CurvePath));
    const UsdGeomPrimvar vertexTint = primvars.CreatePrimvar(
        TfToken("vertexTint"),
        SdfValueTypeNames->FloatArray,
        UsdGeomTokens->vertex);
    const UsdGeomPrimvar uniformTint = primvars.CreatePrimvar(
        TfToken("uniformTint"),
        SdfValueTypeNames->FloatArray,
        UsdGeomTokens->uniform);
    const auto st = primvars.CreatePrimvar(
        TfToken("st"), SdfValueTypeNames->TexCoord2fArray, UsdGeomTokens->vertex);
    const auto color = primvars.CreatePrimvar(
        HdTokens->displayColor, SdfValueTypeNames->Color3fArray, UsdGeomTokens->vertex);
    const auto tint4 = primvars.CreatePrimvar(
        TfToken("tint4"), SdfValueTypeNames->Float4Array, UsdGeomTokens->vertex);
    if (!vertexTint || !vertexTint.Set(VtFloatArray{0.1f, 0.4f, 0.9f}) ||
        !uniformTint || !uniformTint.Set(VtFloatArray{0.5f}) ||
        !st.Set(VtVec2fArray{GfVec2f(0.1f, 1.1f), GfVec2f(0.4f, 1.4f), GfVec2f(0.9f, 1.9f)}) ||
        !color.Set(VtVec3fArray{GfVec3f(0.1f), GfVec3f(0.4f), GfVec3f(0.9f)}) ||
        !tint4.Set(VtVec4fArray{GfVec4f(0.1f), GfVec4f(0.4f), GfVec4f(0.9f)}))
    {
        return 2;
    }
    scene.source->ApplyPendingUpdates();

    {
        DiscardedPrimvarDiagnostic diagnostic;
        const HdSceneIndexPrim converted = scene.converted->GetPrim(CurvePath);
        const HdPrimvarsSchema convertedPrimvars =
            HdPrimvarsSchema::GetFromParent(converted.dataSource);
        const auto outputPoints =
            GeneratedPoints(converted, HdPrimvarsSchemaTokens->points);
        const HdPrimvarSchema retained =
            convertedPrimvars.GetPrimvar(TfToken("vertexTint"));
        if (outputPoints.size() != 9 || !retained.IsDefined() ||
            !retained.GetInterpolation() ||
            retained.GetInterpolation()->GetTypedValue(0.0f) !=
                HdPrimvarSchemaTokens->vertex ||
            !retained.GetPrimvarValue() ||
            convertedPrimvars.GetPrimvar(TfToken("uniformTint")).IsDefined() ||
            diagnostic.discardWarnings != 1 || diagnostic.unexpected)
        {
            return 3;
        }
        const VtValue value = retained.GetPrimvarValue()->GetValue(0.0f);
        if (!value.IsHolding<VtFloatArray>())
        {
            return 4;
        }
        // The tint follows the rational quarter-circle basis: at the middle
        // sample the Bernstein weights (1/4, 1/2, 1/4) scale by (1, w, 1).
        const VtFloatArray tint = value.UncheckedGet<VtFloatArray>();
        const double weight = std::sqrt(0.5);
        const double middle =
            (0.25 * 0.1 + 0.5 * weight * 0.4 + 0.25 * 0.9) / (0.5 + 0.5 * weight);
        if (tint.size() != 9 || std::abs(tint[0] - 0.1) > 1e-5 ||
            std::abs(tint[4] - middle) > 1e-5 || std::abs(tint[8] - 0.9) > 1e-5)
        {
            return 5;
        }
    }

    std::vector<float> expectedTint, expectedSt, expectedColor, expectedTint4;
    const auto appendSample = [&](size_t sample) {
        const double t = static_cast<double>(sample) / 8;
        const double a = (1 - t) * (1 - t);
        const double b = 2 * t * (1 - t) * std::sqrt(0.5);
        const double c = t * t;
        const float tint = static_cast<float>((a * 0.1 + b * 0.4 + c * 0.9) / (a + b + c));
        expectedTint.push_back(tint);
        expectedSt.insert(expectedSt.end(), {tint, tint + 1});
        expectedColor.insert(expectedColor.end(), 3, tint);
        expectedTint4.insert(expectedTint4.end(), 4, tint);
    };
    for (size_t segment = 0; segment < 8; ++segment)
    {
        appendSample(segment);
        appendSample(segment + 1);
    }

    SyncTarget target;
    if (!target.Insert(scene.converted, CurvePath))
    {
        return 6;
    }
    const auto& budget = target.renderDelegate.GetSceneState()->MeshPreparationBudget();
    budget->Configure(64ull * 1024 * 1024);
    if (!target.Sync(target.rprim->GetInitialDirtyBitsMask()))
    {
        return 7;
    }
    const uint64_t peak = budget->Peak();
    const HdSilkCurvePreparationPlan basePlan(9, 1, 8);
    const uint64_t attributeBytes = 4 * (9 + 3 * 16) * 4 * sizeof(float);
    if (budget->Refused() || peak != basePlan.bytes + attributeBytes)
    {
        return 8;
    }
    const auto points = GeneratedPoints(
        scene.converted->GetPrim(CurvePath), HdPrimvarsSchemaTokens->points);
    VtVec3fArray emittedPoints;
    for (size_t sample = 1; sample < points.size(); ++sample)
    {
        emittedPoints.push_back(points[sample - 1]);
        emittedPoints.push_back(points[sample]);
    }
    uint32_t commands = 0;
    const auto page = target.renderDelegate.GetSceneState()->BuildPage(nullptr, &commands);
    if (commands == 0 || !HdSilkTest::HasMeshPoints(page, CurvePath.GetString(), emittedPoints) ||
        !VerifyPublishedVertexAttribute(
            page, CurvePath, "vertexTint", OPENUSD_SILK_ATTRIBUTE_CUSTOM, 1, expectedTint) ||
        !VerifyPublishedVertexAttribute(
            page, CurvePath, "st", OPENUSD_SILK_ATTRIBUTE_TEXCOORD, 2, expectedSt) ||
        !VerifyPublishedVertexAttribute(
            page, CurvePath, "displayColor", OPENUSD_SILK_ATTRIBUTE_COLOR, 3, expectedColor) ||
        !VerifyPublishedVertexAttribute(
            page, CurvePath, "tint4", OPENUSD_SILK_ATTRIBUTE_CUSTOM, 4, expectedTint4))
    {
        return 10;
    }
    const auto reads = std::make_shared<size_t>(0);
    SyncTarget refused;
    if (!refused.Insert(CountPointReads::New(scene.converted, CurvePath, reads), CurvePath))
    {
        return 11;
    }
    const auto& refusedBudget = refused.renderDelegate.GetSceneState()->MeshPreparationBudget();
    refusedBudget->Configure(peak - 1);
    if (!refused.Sync(refused.rprim->GetInitialDirtyBitsMask()) ||
        !refusedBudget->Refused() || *reads != 0 || refusedBudget->Peak() != 0)
    {
        return 12;
    }
    return 0;
}

int32_t VerifyNurbsDiagnosticRecovery()
{
    NurbsScene scene;
    if (!scene.Open())
    {
        return 1;
    }

    if (!scene.SetAtTime(
            CurvePath, "pointWeights", VtValue(VtDoubleArray{}), 0.0) ||
        !scene.SetAtTime(
            CurvePath, "pointWeights", VtValue(VtDoubleArray{}), 0.5) ||
        !scene.SetAtTime(
            CurvePath, "pointWeights", VtValue(VtDoubleArray{1, -1, 1}), 1.0) ||
        !scene.SetAtTime(
            CurvePath, "pointWeights", VtValue(VtDoubleArray{1, 1, 1}), 2.0) ||
        !scene.SetAtTime(
            CurvePath, "pointWeights", VtValue(VtDoubleArray{}), 3.0))
    {
        return 2;
    }

    const HdSceneIndexPrim prim = scene.converted->GetPrim(CurvePath);
    const auto points = HdVec3fArrayDataSource::Cast(
        HdPrimvarsSchema::GetFromParent(prim.dataSource)
            .GetPrimvar(HdPrimvarsSchemaTokens->points).GetPrimvarValue());
    if (!points)
    {
        return 3;
    }

    NurbsFailureSequenceDiagnostic diagnostic(
        CurvePath,
        "curve points or rational weights have an unsupported type.",
        "NURBS curve weights must be finite and have one sign per curve.");
    if (!points->GetTypedValue(0.0f).empty() ||
        !points->GetTypedValue(0.0f).empty() ||
        diagnostic.firstWarnings != 1 ||
        diagnostic.secondWarnings != 0 ||
        !points->GetTypedValue(0.5f).empty() ||
        diagnostic.firstWarnings != 1 ||
        diagnostic.secondWarnings != 0 ||
        !points->GetTypedValue(1.0f).empty() ||
        diagnostic.firstWarnings != 1 ||
        diagnostic.secondWarnings != 1 ||
        points->GetTypedValue(2.0f).size() != 9 ||
        !points->GetTypedValue(3.0f).empty() ||
        diagnostic.firstWarnings != 2 ||
        diagnostic.secondWarnings != 1 ||
        diagnostic.unexpected)
    {
        return 4;
    }
    return 0;
}

int32_t VerifyDirtyWeightUpdate(bool patch)
{
    NurbsScene scene;
    if (!scene.Open())
    {
        return 1;
    }
    const SdfPath& path = patch ? PatchPath : CurvePath;
    const auto before = scene.converted->GetPrim(path);
    if (patch ? !VerifyRationalPatchOutput(before, 2.0)
              : !VerifyRationalCurveOutput(before, std::sqrt(0.5)))
    {
        return 2;
    }
    SyncTarget target;
    if (!target.Insert(scene.converted, path) ||
        !target.Sync(target.rprim->GetInitialDirtyBitsMask()) ||
        !target.VerifyPublished(path, GeneratedPoints(before, HdPrimvarsSchemaTokens->points)))
    {
        return 3;
    }
    auto& tracker = target.index->GetChangeTracker();
    tracker.MarkRprimClean(path, HdChangeTracker::Clean);
    DirtyObserver observer(path);
    scene.converted->AddObserver(TfCreateWeakPtr(&observer));
    const bool edited = scene.Set(path, "pointWeights",
        VtValue(patch ? VtDoubleArray{1, 1, 1, 1} : VtDoubleArray{1, 1, 1}));
    scene.converted->RemoveObserver(TfCreateWeakPtr(&observer));
    const auto after = scene.converted->GetPrim(path);
    const HdDirtyBits dirty = target.rprim->PropagateRprimDirtyBits(
        tracker.GetRprimDirtyBits(path));
    if (!edited || observer.universalDirties == 0 ||
        (dirty & HdChangeTracker::DirtyPoints) == 0 ||
        (dirty & HdChangeTracker::DirtyTopology) == 0 ||
        (patch ? !VerifyRationalPatchOutput(after, 1.0)
               : !VerifyRationalCurveOutput(after, 1.0)) ||
        GeneratedPoints(before, HdPrimvarsSchemaTokens->points) ==
            GeneratedPoints(after, HdPrimvarsSchemaTokens->points))
    {
        return 4;
    }
    if (!target.Sync(dirty) ||
        !target.VerifyPublished(path, GeneratedPoints(after, HdPrimvarsSchemaTokens->points)))
    {
        return 5;
    }
    return 0;
}

int32_t VerifyEmptyPointWeightsRejectionAndAbsentRecovery(bool patch)
{
    NurbsScene scene;
    if (!scene.Open())
    {
        return 1;
    }
    const SdfPath& path = patch ? PatchPath : CurvePath;
    const auto initial = scene.converted->GetPrim(path);
    if (patch ? !VerifyRationalPatchOutput(initial, 2.0)
              : !VerifyRationalCurveOutput(initial, std::sqrt(0.5)))
    {
        return 2;
    }
    SyncTarget target;
    if (!target.Insert(scene.converted, path) ||
        !target.Sync(target.rprim->GetInitialDirtyBitsMask()) ||
        !target.VerifyPublished(path, GeneratedPoints(initial, HdPrimvarsSchemaTokens->points)))
    {
        return 3;
    }

    auto& tracker = target.index->GetChangeTracker();
    tracker.MarkRprimClean(path, HdChangeTracker::Clean);
    if (!scene.Set(path, "pointWeights", VtValue(VtDoubleArray{})))
    {
        return 4;
    }
    {
        PointWeightsDiagnostic diagnostic(
            path, patch
                ? "patch points or rational weights have an unsupported type."
                : "curve points or rational weights have an unsupported type.");
        const auto rejected = scene.converted->GetPrim(path);
        const bool rejectedOutput = patch
            ? VerifyRejectedPatchOutput(rejected)
            : VerifyRejectedCurveOutput(rejected);
        if (!rejectedOutput || diagnostic.rejectionWarnings != 1 || diagnostic.unexpected)
        {
            return 5;
        }
    }

    HdDirtyBits dirty = target.rprim->PropagateRprimDirtyBits(
        tracker.GetRprimDirtyBits(path));
    if ((dirty & HdChangeTracker::DirtyPoints) == 0 ||
        (dirty & HdChangeTracker::DirtyTopology) == 0 || !target.Sync(dirty) ||
        target.renderDelegate.GetSceneState()->HasMeshPath(path.GetString()))
    {
        return 6;
    }
    uint32_t commands = 0;
    const auto removed = target.renderDelegate.GetSceneState()->BuildPage(nullptr, &commands);
    if (!HdSilkTest::HasMeshRemoval(removed, path.GetString()))
    {
        return 7;
    }

    tracker.MarkRprimClean(path, HdChangeTracker::Clean);
    if (!scene.Clear(path, "pointWeights"))
    {
        return 8;
    }
    if (scene.stage->GetPrimAtPath(path)
            .GetAttribute(TfToken("pointWeights")).HasAuthoredValueOpinion())
    {
        return 9;
    }
    const auto recovered = scene.converted->GetPrim(path);
    const bool unitWeightOutput = patch
        ? VerifyUnitWeightPatchOutput(recovered)
        : VerifyUnitWeightCurveOutput(recovered);
    dirty = target.rprim->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(path));
    return unitWeightOutput &&
        (dirty & HdChangeTracker::DirtyPoints) != 0 &&
        (dirty & HdChangeTracker::DirtyTopology) != 0 &&
        target.Sync(dirty) &&
        target.VerifyPublished(path, GeneratedPoints(recovered, HdPrimvarsSchemaTokens->points))
        ? 0 : 10;
}

int32_t VerifyTrimmedPatchRejectionAndRecovery()
{
    NurbsScene scene;
    if (!scene.Open() ||
        !VerifyRationalPatchOutput(scene.converted->GetPrim(PatchPath), 2.0))
    {
        return 1;
    }
    SyncTarget target;
    if (!target.Insert(scene.converted, PatchPath) ||
        !target.Sync(target.rprim->GetInitialDirtyBitsMask()) ||
        !target.VerifyPublished(PatchPath, GeneratedPoints(
            scene.converted->GetPrim(PatchPath), HdPrimvarsSchemaTokens->points)))
    {
        return 2;
    }
    auto& tracker = target.index->GetChangeTracker();
    tracker.MarkRprimClean(PatchPath, HdChangeTracker::Clean);
    const UsdPrim patch = scene.stage->GetPrimAtPath(PatchPath);
    // A closed triangular trim loop, not an invalid patch or missing control net.
    const bool authored =
        patch.GetAttribute(TfToken("trimCurve:counts")).Set(VtIntArray{1}) &&
        patch.GetAttribute(TfToken("trimCurve:orders")).Set(VtIntArray{2}) &&
        patch.GetAttribute(TfToken("trimCurve:vertexCounts")).Set(VtIntArray{4}) &&
        patch.GetAttribute(TfToken("trimCurve:knots")).Set(
            VtDoubleArray{0, 0, 1, 2, 3, 3}) &&
        patch.GetAttribute(TfToken("trimCurve:ranges")).Set(
            VtVec2dArray{GfVec2d(0, 3)}) &&
        patch.GetAttribute(TfToken("trimCurve:points")).Set(
            VtVec3dArray{GfVec3d(0.1, 0.1, 1), GfVec3d(0.9, 0.1, 1),
                GfVec3d(0.1, 0.9, 1), GfVec3d(0.1, 0.1, 1)});
    scene.source->ApplyPendingUpdates();
    if (!authored)
    {
        return 3;
    }
    {
        TrimDiagnostic diagnostic;
        const auto rejected = scene.converted->GetPrim(PatchPath);
        const auto topology = HdMeshSchema::GetFromParent(rejected.dataSource).GetTopology();
        if (rejected.primType != HdPrimTypeTokens->mesh ||
            !topology.GetFaceVertexCounts() || !topology.GetFaceVertexIndices() ||
            !GeneratedPoints(rejected, HdPrimvarsSchemaTokens->points).empty() ||
            !GeneratedPoints(rejected, HdPrimvarsSchemaTokens->normals).empty() ||
            !topology.GetFaceVertexCounts()->GetTypedValue(0.0f).empty() ||
            !topology.GetFaceVertexIndices()->GetTypedValue(0.0f).empty() ||
            diagnostic.trimWarnings != 1 || diagnostic.unexpected)
        {
            return 4;
        }
    }
    HdDirtyBits dirty = target.rprim->PropagateRprimDirtyBits(
        tracker.GetRprimDirtyBits(PatchPath));
    if ((dirty & HdChangeTracker::DirtyTopology) == 0 || !target.Sync(dirty) ||
        target.renderDelegate.GetSceneState()->HasMeshPath(PatchPath.GetString()))
    {
        return 5;
    }
    uint32_t commands = 0;
    const auto removed = target.renderDelegate.GetSceneState()->BuildPage(nullptr, &commands);
    if (!HdSilkTest::HasMeshRemoval(removed, PatchPath.GetString()))
    {
        return 6;
    }
    tracker.MarkRprimClean(PatchPath, HdChangeTracker::Clean);
    for (const char* name : {"trimCurve:counts", "trimCurve:orders", "trimCurve:vertexCounts",
            "trimCurve:knots", "trimCurve:ranges", "trimCurve:points"})
    {
        if (!patch.GetAttribute(TfToken(name)).Clear())
        {
            return 7;
        }
    }
    scene.source->ApplyPendingUpdates();
    const auto recovered = scene.converted->GetPrim(PatchPath);
    dirty = target.rprim->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(PatchPath));
    return VerifyRationalPatchOutput(recovered, 2.0) &&
        (dirty & HdChangeTracker::DirtyTopology) != 0 && target.Sync(dirty) &&
        target.VerifyPublished(PatchPath, GeneratedPoints(recovered, HdPrimvarsSchemaTokens->points))
        ? 0 : 8;
}

// Form tokens describe how the authored control grid and knots already close
// the surface. They add no evaluation rule, so changing them must neither warn
// nor change the generated output.
int32_t VerifyClosedPeriodicPatchFormAcceptance()
{
    struct FormCase
    {
        const char* attribute;
        const char* value;
    };
    const FormCase cases[] = {
        {"uForm", "closed"},
        {"uForm", "periodic"},
        {"vForm", "closed"},
        {"vForm", "periodic"}};

    for (const FormCase& formCase : cases)
    {
        NurbsScene scene;
        if (!scene.Open())
        {
            return 1;
        }
        SyncTarget target;
        const auto initial = scene.converted->GetPrim(PatchPath);
        if (!target.Insert(scene.converted, PatchPath) ||
            !target.Sync(target.rprim->GetInitialDirtyBitsMask()) ||
            !target.VerifyPublished(
                PatchPath, GeneratedPoints(initial, HdPrimvarsSchemaTokens->points)))
        {
            return 2;
        }

        auto& tracker = target.index->GetChangeTracker();
        tracker.MarkRprimClean(PatchPath, HdChangeTracker::Clean);
        HdSceneIndexPrim accepted;
        {
            NoDiagnostic diagnostic;
            if (!scene.Set(
                    PatchPath, formCase.attribute, VtValue(TfToken(formCase.value))))
            {
                return 3;
            }
            accepted = scene.converted->GetPrim(PatchPath);
            if (!VerifyRationalPatchOutput(accepted, 2.0) || diagnostic.unexpected)
            {
                return 4;
            }
        }

        const HdDirtyBits dirty = target.rprim->PropagateRprimDirtyBits(
            tracker.GetRprimDirtyBits(PatchPath));
        if (!target.Sync(dirty) ||
            !target.VerifyPublished(
                PatchPath, GeneratedPoints(accepted, HdPrimvarsSchemaTokens->points)))
        {
            return 5;
        }
    }
    return 0;
}

// Bounded sessions plan generated NURBS from topology alone. A tiny limit
// refuses admission before any authored or generated point is read; a limit
// that covers the plan publishes the evaluated geometry.
int32_t VerifyBoundedNurbsPreparation(bool patch)
{
    const int32_t untrusted = VerifyUntrustedNurbsMetadata(patch);
    if (untrusted != 0)
    {
        return 20 + untrusted;
    }
    const int32_t metadata = VerifyTrustedNurbsPlanValidation(patch);
    if (metadata != 0)
    {
        return 30 + metadata;
    }
    const SdfPath& path = patch ? PatchPath : CurvePath;
    const auto authoredReads = std::make_shared<size_t>(0);
    NurbsScene scene;
    if (!scene.Open(path, authoredReads))
    {
        return 1;
    }
    const auto reads = std::make_shared<size_t>(0);
    const auto counted = CountPointReads::New(scene.converted, path, reads);
    SyncTarget target;
    if (!target.Insert(counted, path))
    {
        return 2;
    }
    const auto& state = target.renderDelegate.GetSceneState();
    const auto& budget = state->MeshPreparationBudget();
    budget->Configure(1);
    if (*reads != 0 || *authoredReads != 0 ||
        !target.Sync(target.rprim->GetInitialDirtyBitsMask()) ||
        !budget->Refused() || *reads != 0 || *authoredReads != 0 || budget->Reserved() != 0 ||
        state->HasMeshPath(path.GetString()))
    {
        return 3;
    }
    try
    {
        budget->ThrowIfRefused();
        return 4;
    }
    catch (const HdSilkMeshPreparationExceeded&)
    {
    }
    uint32_t commands = 0;
    const auto page = state->BuildPage(nullptr, &commands);
    size_t offset = 0;
    uint32_t size = 0;
    if (HdSilkTest::FindMeshCommand(
            page, OPENUSD_SILK_COMMAND_MESH_UPSERT, path.GetString(), &offset, &size))
    {
        return 5;
    }

    budget->Configure(64ull * 1024 * 1024);
    auto& tracker = target.index->GetChangeTracker();
    tracker.MarkRprimClean(path, HdChangeTracker::Clean);
    tracker.MarkRprimDirty(path, HdChangeTracker::AllDirty);
    if (!target.Sync(target.rprim->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(path))) ||
        budget->Refused() || budget->Peak() == 0 || *reads == 0 || *authoredReads == 0 ||
        !target.VerifyPublished(path, GeneratedPoints(
            scene.converted->GetPrim(path), HdPrimvarsSchemaTokens->points)))
    {
        return 6;
    }
    return 0;
}
}

extern "C" OPENUSD_HDSILK_API int32_t
openusd_hdsilk_test_nurbs_sync(const char* pluginPath, int32_t scenario)
{
    if (!pluginPath || pluginPath[0] == '\0')
    {
        return -1;
    }
    PlugRegistry::GetInstance().RegisterPlugins(pluginPath);
    switch (scenario)
    {
    case 0: return VerifyRationalSync(false);
    case 1: return VerifyRationalSync(true);
    case 2: return VerifyTrimmedPatchRejectionAndRecovery();
    case 3: return VerifyDirtyWeightUpdate(false);
    case 4: return VerifyDirtyWeightUpdate(true);
    case 5: return VerifyBoundedNurbsPreparation(false);
    case 6: return VerifyBoundedNurbsPreparation(true);
    case 7: return VerifyClosedPeriodicPatchFormAcceptance();
    case 8: return VerifyEmptyPointWeightsRejectionAndAbsentRecovery(false);
    case 9: return VerifyEmptyPointWeightsRejectionAndAbsentRecovery(true);
    case 10: return VerifyConcurrentNurbsSampling();
    case 11: return VerifyPrimvarRetention();
    case 12: return VerifyNurbsDiagnosticRecovery();
    default: return -1;
    }
}
