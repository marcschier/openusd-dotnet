// Copyright (c) marcschier. Licensed under the MIT License.
#include "internal/review_document.h"
#include "pxr/usd/ar/defaultResolver.h"
#include "pxr/usd/ar/resolver.h"
#include "pxr/usd/ar/resolverContextBinder.h"
#include "pxr/usd/sdf/layerUtils.h"

#include <cctype>
#include <filesystem>
#include <fstream>

namespace OpenUsdReview
{
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
thread_local bool mutatePublishedSource = false;
#endif
namespace
{
std::recursive_mutex originMutex;
std::map<std::string, std::shared_ptr<Origin>> origins;

class SeedFormat final : public SdfFileFormat
{
public:
    SeedFormat(SdfFileFormatConstPtr native, TfRefPtr<CountedData> data)
        : SdfFileFormat(native->GetFormatId(), native->GetVersionString(),
            native->GetTarget(), native->GetPrimaryFileExtension()),
          _native(std::move(native)), _data(std::move(data)) {}
    bool CanRead(const std::string&) const override { return false; }
    bool Read(SdfLayer*, const std::string&, bool) const override { return false; }
protected:
    SdfLayer* _InstantiateNewLayer(const SdfFileFormatConstPtr&, const std::string& identifier,
        const std::string& realPath, const ArAssetInfo& info, const FileFormatArguments& args) const override
    {
        // SdfLayer::New holds its registry lock until _FinishInitialization.
        // _SetLayerData swaps (not diffs) while initialization is incomplete.
        // The resident layer retains the built-in format, never this factory.
        auto* layer = SdfFileFormat::_InstantiateNewLayer(_native, identifier, realPath, info, args);
        SdfAbstractDataRefPtr data = _data;
        _SetLayerData(layer, data);
        return layer;
    }
private:
    SdfFileFormatConstPtr _native;
    TfRefPtr<CountedData> _data;
};

size_t PreflightText(const Bytes& bytes)
{
    Check(!bytes.empty() && bytes.size() <= MaxFileBytes, "Source layer byte budget exceeded (16 MiB per file).");
    const std::string_view text(reinterpret_cast<const char*>(bytes.data()), bytes.size());
    Check(OpenUsdEdit::ValidUtf8(text), "Text-USD source must be strict UTF-8 without NUL; crate is not portable.");
    const auto start = text.find_first_not_of(" \t\r\n");
    Check(start != std::string_view::npos && text.substr(start, 9) == "#usda 1.0",
        "Portable review admits USDA/text-USD only; crate/custom/package formats require conversion.");
    size_t tokens = 0;
    std::vector<char> brackets;
    for (size_t i = 0; i < text.size();)
    {
        const char c = text[i];
        if (std::isspace(static_cast<unsigned char>(c))) { ++i; continue; }
        if (c == '#')
        {
            const auto end = text.find('\n', i);
            i = end == std::string_view::npos ? text.size() : end + 1;
            continue;
        }
        Check(++tokens <= MaxSourceTokens, "Source lexical token budget exceeded (262144 per file).");
        if (c == '"' || c == '\'' || c == '@')
        {
            const bool triple = i + 2 < text.size() && text[i + 1] == c && text[i + 2] == c;
            const size_t width = triple ? 3 : 1;
            i += width;
            const size_t begin = i;
            bool closed = false;
            while (i < text.size())
            {
                Check(i - begin <= OpenUsdEdit::MaxString, "Source string/asset token exceeds 4096 bytes.");
                if (c != '@' && text[i] == '\\') { i += std::min<size_t>(2, text.size() - i); continue; }
                if (text[i] == c && (!triple || (i + 2 < text.size() && text[i + 1] == c && text[i + 2] == c)))
                {
                    i += width; closed = true; break;
                }
                ++i;
            }
            Check(closed, "Unterminated source string/asset token.");
        }
        else if (c == '{' || c == '[' || c == '(')
        {
            Check(brackets.size() < 32, "Source lexical nesting budget exceeded (32).");
            brackets.push_back(c); ++i;
        }
        else if (c == '}' || c == ']' || c == ')')
        {
            Check(!brackets.empty() && ((c == '}' && brackets.back() == '{')
                || (c == ']' && brackets.back() == '[') || (c == ')' && brackets.back() == '(')),
                "Unbalanced source delimiters.");
            brackets.pop_back(); ++i;
        }
        else if (c == ',' || c == '=' || c == ':' || c == ';') { ++i; }
        else
        {
            const size_t begin = i++;
            while (i < text.size() && !std::isspace(static_cast<unsigned char>(text[i]))
                && std::string_view("{}[](),=:;\"'@#").find(text[i]) == std::string_view::npos)
            {
                Check(i - begin < OpenUsdEdit::MaxString, "Source lexical token exceeds 4096 bytes.");
                ++i;
            }
        }
    }
    Check(brackets.empty(), "Unclosed source delimiters.");
    return tokens;
}

class SourceVisitor final : public SdfAbstractDataSpecVisitor
{
public:
    TfRefPtr<CountedData> data = TfCreateRefPtr(new CountedData);
    bool VisitSpec(const SdfAbstractData& input, const SdfPath& path) override
    {
        Check(data->Inventory().size() < MaxSourceSpecs && path.GetPathElementCount() <= OpenUsdEdit::MaxDepth,
            "Source spec/path budget exceeded (65536 specs, 32 elements).");
        // The lexical token preflight bounds this native allocation before List.
        const auto fields = input.List(path);
        Check(fields.size() <= OpenUsdEdit::MaxFields, "Source field budget exceeded (128 per spec).");
        data->CreateSpec(path, input.GetSpecType(path));
        for (const auto& field : fields)
        {
            Check(field.GetString().size() <= OpenUsdEdit::MaxString, "Source field-name byte budget exceeded.");
            Check(field != TfToken("clips"), "Value clips/templates are not in the portable filesystem domain.");
            VtValue value;
            Check(input.Has(path, field, &value), "Parsed source field disappeared.");
            data->Set(path, field, value);
        }
        return true;
    }
    void Done(const SdfAbstractData&) override {}
};

void CheckLayerExtension(const std::string& path)
{
    std::string extension = std::filesystem::u8path(path).extension().u8string();
    std::transform(extension.begin(), extension.end(), extension.begin(), [](unsigned char c)
    {
        return static_cast<char>(std::tolower(c));
    });
    Check(extension == ".usda" || extension == ".usd",
        "Portable composition supports concrete filesystem USDA/text-USD layers only; convert crate/custom/package layers.");
}

}

std::string ResolvePath(const std::string& raw, const std::string& anchorFile)
{
    CheckFilesystemPath(raw);
    const auto candidate = std::filesystem::u8path(raw);
    const auto explicitPath = Path(candidate.is_absolute() ? raw
        : (std::filesystem::u8path(Parent(anchorFile)) / candidate).u8string(), true);
    const auto id = ArGetResolver().CreateIdentifier(raw, ArResolvedPath(anchorFile));
    const auto resolved = ArGetResolver().Resolve(id);
    if (resolved.empty() || Path(resolved.GetPathString(), true) != explicitPath
        || std::filesystem::absolute(std::filesystem::u8path(resolved.GetPathString())).lexically_normal().generic_u8string() != explicitPath)
    {
        throw Conflict("Asset resolution or a filesystem alias differs from its explicit SDK anchor; reconcile aliases/search paths before review persistence.");
    }
    return explicitPath;
}

namespace
{
void Dependencies(const TfRefPtr<CountedData>& data,
    const std::function<void(const std::string&, bool)>& visit)
{
    for (const auto& spec : data->Inventory())
    {
        Check(spec.second <= OpenUsdEdit::MaxFields, "Dependency field inventory exceeds its budget.");
        for (const auto& field : data->List(spec.first))
        {
            const auto value = data->Get(spec.first, field);
            if (field == SdfFieldKeys->SubLayers)
            {
                Check(value.IsHolding<std::vector<std::string>>(), "Invalid sublayer field storage.");
                const auto& paths = value.UncheckedGet<std::vector<std::string>>();
                Check(paths.size() <= OpenUsdEdit::MaxItems, "Sublayer count budget exceeded.");
                for (const auto& path : paths) { visit(path, true); }
            }
            else { VisitAssets(value, visit); }
        }
    }
}
}

void DefaultResolver()
{
    Check(typeid(ArGetUnderlyingResolver()) == typeid(ArDefaultResolver),
        "Portable review requires the default filesystem resolver; custom/URI resolvers are not supported.");
}
void CheckFilesystemPath(const std::string& text)
{
    Check(!text.empty() && text.size() <= OpenUsdEdit::MaxString && OpenUsdEdit::ValidUtf8(text),
        "A nonempty filesystem path of at most 4096 UTF-8 bytes is required.");
    Check(text.find_first_of("[]<>`*?#") == std::string::npos
        && text.find("${") == std::string::npos && text.find("://") == std::string::npos
        && text.find(":SDF_FORMAT_ARGS:") == std::string::npos,
        "URI/package/template/expression/file-format-argument paths are not portable; use concrete filesystem assets.");
    const auto colon = text.find(':');
#if defined(_WIN32)
    Check(colon == std::string::npos || (colon == 1
        && std::isalpha(static_cast<unsigned char>(text[0])) && text.find(':', 2) == std::string::npos),
        "URI and alternate-stream paths are not portable.");
#else
    Check(colon == std::string::npos, "URI paths are not portable.");
#endif
}
std::string Path(const std::string& text, bool existing)
{
    Failpoint(5);
    CheckFilesystemPath(text);
    const auto input = std::filesystem::u8path(text);
    auto path = existing ? std::filesystem::canonical(input)
        : std::filesystem::weakly_canonical(std::filesystem::absolute(input));
    Check(path.is_absolute(), "Portable paths must resolve to absolute filesystem paths.");
    const auto result = path.generic_u8string();
    Check(result.size() <= OpenUsdEdit::MaxString, "Canonical filesystem path exceeds 4096 UTF-8 bytes.");
    return result;
}
std::string Parent(const std::string& path) { return std::filesystem::u8path(path).parent_path().generic_u8string(); }
Bytes ReadFile(const std::string& path, size_t& total)
{
    FileReadScope::Retain(path);
    Check(std::filesystem::is_regular_file(std::filesystem::u8path(path)), "Review dependency is not a regular filesystem file.");
    std::ifstream stream(std::filesystem::u8path(path), std::ios::binary | std::ios::ate);
    Check(stream.good(), "Cannot read review source/dependency; reconcile permissions or restore the file.");
    const auto end = stream.tellg();
    Check(end >= 0 && static_cast<uint64_t>(end) <= MaxFileBytes,
        "Source/dependency file exceeds the 16 MiB admission limit.");
    const auto count = static_cast<size_t>(end);
    Check(total <= MaxSourceBytes && count <= MaxSourceBytes - total,
        "Source/dependency aggregate read exceeds 64 MiB.");
    total += count;
    Bytes bytes(count);
    stream.seekg(0);
    if (count) { stream.read(reinterpret_cast<char*>(bytes.data()), static_cast<std::streamsize>(count)); }
    if (!stream.good() || stream.peek() != std::char_traits<char>::eof())
    {
        throw Conflict("Source/dependency changed during its byte read; retry after reconciling external writes.");
    }
    return bytes;
}

TfRefPtr<CountedData> ParseText(const Bytes& bytes)
{
    PreflightText(bytes);
    const auto layer = SdfLayer::CreateAnonymous("admitted-text.usda");
    Check(static_cast<bool>(layer), "Could not create private text admission layer.");
    OpenUsdEdit::InitializeReviewData(layer);
    TfErrorMark mark;
    const std::string text(reinterpret_cast<const char*>(bytes.data()), bytes.size());
    Check(layer->ImportFromString(text) && mark.IsClean(), "Could not parse admitted USDA byte image.");
    // ImportFromString changes CountedData to SdfUsdaData and therefore adopts
    // parser data without a numeric/fallback equality pass.
    const auto resident = OpenUsdEdit::DataAccess::Get(*layer);
    Check(typeid(*resident) == typeid(SdfUsdaData), "Text parsing did not produce the pinned resident USDA data implementation.");
    SourceVisitor visitor;
    resident->VisitSpecs(&visitor);
    return visitor.data;
}
SdfLayerRefPtr SeedLayer(const std::string& identifier, const TfRefPtr<CountedData>& data, bool anonymous)
{
    std::string extension = std::filesystem::u8path(identifier).extension().u8string();
    std::transform(extension.begin(), extension.end(), extension.begin(), [](unsigned char c)
    {
        return static_cast<char>(std::tolower(c));
    });
    const auto native = SdfFileFormat::FindById(TfToken(extension == ".usd" ? "usd" : "usda"));
    Check(static_cast<bool>(native), "The built-in USDA format is unavailable.");
    const auto seed = TfCreateRefPtr(new SeedFormat(native, data));
    const auto layer = anonymous ? SdfLayer::CreateAnonymous(identifier, seed) : SdfLayer::New(seed, identifier);
    Check(layer && OpenUsdEdit::DataAccess::Get(*layer) == data && layer->GetFileFormat() == native,
        "Could not construct a verified resident layer from admitted bytes without replacing an existing cache entry.");
    return layer;
}

Origin::Origin(const SdfLayerRefPtr& value, TfRefPtr<CountedData> content, File image,
    uint64_t admittedVersion, std::shared_ptr<const std::vector<File>> admittedFiles)
    : layer(value), data(std::move(content)), file(std::move(image)), version(admittedVersion), proof(std::move(admittedFiles)),
      changeKey(TfNotice::Register(TfCreateWeakPtr(this), &Origin::Changed, layer)),
      replaceKey(TfNotice::Register(TfCreateWeakPtr(this), &Origin::Replaced, layer)),
      renameKey(TfNotice::Register(TfCreateWeakPtr(this), &Origin::Renamed, layer)) {}
Origin::~Origin()
{
    TfNotice::RevokeAndWait(changeKey); TfNotice::RevokeAndWait(replaceKey); TfNotice::RevokeAndWait(renameKey);
}
void Origin::Changed(const SdfNotice::LayersDidChangeSentPerLayer&) { changed.store(true); }
void Origin::Replaced(const SdfNotice::LayerDidReplaceContent&) { changed.store(true); }
void Origin::Renamed(const SdfNotice::LayerIdentifierDidChange&) { changed.store(true); }
void Origin::Verify() const
{
    if (!layer || changed.load() || data->Version() != version || layer->IsMuted()
        || OpenUsdEdit::DataAccess::Get(*layer) != data
        || layer->GetIdentifier() != file.path || Path(layer->GetRealPath(), false) != file.path)
    {
        throw Conflict("Verified resident source/dependency was changed, reloaded, renamed or replaced; reconcile and reopen without mutating shared source layers.");
    }
}
void VerifyFiles(const std::vector<File>& files)
{
    size_t total = 0;
    for (const auto& file : files)
    {
        if (Path(file.path, true) != file.path) { throw Conflict("A source/dependency filesystem anchor changed; reconcile before saving/importing."); }
        const auto bytes = ReadFile(file.path, total);
        if (bytes.size() != file.length || Hash(bytes) != file.hash)
        {
            throw Conflict("Source/dependency byte fingerprint changed; reconcile external edits and reopen for review.");
        }
    }
}

Admission Admit(const std::string& root)
{
    DefaultResolver();
    std::lock_guard<std::recursive_mutex> lock(originMutex);
    for (auto it = origins.begin(); it != origins.end();)
    {
        if (!it->second->layer) { it = origins.erase(it); }
        else { ++it; }
    }
    Admission result;
    result.source.root = Path(root, true);
    if (std::filesystem::absolute(std::filesystem::u8path(root)).lexically_normal().generic_u8string() != result.source.root)
    {
        throw Conflict("A source filesystem alias changes the SDK anchor; reconcile alias policy before OpenForReview. No shared source layer was changed.");
    }
    result.source.anchor = Parent(result.source.root);
    result.resolver = ArGetResolver().CreateDefaultContextForAsset(result.source.root);
    ArResolverContextBinder binder(result.resolver);
    struct Pending
    {
        File file;
        Bytes bytes;
        TfRefPtr<CountedData> data;
    };
    std::map<std::string, Pending> files;
    std::map<std::string, std::set<const File*>> edges;
    std::vector<std::string> queue;
    size_t total = 0;
    size_t tokens = 0;
    size_t edgeCount = 0;
    const auto add = [&](const std::string& path, bool layer)
    {
        const auto found = files.find(path);
        if (found != files.end())
        {
            if (layer && found->second.file.kind == 1)
            {
                found->second.file.kind = 0; queue.push_back(path);
            }
            return;
        }
        Check(files.size() < MaxFiles, "Source dependency manifest exceeds 1024 files.");
        Pending pending;
        pending.file.path = path; pending.file.kind = layer ? 0u : 1u;
        pending.bytes = ReadFile(path, total);
        pending.file.length = pending.bytes.size(); pending.file.hash = Hash(pending.bytes);
        files.emplace(path, std::move(pending));
        if (layer) { queue.push_back(path); }
    };
    add(result.source.root, true);
    for (size_t i = 0; i < queue.size(); ++i)
    {
        const std::string name = queue[i];
        auto& file = files.at(name);
        CheckLayerExtension(name);
        const auto cached = SdfLayer::Find(name);
        const auto known = origins.find(name);
        if (cached && (known == origins.end() || known->second->layer != cached))
        {
            throw Conflict("Unverified cached source/dependency: close/reconcile its owners or use an isolated process. Before adding a new composition arc, OpenForReview its dependency and retain that stage until attachment. Shared layers are never reloaded or replaced.");
        }
        if (cached)
        {
            known->second->Verify();
            if (!(known->second->file == file.file))
            {
                throw Conflict("Cached verified source bytes no longer match the filesystem; reconcile and close its owners before reopening.");
            }
            file.data = known->second->data;
        }
        else
        {
            tokens += PreflightText(file.bytes);
            Check(tokens <= OPENUSD_REVIEW_MAX_SOURCE_TOTAL_TOKENS, "Aggregate source lexical budget exceeded (1048576 tokens).");
            file.data = ParseText(file.bytes);
        }
        Dependencies(file.data, [&](const std::string& dependency, bool isLayer)
        {
            if (!dependency.empty())
            {
                const auto path = ResolvePath(dependency, name);
                add(path, isLayer);
                if (edges[name].insert(&files.at(path).file).second)
                {
                    Check(++edgeCount <= OPENUSD_REVIEW_MAX_SOURCE_EDGES, "Source composition/asset edge budget exceeded (65536).");
                }
            }
        });
    }
    for (const auto& entry : files) { result.source.files.push_back(entry.second.file); }
    result.source.fingerprint = files.at(result.source.root).file.hash;
    for (const auto& entry : files)
    {
        const auto known = origins.find(entry.first);
        if (entry.second.file.kind != 0 || known == origins.end() || !known->second->layer) { continue; }
        std::vector<const File*> pending{&entry.second.file};
        std::set<const File*> visited{&entry.second.file};
        const auto& admitted = *known->second->proof;
        while (!pending.empty())
        {
            const auto& current = *pending.back(); pending.pop_back();
            const auto expected = std::lower_bound(admitted.begin(), admitted.end(), current.path,
                [](const File& value, const std::string& name) { return value.path < name; });
            if (expected == admitted.end() || expected->path != current.path
                || expected->hash != current.hash || expected->length != current.length)
            {
                throw Conflict("A cached source's originally admitted dependency bytes or resolved anchor changed; reconcile and close its owners before reopening. Capture cannot retrospectively rebind dependency provenance.");
            }
            const auto dependencies = edges.find(current.path);
            if (dependencies != edges.end())
            {
                for (const auto* dependency : dependencies->second)
                {
                    if (visited.insert(dependency).second) { pending.push_back(dependency); }
                }
            }
        }
    }
    std::map<std::string, TfRefPtr<CountedData>> composition;
    for (const auto& entry : files)
    {
        if (entry.second.data) { composition.emplace(entry.first, entry.second.data); }
    }
    ValidateComposition(composition, {result.source.root});
    VerifyFiles(result.source.files);
    const auto proof = std::make_shared<const std::vector<File>>(result.source.files);
    for (const auto& entry : files)
    {
        if (entry.second.file.kind != 0) { continue; }
        const auto& name = entry.first;
        auto cached = SdfLayer::Find(name);
        std::shared_ptr<Origin> origin;
        if (cached)
        {
            const auto known = origins.find(name);
            if (known == origins.end() || known->second->layer != cached)
            {
                throw Conflict("A competing normal open cached an unverified dependency; close/reconcile it before retrying.");
            }
            origin = known->second; origin->Verify();
        }
        else
        {
            Check(origins.size() < MaxFiles, "Resident verified-origin registry exceeds 1024 files; close unused review stages.");
            const auto admittedVersion = entry.second.data->Version();
            auto layer = SeedLayer(name, entry.second.data);
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
            if (mutatePublishedSource)
            {
                mutatePublishedSource = false;
                layer->SetField(SdfPath("/World.weight"), SdfFieldKeys->Default, VtValue(99.0));
            }
#endif
            origin = std::make_shared<Origin>(layer, entry.second.data, entry.second.file, admittedVersion, proof);
            origin->Verify();
            origins[name] = origin;
            result.layers.push_back(layer);
            cached = layer;
        }

        if (std::find(result.layers.begin(), result.layers.end(), cached) == result.layers.end())
        {
            result.layers.push_back(TfCreateRefPtrFromProtectedWeakPtr(cached));
        }
        result.origins.push_back(origin);
    }
    ValidateSource(result.source);
    return result;
}
void VerifySource(const openusd_stage* stage)
{
    DefaultResolver();
    const auto& context = EditContext(stage);
    Check(context.review != nullptr, "Unverified stage: use OpenForReview before any review edits; legacy cached stages require reconciliation.");
    const auto& state = *context.review;
    if (stage->value->GetRootLayer()->GetIdentifier() != state.source.root
        || stage->value->GetSessionLayer() != state.session || stage->value->GetPathResolverContext() != state.resolver)
    {
        throw Conflict("The verified source root, session or resolver context was replaced; reconcile before persistence.");
    }
    if (!stage->value->GetMutedLayers().empty()
        || stage->value->GetLoadRules() != UsdStageLoadRules::LoadAll()
        || stage->value->GetPopulationMask() != UsdStagePopulationMask::All())
    {
        throw Conflict("The verified source composition was muted, unloaded or masked; reconcile its ambient stage configuration before persistence.");
    }
    for (const auto& origin : state.origins) { origin->Verify(); }
    VerifyFiles(state.source.files);
}

#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
extern "C" OPENUSD_DOTNET_API void openusd_review_test_mutate_published_source()
{
    mutatePublishedSource = true;
}
#endif

SdfLayerRefPtr NewReviewLayer(const openusd_stage* stage, TfRefPtr<CountedData> data)
{
    auto& state = *EditContext(stage).review;
    VerifySource(stage);
    const auto path = (std::filesystem::u8path(state.source.anchor) / (".openusd-review-" + Guid() + ".usda")).generic_u8string();
    Check(!std::filesystem::exists(std::filesystem::u8path(path)), "Unique review anchor path unexpectedly already exists.");
    if (!data)
    {
        data = TfCreateRefPtr(new CountedData);
        data->CreateSpec(SdfPath::AbsoluteRootPath(), SdfSpecTypePseudoRoot);
    }
    auto layer = SeedLayer(path, data);
    Check(!layer->GetResolvedPath().empty() && Parent(Path(layer->GetRealPath(), false)) == state.source.anchor,
        "The new review layer has no verified real filesystem anchor.");
    layer->SetPermissionToSave(false);
    return layer;
}
void UserLayerAttached(const openusd_stage* stage)
{
    auto& context = EditContext(stage);
    auto& state = *context.review;
    state.sessionVersion = state.sessionData->Version();
    const auto data = OpenUsdEdit::DataAccess::Get(*context.user);
    Check(typeid(*data) == typeid(CountedData), "New review must use owned counted data.");
    state.userVersion = static_cast<const CountedData*>(get_pointer(data))->Version();
}
TfRefPtr<CountedData> ReviewData(const openusd_layer* layer)
{
    auto& record = OpenUsdEdit::Record(layer);
    Check(record.local && record.role == 2 && record.layer == EditContext(layer->stage).user,
        "Portable persistence requires the current owned local review layer, never source or physics.");
    const auto data = OpenUsdEdit::Resident(record.layer);
    Check(typeid(*data) == typeid(CountedData),
        "Portable review inventory was replaced by unverified data; reconcile the review target.");
    return TfCreateRefPtrFromProtectedWeakPtr(
        TfWeakPtr<CountedData>(const_cast<CountedData*>(static_cast<const CountedData*>(get_pointer(data)))));
}
std::vector<File> ReviewFiles(const openusd_stage* stage, const SdfLayerRefPtr& review, const TfRefPtr<CountedData>& data)
{
    auto& state = *EditContext(stage).review;
    ArResolverContextBinder binder(state.resolver);
    Check(!review->GetResolvedPath().empty() && Parent(Path(review->GetRealPath(), false)) == state.source.anchor,
        "Review anchor changed or is unverified; saving elsewhere must not reanchor raw asset paths.");
    std::map<std::string, File> files;
    std::set<std::string> checkedLayers;
    size_t total = 0;
    size_t retainedBytes = 0;
    for (const auto& origin : state.origins) { retainedBytes += static_cast<size_t>(origin->file.length); }
    for (const auto& file : state.source.files) { files.emplace(file.path, file); total += static_cast<size_t>(file.length); }
    Dependencies(data, [&](const std::string& raw, bool layer)
    {
        if (raw.empty()) { return; }
        const auto explicitPath = ResolvePath(raw, review->GetRealPath());
        const auto actual = ArGetResolver().Resolve(SdfComputeAssetPathRelativeToLayer(review, raw));
        if (actual.empty() || Path(actual.GetPathString(), true) != explicitPath)
        {
            throw Conflict("Current review asset resolution disagrees with the explicit filesystem anchor.");
        }
        if (layer)
        {
            if (!checkedLayers.insert(explicitPath).second) { return; }
            const auto additional = Admit(explicitPath);
            for (const auto& file : additional.source.files)
            {
                const auto [it, inserted] = files.emplace(file.path, file);
                if (!inserted)
                {
                    Check(it->second.hash == file.hash && it->second.length == file.length,
                        "Review dependency byte images disagree.");
                    if (file.kind == 0) { it->second.kind = 0; }
                }
                else { total += static_cast<size_t>(file.length); }
            }
            for (size_t i = 0; i < additional.origins.size(); ++i)
            {
                const auto& origin = additional.origins[i];
                if (std::find(state.origins.begin(), state.origins.end(), origin) == state.origins.end())
                {
                    Check(state.origins.size() < MaxFiles, "Review resident dependency count exceeds 1024.");
                    Check(retainedBytes <= MaxSourceBytes && origin->file.length <= MaxSourceBytes - retainedBytes,
                        "Retained verified layer images exceed 64 MiB; close/reconcile unused dependency history before continuing.");
                    retainedBytes += static_cast<size_t>(origin->file.length);
                    state.origins.push_back(origin);
                    state.sourceLayers.push_back(TfCreateRefPtrFromProtectedWeakPtr(origin->layer));
                }
            }
        }
        else if (files.find(explicitPath) == files.end())
        {
            const auto bytes = ReadFile(explicitPath, total);
            files.emplace(explicitPath, File{explicitPath, Hash(bytes), bytes.size(), 1});
        }
        Check(files.size() <= MaxFiles && total <= MaxSourceBytes, "Review dependency manifest exceeds 1024 files or 64 MiB.");
    });
    std::vector<File> result;
    for (const auto& file : files) { result.push_back(file.second); }
    std::map<std::string, TfRefPtr<CountedData>> composition;
    for (const auto& origin : state.origins) { composition.emplace(origin->file.path, origin->data); }
    composition.emplace(review->GetIdentifier(), data);
    ValidateComposition(composition, {state.source.root, review->GetIdentifier()});
    VerifyFiles(result);
    return result;
}
bool Pristine(const openusd_stage* stage)
{
    auto& context = EditContext(stage);
    auto& state = *context.review;
    if (state.imported || context.physics || OpenUsdEdit::DataAccess::Get(*state.session) != state.sessionData
        || state.sessionData->Version() != state.sessionVersion || state.sessionData->Inventory().size() != 1)
    {
        return false;
    }
    if (!context.user) { return state.sessionData->FieldCount(SdfPath::AbsoluteRootPath()) == 0; }
    const auto root = SdfPath::AbsoluteRootPath();
    for (const auto& field : state.sessionData->List(root))
    {
        if (field != SdfFieldKeys->SubLayers && field != SdfFieldKeys->SubLayerOffsets) { return false; }
    }
    const auto paths = state.sessionData->Get(root, SdfFieldKeys->SubLayers);
    if (!paths.IsHolding<std::vector<std::string>>()
        || paths.UncheckedGet<std::vector<std::string>>() != std::vector<std::string>{context.user->GetIdentifier()})
    {
        return false;
    }
    const auto offsets = state.sessionData->Get(root, SdfFieldKeys->SubLayerOffsets);
    if (!offsets.IsEmpty())
    {
        if (!offsets.IsHolding<std::vector<SdfLayerOffset>>()) { return false; }
        const auto& values = offsets.UncheckedGet<std::vector<SdfLayerOffset>>();
        if (values.size() != 1 || values[0].GetOffset() != 0 || values[0].GetScale() != 1) { return false; }
    }
    const auto user = OpenUsdEdit::DataAccess::Get(*context.user);
    if (!user || typeid(*user) != typeid(CountedData)) { return false; }
    const auto* data = static_cast<const CountedData*>(get_pointer(user));
    return data->Version() == state.userVersion && data->Inventory().size() == 1
        && data->FieldCount(SdfPath::AbsoluteRootPath()) == 0 && stage->value->HasLocalLayer(context.user);
}
}
