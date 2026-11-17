using EccentricWare.Web3.DataTypes.Rlp;
using Nethereum.RLP;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Thin bridge between the independent <c>Nethereum.RLP</c> 7.1.0 implementation and the test oracle's
/// <see cref="RlpNode"/> tree, so both libraries and the oracle can be compared with one equality. Nothing here uses
/// production code except the public read API in <see cref="ReadLibraryTree"/>.
/// </summary>
internal static class NethereumRlpAdapter
{
    /// <summary>Item budget passed to <c>RLP.Decode(data, maxElements)</c>; equals the library default <c>MaxItems</c>.</summary>
    public const int ItemBudget = 8192;

    /// <summary>
    /// Decodes with <c>RLP.Decode(input, maxItems)</c>. Returns false (and the exception type name in
    /// <paramref name="failure"/>) when Nethereum throws for any reason.
    /// </summary>
    public static bool TryDecode(byte[] input, int maxItems, out RlpNode? tree, out string failure)
    {
        try
        {
            tree = ToNode(RLP.Decode(input, maxItems));
            failure = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            tree = null;
            failure = exception.GetType().FullName ?? exception.GetType().Name;
            return false;
        }
    }

    /// <summary>
    /// Converts a Nethereum element tree to an <see cref="RlpNode"/>. A string element whose data is null is the empty
    /// string (Nethereum represents <c>80</c> that way); a collection is a list.
    /// </summary>
    public static RlpNode ToNode(IRLPElement element)
    {
        if (element is RLPCollection collection)
        {
            var items = new RlpNode[collection.Count];
            for (var i = 0; i < items.Length; i++)
            {
                items[i] = ToNode(collection[i]);
            }

            return new RlpList(items);
        }

        return new RlpString(element.RLPData ?? []);
    }

    /// <summary>Encodes <paramref name="node"/> with <c>RLP.EncodeElement</c> for strings and <c>RLP.EncodeList</c> for lists.</summary>
    public static byte[] Encode(RlpNode node)
    {
        if (node is RlpString text)
        {
            return RLP.EncodeElement(text.Value);
        }

        var list = (RlpList)node;
        var children = new byte[list.Items.Count][];
        for (var i = 0; i < children.Length; i++)
        {
            children[i] = Encode(list.Items[i]);
        }

        return RLP.EncodeList(children);
    }

    /// <summary>
    /// Reads a document accepted by the library into an <see cref="RlpNode"/> using only the public enumerator API.
    /// Recursion depth is bounded by the decode limits (at most 64 lists).
    /// </summary>
    public static RlpNode ReadLibraryTree(RLPData item)
    {
        if (item.Kind == RLPKind.ByteString)
        {
            return new RlpString(item.Payload.ToArray());
        }

        if (item.Kind != RLPKind.List)
        {
            throw new InvalidOperationException("Library item is not valid.");
        }

        var status = item.TryGetChildren(out var children);
        if (status != RLPStatus.Success)
        {
            throw new InvalidOperationException($"TryGetChildren returned {status}.");
        }

        var nodes = new List<RlpNode>();
        while (children.MoveNext())
        {
            nodes.Add(ReadLibraryTree(children.Current));
        }

        return new RlpList(nodes.ToArray());
    }
}
