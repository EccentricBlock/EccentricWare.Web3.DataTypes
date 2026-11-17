using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>A canonical document: its exact bytes (hand-stated, not produced by the oracle) and its value tree.</summary>
public sealed record CanonicalVector(string Name, byte[] Encoded, RlpNode Tree)
{
    public override string ToString() => Name;
}

/// <summary>
/// A byte-exact vector taken from independent knowledge of Ethereum RLP; <paramref name="Source"/> says where it comes from.
/// </summary>
public sealed record KnownEthereumVector(string Name, string Source, byte[] Encoded, RlpNode Tree)
{
    public override string ToString() => Name;
}

/// <summary>
/// One decode expectation. <paramref name="Offset"/> is the expected <c>RLPError.Offset</c> (0 for success rows);
/// when <paramref name="OffsetIsNormative"/> is false the offset is the oracle's documented choice and an
/// implementation may legitimately differ (status is still binding).
/// </summary>
public sealed record DecodeVector(
    string Name,
    byte[] Input,
    RLPDecodeLimits Limits,
    RLPStatus Status,
    int Offset,
    bool OffsetIsNormative)
{
    public override string ToString() => Name;
}

/// <summary>
/// An integer-accessor expectation: decode <paramref name="Encoded"/> as generic RLP (must succeed), then interpret the
/// root as an unsigned 64-bit integer. <paramref name="Value"/> is meaningful only when Status is Success.
/// </summary>
public sealed record IntegerVector(string Name, byte[] Encoded, ulong Value, RLPStatus Status)
{
    public override string ToString() => Name;
}

/// <summary>
/// Data-driven RLP tables derived from the specification text (sections 2.3, 12.1, 12.2) and well-known Ethereum
/// examples. Encodings in the hand-stated tables are written as literal hex, never produced by
/// <see cref="RlpReference.Encode"/>, so decoding and encoding checks against them are independent. The
/// <c>*Rows</c> members are shaped for <c>[DynamicData]</c>: one <c>object[]</c> holding the row per vector.
/// </summary>
public static class RlpVectors
{
    private static byte[] H(string hex) => RlpReference.FromHex(hex);

    private static byte[] Cat(params byte[][] parts) => RlpReference.Concatenate(parts);

    private static byte[] Rep(byte value, int count) => Enumerable.Repeat(value, count).ToArray();

    private static byte[] Pattern(int length) => Enumerable.Range(0, length).Select(i => (byte)((i * 7) + 3)).ToArray();

    private static RlpNode Str(string hex) => RlpNode.FromHex(hex);

    private static RlpNode Items(int count, byte value) =>
        RlpNode.ListOf(Enumerable.Range(0, count).Select(_ => RlpNode.Bytes(value)));

    /// <summary>Hand-written list header for a payload of <paramref name="length"/> bytes (not using the oracle encoder).</summary>
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

        if (length <= 0xffffff)
        {
            return [0xfa, (byte)(length >> 16), (byte)(length >> 8), (byte)length];
        }

        throw new ArgumentOutOfRangeException(nameof(length));
    }

    private static byte[] WrapInList(byte[] payload)
    {
        var header = ListHeader(payload.Length);
        var result = new byte[header.Length + payload.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(payload, 0, result, header.Length, payload.Length);
        return result;
    }

    /// <summary>
    /// <paramref name="count"/> lists nested to that depth (the innermost is the empty list, each outer list holds the
    /// one inner list) with the byte offset of each list's prefix, outermost first.
    /// </summary>
    private static (byte[] Bytes, int[] ListOffsets) NestedLists(int count)
    {
        var headerLengths = new int[count];
        var current = new byte[] { 0xc0 };
        headerLengths[count - 1] = 1;
        for (var level = count - 2; level >= 0; level--)
        {
            var wrapped = WrapInList(current);
            headerLengths[level] = wrapped.Length - current.Length;
            current = wrapped;
        }

        var offsets = new int[count];
        for (var level = 1; level < count; level++)
        {
            offsets[level] = offsets[level - 1] + headerLengths[level - 1];
        }

        return (current, offsets);
    }

    private static RlpNode NestedTree(int count)
    {
        var node = RlpNode.EmptyList;
        for (var level = 1; level < count; level++)
        {
            node = RlpNode.ListOf(node);
        }

        return node;
    }

    private static IEnumerable<object[]> AsRows<T>(IEnumerable<T> vectors) where T : notnull =>
        vectors.Select(v => new object[] { v });

    // ================================================================ canonical vectors

    private static readonly Lazy<IReadOnlyList<CanonicalVector>> CanonicalLazy = new(BuildCanonical);

    /// <summary>Canonical documents covering spec 2.3 and 12.1.</summary>
    public static IReadOnlyList<CanonicalVector> Canonical => CanonicalLazy.Value;

    public static IEnumerable<object[]> CanonicalRows => AsRows(Canonical);

    private static IReadOnlyList<CanonicalVector> BuildCanonical()
    {
        var rows = new List<CanonicalVector>();

        void Add(string name, byte[] encoded, RlpNode tree) => rows.Add(new CanonicalVector(name, encoded, tree));

        // ---- spec section 2.3, every row
        Add("spec2.3 empty byte string", H("80"), RlpNode.EmptyString);
        Add("spec2.3 empty list", H("c0"), RlpNode.EmptyList);
        Add("spec2.3 raw string [00]", H("00"), Str("00"));
        Add("spec2.3 raw string [7f]", H("7f"), Str("7f"));
        Add("spec2.3 raw string [80]", H("81 80"), Str("80"));
        Add("spec2.3 string dog", H("83 64 6f 67"), RlpNode.Ascii("dog"));
        Add("spec2.3 list [cat, dog]", H("c8 83 63 61 74 83 64 6f 67"), RlpNode.ListOf(RlpNode.Ascii("cat"), RlpNode.Ascii("dog")));
        Add("spec2.3 list [[], [[]]]", H("c3 c0 c1 c0"), RlpNode.ListOf(RlpNode.EmptyList, RlpNode.ListOf(RlpNode.EmptyList)));
        Add("spec2.3 unsigned 0", H("80"), RlpNode.EmptyString);
        Add("spec2.3 unsigned 128", H("81 80"), Str("80"));
        Add("spec2.3 unsigned 256", H("82 01 00"), Str("01 00"));
        Add("spec2.3 string of 55 zero bytes", Cat(H("b7"), Rep(0x00, 55)), RlpNode.Bytes(Rep(0x00, 55)));
        Add("spec2.3 string of 56 zero bytes", Cat(H("b8 38"), Rep(0x00, 56)), RlpNode.Bytes(Rep(0x00, 56)));
        Add("spec2.3 list payload 55 (55 literal 01 children)", Cat(H("f7"), Rep(0x01, 55)), Items(55, 0x01));
        Add("spec2.3 list payload 56 (56 literal 01 children)", Cat(H("f8 38"), Rep(0x01, 56)), Items(56, 0x01));

        // ---- well-known examples, integers
        Add("set-theoretic 3", H("c7 c0 c1 c0 c3 c0 c1 c0"),
            RlpNode.ListOf(RlpNode.EmptyList, RlpNode.ListOf(RlpNode.EmptyList), RlpNode.ListOf(RlpNode.EmptyList, RlpNode.ListOf(RlpNode.EmptyList))));
        Add("integer 0", H("80"), RlpNode.EmptyString);
        Add("integer 1", H("01"), Str("01"));
        Add("integer 127", H("7f"), Str("7f"));
        Add("integer 128", H("81 80"), Str("80"));
        Add("integer 255", H("81 ff"), Str("ff"));
        Add("integer 256", H("82 01 00"), Str("01 00"));
        Add("integer 1024", H("82 04 00"), Str("04 00"));
        Add("integer 65535", H("82 ff ff"), Str("ff ff"));
        Add("integer 2^32", H("85 01 00 00 00 00"), Str("01 00 00 00 00"));
        Add("integer 2^56", H("88 01 00 00 00 00 00 00 00"), Str("01 00*7"));
        Add("integer ulong.MaxValue", H("88 ff*8"), Str("ff*8"));
        Add("lorem ipsum 56-byte string", Cat(H("b8 38"), "Lorem ipsum dolor sit amet, consectetur adipisicing elit"u8.ToArray()),
            RlpNode.Ascii("Lorem ipsum dolor sit amet, consectetur adipisicing elit"));

        // ---- all 128 literals and all 128 one-byte strings >= 0x80
        for (var b = 0; b < 0x80; b++)
        {
            Add($"literal {b:x2}", [(byte)b], RlpNode.Bytes((byte)b));
        }

        for (var b = 0x80; b <= 0xff; b++)
        {
            Add($"one-byte string {b:x2}", [0x81, (byte)b], RlpNode.Bytes((byte)b));
        }

        // ---- string lengths with exact canonical prefixes
        var stringPrefixes = new (int Length, string Prefix)[]
        {
            (2, "82"), (54, "b6"), (55, "b7"), (56, "b8 38"), (57, "b8 39"), (254, "b8 fe"), (255, "b8 ff"),
            (256, "b9 01 00"), (65535, "b9 ff ff"), (65536, "ba 01 00 00"),
        };
        Add("string length 0", H("80"), RlpNode.EmptyString);
        Add("string length 1 (>= 0x80)", H("81 aa"), Str("aa"));
        foreach (var (length, prefix) in stringPrefixes)
        {
            var payload = Pattern(length);
            Add($"string length {length}", Cat(H(prefix), payload), RlpNode.Bytes(payload));
        }

        // ---- raw strings with leading zeros are valid generic RLP
        Add("raw string 00 01", H("82 00 01"), Str("00 01"));
        Add("raw string 00 00", H("82 00 00"), Str("00 00"));
        Add("raw string 00 00 00", H("83 00 00 00"), Str("00 00 00"));
        Add("raw string 55 x 00 inside list", Cat(H("f8 38 b7"), Rep(0x00, 55)), RlpNode.ListOf(RlpNode.Bytes(Rep(0x00, 55))));

        // ---- list payloads made from genuine children
        var listPrefixes = new (int Payload, string Prefix)[]
        {
            (0, "c0"), (1, "c1"), (54, "f6"), (55, "f7"), (56, "f8 38"), (57, "f8 39"), (255, "f8 ff"), (256, "f9 01 00"),
        };
        foreach (var (payload, prefix) in listPrefixes)
        {
            Add($"list payload {payload} of literal 01 children", Cat(H(prefix), Rep(0x01, payload)), Items(payload, 0x01));
        }

        Add("list payload 1 [empty string]", H("c1 80"), RlpNode.ListOf(RlpNode.EmptyString));
        Add("list payload 1 [empty list]", H("c1 c0"), RlpNode.ListOf(RlpNode.EmptyList));
        Add("list payload 54 mixed [string53]", Cat(H("f6 b5"), Rep(0xaa, 53)), RlpNode.ListOf(RlpNode.Bytes(Rep(0xaa, 53))));
        Add("list payload 55 mixed [string54]", Cat(H("f7 b6"), Rep(0xaa, 54)), RlpNode.ListOf(RlpNode.Bytes(Rep(0xaa, 54))));
        Add("list payload 56 mixed [string54, 01]", Cat(H("f8 38 b6"), Rep(0xaa, 54), H("01")),
            RlpNode.ListOf(RlpNode.Bytes(Rep(0xaa, 54)), Str("01")));
        Add("list payload 57 mixed [string55, 01]", Cat(H("f8 39 b7"), Rep(0xaa, 55), H("01")),
            RlpNode.ListOf(RlpNode.Bytes(Rep(0xaa, 55)), Str("01")));
        Add("list payload 255 mixed [string253]", Cat(H("f8 ff b8 fd"), Rep(0xaa, 253)), RlpNode.ListOf(RlpNode.Bytes(Rep(0xaa, 253))));
        Add("list payload 256 mixed [string254]", Cat(H("f9 01 00 b8 fe"), Rep(0xaa, 254)), RlpNode.ListOf(RlpNode.Bytes(Rep(0xaa, 254))));
        Add("list payload 56 [list payload 55]", Cat(H("f8 38 f7"), Rep(0x01, 55)), RlpNode.ListOf(Items(55, 0x01)));
        Add("list payload 57 [list payload 55, 01]", Cat(H("f8 39 f7"), Rep(0x01, 55), H("01")), RlpNode.ListOf(Items(55, 0x01), Str("01")));
        Add("list of two long strings", Cat(H("f9 01 69 b8 38"), Rep(0x61, 56), H("b9 01 2c"), Rep(0x62, 300)),
            RlpNode.ListOf(RlpNode.Bytes(Rep(0x61, 56)), RlpNode.Bytes(Rep(0x62, 300))));

        // ---- nesting and mixtures
        Add("mixed [cat, [dog, []], 00, empty]", H("cc 83 63 61 74 c5 83 64 6f 67 c0 00 80"),
            RlpNode.ListOf(RlpNode.Ascii("cat"), RlpNode.ListOf(RlpNode.Ascii("dog"), RlpNode.EmptyList), Str("00"), RlpNode.EmptyString));
        Add("list of 8191 literals (3-byte header)", Cat(H("f9 1f ff"), Rep(0x01, 8191)), Items(8191, 0x01));
        foreach (var depth in new[] { 1, 2, 3, 10, 32 })
        {
            Add($"{depth} nested lists", NestedLists(depth).Bytes, NestedTree(depth));
        }

        return rows;
    }

    // ================================================================ known Ethereum vectors

    private static readonly Lazy<IReadOnlyList<KnownEthereumVector>> KnownEthereumLazy = new(BuildKnownEthereum);

    /// <summary>
    /// At least 20 byte-exact vectors from independent knowledge of Ethereum RLP. Sources: "rlptest.json" is the
    /// ethereum/tests RLPTests/rlptest.json fixture (entry name given); "wiki" is the Ethereum wiki / ethereum.org RLP
    /// examples. Vectors the author was not certain of were omitted.
    /// </summary>
    public static IReadOnlyList<KnownEthereumVector> KnownEthereum => KnownEthereumLazy.Value;

    public static IEnumerable<object[]> KnownEthereumRows => AsRows(KnownEthereum);

    private static IReadOnlyList<KnownEthereumVector> BuildKnownEthereum()
    {
        var rows = new List<KnownEthereumVector>();

        void Add(string name, string source, byte[] encoded, RlpNode tree) =>
            rows.Add(new KnownEthereumVector(name, source, encoded, tree));

        var pair = RlpNode.ListOf(RlpNode.Ascii("asdf"), RlpNode.Ascii("qwer"), RlpNode.Ascii("zxcv"));
        // One token (no spaces) so that the "*n" suffix repeats the whole 16-byte encoding of the triple.
        const string pairHex = "cf84617364668471776572847a786376";
        RlpNode KeyValue(int i) => RlpNode.ListOf(RlpNode.Ascii($"key{i}"), RlpNode.Ascii($"val{i}"));

        Add("emptystring", "rlptest.json emptystring", H("80"), RlpNode.EmptyString);
        Add("bytestring00", "rlptest.json bytestring00", H("00"), Str("00"));
        Add("bytestring01", "rlptest.json bytestring01", H("01"), Str("01"));
        Add("bytestring7F", "rlptest.json bytestring7F", H("7f"), Str("7f"));
        Add("shortstring dog", "rlptest.json shortstring; wiki", H("83 64 6f 67"), RlpNode.Ascii("dog"));
        Add("shortstring2 (55 bytes)", "rlptest.json shortstring2",
            H("b7 4c6f72656d20697073756d20646f6c6f722073697420616d65742c20636f6e7365637465747572206164697069736963696e6720656c69"),
            RlpNode.Ascii("Lorem ipsum dolor sit amet, consectetur adipisicing eli"));
        Add("longstring (56 bytes)", "rlptest.json longstring; wiki",
            H("b8 38 4c6f72656d20697073756d20646f6c6f722073697420616d65742c20636f6e7365637465747572206164697069736963696e6720656c6974"),
            RlpNode.Ascii("Lorem ipsum dolor sit amet, consectetur adipisicing elit"));
        Add("zero", "rlptest.json zero; wiki integer 0", H("80"), RlpNode.FromUInt64(0));
        Add("smallint 1", "rlptest.json smallint", H("01"), RlpNode.FromUInt64(1));
        Add("smallint2 16", "rlptest.json smallint2", H("10"), RlpNode.FromUInt64(16));
        Add("smallint3 79", "rlptest.json smallint3", H("4f"), RlpNode.FromUInt64(79));
        Add("smallint 15", "wiki integer 15", H("0f"), RlpNode.FromUInt64(15));
        Add("smallint4 127", "rlptest.json smallint4", H("7f"), RlpNode.FromUInt64(127));
        Add("mediumint1 128", "rlptest.json mediumint1", H("81 80"), RlpNode.FromUInt64(128));
        Add("mediumint2 1000", "rlptest.json mediumint2", H("82 03 e8"), RlpNode.FromUInt64(1000));
        Add("integer 1024", "wiki integer 1024", H("82 04 00"), RlpNode.FromUInt64(1024));
        Add("mediumint3 100000", "rlptest.json mediumint3", H("83 01 86 a0"), RlpNode.FromUInt64(100000));
        Add("mediumint4 (15-byte integer)", "rlptest.json mediumint4",
            H("8f 10 20 30 40 50 60 70 80 90 a0 b0 c0 d0 e0 f2"), Str("10 20 30 40 50 60 70 80 90 a0 b0 c0 d0 e0 f2"));
        Add("mediumint5 (28-byte integer)", "rlptest.json mediumint5",
            H("9c 01 00 02 00 03 00 04 00 05 00 06 00 07 00 08 00 09 00 0a 00 0b 00 0c 00 0d 00 0e 01"),
            Str("01 00 02 00 03 00 04 00 05 00 06 00 07 00 08 00 09 00 0a 00 0b 00 0c 00 0d 00 0e 01"));
        Add("bigint 2^256 (33-byte integer)", "rlptest.json bigint", H("a1 01 00*32"), Str("01 00*32"));
        Add("stringlist [dog, god, cat]", "rlptest.json stringlist",
            H("cc 83 64 6f 67 83 67 6f 64 83 63 61 74"),
            RlpNode.ListOf(RlpNode.Ascii("dog"), RlpNode.Ascii("god"), RlpNode.Ascii("cat")));
        Add("multilist [zw, [4], 1]", "rlptest.json multilist",
            H("c6 82 7a 77 c1 04 01"),
            RlpNode.ListOf(RlpNode.Ascii("zw"), RlpNode.ListOf(Str("04")), Str("01")));
        Add("shortListMax1 (11 strings, payload 55)", "rlptest.json shortListMax1",
            H("f7 84 61 73 64 66 84 71 77 65 72 84 7a 78 63 76 84 61 73 64 66 84 71 77 65 72 84 7a 78 63 76 84 61 73 64 66 84 71 77 65 72 84 7a 78 63 76 84 61 73 64 66 84 71 77 65 72"),
            RlpNode.ListOf(
                RlpNode.Ascii("asdf"), RlpNode.Ascii("qwer"), RlpNode.Ascii("zxcv"),
                RlpNode.Ascii("asdf"), RlpNode.Ascii("qwer"), RlpNode.Ascii("zxcv"),
                RlpNode.Ascii("asdf"), RlpNode.Ascii("qwer"), RlpNode.Ascii("zxcv"),
                RlpNode.Ascii("asdf"), RlpNode.Ascii("qwer")));
        Add("longList1 (4 x triple, payload 64)", "rlptest.json longList1",
            H($"f8 40 {pairHex}*4"), RlpNode.ListOf(pair, pair, pair, pair));
        Add("longList2 (32 x triple, payload 512)", "rlptest.json longList2",
            H($"f9 02 00 {pairHex}*32"), RlpNode.ListOf(Enumerable.Repeat(pair, 32)));
        Add("listsoflists [[[],[]],[]]", "rlptest.json listsoflists",
            H("c4 c2 c0 c0 c0"),
            RlpNode.ListOf(RlpNode.ListOf(RlpNode.EmptyList, RlpNode.EmptyList), RlpNode.EmptyList));
        Add("listsoflists2 (set-theoretic 3)", "rlptest.json listsoflists2; wiki",
            H("c7 c0 c1 c0 c3 c0 c1 c0"),
            RlpNode.ListOf(RlpNode.EmptyList, RlpNode.ListOf(RlpNode.EmptyList), RlpNode.ListOf(RlpNode.EmptyList, RlpNode.ListOf(RlpNode.EmptyList))));
        Add("dictTest1 (4 key/value pairs)", "rlptest.json dictTest1",
            H("ec ca 84 6b 65 79 31 84 76 61 6c 31 ca 84 6b 65 79 32 84 76 61 6c 32 ca 84 6b 65 79 33 84 76 61 6c 33 ca 84 6b 65 79 34 84 76 61 6c 34"),
            RlpNode.ListOf(KeyValue(1), KeyValue(2), KeyValue(3), KeyValue(4)));
        Add("empty list", "wiki", H("c0"), RlpNode.EmptyList);
        Add("[cat, dog]", "wiki", H("c8 83 63 61 74 83 64 6f 67"), RlpNode.ListOf(RlpNode.Ascii("cat"), RlpNode.Ascii("dog")));

        return rows;
    }

    // ================================================================ decode expectations: invalid inputs

    private static DecodeVector Bad(string name, string hex, RLPStatus status, int offset, bool normative = false) =>
        new(name, H(hex), RLPDecodeLimits.Default, status, offset, normative);

    private static DecodeVector Bad(string name, byte[] input, RLPStatus status, int offset, bool normative = false) =>
        new(name, input, RLPDecodeLimits.Default, status, offset, normative);

    private static readonly Lazy<IReadOnlyList<DecodeVector>> InvalidHandStatedLazy = new(BuildInvalidHandStated);

    /// <summary>
    /// Invalid inputs under default limits with hand-derived status and offset: every spec 12.2 row (names start
    /// with "spec12.2") plus adversarial rows. Rows that are only about precedence the spec leaves open live in
    /// <see cref="AmbiguousPrecedence"/>.
    /// </summary>
    public static IReadOnlyList<DecodeVector> InvalidHandStated => InvalidHandStatedLazy.Value;

    public static IEnumerable<object[]> InvalidHandStatedRows => AsRows(InvalidHandStated);

    private static IReadOnlyList<DecodeVector> BuildInvalidHandStated()
    {
        var rows = new List<DecodeVector>
        {
            // ---- spec 12.2, every row
            Bad("spec12.2 empty input", "", RLPStatus.EmptyInput, 0, true),
            Bad("spec12.2 81 7f", "81 7f", RLPStatus.NonCanonicalEncoding, 0),
            Bad("spec12.2 b8 37 + 55 bytes", "b8 37 aa*55", RLPStatus.NonCanonicalEncoding, 1),
            Bad("spec12.2 f8 37 + 55 valid child bytes", "f8 37 01*55", RLPStatus.NonCanonicalEncoding, 1),
            Bad("spec12.2 b9 00 38 + 56 bytes", "b9 00 38 aa*56", RLPStatus.NonCanonicalEncoding, 1),
            Bad("spec12.2 f9 00 38 + 56 valid child bytes", "f9 00 38 01*56", RLPStatus.NonCanonicalEncoding, 1),
            Bad("spec12.2 b8", "b8", RLPStatus.TruncatedInput, 1, true),
            Bad("spec12.2 b8 38 (no payload)", "b8 38", RLPStatus.TruncatedInput, 2, true),
            Bad("spec12.2 c1 82", "c1 82", RLPStatus.InvalidListBoundary, 1),
            Bad("spec12.2 80 00", "80 00", RLPStatus.TrailingData, 1, true),
            Bad("spec12.2 c0 00", "c0 00", RLPStatus.TrailingData, 1, true),
            Bad("spec12.2 bf ff*8", "bf ff*8", RLPStatus.LengthOverflow, 1),
            Bad("spec12.2 ff ff*8", "ff ff*8", RLPStatus.LengthOverflow, 1),

            // ---- single-byte strings written long or short
            Bad("81 00", "81 00", RLPStatus.NonCanonicalEncoding, 0),
            Bad("81 01", "81 01", RLPStatus.NonCanonicalEncoding, 0),
            Bad("81 7e", "81 7e", RLPStatus.NonCanonicalEncoding, 0),
            Bad("81 00 followed by trailing byte: header error wins over trailing", "81 00 00", RLPStatus.NonCanonicalEncoding, 0),
            Bad("81 7f followed by trailing byte", "81 7f 00", RLPStatus.NonCanonicalEncoding, 0),
            Bad("b8 01 ff (long form for length 1)", "b8 01 ff", RLPStatus.NonCanonicalEncoding, 1),
            Bad("b8 10 + 16 bytes (long form for length 16)", "b8 10 00 11 22 33 44 55 66 77 88 99 aa bb cc dd ee ff", RLPStatus.NonCanonicalEncoding, 1),
            Bad("b8 36 + 54 bytes", "b8 36 aa*54", RLPStatus.NonCanonicalEncoding, 1),
            Bad("b8 00 + 56 bytes (length zero)", "b8 00 aa*56", RLPStatus.NonCanonicalEncoding, 1),
            Bad("b9 00 ff + 255 bytes (fits one length byte)", "b9 00 ff aa*255", RLPStatus.NonCanonicalEncoding, 1),
            Bad("b9 00 37 + 55 bytes", "b9 00 37 aa*55", RLPStatus.NonCanonicalEncoding, 1),
            Bad("f8 01 80 (go-ethereum wrongSizeList)", "f8 01 80", RLPStatus.NonCanonicalEncoding, 1),
            Bad("f8 01 00 (go-ethereum wrongSizeList2)", "f8 01 00", RLPStatus.NonCanonicalEncoding, 1),
            Bad("f8 00", "f8 00", RLPStatus.NonCanonicalEncoding, 1),

            // ---- huge or overflowing lengths
            Bad("bb 7f ff ff ff (int.MaxValue, no payload)", "bb 7f ff ff ff", RLPStatus.TruncatedInput, 5, true),
            Bad("bb 80 00 00 00 (int.MaxValue + 1)", "bb 80 00 00 00", RLPStatus.LengthOverflow, 1),
            Bad("bb ff ff ff ff", "bb ff ff ff ff", RLPStatus.LengthOverflow, 1),
            Bad("fb 7f ff ff ff (int.MaxValue list, no payload)", "fb 7f ff ff ff", RLPStatus.TruncatedInput, 5, true),
            Bad("fb 80 00 00 00", "fb 80 00 00 00", RLPStatus.LengthOverflow, 1),
            Bad("bf 7f ff*7", "bf 7f ff*7", RLPStatus.LengthOverflow, 1),
            Bad("ff 80 00*7", "ff 80 00*7", RLPStatus.LengthOverflow, 1),
            Bad("bf 0f 00 00 00 00 00 00 02 11 11 (go-ethereum int32Overflow)", "bf 0f 00 00 00 00 00 00 02 11 11", RLPStatus.LengthOverflow, 1),
            Bad("ff 0f 00 00 00 00 00 00 02 11 11 (go-ethereum int32Overflow2)", "ff 0f 00 00 00 00 00 00 02 11 11", RLPStatus.LengthOverflow, 1),
            Bad("bf 00 ff*7 (leading zero, not overflow)", "bf 00 ff*7 aa*4", RLPStatus.NonCanonicalEncoding, 1),
            Bad("ff 00*8 (all-zero length)", "ff 00*8 01*4", RLPStatus.NonCanonicalEncoding, 1),
            Bad("ff 00 00 00 00 00 00 00 38 + payload", "ff 00*7 38 01*56", RLPStatus.NonCanonicalEncoding, 1),
            Bad("ff + 8-byte 0x7fffffff (leading zero beats overflow analysis)", "ff 00 00 00 00 7f ff ff ff 01*4", RLPStatus.NonCanonicalEncoding, 1),
            Bad("ff + 8-byte 0x80000000", "ff 00 00 00 00 80 00 00 00 01*4", RLPStatus.NonCanonicalEncoding, 1),
            Bad("ff + 8-byte 0xffffffff", "ff 00 00 00 00 ff ff ff ff 01*4", RLPStatus.NonCanonicalEncoding, 1),
            Bad("bf + 8-byte 0x80000000", "bf 00 00 00 00 80 00 00 00 aa*4", RLPStatus.NonCanonicalEncoding, 1),
            Bad("b8 ff (length 255, no payload)", "b8 ff", RLPStatus.TruncatedInput, 2, true),
            Bad("b9 ff ff (length 65535, no payload)", "b9 ff ff", RLPStatus.TruncatedInput, 3, true),
            Bad("ba ff ff ff (length 16M, no payload)", "ba ff ff ff", RLPStatus.TruncatedInput, 4, true),
            Bad("f8 ff (list length 255, no payload)", "f8 ff", RLPStatus.TruncatedInput, 2, true),

            // ---- truncated payloads and prefixes at the last byte
            Bad("81 (payload byte missing)", "81", RLPStatus.TruncatedInput, 1, true),
            Bad("82 01", "82 01", RLPStatus.TruncatedInput, 2, true),
            Bad("83 64 6f", "83 64 6f", RLPStatus.TruncatedInput, 3, true),
            Bad("b7 + 54 bytes", "b7 aa*54", RLPStatus.TruncatedInput, 55, true),
            Bad("b8 38 + 55 bytes", "b8 38 aa*55", RLPStatus.TruncatedInput, 57, true),
            Bad("b9 01 00 + 255 bytes", "b9 01 00 aa*255", RLPStatus.TruncatedInput, 258, true),
            Bad("f7 + 54 bytes", "f7 01*54", RLPStatus.TruncatedInput, 55, true),
            Bad("f8 38 + 55 bytes", "f8 38 01*55", RLPStatus.TruncatedInput, 57, true),
            Bad("c1 (list payload missing)", "c1", RLPStatus.TruncatedInput, 1, true),
            Bad("c2 80 (list one byte short)", "c2 80", RLPStatus.TruncatedInput, 2, true),
            Bad("c3 82 01 (root list one byte short, child also short)", "c3 82 01", RLPStatus.TruncatedInput, 3, true),
            Bad("c8 83 63 61 74 83 64 6f (list one byte short)", "c8 83 63 61 74 83 64 6f", RLPStatus.TruncatedInput, 8, true),
            Bad("b9 (length bytes missing)", "b9", RLPStatus.TruncatedInput, 1, true),
            Bad("b9 01 (one of two length bytes)", "b9 01", RLPStatus.TruncatedInput, 2, true),
            Bad("b9 01 00 (header only)", "b9 01 00", RLPStatus.TruncatedInput, 3, true),
            Bad("ba 01 00 (two of three length bytes)", "ba 01 00", RLPStatus.TruncatedInput, 3, true),
            Bad("f9 01", "f9 01", RLPStatus.TruncatedInput, 2, true),
            Bad("f8", "f8", RLPStatus.TruncatedInput, 1, true),
            Bad("fa 01 00", "fa 01 00", RLPStatus.TruncatedInput, 3, true),
            Bad("ff", "ff", RLPStatus.TruncatedInput, 1, true),
            Bad("ff 01 02 03", "ff 01 02 03", RLPStatus.TruncatedInput, 4, true),
            Bad("bf 01 + 6 zeros (7 of 8 length bytes)", "bf 01 00*6", RLPStatus.TruncatedInput, 8, true),

            // ---- trailing data
            Bad("00 00", "00 00", RLPStatus.TrailingData, 1, true),
            Bad("7f 7f", "7f 7f", RLPStatus.TrailingData, 1, true),
            Bad("01 02", "01 02", RLPStatus.TrailingData, 1, true),
            Bad("80 80", "80 80", RLPStatus.TrailingData, 1, true),
            Bad("c0 c0", "c0 c0", RLPStatus.TrailingData, 1, true),
            Bad("dog + trailing 80", "83 64 6f 67 80", RLPStatus.TrailingData, 4, true),
            Bad("[cat, dog] + trailing 00", "c8 83 63 61 74 83 64 6f 67 00", RLPStatus.TrailingData, 9, true),
            Bad("56-byte string + trailing 00", "b8 38 aa*56 00", RLPStatus.TrailingData, 58, true),
            Bad("c1 80 80", "c1 80 80", RLPStatus.TrailingData, 2, true),
            Bad("trailing wins over a malformed child: c1 82 00 00", "c1 82 00 00", RLPStatus.TrailingData, 2, true),
            Bad("trailing wins over a truncated child: c1 81 7f", "c1 81 7f", RLPStatus.TrailingData, 2, true),
            Bad("trailing wins over a non-canonical child: c1 81 00 00", "c1 81 00 00", RLPStatus.TrailingData, 2, true),

            // ---- child crossing its parent (but fitting the root or a grandparent)
            Bad("child past its list, fits root: c4 c1 82 80 80", "c4 c1 82 80 80", RLPStatus.InvalidListBoundary, 2),
            Bad("grandchild past its list: c5 c2 83 01 02 03", "c5 c2 83 01 02 03", RLPStatus.InvalidListBoundary, 2),
            Bad("child list past its parent: c4 c1 c2 01 01", "c4 c1 c2 01 01", RLPStatus.InvalidListBoundary, 2),
            Bad("second sibling past root: c3 80 82 01", "c3 80 82 01", RLPStatus.InvalidListBoundary, 2),
            Bad("long string child past list: c3 b8 38 00", "c3 b8 38 00", RLPStatus.InvalidListBoundary, 1),
            Bad("long list child past list: c5 f8 38 01 01 01", "c5 f8 38 01 01 01", RLPStatus.InvalidListBoundary, 1),
            Bad("child header complete, payload missing: c2 b8 38", "c2 b8 38", RLPStatus.InvalidListBoundary, 1),
            Bad("length byte outside the child's list: c4 c1 f8 38 00", "c4 c1 f8 38 00", RLPStatus.InvalidListBoundary, 2),
            Bad("nested 81 00: c2 81 00", "c2 81 00", RLPStatus.NonCanonicalEncoding, 1),
            Bad("nested b8 01 aa: c3 b8 01 aa", "c3 b8 01 aa", RLPStatus.NonCanonicalEncoding, 2),
            Bad("nested b9 00 38 aa: c4 b9 00 38 aa", "c4 b9 00 38 aa", RLPStatus.NonCanonicalEncoding, 2),
            Bad("nested bf ff*8: c9 bf ff*8", "c9 bf ff*8", RLPStatus.LengthOverflow, 2),
            Bad("nested second child bad: c4 80 81 7f 80", "c4 80 81 7f 80", RLPStatus.NonCanonicalEncoding, 2),
        };

        // ---- leading-zero length bytes at every length-of-length 1..8, strings and lists
        for (var lengthOfLength = 1; lengthOfLength <= 8; lengthOfLength++)
        {
            // Length bytes 00 .. 38 (value 56 when it has more than one byte, so only the leading zero is wrong).
            var zeroThen38 = lengthOfLength == 1 ? new byte[] { 0x00 } : Cat(Rep(0x00, lengthOfLength - 1), new byte[] { 0x38 });
            var zeroThenFf = Cat(new byte[] { 0x00 }, Rep(0xff, lengthOfLength - 1));
            var stringPrefix = new[] { (byte)(0xb7 + lengthOfLength) };
            var listPrefix = new[] { (byte)(0xf7 + lengthOfLength) };
            rows.Add(Bad($"leading-zero length, string, length-of-length {lengthOfLength}", Cat(stringPrefix, zeroThen38, Rep(0xaa, 56)), RLPStatus.NonCanonicalEncoding, 1));
            rows.Add(Bad($"leading-zero length, list, length-of-length {lengthOfLength}", Cat(listPrefix, zeroThen38, Rep(0x01, 56)), RLPStatus.NonCanonicalEncoding, 1));
            rows.Add(Bad($"leading-zero then 0xff.. length, string, length-of-length {lengthOfLength}", Cat(stringPrefix, zeroThenFf, Rep(0xaa, 8)), RLPStatus.NonCanonicalEncoding, 1));
        }

        // ---- first-byte-nonzero lengths of 5..8 bytes are always >= 2^32
        for (var lengthOfLength = 5; lengthOfLength <= 8; lengthOfLength++)
        {
            rows.Add(Bad($"length 2^(8*(n-1)), string, length-of-length {lengthOfLength}", Cat([(byte)(0xb7 + lengthOfLength), 0x01], Rep(0x00, lengthOfLength - 1)), RLPStatus.LengthOverflow, 1));
            rows.Add(Bad($"length 2^(8*(n-1)), list, length-of-length {lengthOfLength}", Cat([(byte)(0xf7 + lengthOfLength), 0x01], Rep(0x00, lengthOfLength - 1)), RLPStatus.LengthOverflow, 1));
            rows.Add(Bad($"all-ff length, string, length-of-length {lengthOfLength}", Cat([(byte)(0xb7 + lengthOfLength)], Rep(0xff, lengthOfLength)), RLPStatus.LengthOverflow, 1));
            rows.Add(Bad($"all-ff length, list, length-of-length {lengthOfLength}", Cat([(byte)(0xf7 + lengthOfLength)], Rep(0xff, lengthOfLength)), RLPStatus.LengthOverflow, 1));
        }

        // ---- truncation at every byte of the longest header (length-of-length 8)
        var longestStringHeader = H("bf 01 00 00 00 00 00 00 00");
        var longestListHeader = H("ff 01 00 00 00 00 00 00 00");
        for (var present = 1; present <= 8; present++)
        {
            rows.Add(Bad($"bf 01.. header truncated after {present} byte(s)", longestStringHeader[..present], RLPStatus.TruncatedInput, present, true));
            rows.Add(Bad($"ff 01.. header truncated after {present} byte(s)", longestListHeader[..present], RLPStatus.TruncatedInput, present, true));
        }

        // ---- items sitting after a valid header inside the root list
        rows.Add(Bad("complete 9-byte header, no payload (overflow)", longestStringHeader, RLPStatus.LengthOverflow, 1));

        return rows;
    }

    // ================================================================ decode expectations: nested invalid

    private static readonly Lazy<IReadOnlyList<DecodeVector>> InvalidNestedDerivedLazy = new(BuildInvalidNestedDerived);

    /// <summary>
    /// Every structural-error row of <see cref="InvalidHandStated"/> wrapped in one and in two enclosing lists. The
    /// expectation comes from a closed rule, not from the oracle: the inner status is kept except that root
    /// truncation becomes InvalidListBoundary, and the offset moves by the wrapper header length (a nested
    /// boundary error sits at the bad child's prefix, i.e. exactly the wrapper header length). Rows that stop being
    /// errors when nested (EmptyInput, TrailingData) and limit-dependent rows are excluded.
    /// </summary>
    public static IReadOnlyList<DecodeVector> InvalidNestedDerived => InvalidNestedDerivedLazy.Value;

    public static IEnumerable<object[]> InvalidNestedDerivedRows => AsRows(InvalidNestedDerived);

    private static IReadOnlyList<DecodeVector> BuildInvalidNestedDerived()
    {
        var rows = new List<DecodeVector>();
        foreach (var inner in InvalidHandStated)
        {
            if (inner.Status is not (RLPStatus.TruncatedInput or RLPStatus.NonCanonicalEncoding or RLPStatus.LengthOverflow or RLPStatus.InvalidListBoundary))
            {
                continue;
            }

            var once = WrapInList(inner.Input);
            var shiftOnce = once.Length - inner.Input.Length;
            var onceStatus = inner.Status == RLPStatus.TruncatedInput ? RLPStatus.InvalidListBoundary : inner.Status;
            var onceOffset = inner.Status == RLPStatus.TruncatedInput ? shiftOnce : inner.Offset + shiftOnce;
            rows.Add(new DecodeVector($"nested x1: {inner.Name}", once, RLPDecodeLimits.Default, onceStatus, onceOffset, false));

            var twice = WrapInList(once);
            var shiftTwice = twice.Length - once.Length;
            rows.Add(new DecodeVector($"nested x2: {inner.Name}", twice, RLPDecodeLimits.Default, onceStatus, onceOffset + shiftTwice, false));
        }

        return rows;
    }

    // ================================================================ decode expectations: limits

    private static readonly Lazy<IReadOnlyList<DecodeVector>> LimitVectorsLazy = new(BuildLimitVectors);

    /// <summary>
    /// Resource-limit rows (success and failure) at limit-1, limit and limit+1 for MaxEncodedBytes, MaxListDepth
    /// and MaxItems, invalid limit values, and precedence of limits over content.
    /// </summary>
    public static IReadOnlyList<DecodeVector> LimitVectors => LimitVectorsLazy.Value;

    public static IEnumerable<object[]> LimitVectorsRows => AsRows(LimitVectors);

    private static IReadOnlyList<DecodeVector> BuildLimitVectors()
    {
        const int Big = 1 << 20;
        var rows = new List<DecodeVector>();

        void Add(string name, byte[] input, int maxBytes, int maxDepth, int maxItems, RLPStatus status, int offset, bool normative) =>
            rows.Add(new DecodeVector(name, input, new RLPDecodeLimits(maxBytes, maxDepth, maxItems), status, offset, normative));

        // ---- invalid limit values are reported before the input is looked at
        var invalidLimits = new (string Name, int Bytes, int Depth, int Items)[]
        {
            ("MaxEncodedBytes 0", 0, 32, 8192),
            ("MaxEncodedBytes -1", -1, 32, 8192),
            ("MaxEncodedBytes int.MinValue", int.MinValue, 32, 8192),
            ("MaxListDepth 0", Big, 0, 8192),
            ("MaxListDepth -1", Big, -1, 8192),
            ("MaxListDepth 65 (hard cap + 1)", Big, 65, 8192),
            ("MaxListDepth int.MaxValue", Big, int.MaxValue, 8192),
            ("MaxItems 0", Big, 32, 0),
            ("MaxItems -5", Big, 32, -5),
            ("all zero (default struct)", 0, 0, 0),
        };
        foreach (var (name, bytes, depth, items) in invalidLimits)
        {
            Add($"invalid limits ({name}) with empty input", [], bytes, depth, items, RLPStatus.InvalidArgument, -1, true);
            Add($"invalid limits ({name}) with valid input", H("80"), bytes, depth, items, RLPStatus.InvalidArgument, -1, true);
            Add($"invalid limits ({name}) with malformed input", H("81 7f"), bytes, depth, items, RLPStatus.InvalidArgument, -1, true);
        }

        Add("limits at the extremes are valid: 1 byte, depth 64, 1 item, input 80", H("80"), 1, 64, 1, RLPStatus.Success, 0, true);
        Add("limits at the extremes are valid: int.MaxValue bytes", H("80"), int.MaxValue, 1, 1, RLPStatus.Success, 0, true);
        Add("scalar-only document with depth limit 1", H("83 64 6f 67"), Big, 1, 1, RLPStatus.Success, 0, true);

        // ---- MaxEncodedBytes: before content, empty input reported once within budget
        var dog = H("83 64 6f 67");
        Add("size limit-1 (3 of 4 bytes) -> TooLarge", dog, 3, 32, 8192, RLPStatus.TooLarge, 3, false);
        Add("size limit (4 of 4 bytes) -> Success", dog, 4, 32, 8192, RLPStatus.Success, 0, true);
        Add("size limit+1 (4 of 5 bytes) -> Success", dog, 5, 32, 8192, RLPStatus.Success, 0, true);
        Add("size limit checked before content (malformed, 4 bytes, limit 3)", H("81 7f 00 00"), 3, 32, 8192, RLPStatus.TooLarge, 3, false);
        Add("size limit checked before content (truncated, 2 bytes, limit 1)", H("83 64"), 1, 32, 8192, RLPStatus.TooLarge, 1, false);
        Add("empty input within size budget", [], 1, 32, 8192, RLPStatus.EmptyInput, 0, true);
        Add("one byte limit with one byte input", H("00"), 1, 32, 8192, RLPStatus.Success, 0, true);
        Add("one byte limit with two byte input", H("80 00"), 1, 32, 8192, RLPStatus.TooLarge, 1, false);

        // ---- MaxListDepth at limit-1, limit, limit+1 (offset = prefix of the first list over the limit)
        foreach (var limit in new[] { 1, 2, 3, 31, 32, 33, 63, 64 })
        {
            var within = NestedLists(limit);
            var over = NestedLists(limit + 1);
            if (limit > 1)
            {
                var below = NestedLists(limit - 1);
                Add($"depth limit {limit}: {limit - 1} nested lists", below.Bytes, Big, limit, 8192, RLPStatus.Success, 0, true);
            }

            Add($"depth limit {limit}: {limit} nested lists", within.Bytes, Big, limit, 8192, RLPStatus.Success, 0, true);
            Add($"depth limit {limit}: {limit + 1} nested lists", over.Bytes, Big, limit, 8192, RLPStatus.TooDeep, over.ListOffsets[limit], false);
        }

        var deep = NestedLists(5000);
        Add("5000 nested lists with depth limit 64 stops at the 65th list", deep.Bytes, Big, 64, 8192, RLPStatus.TooDeep, deep.ListOffsets[64], false);
        Add("5000 nested lists with depth limit 1 stops at the 2nd list", deep.Bytes, Big, 1, 8192, RLPStatus.TooDeep, deep.ListOffsets[1], false);
        Add("scalar root never exceeds the depth limit", H("b8 38 aa*56"), Big, 1, 8192, RLPStatus.Success, 0, true);
        Add("empty list is depth 1", H("c0"), Big, 1, 8192, RLPStatus.Success, 0, true);

        // ---- MaxItems at limit-1, limit, limit+1 using literal children (items = children + 1)
        foreach (var limit in new[] { 1, 2, 3, 55, 56, 57 })
        {
            foreach (var children in new[] { limit - 2, limit - 1, limit })
            {
                if (children < 0)
                {
                    continue;
                }

                var input = WrapInList(Rep(0x01, children));
                var headerLength = input.Length - children;
                var fits = children + 1 <= limit;
                Add($"item limit {limit}: list with {children} children ({children + 1} items)", input, Big, 32, limit,
                    fits ? RLPStatus.Success : RLPStatus.TooManyItems, fits ? 0 : headerLength + limit - 1, fits);
            }
        }

        Add("item limit 1: a lone string is one item", H("83 64 6f 67"), Big, 32, 1, RLPStatus.Success, 0, true);
        Add("item limit 3: [[], []] is 3 items", H("c2 c0 c0"), Big, 32, 3, RLPStatus.Success, 0, true);
        Add("item limit 2: [[], []] is 3 items", H("c2 c0 c0"), Big, 32, 2, RLPStatus.TooManyItems, 2, false);
        Add("item limit 3: [[80, 80]] is 4 items", H("c3 c2 80 80"), Big, 32, 3, RLPStatus.TooManyItems, 3, false);
        Add("item limit 4: [[80, 80]] is 4 items", H("c3 c2 80 80"), Big, 32, 4, RLPStatus.Success, 0, true);
        Add("item limit 8192: 8191 children is exactly the default budget", WrapInList(Rep(0x01, 8191)), Big, 32, 8192, RLPStatus.Success, 0, true);
        Add("item limit 8192: 8192 children", WrapInList(Rep(0x01, 8192)), Big, 32, 8192, RLPStatus.TooManyItems, 3 + 8191, false);
        Add("item limit 8192: 9000 children", WrapInList(Rep(0x01, 9000)), Big, 32, 8192, RLPStatus.TooManyItems, 3 + 8191, false);

        // ---- first error in traversal order wins (spec 5.3 rule 6)
        Add("depth error precedes a later boundary error (depth limit 2)", H("c5 c1 c0 83 01 02"), Big, 2, 8192, RLPStatus.TooDeep, 2, false);
        Add("boundary error when the depth limit is generous", H("c5 c1 c0 83 01 02"), Big, 32, 8192, RLPStatus.InvalidListBoundary, 3, false);

        return rows;
    }

    // ================================================================ decode expectations: oracle reading of open points

    private static readonly Lazy<IReadOnlyList<DecodeVector>> AmbiguousPrecedenceLazy = new(BuildAmbiguousPrecedence);

    /// <summary>
    /// Rows where the specification text can be read two ways; these record the oracle's reading (see the
    /// <see cref="RlpReference"/> remarks). An implementation that disagrees should report it, not silently differ.
    /// </summary>
    public static IReadOnlyList<DecodeVector> AmbiguousPrecedence => AmbiguousPrecedenceLazy.Value;

    public static IEnumerable<object[]> AmbiguousPrecedenceRows => AsRows(AmbiguousPrecedence);

    private static IReadOnlyList<DecodeVector> BuildAmbiguousPrecedence()
    {
        const int Big = 1 << 20;
        var rows = new List<DecodeVector>
        {
            Bad("long form below 56 with the payload missing: non-canonical wins over truncation (b8 37)", "b8 37", RLPStatus.NonCanonicalEncoding, 1),
            Bad("long list below 56 with the payload missing (f8 37)", "f8 37", RLPStatus.NonCanonicalEncoding, 1),
            Bad("leading-zero length with the payload missing (b9 00 38)", "b9 00 38", RLPStatus.NonCanonicalEncoding, 1),
            Bad("zero length byte with the payload missing (b8 00)", "b8 00", RLPStatus.NonCanonicalEncoding, 1),
            Bad("zero length byte with the payload missing (f8 00 is a spec-order case too)", "f8 00", RLPStatus.NonCanonicalEncoding, 1),
            Bad("missing length byte beats leading-zero check at the root (b9 00)", "b9 00", RLPStatus.TruncatedInput, 2, true),
            Bad("missing length bytes beat leading-zero check at the root (bf 00*7)", "bf 00*7", RLPStatus.TruncatedInput, 8, true),
            Bad("length byte outside the child's list beats leading-zero check (c4 c1 f8 00 00)", "c4 c1 f8 00 00", RLPStatus.InvalidListBoundary, 2),
            Bad("nested child with missing payload and a length below 56 (c3 b8 37 aa)", "c3 b8 37 aa", RLPStatus.NonCanonicalEncoding, 2),
            new DecodeVector("header error beats the item budget (c1 82 with MaxItems 1)", H("c1 82"), new RLPDecodeLimits(Big, 32, 1), RLPStatus.InvalidListBoundary, 1, false),
            new DecodeVector("item budget beats depth for the same item (c1 c0, depth 1, items 1)", H("c1 c0"), new RLPDecodeLimits(Big, 1, 1), RLPStatus.TooManyItems, 1, false),
            new DecodeVector("item budget is checked when the item is discovered, before a later boundary error (c3 80 80 82, items 3)", H("c3 80 80 82"), new RLPDecodeLimits(Big, 32, 3), RLPStatus.InvalidListBoundary, 3, false),
        };

        // Truncated header prefixes whose first length byte is zero.
        var zeroHeader = H("bf 00 00 00 00 00 00 00 00");
        for (var present = 2; present <= 8; present++)
        {
            rows.Add(Bad($"bf 00.. header truncated after {present} byte(s): truncation wins over leading zero", zeroHeader[..present], RLPStatus.TruncatedInput, present, true));
        }

        return rows;
    }

    // ================================================================ one-byte families

    private static readonly Lazy<IReadOnlyList<DecodeVector>> SingleByteInputsLazy = new(() =>
        Enumerable.Range(0, 256).Select(b =>
        {
            var valid = b < 0x80 || b == 0x80 || b == 0xc0;
            return Bad($"single byte {b:x2}", [(byte)b], valid ? RLPStatus.Success : RLPStatus.TruncatedInput, valid ? 0 : 1, true);
        }).ToArray());

    /// <summary>
    /// Every one-byte input 00..ff: valid for 00..7f, 80 and c0; every other prefix is a header with missing
    /// bytes (TruncatedInput at offset 1). Malformedness is never an unknown prefix.
    /// </summary>
    public static IReadOnlyList<DecodeVector> SingleByteInputs => SingleByteInputsLazy.Value;

    private static readonly Lazy<IReadOnlyList<DecodeVector>> ListWithOneByteChildLazy = new(() =>
        Enumerable.Range(0, 256).Select(b =>
        {
            var valid = b < 0x80 || b == 0x80 || b == 0xc0;
            return Bad($"c1 {b:x2}", [0xc1, (byte)b], valid ? RLPStatus.Success : RLPStatus.InvalidListBoundary, valid ? 0 : 1);
        }).ToArray());

    /// <summary>
    /// <c>c1 xx</c> for every xx: a list whose single payload byte is a child prefix. Valid only for a complete
    /// one-byte child (00..80 and c0); every other prefix needs bytes beyond the list (InvalidListBoundary).
    /// </summary>
    public static IReadOnlyList<DecodeVector> ListWithOneByteChild => ListWithOneByteChildLazy.Value;

    // ================================================================ integer accessor expectations

    private static readonly Lazy<IReadOnlyList<IntegerVector>> IntegerLazy = new(BuildInteger);

    /// <summary>
    /// Integer rows (spec 2.2 and 12.2): encoded document, expected value or expected status. Each document is valid
    /// generic RLP; only the integer interpretation fails.
    /// </summary>
    public static IReadOnlyList<IntegerVector> Integer => IntegerLazy.Value;

    public static IEnumerable<object[]> IntegerRows => AsRows(Integer);

    private static IReadOnlyList<IntegerVector> BuildInteger()
    {
        var rows = new List<IntegerVector>();

        void Ok(string name, string hex, ulong value) => rows.Add(new IntegerVector(name, H(hex), value, RLPStatus.Success));
        void Fail(string name, string hex, RLPStatus status) => rows.Add(new IntegerVector(name, H(hex), 0, status));

        Ok("empty string is zero", "80", 0);
        Ok("1", "01", 1);
        Ok("127", "7f", 127);
        Ok("128", "81 80", 128);
        Ok("255", "81 ff", 255);
        Ok("256", "82 01 00", 256);
        Ok("1024", "82 04 00", 1024);
        Ok("65535", "82 ff ff", 0xffff);
        Ok("2^32", "85 01 00 00 00 00", 1UL << 32);
        Ok("2^56", "88 01 00 00 00 00 00 00 00", 1UL << 56);
        Ok("0xdeadbeef", "84 de ad be ef", 0xdeadbeef);
        Ok("ulong.MaxValue", "88 ff*8", ulong.MaxValue);
        Ok("0x8000000000000000", "88 80 00 00 00 00 00 00 00", 1UL << 63);
        for (var width = 1; width <= 8; width++)
        {
            var value = width == 8 ? ulong.MaxValue : (1UL << (8 * width)) - 1;
            Ok($"all-ff magnitude of {width} byte(s)", $"{0x80 + width:x2} ff*{width}", value);
        }

        Fail("raw string 00 is not an integer", "00", RLPStatus.NonCanonicalInteger);
        Fail("82 00 01 leading zero", "82 00 01", RLPStatus.NonCanonicalInteger);
        Fail("82 00 00", "82 00 00", RLPStatus.NonCanonicalInteger);
        Fail("83 00 00 01", "83 00 00 01", RLPStatus.NonCanonicalInteger);
        Fail("88 00 01 02 03 04 05 06 07 (8 bytes, leading zero)", "88 00 01 02 03 04 05 06 07", RLPStatus.NonCanonicalInteger);
        Fail("89 + 9 nonzero bytes", "89 01 02 03 04 05 06 07 08 09", RLPStatus.IntegerOverflow);
        Fail("89 01 00*8 (2^64)", "89 01 00*8", RLPStatus.IntegerOverflow);
        Fail("89 ff*9", "89 ff*9", RLPStatus.IntegerOverflow);
        Fail("56-byte string with nonzero first byte", "b8 38 01*56", RLPStatus.IntegerOverflow);
        Fail("empty list", "c0", RLPStatus.WrongKind);
        Fail("list [01]", "c1 01", RLPStatus.WrongKind);
        Fail("list [[]]", "c1 c0", RLPStatus.WrongKind);
        Fail("list [cat, dog]", "c8 83 63 61 74 83 64 6f 67", RLPStatus.WrongKind);

        return rows;
    }

    private static readonly Lazy<IReadOnlyList<IntegerVector>> AmbiguousIntegerLazy = new(() =>
    [
        new IntegerVector("9-byte string with a leading zero (leading-zero rule first)", H("89 00 01 02 03 04 05 06 07 08"), 0, RLPStatus.NonCanonicalInteger),
        new IntegerVector("56 zero bytes (leading-zero rule first)", H("b8 38 00*56"), 0, RLPStatus.NonCanonicalInteger),
    ]);

    /// <summary>
    /// Integer rows where both the leading-zero rule and the width rule apply; the spec gives no order. The oracle
    /// applies the leading-zero rule first.
    /// </summary>
    public static IReadOnlyList<IntegerVector> AmbiguousInteger => AmbiguousIntegerLazy.Value;

    public static IEnumerable<object[]> AmbiguousIntegerRows => AsRows(AmbiguousInteger);
}
