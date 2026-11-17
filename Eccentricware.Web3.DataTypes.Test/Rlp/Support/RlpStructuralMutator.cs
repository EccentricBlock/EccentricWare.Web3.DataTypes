using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>The structural mutation families used by the differential and fuzz tests.</summary>
public enum RlpMutationKind
{
    /// <summary>Replace the prefix byte of one item.</summary>
    Prefix,

    /// <summary>Change, insert or drop length bytes of one item, or make a short form long.</summary>
    LengthBytes,

    /// <summary>Cut the document at a random position.</summary>
    Truncate,

    /// <summary>Append bytes after the root item.</summary>
    Append,

    /// <summary>Make a child or its parent claim a different length so a child crosses its list boundary.</summary>
    ChildBoundary,

    /// <summary>Wrap the document in enclosing lists, sometimes with one wrong wrapper length.</summary>
    Nesting,

    /// <summary>The unstructured mutator of <see cref="RlpReference.MutateRandomly"/>.</summary>
    Generic,

    /// <summary>Rewrite one item's header as a long form with a random length-of-length 1..8 (leading zeros, below-56, truncated or damaged lengths).</summary>
    LongForm,
}

/// <summary>
/// Seeded structure-aware mutations of a valid RLP document. The item map is read with the production reader, so the
/// input must be a document the library accepts; the mutations themselves only edit bytes.
/// </summary>
public static class RlpStructuralMutator
{
    private static readonly byte[] BoundaryPrefixes = [0x00, 0x7f, 0x80, 0x81, 0xb6, 0xb7, 0xb8, 0xb9, 0xbf, 0xc0, 0xf6, 0xf7, 0xf8, 0xf9, 0xff];

    private readonly record struct ItemSpan(int Start, int PayloadStart, int End, bool IsList, int ParentIndex);

    /// <summary>Applies one mutation of <paramref name="kind"/>; the result may equal the input or be empty.</summary>
    public static byte[] Mutate(Random rng, byte[] valid, RlpMutationKind kind)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(valid);
        if (kind == RlpMutationKind.Generic)
        {
            return RlpReference.MutateRandomly(rng, valid);
        }

        var items = MapItems(valid);
        if (items.Count == 0)
        {
            return RlpReference.MutateRandomly(rng, valid);
        }

        return kind switch
        {
            RlpMutationKind.Prefix => MutatePrefix(rng, valid, items),
            RlpMutationKind.LengthBytes => MutateLengthBytes(rng, valid, items),
            RlpMutationKind.Truncate => valid[..rng.Next(0, valid.Length)],
            RlpMutationKind.Append => Append(rng, valid),
            RlpMutationKind.ChildBoundary => MutateChildBoundary(rng, valid, items),
            RlpMutationKind.LongForm => MutateLongFormHeader(rng, valid, items),
            _ => MutateNesting(rng, valid),
        };
    }

    /// <summary>One mutation of a randomly chosen kind (weighted equally).</summary>
    public static byte[] MutateAny(Random rng, byte[] valid, out RlpMutationKind kind)
    {
        kind = (RlpMutationKind)rng.Next(Enum.GetValues<RlpMutationKind>().Length);
        return Mutate(rng, valid, kind);
    }

    private static List<ItemSpan> MapItems(byte[] valid)
    {
        var items = new List<ItemSpan>();
        var limits = new RLPDecodeLimits(Math.Max(1, valid.Length), RLPDecodeLimits.HardMaxListDepth, int.MaxValue);
        if (RLPData.TryDecodeComplete(valid, in limits, out var root, out _) != RLPStatus.Success)
        {
            return items;
        }

        Collect(root, -1, items);
        return items;
    }

    private static void Collect(RLPData item, int parentIndex, List<ItemSpan> items)
    {
        var index = items.Count;
        var encoded = item.EncodedRange;
        items.Add(new ItemSpan(encoded.Offset, item.PayloadRange.Offset, encoded.Offset + encoded.Length, item.Kind == RLPKind.List, parentIndex));
        if (item.Kind != RLPKind.List || item.TryGetChildren(out var children) != RLPStatus.Success)
        {
            return;
        }

        while (children.MoveNext())
        {
            Collect(children.Current, index, items);
        }
    }

    private static byte RandomPrefix(Random rng, byte current) => rng.Next(4) switch
    {
        0 => (byte)rng.Next(256),
        1 => BoundaryPrefixes[rng.Next(BoundaryPrefixes.Length)],
        2 => (byte)(current + 1),
        _ => (byte)(current - 1),
    };

    private static byte[] MutatePrefix(Random rng, byte[] valid, List<ItemSpan> items)
    {
        var item = items[rng.Next(items.Count)];
        var result = (byte[])valid.Clone();
        result[item.Start] = RandomPrefix(rng, result[item.Start]);
        return result;
    }

    private static byte[] Splice(byte[] source, int index, int removeCount, params byte[] insert)
    {
        var result = new byte[source.Length - removeCount + insert.Length];
        source.AsSpan(0, index).CopyTo(result);
        insert.CopyTo(result.AsSpan(index));
        source.AsSpan(index + removeCount).CopyTo(result.AsSpan(index + insert.Length));
        return result;
    }

    private static byte[] MutateLengthBytes(Random rng, byte[] valid, List<ItemSpan> items)
    {
        // Choose among: edit a length byte, insert a leading zero length byte, drop a length byte, or write a short
        // form long. Items without a header (literals) fall back to a prefix change.
        var item = items[rng.Next(items.Count)];
        var prefix = valid[item.Start];
        var headerLength = item.PayloadStart - item.Start;
        var isLiteral = headerLength == 0;
        if (isLiteral)
        {
            return MutatePrefix(rng, valid, items);
        }

        var isLongForm = prefix is >= 0xb8 and <= 0xbf or >= 0xf8;
        switch (rng.Next(4))
        {
            case 0 when isLongForm:
                {
                    var result = (byte[])valid.Clone();
                    var position = item.Start + 1 + rng.Next(headerLength - 1);
                    result[position] = rng.Next(5) switch
                    {
                        0 => 0x00,
                        1 => 0xff,
                        2 => (byte)(result[position] + 1),
                        3 => (byte)(result[position] - 1),
                        _ => (byte)rng.Next(256),
                    };
                    return result;
                }

            case 1 when isLongForm:
                {
                    // Leading zero length byte, prefix adjusted so the byte count is self-consistent.
                    var bumped = (byte)(prefix + 1);
                    return Splice(valid, item.Start, 2, bumped, 0x00, valid[item.Start + 1]);
                }

            case 2 when isLongForm && headerLength > 2:
                {
                    // Drop the first length byte and lower the length-of-length by one.
                    var result = Splice(valid, item.Start + 1, 1);
                    result[item.Start] = (byte)(prefix - 1);
                    return result;
                }

            case 3 when !isLongForm:
                {
                    // Short form rewritten as the one-byte long form (non-canonical below 56).
                    var payloadLength = item.End - item.PayloadStart;
                    var longPrefix = prefix >= 0xc0 ? (byte)0xf8 : (byte)0xb8;
                    return Splice(valid, item.Start, 1, longPrefix, (byte)payloadLength);
                }

            default:
                {
                    var result = (byte[])valid.Clone();
                    result[item.Start] = isLongForm ? (byte)(prefix + (rng.Next(2) == 0 ? 1 : -1)) : RandomPrefix(rng, prefix);
                    return result;
                }
        }
    }

    private static byte[] MutateLongFormHeader(Random rng, byte[] valid, List<ItemSpan> items)
    {
        // Prefer the root: with the parent untouched a nested rewrite mostly ends in a list-boundary error, while a root
        // rewrite exercises the canonical-length rules alone.
        var item = rng.Next(5) < 2 ? items[0] : items[rng.Next(items.Count)];
        var headerLength = item.PayloadStart - item.Start;
        if (headerLength == 0)
        {
            return MutatePrefix(rng, valid, items);
        }

        var payloadLength = item.End - item.PayloadStart;
        var lengthOfLength = rng.Next(1, 9);
        var header = new byte[1 + lengthOfLength];
        header[0] = (byte)((item.IsList ? 0xf7 : 0xb7) + lengthOfLength);
        var remaining = (ulong)payloadLength;
        for (var i = lengthOfLength; i >= 1; i--)
        {
            header[i] = (byte)remaining;
            remaining >>= 8;
        }

        if (rng.Next(6) == 0)
        {
            header[rng.Next(1, header.Length)] ^= (byte)(1 << rng.Next(8));
        }

        return Splice(valid, item.Start, headerLength, header);
    }

    private static byte[] Append(Random rng, byte[] valid)
    {
        var extra = new byte[rng.Next(1, 5)];
        for (var i = 0; i < extra.Length; i++)
        {
            extra[i] = rng.Next(2) == 0 ? (byte)rng.Next(256) : BoundaryPrefixes[rng.Next(BoundaryPrefixes.Length)];
        }

        return Splice(valid, valid.Length, 0, extra);
    }

    private static byte[] ShiftLength(byte[] valid, ItemSpan item, int delta)
    {
        // Short form: the length lives in the prefix. Long form: it lives in the last length byte.
        var result = (byte[])valid.Clone();
        var prefix = valid[item.Start];
        if (prefix is >= 0x81 and <= 0xb7 or >= 0xc1 and <= 0xf7)
        {
            result[item.Start] = (byte)(prefix + delta);
        }
        else if (item.PayloadStart - item.Start > 1)
        {
            result[item.PayloadStart - 1] = (byte)(result[item.PayloadStart - 1] + delta);
        }
        else
        {
            result[item.Start] = (byte)(prefix + delta);
        }

        return result;
    }

    private static byte[] MutateChildBoundary(Random rng, byte[] valid, List<ItemSpan> items)
    {
        var deltas = new[] { -3, -2, -1, 1, 2, 3 };
        var delta = deltas[rng.Next(deltas.Length)];
        var children = new List<int>();
        for (var i = 1; i < items.Count; i++)
        {
            children.Add(i);
        }

        if (children.Count == 0 || rng.Next(3) == 0)
        {
            // Parent (here: any list, preferably the root) claims a different length than its children fill.
            var lists = Enumerable.Range(0, items.Count).Where(i => items[i].IsList).ToArray();
            return lists.Length == 0 ? MutatePrefix(rng, valid, items) : ShiftLength(valid, items[lists[rng.Next(lists.Length)]], delta);
        }

        return ShiftLength(valid, items[children[rng.Next(children.Count)]], delta);
    }

    private static byte[] MutateNesting(Random rng, byte[] valid)
    {
        var layers = rng.Next(4) == 0 ? rng.Next(60, 71) : rng.Next(1, 6);
        var current = valid;
        var corruptLayer = rng.Next(3) == 0 ? rng.Next(layers) : -1;
        for (var layer = 0; layer < layers; layer++)
        {
            var length = current.Length + (layer == corruptLayer ? rng.Next(2) == 0 ? 1 : -1 : 0);
            current = Splice(current, 0, 0, ListHeader(Math.Max(0, length)));
        }

        return current;
    }

    private static byte[] ListHeader(int length)
    {
        if (length <= 55)
        {
            return [(byte)(0xc0 + length)];
        }

        if (length <= 0xff)
        {
            return [0xf8, (byte)length];
        }

        if (length <= 0xffff)
        {
            return [0xf9, (byte)(length >> 8), (byte)length];
        }

        return [0xfa, (byte)(length >> 16), (byte)(length >> 8), (byte)length];
    }
}
