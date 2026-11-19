using System.Numerics;
using System.Text;
using EccentricWare.Web3.DataTypes.ABI;
using Nethereum.ABI;
using Nethereum.ABI.FunctionEncoding;
using Nethereum.ABI.Model;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Differential tests against Nethereum.ABI 7.1.0, an independent Solidity ABI implementation that is a TEST-ONLY dependency (never
/// referenced by the library; same package family as the Nethereum.RLP used by the RLP differential tests, restored from the same feed).
/// It is a second implementation next to the Foundry <c>cast</c> fixtures and the test-local reference model (spec 10.4). Scope: the
/// types Nethereum supports (no <c>function</c>, no fixed-point, no empty tuples), canonical documents only. Nethereum's DECODER is
/// permissive (it accepts trailing bytes, <c>bool</c> = 2, <c>uint8</c> = 256), so it cannot judge strictness; the strictness tests below
/// document exactly that difference and assert the library's rejection.
/// </summary>
[TestClass]
public sealed class ABINethereumDifferentialTests
{
    private static readonly ParametersEncoder Encoder = new();
    private static readonly ParameterDecoder Decoder = new();

    // ------------------------------------------------------------------ adapter

    /// <summary>True when Nethereum can represent <paramref name="type"/> (no function, no fixed-point, no empty tuple).</summary>
    private static bool Supported(RType type) => type switch
    {
        RElementary e => e.Kind is not (ABITypeKind.Function or ABITypeKind.Fixed or ABITypeKind.UFixed),
        RTupleType t => t.Items.Length > 0 && t.Items.All(Supported),
        RFixedArrayType f => Supported(f.Element),
        RDynamicArrayType d => Supported(d.Element),
        _ => true,
    };

    private static Parameter ParameterFor(RType type)
    {
        var suffixes = new List<string>();
        RType core = type;
        while (core is RFixedArrayType or RDynamicArrayType)
        {
            suffixes.Insert(0, core is RFixedArrayType f ? $"[{f.Length}]" : "[]");
            core = core is RFixedArrayType fa ? fa.Element : ((RDynamicArrayType)core).Element;
        }

        var parameter = new Parameter((core is RTupleType ? "tuple" : core.Text) + string.Concat(suffixes), "p");
        if (core is RTupleType tuple)
        {
            ABIType element = parameter.ABIType;
            while (element is ArrayType array)
            {
                element = array.ElementType;
            }

            ((TupleType)element).SetComponents(tuple.Items.Select(ParameterFor).ToArray());
        }

        return parameter;
    }

    private static object ValueFor(RType type, RValue value)
    {
        switch (type)
        {
            case RElementary e:
                byte[] word = ((RWord)value).Word;
                return e.Kind switch
                {
                    ABITypeKind.UInt => ABIReference.Unsigned(word),
                    ABITypeKind.Int => ABIReference.Signed(word),
                    ABITypeKind.Address => "0x" + Convert.ToHexStringLower(word[12..]),
                    ABITypeKind.Bool => word[31] == 1,
                    ABITypeKind.FixedBytes => word[..e.Width],
                    _ => throw new InvalidOperationException(),
                };
            case RBlobType blob:
                byte[] data = ((RBlob)value).Data;
                return blob.IsString ? Encoding.UTF8.GetString(data) : data;
            case RTupleType tuple:
                return ((RSeq)value).Items.Select((v, i) => ValueFor(tuple.Items[i], v)).ToArray();
            case RFixedArrayType fixedArray:
                return ((RSeq)value).Items.Select(v => ValueFor(fixedArray.Element, v)).ToList();
            default:
                return ((RSeq)value).Items.Select(v => ValueFor(((RDynamicArrayType)type).Element, v)).ToList();
        }
    }

    private static byte[] NethereumEncode(RTupleType type, RSeq value) =>
        Encoder.EncodeParameters(type.Items.Select(ParameterFor).ToArray(), type.Items.Select((t, i) => ValueFor(t, value.Items[i])).ToArray());

    /// <summary>Compares what Nethereum decoded with the reference value of the same type; the message names the path.</summary>
    private static void AssertSameValue(RType type, RValue expected, object? actual, string where)
    {
        if (actual is ParameterOutput output)
        {
            actual = output.Result;
        }

        switch (type)
        {
            case RElementary e:
                byte[] word = ((RWord)expected).Word;
                switch (e.Kind)
                {
                    case ABITypeKind.UInt: Assert.AreEqual(ABIReference.Unsigned(word), (BigInteger)actual!, where); break;
                    case ABITypeKind.Int: Assert.AreEqual(ABIReference.Signed(word), (BigInteger)actual!, where); break;
                    case ABITypeKind.Address: Assert.AreEqual("0x" + Convert.ToHexStringLower(word[12..]), ((string)actual!).ToLowerInvariant(), where); break;
                    case ABITypeKind.Bool: Assert.AreEqual(word[31] == 1, (bool)actual!, where); break;
                    default: CollectionAssert.AreEqual(word[..e.Width], (byte[])actual!, where); break;
                }

                break;
            case RBlobType blob:
                byte[] data = ((RBlob)expected).Data;
                if (blob.IsString)
                {
                    Assert.AreEqual(Encoding.UTF8.GetString(data), (string)actual!, where);
                }
                else
                {
                    CollectionAssert.AreEqual(data, (byte[])actual!, where);
                }

                break;
            case RTupleType tuple:
                var components = ((System.Collections.IEnumerable)actual!).Cast<object>().ToList();
                Assert.HasCount(tuple.Items.Length, components, where);
                for (int i = 0; i < components.Count; i++)
                {
                    AssertSameValue(tuple.Items[i], ((RSeq)expected).Items[i], components[i], $"{where}.{i}");
                }

                break;
            default:
                RType element = type is RFixedArrayType f ? f.Element : ((RDynamicArrayType)type).Element;
                var items = ((System.Collections.IEnumerable)actual!).Cast<object>().ToList();
                RValue[] expectedItems = ((RSeq)expected).Items;
                Assert.HasCount(expectedItems.Length, items, where);
                for (int i = 0; i < items.Count; i++)
                {
                    AssertSameValue(element, expectedItems[i], items[i], $"{where}[{i}]");
                }

                break;
        }
    }

    private static void AssertNethereumDecodes(RTupleType type, RSeq value, byte[] document, string label)
    {
        List<ParameterOutput> decoded = Decoder.DecodeDefaultData(document, type.Items.Select(ParameterFor).ToArray());
        Assert.HasCount(type.Items.Length, decoded, label);
        for (int i = 0; i < decoded.Count; i++)
        {
            AssertSameValue(type.Items[i], value.Items[i], decoded[i].Result, $"{label} arg {i}");
        }
    }

    // ------------------------------------------------------------------ tests

    [TestMethod]
    public void RandomSupportedShapes_AreByteIdentical_AcrossNethereum_TheReferenceEncoder_AndTheLibraryWriter()
    {
        int compared = 0;
        int dynamicRoots = 0;
        for (ulong seed = 1; compared < 250 && seed < 5_000; seed++)
        {
            var rng = new DeterministicRng(seed * 2_654_435_761);
            RTupleType type = ABIReferenceGenerator.RandomRoot(rng, depth: 3);
            if (!Supported(type) || type.Items.Length == 0)
            {
                continue;
            }

            var value = (RSeq)ABIWriteTestSupport.Utf8Sanitised(type, ABIReferenceGenerator.RandomValue(rng, type), rng);
            string label = $"seed {seed * 2_654_435_761} {type.Text}";

            byte[] reference = ABIReference.EncodeRoot(type, value);
            byte[] nethereum = NethereumEncode(type, value);
            CollectionAssert.AreEqual(reference, nethereum, $"{label}: Nethereum encoder vs reference encoder");

            ABISchema schema = ABISchemaTestSupport.CompileTuple(type.Text);
            byte[] written = ABIWriteTestSupport.WriteOk(ABIWriteTestSupport.Script(type, value), schema);
            CollectionAssert.AreEqual(nethereum, written, $"{label}: library writer vs Nethereum encoder");

            // Library decodes the independent encoder's bytes and reads every value as the reference says...
            ABIData data = DecodeOk(nethereum, schema, Limits(work: 1_000_000, visited: 100_000));
            ABIReferenceAssertions.AssertEveryPathMatches(in data, type, value, nethereum);

            // ...and the independent decoder reads the library writer's bytes as the same values.
            AssertNethereumDecodes(type, value, written, label);

            compared++;
            dynamicRoots += type.IsDynamic ? 1 : 0;
        }

        Assert.AreEqual(250, compared, "the generator must yield enough Nethereum-supported shapes");
        Assert.IsGreaterThan(60, dynamicRoots, "dynamic roots");
    }

    /// <summary>Fixtures (cast output, Solidity docs, real-chain shapes) whose types Nethereum supports.</summary>
    private static IEnumerable<(string Name, RTupleType Type, byte[] Document)> SupportedFixtures()
    {
        foreach (ABIFixtureCatalog.Fixture f in ABIFixtureCatalog.All)
        {
            RTupleType type = ABIReference.ParseTuple(f.Tuple);
            if (Supported(type))
            {
                yield return (f.Name, type, ABIFixtureCatalog.ArgumentBytes(f));
            }
        }

        foreach (ABIWriteFixtures.Fixture f in ABIWriteFixtures.All)
        {
            RTupleType type = ABIReference.ParseTuple(f.Tuple);
            if (Supported(type) && type.Items.Length > 0)
            {
                byte[] all = Convert.FromHexString(f.Hex);
                yield return (f.Name, type, f.IsCall ? all[4..] : all);
            }
        }
    }

    [TestMethod]
    public void IndependentlyProducedFixtures_ArePutThroughNethereumAndTheLibraryWithTheSameResult()
    {
        int checkedFixtures = 0;
        foreach (var (name, type, document) in SupportedFixtures())
        {
            // The cast/Solidity bytes are the ground truth; neither implementation may disagree with them.
            Assert.IsTrue(ABIReference.TryDecodePermissive(type, document, out RSeq? value), name);

            // String payloads in the fixtures are valid UTF-8 except MalformedUtf8Payload, which Nethereum cannot represent as text.
            if (name == nameof(ABIFixtures.MalformedUtf8Payload))
            {
                continue;
            }

            AssertNethereumDecodes(type, value!, document, $"{name}: Nethereum decode of the fixture");
            CollectionAssert.AreEqual(document, NethereumEncode(type, value!), $"{name}: Nethereum encode of the decoded fixture");

            ABISchema schema = ABISchemaTestSupport.CompileTuple(type.Text);
            ABIData data = DecodeOk(document, schema);
            ABIReferenceAssertions.AssertEveryPathMatches(in data, type, value!, document);
            CollectionAssert.AreEqual(document, ABIWriteTestSupport.WriteOk(ABIWriteTestSupport.Script(type, value!, rawStrings: true), schema), $"{name}: library re-encode");
            checkedFixtures++;
        }

        Assert.IsGreaterThan(40, checkedFixtures, "fixtures compared");
    }

    [TestMethod]
    public void NethereumDecoderIsPermissive_TheLibraryRejectsEachOfTheseOnPurpose()
    {
        // (parameter type, document, library status). Nethereum 7.1.0 returns a value for every one of these (it never throws),
        // which is the compatibility gap of spec 2.3: a signing path that used a permissive decoder would read bytes the strict one refuses.
        byte[] gap = Cat(Word(0x40), Word(0), Word(1), Padded([0xaa])); // the bytes pointer skips one word
        var cases = new (string Type, byte[] Document, ABIStatus Expected)[]
        {
            ("uint256", [.. Word(5), 0x00], ABIStatus.NonCanonicalLayout),
            ("bool", Word(2), ABIStatus.InvalidScalar),
            ("uint8", Word(256), ABIStatus.InvalidScalar),
            ("address", Filled(0xff), ABIStatus.InvalidScalar),
            ("bytes", gap, ABIStatus.NonCanonicalLayout),
            ("bytes", Cat(Word(0x20), Word(1), [0xaa, 0x01, .. new byte[30]]), ABIStatus.InvalidPadding),
        };
        // What Nethereum returns for each (it never throws): the trailing byte is ignored, bool 2 reads as true, uint8 256 as 256, the dirty address as some address,
        // the gapped pointer is followed, dirty padding is ignored.
        object[] nethereumReads = [new BigInteger(5), true, new BigInteger(256), null!, new byte[] { 0xaa }, new byte[] { 0xaa }];
        for (int i = 0; i < cases.Length; i++)
        {
            var (type, document, expected) = cases[i];
            List<ParameterOutput> read = Decoder.DecodeDefaultData(document, [new Parameter(type, "p")]);
            Assert.HasCount(1, read, $"Nethereum rejected the {type} document it is documented to accept");
            if (nethereumReads[i] is not null)
            {
                Assert.AreEqual(nethereumReads[i] is byte[] bytes ? Convert.ToHexString(bytes) : nethereumReads[i], read[0].Result is byte[] got ? Convert.ToHexString(got) : read[0].Result, $"{type} case {i}");
            }
            else
            {
                Assert.IsNotNull(read[0].Result, "an address string");
            }

            DecodeFails($"({type})", document, expected);
        }

        // Nethereum follows the gapped pointer and returns the payload; the library refuses the same bytes.
        List<ParameterOutput> decoded = Decoder.DecodeDefaultData(gap, [new Parameter("bytes", "p")]);
        CollectionAssert.AreEqual(new byte[] { 0xaa }, (byte[])decoded[0].Result);
    }

    [TestMethod]
    public void FunctionSelectors_EqualNethereumsKeccak_ForTheListedRealSignatures()
    {
        string[] signatures =
        [
            "transfer(address,uint256)", "approve(address,uint256)", "balanceOf(address)", "multicall(bytes[])", "totalSupply()",
            "transferFrom(address,address,uint256)", "baz(uint32,bool)", "bar(bytes3[2])", "sam(bytes,bool,uint256[])",
            "f(uint256,uint32[],bytes10,bytes)", "g(uint256[][],string[])", "exactInputSingle((address,address,uint24,address,uint256,uint256,uint256,uint160))",
            "safeBatchTransferFrom(address,address,uint256[],uint256[],bytes)", "multicall(uint256,bytes[])",
            "swapExactTokensForTokens(uint256,uint256,address[],address,uint256)",
        ];
        foreach (string signature in signatures)
        {
            var limits = ABICompileLimits.Default;
            Assert.IsTrue(ABISchemaCompiler.TryCompileFunctionSignature(signature, in limits, out ABIFunctionSchema? function, out ABISchemaError error), $"{signature}: {error}");
            byte[] hash = Nethereum.Util.Sha3Keccack.Current.CalculateHash(Encoding.ASCII.GetBytes(signature));
            Assert.AreEqual(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hash), function.Selector, signature);
        }

        // Aliases normalise before hashing: the selector of f(uint) is that of f(uint256).
        var aliasLimits = ABICompileLimits.Default;
        Assert.IsTrue(ABISchemaCompiler.TryCompileFunctionSignature("f(uint,int)", in aliasLimits, out ABIFunctionSchema? alias, out _));
        byte[] aliasHash = Nethereum.Util.Sha3Keccack.Current.CalculateHash(Encoding.ASCII.GetBytes("f(uint256,int256)"));
        Assert.AreEqual(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(aliasHash), alias.Selector);
    }
}
