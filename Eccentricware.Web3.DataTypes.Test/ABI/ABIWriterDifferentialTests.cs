using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIWriteTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Differential and property tests (spec 10.3, 10.4, 7.7). Random schemas and values are encoded by two independent test-local
/// encoders (<see cref="ABIReference"/>, <see cref="ABIIndependentEncoder"/>); the writer, the measurer and the call encoder must
/// agree with both byte for byte (and in length), the strict decoder must accept the result and read back every value, and the
/// writer must be exact about capacity. Seeds are fixed, so a failure reproduces; the seed is in every message.
/// </summary>
[TestClass]
public sealed class ABIWriterDifferentialTests
{
    private const int Schemas = 500;

    [TestMethod]
    public void RandomSchemasAndValues_WriterMeasurerCallEncoderAndBothReferenceEncodersAgree()
    {
        int dynamicRoots = 0;
        int nonEmpty = 0;
        for (ulong seed = 1; seed <= Schemas; seed++)
        {
            var (type, value, expected, schema, script) = Generate(seed);
            string label = $"seed {seed} {type.Text}";

            CollectionAssert.AreEqual(expected, ABIIndependentEncoder.EncodeArguments(type, value), $"{label}: the two reference encoders disagree");

            byte[] written = WriteOk(script, schema);
            CollectionAssert.AreEqual(expected, written, $"{label}: writer");
            AssertDecodesTo(written, schema, type, value);

            RunResult measured = RunMeasurer(script, schema, GenerousEncode);
            Assert.IsTrue(measured.Ok, label);
            Assert.AreEqual(expected.Length, measured.Length, $"{label}: measurer length");

            ABIFunctionSchema function = Function("f" + type.Text);
            byte[] call = CallOk(script, function);
            Assert.AreEqual(4 + expected.Length, call.Length, label);
            CollectionAssert.AreEqual(expected, call[4..], $"{label}: call arguments");
            Assert.AreEqual(function.Selector, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(call), label);

            dynamicRoots += type.IsDynamic ? 1 : 0;
            nonEmpty += expected.Length > 0 ? 1 : 0;
        }

        Assert.IsTrue(dynamicRoots > Schemas / 4, $"only {dynamicRoots} dynamic roots");
        Assert.IsTrue(nonEmpty > Schemas / 2, $"only {nonEmpty} non-empty documents");
    }

    [TestMethod]
    public void RandomValuesWithStringsThatAreNotText_AreRefusedByBothEntryPoints_AndRoundTripOnceTheyAreText()
    {
        // The generator's strings are random bytes (mostly invalid UTF-8): exactly what an attacker or an odd contract puts on chain. The
        // writer and measurer refuse them through either entry point; the same shape with text strings encodes and round-trips exactly.
        int refused = 0;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var rng = new DeterministicRng(seed * 71);
            RTupleType type = ABIReferenceGenerator.RandomRoot(rng, 3);
            RSeq value = ABIReferenceGenerator.RandomValue(rng, type);
            ABISchema schema = CompileTuple(type.Text);
            if (!StringsAreText(type, value))
            {
                refused++;
                foreach (bool raw in new[] { true, false })
                {
                    List<Ev> bad = Script(type, value, rawStrings: raw);
                    byte[] destination = new byte[ABIReference.EncodeRoot(type, value).Length];
                    Assert.AreEqual(ABIStatus.InvalidUtf8, RunWriter(bad, schema, destination, GenerousEncode).Status, $"seed {seed * 71} raw={raw}");
                    Assert.AreEqual(ABIStatus.InvalidUtf8, RunMeasurer(bad, schema, GenerousEncode).Status, $"seed {seed * 71} raw={raw}: measurer");
                }

                value = (RSeq)Utf8Sanitised(type, value, rng);
            }

            byte[] expected = ABIReference.EncodeRoot(type, value);
            foreach (bool raw in new[] { true, false })
            {
                byte[] written = WriteOk(Script(type, value, rawStrings: raw), schema);
                CollectionAssert.AreEqual(expected, written, $"seed {seed * 71} {type.Text} raw={raw}");
                Assert.IsTrue(ABIReference.TryDecodePermissive(type, written, out RSeq? decoded));
                CollectionAssert.AreEqual(written, WriteOk(Script(type, decoded!, rawStrings: raw), schema), $"seed {seed * 71}: encode(decode(x)) must be the identity");
                AssertDecodesTo(written, schema, type, value);
            }
        }

        Assert.IsTrue(refused > 30, $"only {refused} documents contained a string that is not text");
    }

    [TestMethod]
    public void DeeperRandomSchemas_AgreeAsWell()
    {
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var (type, value, expected, schema, script) = Generate(seed * 7_919, depth: 4);
            byte[] written = WriteOk(script, schema);
            CollectionAssert.AreEqual(expected, written, $"seed {seed * 7_919} {type.Text}");
            CollectionAssert.AreEqual(ABIIndependentEncoder.EncodeArguments(type, value), written, $"seed {seed * 7_919}");
        }
    }

    [TestMethod]
    public void EncodeDecodeEncode_IsTheIdentity_ForCanonicalDocuments()
    {
        for (ulong seed = 1; seed <= 250; seed++)
        {
            var (type, _, _, schema, script) = Generate(seed * 31);
            byte[] first = WriteOk(script, schema);
            Assert.IsTrue(ABIReference.TryDecodePermissive(type, first, out RSeq? decoded), $"seed {seed * 31}");
            byte[] second = WriteOk(Script(type, decoded!), schema);
            CollectionAssert.AreEqual(first, second, $"seed {seed * 31} {type.Text}");
        }
    }

    [TestMethod]
    public void WriterIsExactAboutCapacity_OneByteLessAlwaysFails_ExactSizeAlwaysSucceeds()
    {
        for (ulong seed = 1; seed <= Schemas; seed++)
        {
            var (type, _, expected, schema, script) = Generate(seed * 13);
            string label = $"seed {seed * 13} {type.Text}";

            var exact = new byte[expected.Length];
            Array.Fill(exact, Garbage);
            RunResult ok = RunWriter(script, schema, exact, GenerousEncode);
            Assert.IsTrue(ok.Ok, $"{label}: {ok.Status}");
            CollectionAssert.AreEqual(expected, exact, label);

            if (expected.Length > 0)
            {
                var small = new byte[expected.Length - 1];
                RunResult bad = RunWriter(script, schema, small, GenerousEncode);
                Assert.IsFalse(bad.Ok, label);
                Assert.AreEqual(ABIStatus.InsufficientDestination, bad.Status, label);
            }
        }
    }

    [TestMethod]
    public void EveryShorterDestination_FailsWithInsufficientDestination_ForSmallDocuments()
    {
        int documents = 0;
        for (ulong seed = 1; seed <= 400 && documents < 60; seed++)
        {
            var (type, _, expected, schema, script) = Generate(seed * 17);
            if (expected.Length == 0 || expected.Length > 700)
            {
                continue;
            }

            documents++;
            for (int length = 0; length < expected.Length; length++)
            {
                RunResult result = RunWriter(script, schema, new byte[length], GenerousEncode);
                Assert.IsFalse(result.Ok, $"seed {seed * 17} {type.Text} destination {length}/{expected.Length}");
                Assert.AreEqual(ABIStatus.InsufficientDestination, result.Status, $"seed {seed * 17} destination {length}/{expected.Length}");
            }
        }

        Assert.IsTrue(documents >= 40, $"only {documents} documents were small enough");
    }

    [TestMethod]
    public void DestinationFilledWithDifferentGarbage_NeverChangesTheOutput()
    {
        // The output must depend on the values only: zero, 0xff and a pattern-filled destination give identical bytes.
        for (ulong seed = 1; seed <= 150; seed++)
        {
            var (type, _, expected, schema, script) = Generate(seed * 101);
            foreach (byte fill in new byte[] { 0x00, 0xff, 0x5a })
            {
                var buffer = new byte[expected.Length];
                Array.Fill(buffer, fill);
                Assert.IsTrue(RunWriter(script, schema, buffer, GenerousEncode).Ok);
                CollectionAssert.AreEqual(expected, buffer, $"seed {seed * 101} {type.Text} fill {fill:x2}");
            }
        }
    }

    [TestMethod]
    public void WriterOutput_IsStrictlyCanonical_AcceptedByTheDecoder_AndAnyOneByteSuffixOrTruncationIsNot()
    {
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var (_, _, expected, schema, script) = Generate(seed * 211);
            byte[] written = WriteOk(script, schema);
            DecodeOk(written, schema, Generous);
            DecodeFails([.. written, 0], schema, ABIStatus.NonCanonicalLayout, why: $"seed {seed * 211} +1");
            if (written.Length > 0)
            {
                DecodeFails(written[..^1], schema, ABIStatus.Truncated, why: $"seed {seed * 211} -1");
            }

            Assert.AreEqual(expected.Length, written.Length);
        }
    }

    [TestMethod]
    public void SameSchemaAndScriptTwice_GivesIdenticalBytes_AndTheMeasurerIsRepeatable()
    {
        var (_, _, _, schema, script) = Generate(4242);
        byte[] a = WriteOk(script, schema);
        byte[] b = WriteOk(script, schema);
        CollectionAssert.AreEqual(a, b);
        Assert.AreEqual(RunMeasurer(script, schema, GenerousEncode), RunMeasurer(script, schema, GenerousEncode));
    }
}
