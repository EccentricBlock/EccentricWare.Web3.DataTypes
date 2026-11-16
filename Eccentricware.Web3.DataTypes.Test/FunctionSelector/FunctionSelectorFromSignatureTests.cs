using System.Reflection;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// FromSignature (string, UTF-16 span, UTF-8 span) against vectors from an independent Keccak-256 reference
/// implementation (python3), plus the published Erc20/Erc721 constants and the allocation behaviour of the char overload.
/// </summary>
[TestClass]
public sealed class FunctionSelectorFromSignatureTests
{
    private static void AssertAllOverloads(uint expected, string signature, string label)
    {
        Assert.AreEqual(expected, FunctionSelector.FromSignature(signature).Value, "string: " + label);
        Assert.AreEqual(expected, FunctionSelector.FromSignature(signature.AsSpan()).Value, "chars: " + label);
        Assert.AreEqual(expected, FunctionSelector.FromSignature(Encoding.UTF8.GetBytes(signature)).Value, "utf8: " + label);
    }

    // ----------------- known-answer vectors -----------------

    [TestMethod]
    [DataRow("", 0xc5d24601u)]                                    // Keccak-256(""), not SHA3-256 (a7ffc6f8...), and not zero
    [DataRow("a", 0x3ac22516u)]
    [DataRow("f()", 0x26121ff0u)]
    [DataRow("transfer(address,uint256)", 0xa9059cbbu)]
    [DataRow("Transfer(address,uint256)", 0x69ca02ddu)]           // case sensitive
    [DataRow(" transfer(address,uint256)", 0x1e7a7538u)]          // whitespace is part of the signature
    [DataRow("transfer(address,uint256) ", 0x181f64d9u)]
    [DataRow("transfer(address, uint256)", 0x9d61d234u)]
    [DataRow("exactInputSingle((address,address,uint24,address,uint256,uint256,uint256,uint160))", 0x414bf389u)]
    [DataRow("f(\0)", 0xb01e7749u)]                               // embedded NUL is hashed, not a terminator
    [DataRow("f(\u00E9)", 0x2a842acfu)]                           // 2-byte UTF-8
    [DataRow("f(\u20AC)", 0x7ad9cddau)]                           // 3-byte UTF-8
    [DataRow("f(\U0001F600)", 0x008d83b4u)]                       // surrogate pair => 4-byte UTF-8
    [DataRow("\u8F6C\u8D26(\u5730\u5740,uint256)", 0x2ab8d872u)]
    public void FromSignature_MatchesIndependentVectors_ForEveryOverload(string signature, uint expected)
        => AssertAllOverloads(expected, signature, signature);

    // Vectors from independent reference implementation: Keccak-256 absorbs 136-byte blocks, so lengths around
    // 135/136/137 (single-byte padding, exact block, spill into a second block) and 271..273 are the padding edge cases.
    [TestMethod]
    [DataRow(1, 0x3ac22516u)]
    [DataRow(134, 0xe5de5653u)]
    [DataRow(135, 0x34367dc2u)]
    [DataRow(136, 0xa6c4d403u)]
    [DataRow(137, 0xd869f639u)]
    [DataRow(255, 0xd44e86b5u)]
    [DataRow(256, 0x1daa7034u)]
    [DataRow(257, 0xf32e95e2u)]
    [DataRow(271, 0x132f47efu)]
    [DataRow(272, 0xcf7fcd4fu)]
    [DataRow(273, 0x5a7b8187u)]
    [DataRow(512, 0x8089f7e5u)]
    public void FromSignature_RepeatedAscii_MatchesVectors_AtKeccakBlockAndBufferBoundaries(int length, uint expected)
        => AssertAllOverloads(expected, new string('a', length), "len=" + length);

    // "f(" + 'a' * (L - 3) + ")" has exactly L UTF-8 bytes; 256 is the last stackalloc size, 257 the first pooled one.
    [TestMethod]
    [DataRow(135, 0x35eed85au)]
    [DataRow(136, 0xc0accde9u)]
    [DataRow(137, 0x0ce15b3bu)]
    [DataRow(255, 0xcf87694bu)]
    [DataRow(256, 0x4b68033cu)]
    [DataRow(257, 0xc408b6a6u)]
    public void FromSignature_SignatureShapedInput_MatchesVectors_AroundStackLimit(int utf8Length, uint expected)
        => AssertAllOverloads(expected, "f(" + new string('a', utf8Length - 3) + ")", "bytes=" + utf8Length);

    [TestMethod]
    public void FromSignature_MultiByteInputs_MatchVectors_WhenCharCountDiffersFromByteCount()
    {
        // (signature, UTF-8 byte count, expected). Byte counts straddle the 256-byte stack limit while char counts do not.
        (string Signature, int Bytes, uint Expected)[] cases =
        [
            ("f(" + new string('\u00E9', 126) + ")", 255, 0xd56b1624u),
            ("f(" + new string('\u00E9', 127) + ")", 257, 0x2a5ce206u),
            ("f(" + new string('\u20AC', 100) + ")", 303, 0xf928a5b8u),
            ("f(" + string.Concat(Enumerable.Repeat("\U0001F600", 80)) + ")", 323, 0xef091897u),
        ];

        foreach (var (signature, bytes, expected) in cases)
        {
            Assert.AreEqual(bytes, Encoding.UTF8.GetByteCount(signature), "test data sanity");
            AssertAllOverloads(expected, signature, "bytes=" + bytes);
        }
    }

    [TestMethod]
    public void FromSignature_LoneSurrogates_HashTheUtf8ReplacementCharacter()
    {
        // .NET encodes each unpaired surrogate as U+FFFD (EF BF BD); the sizing pass and encoding pass must agree.
        // Vectors from independent reference implementation, hashing EF BF BD for each lone surrogate.
        (string Signature, uint Expected)[] cases =
        [
            ("f(\uD800)", 0x8f492d50u),
            ("f(\uDC00)", 0x8f492d50u),                                   // lone low and lone high hash identically
            ("f(a\uD83Dz)", 0xbad39eb3u),
            ("f(" + new string('\uD800', 120) + ")", 0x0f1ab055u),       // 363 bytes => pooled buffer
        ];

        foreach (var (signature, expected) in cases)
        {
            Assert.AreEqual(expected, FunctionSelector.FromSignature(signature).Value, signature.Length.ToString());
            Assert.AreEqual(expected, FunctionSelector.FromSignature(signature.AsSpan()).Value, signature.Length.ToString());
            Assert.AreEqual(expected, FunctionSelector.FromSignature(Encoding.UTF8.GetBytes(signature)).Value, signature.Length.ToString());
        }
    }

    [TestMethod]
    public void FromSignature_Overloads_Agree_AcrossTheStackBoundary_AndForNonAscii()
    {
        // ASCII lengths 250..262 straddle the 256-byte stack/pool switch; the non-ASCII cases have a UTF-8 byte count
        // different from their char count (2-byte, 3-byte, and a surrogate pair = 4 bytes).
        var signatures = new List<string>();
        for (int length = 250; length <= 262; length++)
            signatures.Add("f(" + new string('a', length - 3) + ")");
        signatures.Add("f(" + new string('\u00E9', 126) + ")");   // 2 + 252 + 1 = 255 bytes (fits the stack buffer)
        signatures.Add("f(" + new string('\u00E9', 127) + ")");   // 2 + 254 + 1 = 257 bytes (first pooled size)
        signatures.Add("f(" + new string('\u00E9', 128) + ")");   // 2 + 256 + 1 = 259 bytes
        signatures.Add("f(" + new string('\u20AC', 100) + ")");   // 3-byte chars
        signatures.Add("f(" + string.Concat(Enumerable.Repeat("\U0001F600", 80)) + ")"); // surrogate pairs

        foreach (string signature in signatures)
        {
            FunctionSelector expected = FunctionSelector.FromSignature(Encoding.UTF8.GetBytes(signature));

            Assert.AreEqual(expected, FunctionSelector.FromSignature(signature), $"string len={signature.Length}");
            Assert.AreEqual(expected, FunctionSelector.FromSignature(signature.AsSpan()), $"chars len={signature.Length}");
        }
    }

    [TestMethod]
    public void FromSignature_StaleBufferContents_DoNotLeakIntoLaterCalls()
    {
        // Stack and pooled buffers are reused and not cleared: a short input after a longer one must hash only its own bytes.
        // Vectors from independent reference implementation.
        Assert.AreEqual(0x922fe420u, FunctionSelector.FromSignature(new string('b', 600).AsSpan()).Value);
        Assert.AreEqual(0x8089f7e5u, FunctionSelector.FromSignature(new string('a', 512).AsSpan()).Value);
        Assert.AreEqual(0x5a7b8187u, FunctionSelector.FromSignature(new string('a', 273).AsSpan()).Value);
        Assert.AreEqual(0xcf7fcd4fu, FunctionSelector.FromSignature(new string('a', 272).AsSpan()).Value);
        Assert.AreEqual(0x26121ff0u, FunctionSelector.FromSignature("f()".AsSpan()).Value);
        Assert.AreEqual(0xc5d24601u, FunctionSelector.FromSignature(ReadOnlySpan<char>.Empty).Value);
    }

    [TestMethod]
    public void FromSignature_OnlyTheSliceIsHashed_NotTheUnderlyingBuffer()
    {
        const string text = "xxtransfer(address,uint256)yy";

        Assert.AreEqual(0xa9059cbbu, FunctionSelector.FromSignature(text.AsSpan(2, 25)).Value);
        Assert.AreEqual(0xa9059cbbu, FunctionSelector.FromSignature(Encoding.UTF8.GetBytes(text).AsSpan(2, 25)).Value);
    }

    [TestMethod]
    public void FromSignature_Null_ThrowsArgumentNullException_WithParameterName()
    {
        var ex = Assert.ThrowsExactly<ArgumentNullException>(() => _ = FunctionSelector.FromSignature((string)null!));
        Assert.AreEqual("signature", ex.ParamName);
    }

    [TestMethod]
    public void FromSignature_DefaultSpans_HashTheEmptyString()
    {
        Assert.AreEqual(0xc5d24601u, FunctionSelector.FromSignature(default(ReadOnlySpan<char>)).Value);
        Assert.AreEqual(0xc5d24601u, FunctionSelector.FromSignature(default(ReadOnlySpan<byte>)).Value);
    }

    // ----------------- real collisions -----------------

    [TestMethod]
    public void FromSignature_RealCollision_BurnAndCollatePropagateStorage_ShareASelector()
    {
        // 4-byte selectors collide in practice: both signatures hash to 0x42966c68 (independent reference implementation).
        FunctionSelector burn = FunctionSelector.FromSignature("burn(uint256)");
        FunctionSelector collate = FunctionSelector.FromSignature("collate_propagate_storage(bytes16)");

        Assert.AreEqual(0x42966c68u, burn.Value);
        Assert.AreEqual(burn, collate);
        Assert.AreEqual(burn.GetHashCode(), collate.GetHashCode());
        Assert.IsTrue(burn == collate);
    }

    // ----------------- published constants -----------------

    private static readonly (string Class, string Field, string Signature, uint Literal)[] PublishedConstants =
    [
        ("Erc20", "Transfer", "transfer(address,uint256)", 0xa9059cbbu),
        ("Erc20", "Approve", "approve(address,uint256)", 0x095ea7b3u),
        ("Erc20", "TransferFrom", "transferFrom(address,address,uint256)", 0x23b872ddu),
        ("Erc20", "BalanceOf", "balanceOf(address)", 0x70a08231u),
        ("Erc20", "Allowance", "allowance(address,address)", 0xdd62ed3eu),
        ("Erc20", "TotalSupply", "totalSupply()", 0x18160dddu),
        ("Erc721", "SafeTransferFrom", "safeTransferFrom(address,address,uint256)", 0x42842e0eu),
        ("Erc721", "SafeTransferFromWithData", "safeTransferFrom(address,address,uint256,bytes)", 0xb88d4fdeu),
        ("Erc721", "OwnerOf", "ownerOf(uint256)", 0x6352211eu),
        ("Erc721", "TokenUri", "tokenURI(uint256)", 0xc87b56ddu),
    ];

    private static Type ConstantsClass(string name) => typeof(FunctionSelector).GetNestedType(name, BindingFlags.Public)!;

    [TestMethod]
    public void PublishedConstants_EqualTheSelector_OfTheirDocumentedSignature_AndTheIndependentLiteral()
    {
        foreach (var (cls, field, signature, literal) in PublishedConstants)
        {
            var actual = (FunctionSelector)ConstantsClass(cls).GetField(field, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

            Assert.AreEqual(literal, actual.Value, $"{cls}.{field} vs independent literal");
            Assert.AreEqual(FunctionSelector.FromSignature(signature), actual, $"{cls}.{field} vs FromSignature(\"{signature}\")");
        }
    }

    [TestMethod]
    [DataRow("Erc20")]
    [DataRow("Erc721")]
    public void PublishedConstants_TableCoversEveryPublicField_AndSelectorsAreDistinct(string cls)
    {
        FieldInfo[] fields = ConstantsClass(cls).GetFields(BindingFlags.Public | BindingFlags.Static);
        string[] tabled = PublishedConstants.Where(c => c.Class == cls).Select(c => c.Field).Order().ToArray();

        CollectionAssert.AreEqual(tabled, fields.Select(f => f.Name).Order().ToArray(), "a constant was added or removed without updating the guard table");
        Assert.HasCount(fields.Length, fields.Select(f => ((FunctionSelector)f.GetValue(null)!).Value).Distinct().ToArray());
        Assert.IsTrue(fields.All(f => f.IsInitOnly && f.FieldType == typeof(FunctionSelector)));
    }

    // ----------------- allocation -----------------

    private static long MinimumAllocatedBytes(Action batch, int trials = 8)
    {
        batch(); batch(); // warm-up: JIT, ArrayPool thread-local bucket
        long best = long.MaxValue;
        for (int t = 0; t < trials; t++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            batch();
            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return best;
    }

    [TestMethod]
    public void FromSignature_Chars_AddsNoAllocation_OverTheUtf8Overload_ForShortOrPooledSignatures()
    {
        // The UTF-8 overload is the baseline: it is the hash alone (Debug builds of the hash allocate a little per call, Release
        // builds do not). The char overload must add nothing on top: no per-call encoding array, even above the stack limit.
        const int Calls = 200;
        string shortSignature = "transfer(address,uint256)";
        string longSignature = "f(" + string.Join(",", Enumerable.Repeat("uint256", 200)) + ")"; // 1602 bytes => pooled
        byte[] shortUtf8 = Encoding.UTF8.GetBytes(shortSignature);
        byte[] longUtf8 = Encoding.UTF8.GetBytes(longSignature);

        long shortBaseline = MinimumAllocatedBytes(() => { for (int i = 0; i < Calls; i++) FunctionSelector.FromSignature(shortUtf8.AsSpan()); });
        long shortChars = MinimumAllocatedBytes(() => { for (int i = 0; i < Calls; i++) FunctionSelector.FromSignature(shortSignature.AsSpan()); });
        long longBaseline = MinimumAllocatedBytes(() => { for (int i = 0; i < Calls; i++) FunctionSelector.FromSignature(longUtf8.AsSpan()); });
        long longChars = MinimumAllocatedBytes(() => { for (int i = 0; i < Calls; i++) FunctionSelector.FromSignature(longSignature.AsSpan()); });

        // A regression to a per-call array would add at least 200 * 32 bytes (short) or 200 * 1602 bytes (long).
        Assert.IsLessThan(256, shortChars - shortBaseline, "short signature batch");
        Assert.IsLessThan(256, longChars - longBaseline, "pooled signature batch");
    }
}
