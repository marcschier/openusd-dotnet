// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Skel;

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
    public async Task PrimIndexNodesCompareCollectionContentsByValue()
    {
        PcpPrimIndexNode left = CreateNode("/World", ["root.usda", "sub.usda", "session.usda"]);
        PcpPrimIndexNode right = CreateNode("/World", ["root.usda", "sub.usda", "session.usda"]);

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
        await Assert.That(left).IsNotEqualTo(CreateNode("/World", ["other.usda", "sub.usda", "session.usda"]));
        await Assert.That(left).IsNotEqualTo(CreateNode("/World", ["root.usda", "other.usda", "session.usda"]));
        await Assert.That(left).IsNotEqualTo(CreateNode("/World", ["root.usda", "sub.usda", "other.usda"]));

        await Assert.That(left.ToString()).Contains("LayerIdentifiers = [root.usda, sub.usda, session.usda]");
        await Assert.That(left.LayerIdentifiers.Count).IsEqualTo(3);
        await Assert.That(left.LayerIdentifiers[1]).IsEqualTo("sub.usda");
        await Assert.That(left.ArcType).IsEqualTo(PcpArcType.Reference);
    }

    [Test]
    public async Task PrimIndexSnapshotsCompareNodeAndErrorSequencesByValue()
    {
        var left = new PcpPrimIndex(
            [CreateNode("/World/A"), CreateNode("/World/B"), CreateNode("/World/C")],
            ["first error", "middle error", "last error"]);
        var right = new PcpPrimIndex(
            [CreateNode("/World/A"), CreateNode("/World/B"), CreateNode("/World/C")],
            ["first error", "middle error", "last error"]);

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
        await Assert.That(left).IsNotEqualTo(new PcpPrimIndex(
            [CreateNode("/World/Z"), CreateNode("/World/B"), CreateNode("/World/C")],
            ["first error", "middle error", "last error"]));
        await Assert.That(left).IsNotEqualTo(new PcpPrimIndex(
            [CreateNode("/World/A"), CreateNode("/World/Z"), CreateNode("/World/C")],
            ["first error", "middle error", "last error"]));
        await Assert.That(left).IsNotEqualTo(new PcpPrimIndex(
            [CreateNode("/World/A"), CreateNode("/World/B"), CreateNode("/World/Z")],
            ["first error", "middle error", "last error"]));
        await Assert.That(left).IsNotEqualTo(new PcpPrimIndex(
            [CreateNode("/World/A"), CreateNode("/World/B"), CreateNode("/World/C")],
            ["other error", "middle error", "last error"]));
        await Assert.That(left).IsNotEqualTo(new PcpPrimIndex(
            [CreateNode("/World/A"), CreateNode("/World/B"), CreateNode("/World/C")],
            ["first error", "other error", "last error"]));
        await Assert.That(left).IsNotEqualTo(new PcpPrimIndex(
            [CreateNode("/World/A"), CreateNode("/World/B"), CreateNode("/World/C")],
            ["first error", "middle error", "other error"]));

        await Assert.That(left.ToString()).Contains("Errors = [first error, middle error, last error]");
        await Assert.That(left.Nodes.Count).IsEqualTo(3);
        await Assert.That(left.Errors.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ValidationErrorsCompareSitesByValue()
    {
        UsdValidationSeverity severity = Enum.GetValues<UsdValidationSeverity>()[0];
        var left = new UsdValidationError(
            severity, "StageMetadataChecker", "MissingDefaultPrim", "no default prim", ["/A", "/B", "/C"]);
        var right = new UsdValidationError(
            severity, "StageMetadataChecker", "MissingDefaultPrim", "no default prim", ["/A", "/B", "/C"]);

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
        await Assert.That(left).IsNotEqualTo(new UsdValidationError(
            severity, "StageMetadataChecker", "MissingDefaultPrim", "no default prim", ["/Z", "/B", "/C"]));
        await Assert.That(left).IsNotEqualTo(new UsdValidationError(
            severity, "StageMetadataChecker", "MissingDefaultPrim", "no default prim", ["/A", "/Z", "/C"]));
        await Assert.That(left).IsNotEqualTo(new UsdValidationError(
            severity, "StageMetadataChecker", "MissingDefaultPrim", "no default prim", ["/A", "/B", "/Z"]));
        await Assert.That(left.ToString()).Contains("Sites = [/A, /B, /C]");
        await Assert.That(left.Severity).IsEqualTo(severity);
        await Assert.That(left.Sites.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ValidatorInfoComparesKeywordAndSchemaSequencesByValue()
    {
        var left = new UsdValidationValidatorInfo(
            "StageMetadataChecker",
            "Checks stage metadata.",
            "usdValidation",
            ["metadata", "stage", "root"],
            ["UsdStage", "UsdPrim", "UsdProperty"],
            IsSuite: false,
            IsTimeDependent: false);
        var right = new UsdValidationValidatorInfo(
            "StageMetadataChecker",
            "Checks stage metadata.",
            "usdValidation",
            ["metadata", "stage", "root"],
            ["UsdStage", "UsdPrim", "UsdProperty"],
            IsSuite: false,
            IsTimeDependent: false);

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
        await Assert.That(left).IsNotEqualTo(left with { Keywords = ["other", "stage", "root"] });
        await Assert.That(left).IsNotEqualTo(left with { Keywords = ["metadata", "other", "root"] });
        await Assert.That(left).IsNotEqualTo(left with { Keywords = ["metadata", "stage", "other"] });
        await Assert.That(left).IsNotEqualTo(left with { SchemaTypes = ["Other", "UsdPrim", "UsdProperty"] });
        await Assert.That(left).IsNotEqualTo(left with { SchemaTypes = ["UsdStage", "Other", "UsdProperty"] });
        await Assert.That(left).IsNotEqualTo(left with { SchemaTypes = ["UsdStage", "UsdPrim", "Other"] });
        await Assert.That(left.ToString()).Contains("Keywords = [metadata, stage, root]");
        await Assert.That(left.ToString()).Contains("SchemaTypes = [UsdStage, UsdPrim, UsdProperty]");
        await Assert.That(left.IsSuite).IsFalse();
    }

    [Test]
    public async Task SkelJointInfluencesCompareArraysByValue()
    {
        var left = new UsdSkelJointInfluences([1, 2, 3], [0.25F, 0.5F, 0.75F], 3, UsdSkelInterpolation.Vertex);
        var right = new UsdSkelJointInfluences([1, 2, 3], [0.25F, 0.5F, 0.75F], 3, UsdSkelInterpolation.Vertex);

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
        await Assert.That(left).IsNotEqualTo(left with { JointIndices = [9, 2, 3] });
        await Assert.That(left).IsNotEqualTo(left with { JointIndices = [1, 9, 3] });
        await Assert.That(left).IsNotEqualTo(left with { JointIndices = [1, 2, 9] });
        await Assert.That(left).IsNotEqualTo(left with { JointWeights = [9F, 0.5F, 0.75F] });
        await Assert.That(left).IsNotEqualTo(left with { JointWeights = [0.25F, 9F, 0.75F] });
        await Assert.That(left).IsNotEqualTo(left with { JointWeights = [0.25F, 0.5F, 9F] });
        await Assert.That(left.ToString()).Contains("JointIndices = [1, 2, 3]");
        await Assert.That(left.ToString()).Contains("JointWeights = [0.25, 0.5, 0.75]");
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
