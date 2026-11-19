using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Deterministic property and mutation tests against the independent reference model (ABIReferenceModel.cs). Random schemas and values are
/// encoded by the reference encoder; the library must accept them and locate and read every value exactly as the encoder placed it. Mutated
/// documents (bit flips, overwritten pointer/count/padding words, truncation, suffixes, shifted words) must never throw or hang, and the library
/// must accept a mutant exactly when the reference model says it is canonical: permissive decode succeeds AND re-encoding the decoded value
/// reproduces the mutant byte for byte. Seeds are fixed, so a failure reproduces; the failing seed is in the message.
/// </summary>
[TestClass]
public sealed class ABIDecodeDifferentialTests
{
    private const int Schemas = 350;
    private const int MutationsPerDocument = 36;

    private static readonly ulong[] SpecialWords =
    [
        0, 1, 2, 0x1f, 0x20, 0x21, 0x40, 0x60, 0x80, 0xa0, 0xc0, 0x100, 0x1000, 0xffff_ffff, 0x7fff_ffff, 0x8000_0000, 0x1_0000_0000, ulong.MaxValue,
    ];

    private static readonly ABIStatus[] DecoderFailures =
    [
        ABIStatus.Truncated, ABIStatus.InvalidOffset, ABIStatus.NonCanonicalLayout, ABIStatus.InvalidPadding, ABIStatus.InvalidScalar,
        ABIStatus.ElementLimitExceeded, ABIStatus.WorkLimitExceeded, ABIStatus.DepthLimitExceeded,
    ];

    private static (RTupleType Type, RSeq Value, byte[] Bytes, ABISchema Schema) Generate(ulong seed)
    {
        var rng = new DeterministicRng(seed);
        RTupleType type = ABIReferenceGenerator.RandomRoot(rng, depth: 3);
        RSeq value = ABIReferenceGenerator.RandomValue(rng, type);
        byte[] bytes = ABIReference.EncodeRoot(type, value);
        return (type, value, bytes, CompileTuple(type.Text));
    }

    [TestMethod]
    public void RandomSchemasAndValues_AreAccepted_AndEveryPathAndAccessorMatchesTheReferenceEncoder()
    {
        int dynamicRoots = 0;
        for (ulong seed = 1; seed <= Schemas; seed++)
        {
            var (type, value, bytes, schema) = Generate(seed);
            dynamicRoots += type.IsDynamic ? 1 : 0;
            ABIData data = DecodeOk(bytes, schema, Limits(work: 1_000_000, visited: 100_000));
            ABIReferenceAssertions.AssertEveryPathMatches(in data, type, value, bytes);
        }

        Assert.IsTrue(dynamicRoots > Schemas / 4, "the generator must produce plenty of dynamic roots");
    }

    [TestMethod]
    public void EveryProperPrefixOfARandomDocument_IsTruncated_AndAnySuffixIsTrailingData()
    {
        for (ulong seed = 1; seed <= 120; seed++)
        {
            var (_, _, bytes, schema) = Generate(seed * 7919);
            for (int length = 0; length < bytes.Length; length++)
            {
                DecodeFails(bytes[..length], schema, ABIStatus.Truncated, why: $"seed {seed * 7919} prefix {length}/{bytes.Length} of {schema.CanonicalType}");
            }

            DecodeFails([.. bytes, 0x00], schema, ABIStatus.NonCanonicalLayout, why: $"seed {seed * 7919} +1");
            DecodeFails([.. bytes, .. new byte[32]], schema, ABIStatus.NonCanonicalLayout, why: $"seed {seed * 7919} +32");
        }
    }

    [TestMethod]
    public void MutatedDocuments_NeverThrow_AndAreAcceptedExactlyWhenCanonical()
    {
        int accepted = 0;
        int rejected = 0;
        var statuses = new HashSet<ABIStatus>();
        for (ulong seed = 1; seed <= Schemas; seed++)
        {
            var (type, _, original, schema) = Generate(seed * 104_729);
            var rng = new DeterministicRng(seed * 31 + 5);
            for (int round = 0; round < MutationsPerDocument; round++)
            {
                byte[] mutant = Mutate(original, rng);
                string label = $"seed {seed * 104_729} round {round} schema {schema.CanonicalType}";

                var limits = Limits(work: 1_000_000, visited: 100_000);
                bool ok = ABIData.TryDecodeComplete(mutant, schema, in limits, out ABIData data, out ABIStatus status, out ABIError error);
                Assert.AreEqual(ok, data.IsValid, label);
                Assert.AreEqual(status, error.Status, label);

                bool referenceDecodes = ABIReference.TryDecodePermissive(type, mutant, out RSeq? decoded);
                bool canonical = referenceDecodes && ABIReference.EncodeRoot(type, decoded!).AsSpan().SequenceEqual(mutant);
                Assert.AreEqual(canonical, ok, $"{label}: status {status} at {error.DocumentOffset}");

                if (ok)
                {
                    accepted++;
                    ABIReferenceAssertions.AssertEveryPathMatches(in data, type, decoded!, mutant);
                }
                else
                {
                    rejected++;
                    statuses.Add(status);
                    CollectionAssert.Contains(DecoderFailures, status, label);
                    Assert.IsTrue(error.DocumentOffset >= -1 && error.DocumentOffset <= mutant.Length, $"{label}: offset {error.DocumentOffset} outside [0,{mutant.Length}]");
                    Assert.IsNull(data.Schema, label);
                }
            }
        }

        Assert.IsTrue(accepted > 200, $"only {accepted} mutants were accepted: the mutator is too destructive to test acceptance");
        Assert.IsTrue(rejected > 3_000, $"only {rejected} mutants were rejected");
        // The mutator reaches the main failure classes.
        foreach (ABIStatus expected in new[] { ABIStatus.Truncated, ABIStatus.InvalidOffset, ABIStatus.NonCanonicalLayout, ABIStatus.InvalidPadding, ABIStatus.InvalidScalar })
        {
            Assert.IsTrue(statuses.Contains(expected), $"no mutant produced {expected}");
        }
    }

    [TestMethod]
    public void MutatedCalls_AreRejectedOrIdenticalToTheCanonicalDecode_AndNeverThrow()
    {
        for (ulong seed = 1; seed <= 120; seed++)
        {
            var (type, _, original, schema) = Generate(seed * 15_485_863);
            var limits = ABICompileLimits.Default;
            Assert.IsTrue(ABISchemaCompiler.TryCompileFunction("fn", ABITupleParse(type.Text), in limits, out var function, out _));
            byte[] selector = Hex(function.Selector.ToString("x8"));
            var rng = new DeterministicRng(seed + 17);
            for (int round = 0; round < 12; round++)
            {
                byte[] mutant = Mutate([.. selector, .. original], rng);
                var decodeLimits = Limits(work: 1_000_000, visited: 100_000);
                bool ok = ABICallData.TryDecode(mutant, function, in decodeLimits, out ABICallData call, out ABIStatus status);
                Assert.AreEqual(ok, call.IsValid);
                if (!ok)
                {
                    Assert.IsFalse(call.Arguments.IsValid);
                    continue;
                }

                // Accepted: the selector is intact and the arguments are accepted by the plain document decoder as well.
                CollectionAssert.AreEqual(selector, mutant[..4]);
                Assert.IsTrue(ABIData.TryDecodeComplete(mutant.AsSpan(4), schema, in decodeLimits, out _, out status), status.ToString());
            }
        }
    }

    private static ABITupleDefinition ABITupleParse(string text)
    {
        Assert.IsTrue(ABITupleDefinition.TryParse(text, out var definition, out var error), error.ToString());
        return definition;
    }

    internal static byte[] Mutate(byte[] original, DeterministicRng rng)
    {
        byte[] bytes = (byte[])original.Clone();
        int words = bytes.Length / 32;
        switch (rng.Next(9))
        {
            case 0 when bytes.Length > 0:
                bytes[rng.Next(bytes.Length)] ^= (byte)(1 << rng.Next(8));
                return bytes;
            case 1 when words > 0:
                Word(SpecialWords[rng.Next(SpecialWords.Length)]).CopyTo(bytes, rng.Next(words) * 32);
                return bytes;
            case 2 when bytes.Length > 0:
                return bytes[..(bytes.Length - Math.Min(bytes.Length, 1 + rng.Next(64)))];
            case 3:
                return [.. bytes, .. rng.Bytes(1 + rng.Next(40))];
            case 4 when words > 1:
            {
                int a = rng.Next(words);
                int b = rng.Next(words);
                byte[] first = bytes.AsSpan(a * 32, 32).ToArray();
                bytes.AsSpan(b * 32, 32).CopyTo(bytes.AsSpan(a * 32, 32));
                first.CopyTo(bytes, b * 32);
                return bytes;
            }

            case 5:
            {
                int at = words == 0 ? 0 : rng.Next(words + 1) * 32;
                return [.. bytes[..at], .. new byte[32], .. bytes[at..]];
            }

            case 6 when words > 0:
            {
                int at = rng.Next(words) * 32;
                return [.. bytes[..at], .. bytes[(at + 32)..]];
            }

            case 7 when words > 0:
            {
                // Nudge a small number by +-32: the classic off-by-one-word pointer or count.
                int at = rng.Next(words) * 32;
                ulong current = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(at + 24, 8));
                if (bytes.AsSpan(at, 24).IndexOfAnyExcept((byte)0) < 0 && current < 100_000)
                {
                    Word(rng.Next(2) == 0 ? current + 32 : current - Math.Min(current, 32)).CopyTo(bytes, at);
                }

                return bytes;
            }

            case 8 when bytes.Length > 0:
                bytes[rng.Next(bytes.Length)] = 0xff;
                return bytes;
            default:
                return bytes;
        }
    }

    [TestMethod]
    public void MutationSeeds_AreReproducible()
    {
        var (_, _, first, _) = Generate(42);
        var (_, _, second, _) = Generate(42);
        CollectionAssert.AreEqual(first, second);
        CollectionAssert.AreEqual(Mutate(first, new DeterministicRng(5)), Mutate(second, new DeterministicRng(5)));
    }
}
