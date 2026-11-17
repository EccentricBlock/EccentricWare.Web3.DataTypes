using System.Text;
using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Immutable RLP value tree used by the test oracle: either a byte string or a list of nodes. Equality is deep and
/// by content (byte strings compare by bytes, lists by element sequence).
/// </summary>
public abstract class RlpNode : IEquatable<RlpNode>
{
    private protected RlpNode() { }

    /// <summary>True for a list, false for a byte string.</summary>
    public abstract bool IsList { get; }

    /// <summary>A byte string holding a copy of <paramref name="value"/>.</summary>
    public static RlpNode Bytes(params byte[] value) => new RlpString((byte[])value.Clone());

    /// <summary>A byte string holding the ASCII bytes of <paramref name="text"/>.</summary>
    public static RlpNode Ascii(string text) => new RlpString(Encoding.ASCII.GetBytes(text));

    /// <summary>A byte string parsed with <see cref="RlpReference.FromHex"/>.</summary>
    public static RlpNode FromHex(string hex) => new RlpString(RlpReference.FromHex(hex));

    /// <summary>A list of <paramref name="items"/> (the sequence is copied).</summary>
    public static RlpNode ListOf(params RlpNode[] items) => new RlpList(items.ToArray());

    /// <summary>A list of <paramref name="items"/> (the sequence is copied).</summary>
    public static RlpNode ListOf(IEnumerable<RlpNode> items) => new RlpList(items.ToArray());

    /// <summary>The Ethereum unsigned-integer string: shortest big-endian magnitude, empty for zero.</summary>
    public static RlpNode FromUInt64(ulong value)
    {
        var magnitude = new List<byte>();
        for (var remaining = value; remaining != 0; remaining >>= 8)
        {
            magnitude.Insert(0, (byte)(remaining & 0xff));
        }

        return new RlpString(magnitude.ToArray());
    }

    /// <summary>The empty byte string (encodes as <c>80</c>).</summary>
    public static RlpNode EmptyString => new RlpString([]);

    /// <summary>The empty list (encodes as <c>c0</c>).</summary>
    public static RlpNode EmptyList => new RlpList([]);

    public sealed override bool Equals(object? obj) => obj is RlpNode other && Equals(other);

    public abstract bool Equals(RlpNode? other);

    public abstract override int GetHashCode();

    public abstract override string ToString();
}

/// <summary>A byte-string node.</summary>
public sealed class RlpString : RlpNode
{
    public RlpString(byte[] value) => Value = value;

    /// <summary>The string bytes. Treat as read-only.</summary>
    public byte[] Value { get; }

    public override bool IsList => false;

    public override bool Equals(RlpNode? other) => other is RlpString s && Value.AsSpan().SequenceEqual(s.Value);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(false);
        hash.AddBytes(Value);
        return hash.ToHashCode();
    }

    public override string ToString() => "0x" + RlpReference.ToHex(Value);
}

/// <summary>A list node.</summary>
public sealed class RlpList : RlpNode
{
    public RlpList(RlpNode[] items) => Items = items;

    /// <summary>The children. Treat as read-only.</summary>
    public IReadOnlyList<RlpNode> Items { get; }

    public override bool IsList => true;

    public override bool Equals(RlpNode? other)
    {
        if (other is not RlpList list || list.Items.Count != Items.Count)
        {
            return false;
        }

        for (var i = 0; i < Items.Count; i++)
        {
            if (!Items[i].Equals(list.Items[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(true);
        foreach (var item in Items)
        {
            hash.Add(item.GetHashCode());
        }

        return hash.ToHashCode();
    }

    public override string ToString() => "[" + string.Join(", ", Items.Select(i => i.ToString())) + "]";
}

/// <summary>
/// Outcome of <see cref="RlpReference.Decode"/>. On failure <see cref="Root"/> is null and <see cref="Offset"/> is the
/// expected <c>RLPError.Offset</c>. <see cref="OffsetIsNormative"/> is true only when the specification text itself
/// fixes the offset (invalid arguments: -1; empty input: 0; root truncation: first missing byte = input length;
/// trailing data: first trailing byte). For every other failure the offset is the oracle's documented choice, see
/// <see cref="RlpReference"/>.
/// </summary>
public sealed record RlpReferenceResult(
    RLPStatus Status,
    int Offset,
    bool OffsetIsNormative,
    RlpNode? Root,
    int ItemCount,
    int ListDepth)
{
    public bool IsSuccess => Status == RLPStatus.Success;
}

/// <summary>
/// Independent, deliberately naive RLP oracle written from spec sections 2, 5.1 to 5.3 and 12 only. Recursive,
/// array based, allocating; nothing here is shared with production code beyond the RLPStatus enum.
///
/// Decisions on points the spec leaves open (also reported to the orchestrator):
/// <list type="bullet">
/// <item>Header checks follow 5.1 literally: container bounds, classify, long-form length bytes present inside the
/// container (a missing byte is Truncated at the root, InvalidListBoundary nested), first length byte non-zero,
/// accumulate, below 56 NonCanonical or above int.MaxValue LengthOverflow, <c>81 xx</c> with xx below 0x80
/// NonCanonical, then header plus payload fit. So truncation beats non-canonical length bytes, and a non-canonical
/// length beats a missing payload.</item>
/// <item>Offsets: root truncation = input length; nested missing bytes = offset of the child prefix; <c>81 xx</c> =
/// offset of the <c>81</c> prefix; non-canonical or overflowing long-form length = offset of the first length byte
/// (itemStart + 1); trailing data = first trailing byte; TooLarge = MaxEncodedBytes (first byte over the budget);
/// TooDeep and TooManyItems = offset of the prefix of the item whose discovery broke the budget.</item>
/// <item>Budgets are checked after the item's header parsed successfully (spec 5.2 step 4 before step 5), item count
/// before list depth for the same item.</item>
/// </list>
/// </summary>
public static class RlpReference
{
    private const int HardMaxListDepth = 64;
    private const int ShortFormMaxLength = 55;

    // ---------------------------------------------------------------- encoding

    /// <summary>Canonical RLP encoding of <paramref name="node"/>.</summary>
    public static byte[] Encode(RlpNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is RlpString text)
        {
            var value = text.Value;
            if (value.Length == 1 && value[0] < 0x80)
            {
                return [value[0]];
            }

            return Concatenate(LengthPrefix(value.Length, 0x80), value);
        }

        var list = (RlpList)node;
        var payload = Concatenate(list.Items.Select(Encode).ToArray());
        return Concatenate(LengthPrefix(payload.Length, 0xc0), payload);
    }

    /// <summary>Prefix for a payload of <paramref name="length"/> bytes; <paramref name="shortBase"/> is 0x80 or 0xc0.</summary>
    private static byte[] LengthPrefix(int length, int shortBase)
    {
        if (length <= ShortFormMaxLength)
        {
            return [(byte)(shortBase + length)];
        }

        var lengthBytes = new List<byte>();
        for (var remaining = length; remaining != 0; remaining >>= 8)
        {
            lengthBytes.Insert(0, (byte)(remaining & 0xff));
        }

        // 0xb7 = 0x80 + 55 and 0xf7 = 0xc0 + 55, so long forms start at base + 55 + lengthOfLength.
        return Concatenate([(byte)(shortBase + ShortFormMaxLength + lengthBytes.Count)], lengthBytes.ToArray());
    }

    /// <summary>Concatenates byte arrays.</summary>
    public static byte[] Concatenate(params byte[][] parts)
    {
        var result = new List<byte>();
        foreach (var part in parts)
        {
            result.AddRange(part);
        }

        return result.ToArray();
    }

    // ---------------------------------------------------------------- tree measurements

    /// <summary>Items as counted by MaxItems: every string and every list, including the root.</summary>
    public static int CountItems(RlpNode node) =>
        node is RlpList list ? 1 + list.Items.Sum(CountItems) : 1;

    /// <summary>List depth: root scalar 0, root list 1, a list inside it 2.</summary>
    public static int ListDepth(RlpNode node) =>
        node is RlpList list ? 1 + (list.Items.Count == 0 ? 0 : list.Items.Max(ListDepth)) : 0;

    /// <summary>Length of the payload (bytes after the prefix) of the item's canonical encoding.</summary>
    public static int PayloadLength(RlpNode node)
    {
        if (node is RlpString text)
        {
            return text.Value.Length;
        }

        return ((RlpList)node).Items.Sum(child => Encode(child).Length);
    }

    // ---------------------------------------------------------------- decoding

    /// <summary>
    /// Strict decode of exactly one RLP item. Never throws for any <paramref name="input"/> or limits. Recursion is
    /// bounded by <paramref name="maxListDepth"/> (at most 64) because over-deep lists are rejected before descending.
    /// </summary>
    public static RlpReferenceResult Decode(ReadOnlySpan<byte> input, int maxEncodedBytes, int maxListDepth, int maxItems)
    {
        // 5.3 rule 1: limits before input. 5.3 rule 2: size before content, empty input once within budget.
        if (maxEncodedBytes <= 0 || maxListDepth < 1 || maxListDepth > HardMaxListDepth || maxItems < 1)
        {
            return Failure(RLPStatus.InvalidArgument, -1, true);
        }

        if (input.Length > maxEncodedBytes)
        {
            return Failure(RLPStatus.TooLarge, maxEncodedBytes, false);
        }

        if (input.Length == 0)
        {
            return Failure(RLPStatus.EmptyInput, 0, true);
        }

        var session = new DecodeSession(input.ToArray(), maxListDepth, maxItems);
        var root = session.ParseItem(0, session.Bytes.Length, isRoot: true, parentListDepth: 0, out _);
        if (root is null)
        {
            return Failure(session.Status, session.Offset, session.OffsetIsNormative);
        }

        return new RlpReferenceResult(RLPStatus.Success, 0, true, root, session.ItemCount, session.DeepestList);
    }

    private static RlpReferenceResult Failure(RLPStatus status, int offset, bool normative) =>
        new(status, offset, normative, null, 0, 0);

    private sealed class DecodeSession(byte[] bytes, int maxListDepth, int maxItems)
    {
        public byte[] Bytes { get; } = bytes;
        public int ItemCount { get; private set; }
        public int DeepestList { get; private set; }
        public RLPStatus Status { get; private set; }
        public int Offset { get; private set; }
        public bool OffsetIsNormative { get; private set; }

        private bool Fail(RLPStatus status, int offset, bool normative)
        {
            Status = status;
            Offset = offset;
            OffsetIsNormative = normative;
            return false;
        }

        // Bytes the item needs are not inside the container: root => Truncated at the first missing byte (the end
        // of the input), nested => the child crosses its list, reported at the child's prefix.
        private bool Missing(int start, int containerEnd, bool isRoot) =>
            isRoot
                ? Fail(RLPStatus.TruncatedInput, containerEnd, true)
                : Fail(RLPStatus.InvalidListBoundary, start, false);

        private bool TryReadHeader(int start, int containerEnd, bool isRoot, out bool isList, out int headerLength, out int payloadLength)
        {
            isList = false;
            headerLength = 0;
            payloadLength = 0;
            int first = Bytes[start];
            long lengthOfLength = 0;
            long shortLength = 0;
            var literal = false;

            if (first < 0x80)
            {
                literal = true;
            }
            else if (first <= 0xb7)
            {
                shortLength = first - 0x80;
            }
            else if (first <= 0xbf)
            {
                lengthOfLength = first - 0xb7;
            }
            else if (first <= 0xf7)
            {
                isList = true;
                shortLength = first - 0xc0;
            }
            else
            {
                isList = true;
                lengthOfLength = first - 0xf7;
            }

            if (literal)
            {
                headerLength = 0;
                payloadLength = 1;
                return true;
            }

            long length = shortLength;
            headerLength = 1;
            if (lengthOfLength > 0)
            {
                if ((long)containerEnd - (start + 1) < lengthOfLength)
                {
                    return Missing(start, containerEnd, isRoot);
                }

                if (Bytes[start + 1] == 0)
                {
                    return Fail(RLPStatus.NonCanonicalEncoding, start + 1, false);
                }

                ulong accumulated = 0;
                for (var i = 0; i < lengthOfLength; i++)
                {
                    accumulated = (accumulated << 8) | Bytes[start + 1 + i];
                }

                if (accumulated < 56)
                {
                    return Fail(RLPStatus.NonCanonicalEncoding, start + 1, false);
                }

                if (accumulated > int.MaxValue)
                {
                    return Fail(RLPStatus.LengthOverflow, start + 1, false);
                }

                length = (long)accumulated;
                headerLength = 1 + (int)lengthOfLength;
            }

            if (!isList && first == 0x81 && start + 1 < containerEnd && Bytes[start + 1] < 0x80)
            {
                return Fail(RLPStatus.NonCanonicalEncoding, start, false);
            }

            if ((long)containerEnd - start < headerLength + length)
            {
                return Missing(start, containerEnd, isRoot);
            }

            payloadLength = (int)length;
            return true;
        }

        /// <summary>
        /// Parses the item at <paramref name="start"/> inside [start, containerEnd). Precondition:
        /// start &lt; containerEnd. Returns null after setting Status/Offset on failure.
        /// </summary>
        public RlpNode? ParseItem(int start, int containerEnd, bool isRoot, int parentListDepth, out int end)
        {
            end = start;
            if (!TryReadHeader(start, containerEnd, isRoot, out var isList, out var headerLength, out var payloadLength))
            {
                return null;
            }

            var payloadStart = start + headerLength;
            var payloadEnd = payloadStart + payloadLength;
            if (isRoot && payloadEnd < Bytes.Length)
            {
                // 5.3 rule 5: trailing data is reported before descending into the root's children.
                Fail(RLPStatus.TrailingData, payloadEnd, true);
                return null;
            }

            ItemCount++;
            if (ItemCount > maxItems)
            {
                Fail(RLPStatus.TooManyItems, start, false);
                return null;
            }

            end = payloadEnd;
            if (!isList)
            {
                return new RlpString(Bytes.AsSpan(payloadStart, payloadLength).ToArray());
            }

            var depth = parentListDepth + 1;
            if (depth > maxListDepth)
            {
                // Rejected before recursing, so the oracle's own stack use is bounded by maxListDepth.
                Fail(RLPStatus.TooDeep, start, false);
                return null;
            }

            DeepestList = Math.Max(DeepestList, depth);
            var children = new List<RlpNode>();
            var cursor = payloadStart;
            while (cursor < payloadEnd)
            {
                var child = ParseItem(cursor, payloadEnd, isRoot: false, depth, out var childEnd);
                if (child is null)
                {
                    return null;
                }

                children.Add(child);
                cursor = childEnd;
            }

            return new RlpList(children.ToArray());
        }
    }

    // ---------------------------------------------------------------- integer semantics (spec 2.2)

    /// <summary>
    /// Interprets a generic node as an unsigned 64-bit integer. Lists: WrongKind. Empty string: 0. A non-empty string
    /// starting with 00 (including the raw string 00): NonCanonicalInteger. More than 8 bytes: IntegerOverflow. The
    /// leading-zero test runs before the width test (the spec states both rules without an order).
    /// </summary>
    public static bool TryInterpretAsUInt64(RlpNode node, out ulong value, out RLPStatus status)
    {
        value = 0;
        if (node is not RlpString text)
        {
            status = RLPStatus.WrongKind;
            return false;
        }

        var bytes = text.Value;
        if (bytes.Length == 0)
        {
            status = RLPStatus.Success;
            return true;
        }

        if (bytes[0] == 0)
        {
            status = RLPStatus.NonCanonicalInteger;
            return false;
        }

        if (bytes.Length > 8)
        {
            status = RLPStatus.IntegerOverflow;
            return false;
        }

        ulong accumulated = 0;
        foreach (var b in bytes)
        {
            accumulated = (accumulated << 8) | b;
        }

        value = accumulated;
        status = RLPStatus.Success;
        return true;
    }

    // ---------------------------------------------------------------- hex

    /// <summary>Lowercase hex; with <paramref name="spaced"/> bytes are separated by one space.</summary>
    public static string ToHex(ReadOnlySpan<byte> bytes, bool spaced = false)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < bytes.Length; i++)
        {
            if (spaced && i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(bytes[i].ToString("x2"));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Parses whitespace separated tokens, case-insensitive. A token is an even number of hex digits optionally
    /// followed by <c>*count</c>, which repeats that token's bytes <c>count</c> times (<c>01*55</c> is 55 bytes of 01).
    /// Throws <see cref="FormatException"/> on malformed text.
    /// </summary>
    public static byte[] FromHex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new List<byte>();
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var star = token.IndexOf('*');
            var digits = star < 0 ? token : token[..star];
            var repeat = 1;
            if (star >= 0 && (!int.TryParse(token[(star + 1)..], out repeat) || repeat < 0))
            {
                throw new FormatException($"Bad repeat count in token '{token}'.");
            }

            if (digits.Length % 2 != 0)
            {
                throw new FormatException($"Odd number of hex digits in token '{token}'.");
            }

            var tokenBytes = new byte[digits.Length / 2];
            for (var i = 0; i < tokenBytes.Length; i++)
            {
                tokenBytes[i] = (byte)((HexValue(digits[2 * i]) << 4) | HexValue(digits[(2 * i) + 1]));
            }

            for (var r = 0; r < repeat; r++)
            {
                result.AddRange(tokenBytes);
            }
        }

        return result.ToArray();
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => throw new FormatException($"'{c}' is not a hex digit."),
    };

    // ---------------------------------------------------------------- random trees

    private static readonly int[] BoundaryStringLengths = [0, 1, 2, 54, 55, 56, 57, 255, 256];
    private static readonly int[] BoundaryListPayloadSizes = [0, 1, 54, 55, 56, 57, 255, 256];

    /// <summary>
    /// Generates a bounded canonical tree whose <see cref="ListDepth"/> is at most <paramref name="maxDepth"/> (0 means
    /// scalar roots only) and whose <see cref="CountItems"/> is at most <paramref name="maxItems"/>. Strings are biased
    /// to the boundary lengths 0,1,2,54,55,56,57,255,256 (those not above <paramref name="maxStringLength"/>), lists
    /// to payload sizes 0,1,54,55,56,57,255,256 built from genuine children when the item budget allows.
    /// </summary>
    public static RlpNode GenerateRandomTree(Random rng, int maxDepth, int maxItems, int maxStringLength = 300)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxItems, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(maxStringLength);
        return new TreeGenerator(rng, maxItems, maxStringLength).GenerateItem(maxDepth);
    }

    private sealed class TreeGenerator(Random rng, int itemBudget, int maxStringLength)
    {
        private int budget = itemBudget;

        /// <summary>Precondition: budget &gt;= 1.</summary>
        public RlpNode GenerateItem(int listLevelsLeft)
        {
            budget--;
            if (listLevelsLeft > 0 && rng.Next(100) < 45)
            {
                return GenerateList(listLevelsLeft);
            }

            return GenerateString();
        }

        private RlpNode GenerateList(int listLevelsLeft)
        {
            if (rng.Next(100) < 35)
            {
                var snapshot = budget;
                var size = BoundaryListPayloadSizes[rng.Next(BoundaryListPayloadSizes.Length)];
                var exact = FillChildren(size, listLevelsLeft - 1);
                if (exact is not null)
                {
                    return new RlpList(exact.ToArray());
                }

                budget = snapshot;
            }

            var count = rng.Next(0, Math.Min(budget, 6) + 1);
            var children = new List<RlpNode>();
            for (var i = 0; i < count && budget >= 1; i++)
            {
                children.Add(GenerateItem(listLevelsLeft - 1));
            }

            return new RlpList(children.ToArray());
        }

        private RlpNode GenerateString()
        {
            int length;
            if (rng.Next(100) < 60)
            {
                length = BoundaryStringLengths[rng.Next(BoundaryStringLengths.Length)];
                if (length > maxStringLength)
                {
                    length = rng.Next(0, maxStringLength + 1);
                }
            }
            else
            {
                length = rng.Next(0, maxStringLength + 1);
            }

            return new RlpString(RandomBytes(length, forceHighFirstByte: false));
        }

        private byte[] RandomBytes(int length, bool forceHighFirstByte)
        {
            var bytes = new byte[length];
            switch (rng.Next(5))
            {
                case 0:
                    rng.NextBytes(bytes);
                    break;
                case 1:
                    break; // all zero: raw strings of 00 are valid generic RLP
                case 2:
                    rng.NextBytes(bytes);
                    if (length > 0)
                    {
                        bytes[0] = 0;
                    }

                    break;
                case 3:
                    for (var i = 0; i < length; i++)
                    {
                        bytes[i] = (byte)rng.Next(0, 0x80);
                    }

                    break;
                default:
                    for (var i = 0; i < length; i++)
                    {
                        bytes[i] = (byte)rng.Next(0x80, 0x100);
                    }

                    break;
            }

            if (forceHighFirstByte && length > 0)
            {
                bytes[0] = (byte)rng.Next(0x80, 0x100);
            }

            return bytes;
        }

        // Children whose encodings total exactly `total` bytes, or null when the item budget cannot afford it.
        private List<RlpNode>? FillChildren(int total, int listLevelsLeft)
        {
            var snapshot = budget;
            var result = FillChildrenCore(total, listLevelsLeft);
            if (result is null)
            {
                budget = snapshot;
            }

            return result;
        }

        private List<RlpNode>? FillChildrenCore(int total, int listLevelsLeft)
        {
            if (total == 0)
            {
                return [];
            }

            if (budget < 1)
            {
                return null;
            }

            if (rng.Next(100) < (total > 56 ? 70 : 40))
            {
                var single = ItemOfEncodedSize(total, listLevelsLeft);
                if (single is not null)
                {
                    return [single];
                }
            }

            RlpNode? head = null;
            var headSize = 1;
            for (var attempt = 0; attempt < 8 && head is null; attempt++)
            {
                headSize = rng.Next(1, Math.Min(total, 60) + 1);
                head = ItemOfEncodedSize(headSize, listLevelsLeft);
            }

            if (head is null)
            {
                headSize = 1;
                head = ItemOfEncodedSize(1, listLevelsLeft);
                if (head is null)
                {
                    return null;
                }
            }

            var tail = FillChildren(total - headSize, listLevelsLeft);
            if (tail is null)
            {
                return null;
            }

            tail.Insert(0, head);
            return tail;
        }

        private RlpNode? ItemOfEncodedSize(int size, int listLevelsLeft)
        {
            if (budget < 1)
            {
                return null;
            }

            var snapshot = budget;
            budget--;
            if (listLevelsLeft > 0 && rng.Next(100) < 40)
            {
                var list = ListOfEncodedSize(size, listLevelsLeft);
                if (list is not null)
                {
                    return list;
                }

                budget = snapshot - 1;
            }

            var text = StringOfEncodedSize(size);
            if (text is null)
            {
                budget = snapshot;
            }

            return text;
        }

        private RlpNode? ListOfEncodedSize(int size, int listLevelsLeft)
        {
            // Encoded size of a list with payload p: 1 + p when p <= 55, else 1 + lengthBytes(p) + p.
            int? payload = null;
            if (size - 1 <= ShortFormMaxLength)
            {
                payload = size - 1;
            }
            else
            {
                for (var lengthOfLength = 1; lengthOfLength <= 4 && payload is null; lengthOfLength++)
                {
                    var candidate = size - 1 - lengthOfLength;
                    if (candidate >= 56 && MinimalByteCount(candidate) == lengthOfLength)
                    {
                        payload = candidate;
                    }
                }
            }

            if (payload is null)
            {
                return null;
            }

            var children = FillChildren(payload.Value, listLevelsLeft - 1);
            return children is null ? null : new RlpList(children.ToArray());
        }

        private RlpNode? StringOfEncodedSize(int size)
        {
            if (size == 1)
            {
                return rng.Next(2) == 0
                    ? new RlpString([(byte)rng.Next(0, 0x80)])
                    : new RlpString([]);
            }

            int? length = null;
            if (size - 1 <= ShortFormMaxLength)
            {
                length = size - 1;
            }
            else
            {
                for (var lengthOfLength = 1; lengthOfLength <= 4 && length is null; lengthOfLength++)
                {
                    var candidate = size - 1 - lengthOfLength;
                    if (candidate >= 56 && MinimalByteCount(candidate) == lengthOfLength)
                    {
                        length = candidate;
                    }
                }
            }

            // A one-byte string only encodes to 2 bytes when the byte is >= 0x80.
            return length is null ? null : new RlpString(RandomBytes(length.Value, forceHighFirstByte: length.Value == 1));
        }

        private static int MinimalByteCount(int value)
        {
            var count = 0;
            for (var remaining = value; remaining != 0; remaining >>= 8)
            {
                count++;
            }

            return count;
        }
    }

    // ---------------------------------------------------------------- mutation

    private static readonly byte[] InterestingBytes = [0x00, 0x7f, 0x80, 0xb7, 0xb8, 0xbf, 0xc0, 0xf7, 0xf8, 0xff];

    /// <summary>
    /// Applies one to three seeded mutations: flip a bit, replace a byte with an interesting prefix value, truncate,
    /// append, delete a byte, duplicate a slice, insert an interesting byte. The result may be empty.
    /// </summary>
    public static byte[] MutateRandomly(Random rng, byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(encoded);
        var bytes = new List<byte>(encoded);
        var operations = rng.Next(1, 4);
        for (var step = 0; step < operations; step++)
        {
            switch (rng.Next(7))
            {
                case 0 when bytes.Count > 0:
                    bytes[rng.Next(bytes.Count)] ^= (byte)(1 << rng.Next(8));
                    break;
                case 1 when bytes.Count > 0:
                    bytes[rng.Next(bytes.Count)] = InterestingBytes[rng.Next(InterestingBytes.Length)];
                    break;
                case 2 when bytes.Count > 0:
                    {
                        var keep = rng.Next(0, bytes.Count);
                        bytes.RemoveRange(keep, bytes.Count - keep);
                        break;
                    }

                case 3:
                    {
                        var extra = rng.Next(1, 5);
                        for (var i = 0; i < extra; i++)
                        {
                            bytes.Add(rng.Next(2) == 0 ? (byte)rng.Next(256) : InterestingBytes[rng.Next(InterestingBytes.Length)]);
                        }

                        break;
                    }

                case 4 when bytes.Count > 0:
                    bytes.RemoveAt(rng.Next(bytes.Count));
                    break;
                case 5 when bytes.Count > 0:
                    {
                        var start = rng.Next(bytes.Count);
                        var length = rng.Next(1, Math.Min(8, bytes.Count - start) + 1);
                        var slice = bytes.GetRange(start, length);
                        bytes.InsertRange(rng.Next(bytes.Count + 1), slice);
                        break;
                    }

                case 6:
                    bytes.Insert(rng.Next(bytes.Count + 1), InterestingBytes[rng.Next(InterestingBytes.Length)]);
                    break;
            }
        }

        return bytes.ToArray();
    }
}
