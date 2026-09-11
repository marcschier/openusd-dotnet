// Copyright (c) marcschier. Licensed under the MIT License.

#include "../src/meshVertexLayout.h"

#include <iostream>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
void Require(bool value, const char* message)
{
    if (!value)
    {
        throw std::runtime_error(message);
    }
}

HdSilkMeshAttribute Attribute(uint32_t components, std::vector<float> values, bool constant = false)
{
    HdSilkMeshAttribute attribute;
    attribute.name = "probe";
    attribute.componentCount = components;
    attribute.interpolation = constant
        ? OPENUSD_SILK_INTERPOLATION_CONSTANT : OPENUSD_SILK_INTERPOLATION_VERTEX;
    attribute.data = std::move(values);
    return attribute;
}

void SharedCornersPreserveExactValues()
{
    const VtArray<uint32_t> indices{0, 1, 2, 0, 2, 3};
    HdSilkMeshAttributes attributes{
        Attribute(2, {0, 0, 1, 0, 1, 1, 0, 0, 1, 1, 0, 1}),
        Attribute(1, {7}, true)};
    const HdSilkMeshVertexLayout layout = HdSilkBuildSharedVertexLayout(indices, 4, attributes);
    Require(layout.pointIndices == VtArray<uint32_t>({0, 1, 2, 3}), "shared point origins changed");
    Require(layout.triangleIndices == indices, "triangle order changed");
    Require(attributes[0].data == std::vector<float>({0, 0, 1, 0, 1, 1, 0, 1}), "UV values changed");
    Require(attributes[1].data == std::vector<float>({7}), "constant value changed");
}

void EveryAttributeAndOriginalPointParticipate()
{
    const VtArray<uint32_t> indices{0, 1, 2, 0, 2, 3};
    HdSilkMeshAttributes attributes{
        Attribute(2, {0, 0, 1, 0, 1, 1, 0, 0, 1, 1, 0, 1}),
        Attribute(3, {0, 0, 1, 0, 0, 1, 0, 0, 1, 1, 0, 0, 0, 0, 1, 0, 0, 1})};
    const HdSilkMeshVertexLayout layout = HdSilkBuildSharedVertexLayout(indices, 4, attributes);
    Require(layout.pointIndices == VtArray<uint32_t>({0, 1, 2, 0, 3}), "normal seam was merged");
    Require(layout.triangleIndices == VtArray<uint32_t>({0, 1, 2, 3, 2, 4}), "seam indices changed");
    Require(attributes[1].data[9] == 1 && attributes[1].data[11] == 0, "split normal changed");
    HdSilkMeshAttributes identical{Attribute(1, {1, 1, 1, 1, 1, 1})};
    const HdSilkMeshVertexLayout distinct = HdSilkBuildSharedVertexLayout({0, 1, 2, 3, 4, 5}, 6, identical);
    Require(distinct.pointIndices.size() == 6, "different authored points were merged");
}

void SignedZeroAndCloseValuesRemainDistinct()
{
    const VtArray<uint32_t> indices{0, 1, 2, 0, 2, 3};
    HdSilkMeshAttributes attributes{Attribute(1, {0.0F, 1, 2, -0.0F, 2, 3})};
    const HdSilkMeshVertexLayout layout = HdSilkBuildSharedVertexLayout(indices, 4, attributes);
    Require(layout.pointIndices.size() == 5, "signed-zero distinction was lost");
    uint32_t bits = 0;
    std::memcpy(&bits, &attributes[0].data[3], sizeof(bits));
    Require(bits == 0x80000000u, "negative zero was not retained bit-for-bit");
    attributes = {Attribute(1, {1, 2, 3, 1.00000011920928955078125F, 3, 4})};
    Require(HdSilkBuildSharedVertexLayout(indices, 4, attributes).pointIndices.size() == 5,
        "nearby but unequal values were merged");
}

void InvalidInputDoesNotMutateAttributes()
{
    for (bool invalidPoint : {false, true})
    {
        HdSilkMeshAttributes attributes{
            Attribute(1, invalidPoint ? std::vector<float>{10, 20, 30} : std::vector<float>{10, 20})};
        const std::vector<float> original = attributes[0].data;
        bool refused = false;
        try
        {
            (void)HdSilkBuildSharedVertexLayout({0, 1, 2}, invalidPoint ? 2 : 3, attributes);
        }
        catch (const std::invalid_argument&)
        {
            refused = true;
        }
        Require(refused && attributes[0].data == original, "invalid input was accepted or changed attributes");
    }
}

void EmptyAndRepeatedLayoutsAreDeterministic()
{
    HdSilkMeshAttributes empty;
    const HdSilkMeshVertexLayout none = HdSilkBuildSharedVertexLayout({}, 0, empty);
    Require(none.pointIndices.empty() && none.triangleIndices.empty(), "empty layout gained vertices");
    HdSilkMeshAttributes first{Attribute(1, {0, 1, 2, 0, 2, 3})};
    HdSilkMeshAttributes second = first;
    const auto one = HdSilkBuildSharedVertexLayout({0, 1, 2, 0, 2, 3}, 4, first);
    const auto two = HdSilkBuildSharedVertexLayout({0, 1, 2, 0, 2, 3}, 4, second);
    Require(one.pointIndices == two.pointIndices && one.triangleIndices == two.triangleIndices &&
        first[0].data == second[0].data, "repeated layout changed order or values");
}

void RecordCopiesShareAttributeStorage()
{
    HdSilkMeshRecord source;
    source.attributes.push_back(Attribute(2, {0, 0, 1, 0, 1, 1, 0, 1}));
    source.attributes.push_back(Attribute(1, {7}, true));
    const HdSilkMeshRecord& authored = source;
    const HdSilkMeshRecord published = source;
    const HdSilkMeshRecord copied = published;
    Require(authored.attributes.data() == published.attributes.data() &&
        published.attributes.data() == copied.attributes.data(),
        "RecordCopiesShareAttributeStorage: publishing copied the attribute allocation");
    Require(authored.attributes[0].data.data() == copied.attributes[0].data.data() &&
        copied.attributes[0].data == std::vector<float>({0, 0, 1, 0, 1, 1, 0, 1}) &&
        copied.attributes[1].data == std::vector<float>({7}),
        "RecordCopiesShareAttributeStorage: nested arrays were copied or changed");
    const HdSilkMeshRecord reference = HdSilkMakeInstanceReference(source);
    Require(reference.attributes.empty() && reference.attributes.capacity() == 0,
        "RecordCopiesShareAttributeStorage: an instance reference retained prototype attributes");
}

void AttributeWritesDetachWithoutChangingPublishedRecords()
{
    HdSilkMeshRecord source;
    source.attributes.push_back(Attribute(1, {10, 20, 30}));
    source.attributes.push_back(Attribute(1, {7}, true));
    const HdSilkMeshRecord published = source;
    HdSilkMeshRecord edited = source;
    edited.attributes[0].data[1] = 99;
    edited.attributes[1].name = "edited";
    const HdSilkMeshRecord& writer = edited;
    Require(published.attributes[0].data == std::vector<float>({10, 20, 30}) &&
        published.attributes[1].name == "probe" &&
        writer.attributes[0].data == std::vector<float>({10, 99, 30}) &&
        writer.attributes[1].name == "edited" &&
        writer.attributes.data() != published.attributes.data(),
        "AttributeWritesDetachWithoutChangingPublishedRecords: a writer changed another record");
    source.attributes.clear();
    source.attributes.push_back(Attribute(1, {-1}));
    const HdSilkMeshRecord& refreshed = source;
    Require(refreshed.attributes.size() == 1 &&
        refreshed.attributes[0].data == std::vector<float>({-1}) &&
        published.attributes[0].data == std::vector<float>({10, 20, 30}) &&
        published.attributes[1].data == std::vector<float>({7}) &&
        writer.attributes[0].data == std::vector<float>({10, 99, 30}),
        "AttributeWritesDetachWithoutChangingPublishedRecords: a refresh invalidated published storage");
}

void CompactionPreservesAnEarlierAttributeSnapshot()
{
    HdSilkMeshRecord source;
    source.attributes.push_back(Attribute(2, {0, 0, 1, 0, 1, 1, 0, 0, 1, 1, 0, 1}));
    const HdSilkMeshRecord published = source;
    const HdSilkMeshVertexLayout layout =
        HdSilkBuildSharedVertexLayout({0, 1, 2, 0, 2, 3}, 4, source.attributes);
    const HdSilkMeshRecord& compacted = source;
    Require(layout.pointIndices == VtArray<uint32_t>({0, 1, 2, 3}) &&
        compacted.attributes[0].data == std::vector<float>({0, 0, 1, 0, 1, 1, 0, 1}) &&
        published.attributes[0].data == std::vector<float>({0, 0, 1, 0, 1, 1, 0, 0, 1, 1, 0, 1}),
        "CompactionPreservesAnEarlierAttributeSnapshot: compaction modified its published predecessor");
}

void RecordCopiesShareIndexAndIdentityStorage()
{
    HdSilkMeshRecord source;
    source.indices = {2, 0, 1, 2, 1, 3};
    source.triangleSubprims = {7, 9};
    source.pointOrigins = {4, 2, OPENUSD_SILK_SUBPRIM_NONE, 8};
    source.cornerEdges = {5, 6, OPENUSD_SILK_SUBPRIM_NONE, 10, 11, OPENUSD_SILK_SUBPRIM_NONE};
    const HdSilkMeshRecord& authored = source;
    const HdSilkMeshRecord published = source;
    const HdSilkMeshRecord copied = published;
    for (auto member : {&HdSilkMeshRecord::indices, &HdSilkMeshRecord::triangleSubprims,
             &HdSilkMeshRecord::pointOrigins, &HdSilkMeshRecord::cornerEdges})
    {
        Require((authored.*member).data() == (published.*member).data() &&
            (published.*member).data() == (copied.*member).data() &&
            (authored.*member) == (copied.*member),
            "RecordCopiesShareIndexAndIdentityStorage: publishing duplicated a topology/identity buffer");
    }
    const HdSilkMeshRecord reference = HdSilkMakeInstanceReference(source);
    Require(reference.indices.empty() && reference.indices.capacity() == 0 &&
        reference.triangleSubprims.empty() && reference.triangleSubprims.capacity() == 0 &&
        reference.pointOrigins.empty() && reference.pointOrigins.capacity() == 0 &&
        reference.cornerEdges.empty() && reference.cornerEdges.capacity() == 0,
        "RecordCopiesShareIndexAndIdentityStorage: an instance retained prototype topology/identity storage");
}

void IndexAndIdentityWritesDetachOnlyTheChangedBuffer()
{
    HdSilkMeshRecord source;
    source.indices = {2, 0, 1};
    source.triangleSubprims = {7};
    source.pointOrigins = {4, 2, OPENUSD_SILK_SUBPRIM_NONE};
    source.cornerEdges = {5, 6, OPENUSD_SILK_SUBPRIM_NONE};
    const HdSilkMeshRecord published = source;
    const auto members = {&HdSilkMeshRecord::indices, &HdSilkMeshRecord::triangleSubprims,
        &HdSilkMeshRecord::pointOrigins, &HdSilkMeshRecord::cornerEdges};
    for (auto changed : members)
    {
        HdSilkMeshRecord edited = source;
        const uint32_t original = (published.*changed)[0];
        (edited.*changed)[0] = 99;
        const HdSilkMeshRecord& writer = edited;
        Require((writer.*changed)[0] == 99 && (published.*changed)[0] == original &&
            (writer.*changed).data() != (published.*changed).data(),
            "IndexAndIdentityWritesDetachOnlyTheChangedBuffer: a write changed the earlier snapshot");
        for (auto unchanged : members)
        {
            if (unchanged != changed)
            {
                Require((writer.*unchanged).data() == (published.*unchanged).data(),
                    "IndexAndIdentityWritesDetachOnlyTheChangedBuffer: a write copied unrelated storage");
            }
        }
    }
}

void IdentityRetirementReleasesOnlyTheRetiredOwner()
{
    HdSilkMeshRecord source;
    source.indices = {0, 1, 2};
    source.triangleSubprims = {7};
    source.pointOrigins = {4, 2, OPENUSD_SILK_SUBPRIM_NONE};
    source.cornerEdges = {5, 6, OPENUSD_SILK_SUBPRIM_NONE};
    source.subprimIdentity = OPENUSD_SILK_SUBPRIM_IDENTITY_FACE |
        OPENUSD_SILK_SUBPRIM_IDENTITY_POINT | OPENUSD_SILK_SUBPRIM_IDENTITY_EDGE;
    source.authoredPointCount = 5;
    source.authoredEdgeCount = 7;
    const HdSilkMeshRecord published = source;
    for (bool reject : {false, true})
    {
        HdSilkMeshRecord retired = source;
        if (reject)
        {
            retired.RejectSubprimIdentity(OPENUSD_SILK_SUBPRIM_UNSUPPORTED_BUDGET);
        }
        else
        {
            retired.ClearSubprimIdentity();
        }
        const HdSilkMeshRecord& view = retired;
        Require(view.pointOrigins.empty() && view.pointOrigins.capacity() == 0 &&
            view.cornerEdges.empty() && view.cornerEdges.capacity() == 0 &&
            view.authoredPointCount == 0 && view.authoredEdgeCount == 0 &&
            published.pointOrigins[0] == 4 && published.pointOrigins[2] == OPENUSD_SILK_SUBPRIM_NONE &&
            published.cornerEdges[0] == 5 && published.cornerEdges[2] == OPENUSD_SILK_SUBPRIM_NONE &&
            published.authoredPointCount == 5 && published.authoredEdgeCount == 7,
            "IdentityRetirementReleasesOnlyTheRetiredOwner: retirement changed published identity");
        Require(view.indices.data() == published.indices.data() &&
            view.triangleSubprims.data() == published.triangleSubprims.data() &&
            view.subprimIdentity == (reject ? OPENUSD_SILK_SUBPRIM_IDENTITY_FACE : 0u) &&
            view.subprimUnsupported == (reject ? OPENUSD_SILK_SUBPRIM_UNSUPPORTED_BUDGET : 0u),
            "IdentityRetirementReleasesOnlyTheRetiredOwner: retirement changed geometry or refusal semantics");
    }
}
}

int main()
{
    try
    {
        SharedCornersPreserveExactValues();
        EveryAttributeAndOriginalPointParticipate();
        SignedZeroAndCloseValuesRemainDistinct();
        InvalidInputDoesNotMutateAttributes();
        EmptyAndRepeatedLayoutsAreDeterministic();
        RecordCopiesShareAttributeStorage();
        AttributeWritesDetachWithoutChangingPublishedRecords();
        CompactionPreservesAnEarlierAttributeSnapshot();
        RecordCopiesShareIndexAndIdentityStorage();
        IndexAndIdentityWritesDetachOnlyTheChangedBuffer();
        IdentityRetirementReleasesOnlyTheRetiredOwner();
        std::cout << "hdsilk_vertex_layout_probe: 11 contracts passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "hdsilk_vertex_layout_probe: " << error.what() << '\n';
        return 1;
    }
}
