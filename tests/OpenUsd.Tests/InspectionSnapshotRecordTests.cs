// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Tests;

/// <summary>
/// Covers the detached record types returned by the Pcp, Ts and
/// UsdValidation inspection APIs.
/// </summary>
/// <remarks>
/// These three APIs deliberately return **detached snapshots** so a UI can
/// pull one result across the ABI and then walk it entirely in managed code,
/// rather than making a P/Invoke per tree node. That design choice makes value
/// semantics part of the contract, not an incidental detail of using records:
/// a consumer diffing two snapshots to decide whether to repaint relies on
/// equality comparing by value, and on two structurally identical snapshots
/// being equal even though they are different instances.
///
/// The behavioural coverage for these APIs lives in
/// <c>tests/OpenUsd.NativeProbe/PcpTsValidationProbe.cs</c>, because
/// evaluating a spline or validating a stage needs the native runtime, and
/// this suite is deliberately native-independent. What is asserted here is the
/// part that is genuinely managed: the shape of the records, their equality,
/// and the stability of the enum values that cross the ABI as integers.
///
/// Those enum values are not cosmetic. They are marshalled as plain integers,
/// so renumbering one silently changes the meaning of data already crossing
/// the boundary -- a reference arc would start reading as a payload.
/// </remarks>
public sealed class InspectionSnapshotRecordTests
{
    [Test]
    public async Task PcpArcTypeValuesAreStableAcrossTheAbi()
    {
        // Marshalled as integers, so these numbers are the wire contract:
        // renumbering one silently turns a reference arc into a payload for
        // data already crossing the boundary. Read at runtime rather than
        // written as constants, so the assertion is against the enum as
        // compiled rather than against a folded literal.
        Dictionary<string, int> actual = Enum.GetValues<PcpArcType>()
            .ToDictionary(arc => arc.ToString(), arc => (int)arc, StringComparer.Ordinal);

        Dictionary<string, int> expected = new(StringComparer.Ordinal)
        {
            ["Root"] = 0,
            ["Inherit"] = 1,
            ["Variant"] = 2,
            ["Relocate"] = 3,
            ["Reference"] = 4,
            ["Payload"] = 5,
            ["Specialize"] = 6,
        };

        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    public async Task UsdValidationSeverityValuesAreStableAcrossTheAbi()
    {
        string[] names = Enum.GetNames<UsdValidationSeverity>();
        await Assert.That(names.Length).IsGreaterThan(1);

        // Every declared value must be distinct, or two severities collapse
        // into one after marshalling.
        int[] values = Enum.GetValues<UsdValidationSeverity>()
            .Select(severity => (int)severity)
            .ToArray();
        await Assert.That(values.Distinct().Count()).IsEqualTo(values.Length);
    }

    [Test]
    public async Task PrimIndexNodesCompareByValueForScalarsButNotCollections()
    {
        IReadOnlyList<string> sharedLayers = ["root.usda", "sub.usda"];
        PcpPrimIndexNode left = CreateNode("/World", sharedLayers);
        PcpPrimIndexNode right = CreateNode("/World", sharedLayers);
        PcpPrimIndexNode other = CreateNode("/Other", sharedLayers);

        // Scalar members compare by value, as records do.
        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
        await Assert.That(left).IsNotEqualTo(other);

        // But the collection member does NOT. The compiler-generated Equals
        // uses EqualityComparer<IReadOnlyList<string>>.Default, which is
        // reference equality for a list, so two structurally identical
        // snapshots built from separate lists are NOT equal. That is a real
        // trap for the intended use -- diffing two snapshots to decide whether
        // to repaint would report a change that did not happen -- so it is
        // asserted here rather than assumed away. See the todo
        // 'inspection-snapshot-value-equality'.
        PcpPrimIndexNode separateList = CreateNode("/World", ["root.usda", "sub.usda"]);
        await Assert.That(left).IsNotEqualTo(separateList);

        await Assert.That(left.LayerIdentifiers.Count).IsEqualTo(2);
        await Assert.That(left.LayerIdentifiers[1]).IsEqualTo("sub.usda");
        await Assert.That(left.ArcType).IsEqualTo(PcpArcType.Reference);
    }

    [Test]
    public async Task PrimIndexSnapshotCarriesNodesAndErrorsSeparately()
    {
        var snapshot = new PcpPrimIndex(
            [CreateNode("/World"), CreateNode("/World/Child")],
            ["could not resolve reference"]);

        await Assert.That(snapshot.Nodes.Count).IsEqualTo(2);
        await Assert.That(snapshot.Errors.Count).IsEqualTo(1);
        await Assert.That(snapshot.Nodes[1].SitePath).IsEqualTo("/World/Child");

        // Errors must not be conflated with nodes: a composition that produced
        // an error still produces nodes, and a UI shows both.
        await Assert.That(snapshot.Errors[0]).IsEqualTo("could not resolve reference");
    }

    [Test]
    public async Task ValidationErrorsCompareByValueAndPreserveSeverity()
    {
        UsdValidationSeverity severity = Enum.GetValues<UsdValidationSeverity>()[0];
        IReadOnlyList<string> sites = ["/"];
        var left = new UsdValidationError(
            severity, "StageMetadataChecker", "MissingDefaultPrim", "no default prim", sites);
        var right = new UsdValidationError(
            severity, "StageMetadataChecker", "MissingDefaultPrim", "no default prim", sites);
        var differentMessage = new UsdValidationError(
            severity, "StageMetadataChecker", "MissingDefaultPrim", "different message", sites);

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left).IsNotEqualTo(differentMessage);
        await Assert.That(left.Severity).IsEqualTo(severity);
        await Assert.That(left.Sites.Count).IsEqualTo(1);

        // Same caveat as the prim-index node: Sites is compared by reference,
        // so an identical error rebuilt from a separate list is not equal.
        var separateList = new UsdValidationError(
            severity, "StageMetadataChecker", "MissingDefaultPrim", "no default prim", ["/"]);
        await Assert.That(left).IsNotEqualTo(separateList);
    }

    [Test]
    public async Task ValidatorInfoCarriesKeywordsAndSchemaTypesSeparately()
    {
        var info = new UsdValidationValidatorInfo(
            "StageMetadataChecker",
            "Checks stage metadata.",
            "usdValidation",
            ["metadata", "stage"],
            ["UsdStage"],
            IsSuite: false,
            IsTimeDependent: false);

        await Assert.That(info.Keywords.Count).IsEqualTo(2);
        await Assert.That(info.SchemaTypes.Count).IsEqualTo(1);
        await Assert.That(info.Keywords[1]).IsEqualTo("stage");
        await Assert.That(info.IsSuite).IsFalse();
    }

    [Test]
    public async Task SplineKnotsAndExtrapolationCompareByValue()
    {
        var left = new TsExtrapolation(TsExtrapMode.Linear, 2.5);
        var right = new TsExtrapolation(TsExtrapMode.Linear, 2.5);
        var different = new TsExtrapolation(TsExtrapMode.Linear, 2.75);

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left).IsNotEqualTo(different);
        await Assert.That(left.Slope).IsEqualTo(2.5);
    }

    [Test]
    public async Task SplineEnumValuesAreDistinctWithinEachFamily()
    {
        // Each of these crosses the ABI as an integer, so a duplicate value
        // would make two modes indistinguishable on the native side.
        await AssertDistinct<TsInterpMode>();
        await AssertDistinct<TsCurveType>();
        await AssertDistinct<TsExtrapMode>();
        await AssertDistinct<TsTangentAlgorithm>();
    }

    private static async Task AssertDistinct<T>()
        where T : struct, Enum
    {
        int[] values = Enum.GetValues<T>()
            .Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture))
            .ToArray();
        await Assert.That(values.Length).IsGreaterThan(0);
        await Assert.That(values.Distinct().Count()).IsEqualTo(values.Length);
    }

    private static PcpPrimIndexNode CreateNode(
        string sitePath,
        IReadOnlyList<string>? layerIdentifiers = null) =>
        new(
            ParentIndex: 0,
            ArcType: PcpArcType.Reference,
            IsCulled: false,
            IsInert: false,
            IsDueToAncestor: false,
            HasSpecs: true,
            CanContributeSpecs: true,
            NamespaceDepth: 1,
            DepthBelowIntroduction: 0,
            SiblingIndexAtOrigin: 0,
            SitePath: sitePath,
            IntroPath: "/",
            PathAtIntroduction: sitePath,
            PathAtOriginRootIntroduction: sitePath,
            LayerStackIdentifier: "root.usda",
            LayerIdentifiers: layerIdentifiers ?? ["root.usda", "sub.usda"]);
}
