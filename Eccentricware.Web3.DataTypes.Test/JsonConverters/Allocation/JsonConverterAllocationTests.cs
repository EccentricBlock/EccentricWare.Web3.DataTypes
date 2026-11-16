using EccentricWare.Web3.DataTypes.JsonConverters;
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Regression guard for the zero-allocation claim of the fixed-width and 256-bit converters: no heap allocation on any
/// read path (contiguous, padded, escaped, split across segments, pooled-size tokens, dictionary key, number token) or
/// write path. HexBigInteger allocates its BigInteger by design and HexBytes allocates its result array, so they are
/// covered elsewhere.
/// </summary>
/// <remarks>
/// Every path is warmed up with the same input on the same thread, then measured over several trials of 100 calls and the
/// minimum is asserted to be zero: a one-off allocation (JIT tiering, a trimmed pool buffer) can only hit some trials,
/// while a real per-call allocation shows in every trial and fails the assertion.
/// </remarks>
[TestClass]
public sealed class JsonConverterAllocationTests
{
    private const int WarmUpCalls = 200;
    private const int Trials = 5;
    private const int CallsPerTrial = 100;

    private static long BestAllocationPerTrial(Action run)
    {
        for (int i = 0; i < WarmUpCalls; i++)
            run();

        long best = long.MaxValue;
        for (int trial = 0; trial < Trials; trial++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < CallsPerTrial; i++)
                run();
            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return best;
    }

    private static string EscapeFirst(string text) => JsonTokenBodies.EscapeFirst(text);

    private static void AssertNoAllocation<T>(
        string name,
        JsonConverter<T> converter,
        T value,
        string text,
        string? numberText = null)
        where T : struct
    {
        var options = new JsonSerializerOptions();
        string pooledPadding = new(' ', 300); // pushes an escaped or split token past the 256-byte stack buffer

        byte[] contiguous = JsonTokenBodies.Value(text);
        byte[] padded = JsonTokenBodies.Value("  " + text + "   "); // literal spaces: unescaped, trimmed in place
        byte[] escaped = JsonTokenBodies.Value(EscapeFirst(text));
        byte[] pooledEscaped = JsonTokenBodies.Value(EscapeFirst(text) + pooledPadding);
        byte[] key = JsonTokenBodies.Key(text);
        byte[] escapedKey = JsonTokenBodies.Key(EscapeFirst(text));
        byte[] pooledEscapedKey = JsonTokenBodies.Key(EscapeFirst(text) + pooledPadding);
        ReadOnlySequence<byte> split = SegmentedUtf8Json.SplitAt(contiguous, 6);
        ReadOnlySequence<byte> pooledSplit = SegmentedUtf8Json.SplitAt(JsonTokenBodies.Value(text + pooledPadding), 6);
        ReadOnlySequence<byte> splitKey = SegmentedUtf8Json.SplitAt(key, 6);
        var buffer = new ArrayBufferWriter<byte>(1024);
        using var writer = new Utf8JsonWriter(buffer);

        T ReadValue(byte[] json)
        {
            var reader = new Utf8JsonReader(json);
            reader.Read();
            return converter.Read(ref reader, typeof(T), options);
        }

        T ReadKey(byte[] json)
        {
            var reader = new Utf8JsonReader(json);
            reader.Read();
            reader.Read();
            return converter.ReadAsPropertyName(ref reader, typeof(T), options);
        }

        T ReadSequence(ReadOnlySequence<byte> sequence, bool asKey)
        {
            Utf8JsonReader reader = SegmentedUtf8Json.Reader(sequence);
            reader.Read();
            if (asKey)
            {
                reader.Read();
                Assert.IsTrue(reader.HasValueSequence, "test setup: the key must span segments");
                return converter.ReadAsPropertyName(ref reader, typeof(T), options);
            }

            Assert.IsTrue(reader.HasValueSequence, "test setup: the value must span segments");
            return converter.Read(ref reader, typeof(T), options);
        }

        void WriteValue()
        {
            buffer.ResetWrittenCount();
            writer.Reset(buffer);
            converter.Write(writer, value, options);
            writer.Flush();
        }

        void WriteKey()
        {
            buffer.ResetWrittenCount();
            writer.Reset(buffer);
            writer.WriteStartObject();
            converter.WriteAsPropertyName(writer, value, options);
            writer.WriteNumberValue(1);
            writer.WriteEndObject();
            writer.Flush();
        }

        var paths = new List<(string Path, Action Run, Func<T>? Check)>
        {
            ("read", () => ReadValue(contiguous), () => ReadValue(contiguous)),
            ("read padded (literal spaces)", () => ReadValue(padded), () => ReadValue(padded)),
            ("read escaped", () => ReadValue(escaped), () => ReadValue(escaped)),
            ("read escaped, pooled size", () => ReadValue(pooledEscaped), () => ReadValue(pooledEscaped)),
            ("read split", () => ReadSequence(split, asKey: false), () => ReadSequence(split, asKey: false)),
            ("read split, pooled size", () => ReadSequence(pooledSplit, asKey: false), () => ReadSequence(pooledSplit, asKey: false)),
            ("read key", () => ReadKey(key), () => ReadKey(key)),
            ("read escaped key", () => ReadKey(escapedKey), () => ReadKey(escapedKey)),
            ("read escaped key, pooled size", () => ReadKey(pooledEscapedKey), () => ReadKey(pooledEscapedKey)),
            ("read split key", () => ReadSequence(splitKey, asKey: true), () => ReadSequence(splitKey, asKey: true)),
            ("write", WriteValue, null),
            ("write key", WriteKey, null),
        };

        if (numberText is not null)
        {
            byte[] number = Encoding.ASCII.GetBytes(numberText);
            ReadOnlySequence<byte> splitNumber = SegmentedUtf8Json.SplitAt(number, Math.Max(1, number.Length / 2));
            paths.Add(("read number", () => ReadValue(number), () => ReadValue(number)));
            paths.Add(("read number, split", () => ReadSequence(splitNumber, asKey: false), () => ReadSequence(splitNumber, asKey: false)));
        }

        // Correctness first, so a "zero allocation" result cannot come from skipping the work.
        foreach ((string path, _, Func<T>? check) in paths)
        {
            if (check is not null)
                Assert.AreEqual(value, check(), $"{name} {path}");
        }

        foreach ((string path, Action run, _) in paths)
        {
            long best = BestAllocationPerTrial(run);
            Assert.AreEqual(0L, best, $"{name} {path}: at least {best} bytes per {CallsPerTrial} calls in every trial");
        }
    }

    [TestMethod]
    public void Address_Evm_DoesNotAllocate() =>
        AssertNoAllocation("Address(evm)", new AddressJsonConverter(), Address.FromEvmBytes(Convert.FromHexString(new string('a', 40))), "0x" + new string('a', 40));

    [TestMethod]
    public void Address_Solana_DoesNotAllocate() =>
        AssertNoAllocation("Address(solana)", new AddressJsonConverter(),
            Address.FromSolanaBytes(Convert.FromHexString(JsonConverterCases.SolanaTokenProgramHex)), JsonConverterCases.SolanaTokenProgramBase58);

    [TestMethod]
    public void FunctionSelector_DoesNotAllocate() =>
        AssertNoAllocation("FunctionSelector", new FunctionSelectorJsonConverter(), new FunctionSelector(0xA9059CBBu), "0xa9059cbb");

    [TestMethod]
    public void InstructionDiscriminator_DoesNotAllocate() =>
        AssertNoAllocation("InstructionDiscriminator", new InstructionDiscriminatorJsonConverter(), new InstructionDiscriminator(0xAFAF6D1F0D989BEDUL), "0xafaf6d1f0d989bed");

    [TestMethod]
    public void Hash32_Hex_DoesNotAllocate() =>
        AssertNoAllocation("Hash32(hex)", new Hash32JsonConverter(), new Hash32(Convert.FromHexString(JsonConverterCases.AscendingHash32Hex)), "0x" + JsonConverterCases.AscendingHash32Hex);

    [TestMethod]
    public void Hash32_Base58_DoesNotAllocate() =>
        AssertNoAllocation("Hash32(base58)", new Hash32JsonConverter(), new Hash32(Convert.FromHexString(JsonConverterCases.AscendingHash32Hex)), JsonConverterCases.AscendingHash32Base58);

    [TestMethod]
    public void Hash32_PaddedBase64_DoesNotAllocate() =>
        AssertNoAllocation("Hash32(padded base64)", new Hash32JsonConverter(), new Hash32(Convert.FromHexString(JsonConverterCases.AscendingHash32Hex)), JsonConverterCases.AscendingHash32Base64);

    [TestMethod]
    public void Hash32_WithoutPrefix_DoesNotAllocate() =>
        AssertNoAllocation("Hash32(no prefix)", new Hash32JsonConverter(writeWith0xPrefix: false), new Hash32(Convert.FromHexString(JsonConverterCases.AscendingHash32Hex)), JsonConverterCases.AscendingHash32Hex);

    [TestMethod]
    public void Signature_Hex_DoesNotAllocate() =>
        AssertNoAllocation("Signature(hex)", new SignatureJsonConverter(), Signature.FromEvmBytes(Convert.FromHexString(new string('c', 130))), "0x" + new string('c', 130));

    [TestMethod]
    public void Signature_SolanaBase58_ReadAndWrite_DoNotAllocate() =>
        AssertNoAllocation("Signature(solana base58)", new SignatureJsonConverter(writeSolanaAsBase58: true),
            Signature.FromSolanaBytes(Convert.FromHexString(JsonConverterCases.SolanaSignatureHex)), JsonConverterCases.SolanaSignatureBase58);

    [TestMethod]
    public void UInt256_Hex_DoesNotAllocate() =>
        AssertNoAllocation("uint256(hex)", new UInt256JsonConverter(), uint256.MaxValue, "0x" + new string('f', 64), numberText: JsonConverterCases.Uint256MaxDecimal);

    [TestMethod]
    public void UInt256_Decimal_DoesNotAllocate() =>
        AssertNoAllocation("uint256(decimal)", new UInt256JsonConverter(), uint256.MaxValue, JsonConverterCases.Uint256MaxDecimal);

    [TestMethod]
    public void Int256_Hex_DoesNotAllocate() =>
        AssertNoAllocation("int256(hex)", new Int256JsonConverter(), new int256(-1L), "0x" + new string('f', 64), numberText: "-1");

    [TestMethod]
    public void Int256_Decimal_DoesNotAllocate() =>
        AssertNoAllocation("int256(decimal)", new Int256JsonConverter(), int256.MinValue, JsonConverterCases.Int256MinDecimal, numberText: JsonConverterCases.Int256MinDecimal);
}
