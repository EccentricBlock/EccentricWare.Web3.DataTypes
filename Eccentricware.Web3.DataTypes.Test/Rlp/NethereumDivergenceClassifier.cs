using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Compares the library with <c>Nethereum.RLP</c> 7.1.0 on one input and names every disagreement by a pattern key.
/// A disagreement is only given one of the documented keys when its stated reason really holds for that input and
/// limits; anything else is reported as <c>Unexplained.*</c>, so a new kind of divergence cannot hide inside a known one.
/// </summary>
internal static class NethereumDivergenceClassifier
{
    /// <summary>The library rejected out-of-range limits; Nethereum has no limits object.</summary>
    public const string LibraryRejectsInvalidLimits = "LibraryRejectsNethereumAccepts.InvalidArgument";

    /// <summary>The input nests deeper than the configured depth but within Nethereum's fixed cap of 64.</summary>
    public const string LibraryRejectsDepthBelowNethereumCap = "LibraryRejectsNethereumAccepts.TooDeep";

    /// <summary>The input is larger than the configured size budget; Nethereum has no size budget.</summary>
    public const string LibraryRejectsSizeBudget = "LibraryRejectsNethereumAccepts.TooLarge";

    /// <summary>Both accepted but the decoded trees differ (never expected).</summary>
    public const string BothAcceptDifferentTree = "Unexplained.BothAccept.DifferentTree";

    /// <summary>
    /// Every divergence pattern found when the whole test corpus is compared, with the wire-rule reason. The
    /// differential tests assert that the observed set equals this set exactly.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Reason)> DocumentedPatterns =
    [
        (
            LibraryRejectsInvalidLimits,
            "Not a wire-format difference. RLPDecodeLimits with MaxEncodedBytes <= 0, MaxListDepth outside 1..64 or MaxItems <= 0 is "
            + "rejected before the input is read (spec 5.3 rule 1). Nethereum.RLP has no limits object, so it decodes the valid document. "
            + "The library is right by its own contract."),
        (
            LibraryRejectsDepthBelowNethereumCap,
            "Not a wire-format difference. Nethereum's nesting cap is the constant RLP.MaxDecodeDepth = 64; the library's MaxListDepth is "
            + "configurable from 1 to 64 and defaults to 32. A document nested deeper than the configured depth but at most 64 deep is "
            + "valid RLP, so Nethereum accepting it is correct and the library rejecting it is the configured policy. At depth 65 both reject."),
        (
            LibraryRejectsSizeBudget,
            "Not a wire-format difference. The library enforces MaxEncodedBytes before reading the content (spec 5.3 rule 2); "
            + "Nethereum.RLP.Decode has no document-size parameter. The document is valid RLP."),
    ];

    /// <summary>Everything observed for one input; the classifier is a pure function of this and the limits.</summary>
    public readonly record struct Observation(RLPStatus LibraryStatus, RlpNode? LibraryTree, bool NethereumAccepts, RlpNode? NethereumTree);

    /// <summary>Decodes with both libraries and classifies; null means they agree (both reject, or both accept the same tree).</summary>
    public static string? Compare(byte[] input, RLPDecodeLimits limits, int nethereumItems)
    {
        var status = RLPData.TryDecodeComplete(input, in limits, out var document, out _);
        var libraryTree = status == RLPStatus.Success ? NethereumRlpAdapter.ReadLibraryTree(document) : null;
        var nethereumAccepts = NethereumRlpAdapter.TryDecode(input, nethereumItems, out var nethereumTree, out var failure);
        if (!nethereumAccepts && failure != typeof(Exception).FullName)
        {
            // Nethereum 7.1.0 rejects only with a plain System.Exception; any other type (IndexOutOfRange, NullReference, ...)
            // would be a crash, not a rejection, and must not count as agreeing with the library.
            return $"Unexplained.NethereumThrew.{failure}";
        }

        return Classify(new Observation(status, libraryTree, nethereumAccepts, nethereumTree), input, limits);
    }

    /// <summary>
    /// The limits rule restated here on purpose (spec 9: bytes positive, depth 1..64, items positive) so a defect in
    /// <c>RLPDecodeLimits.IsValid</c> cannot make an unexplained rejection look like a documented one.
    /// </summary>
    private static bool AreValid(RLPDecodeLimits limits) =>
        limits.MaxEncodedBytes > 0 && limits.MaxListDepth is >= 1 and <= 64 && limits.MaxItems > 0;

    /// <summary>Pure classification; see the class remarks.</summary>
    public static string? Classify(Observation observed, byte[] input, RLPDecodeLimits limits)
    {
        var libraryAccepts = observed.LibraryStatus == RLPStatus.Success;
        if (libraryAccepts && observed.NethereumAccepts)
        {
            return Equals(observed.LibraryTree, observed.NethereumTree) ? null : BothAcceptDifferentTree;
        }

        if (!libraryAccepts && !observed.NethereumAccepts)
        {
            return null;
        }

        if (libraryAccepts)
        {
            return $"Unexplained.LibraryAcceptsNethereumRejects.{observed.LibraryStatus}";
        }

        // Library rejects, Nethereum accepts: allowed only for the three limit-policy reasons, each checked.
        var unexplained = $"Unexplained.LibraryRejectsNethereumAccepts.{observed.LibraryStatus}";
        switch (observed.LibraryStatus)
        {
            case RLPStatus.InvalidArgument:
                return AreValid(limits) ? unexplained : LibraryRejectsInvalidLimits;
            case RLPStatus.TooDeep:
                return AreValid(limits) && observed.NethereumTree is not null && RlpReference.ListDepth(observed.NethereumTree) > limits.MaxListDepth
                    && RlpReference.ListDepth(observed.NethereumTree) <= 64
                    ? LibraryRejectsDepthBelowNethereumCap
                    : unexplained;
            case RLPStatus.TooLarge:
                return AreValid(limits) && input.Length > limits.MaxEncodedBytes ? LibraryRejectsSizeBudget : unexplained;
            default:
                return unexplained;
        }
    }
}
