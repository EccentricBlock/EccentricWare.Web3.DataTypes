using EccentricWare.Web3.DataTypes.ValueConverters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Behaviour every EF Core value converter shares: the mapping hints it declares, how caller hints combine with them, and
/// which exception a malformed provider value throws. Per-type behaviour lives in the per-type test classes.
/// </summary>
[TestClass]
public sealed class ValueConverterPolicyTests
{
    private static IEnumerable<Type> ConverterTypes() =>
        typeof(SignatureValueConverter).Assembly.GetExportedTypes()
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false } && typeof(ValueConverter).IsAssignableFrom(t));

    /// <summary>The size hint each converter must declare; null for scalar and variable-length storage.</summary>
    private static readonly Dictionary<Type, int?> ExpectedSizes = new()
    {
        [typeof(AddressValueConverter)] = 33,
        [typeof(FunctionSelectorValueConverter)] = null,
        [typeof(FunctionSelectorBinaryValueConverter)] = 4,
        [typeof(Hash32ValueConverter)] = 32,
        [typeof(HexBigIntegerToBytesConverter)] = null,
        [typeof(HexBytesValueConverter)] = null,
        [typeof(HexBytesStringValueConverter)] = null,
        [typeof(InstructionDiscriminatorValueConverter)] = null,
        [typeof(InstructionDiscriminatorBinaryValueConverter)] = 8,
        [typeof(Int256BytesValueConverter)] = 32,
        [typeof(SignatureValueConverter)] = 66,
        [typeof(UInt256BytesValueConverter)] = 32,
    };

    [TestMethod]
    public void EveryConverter_DeclaresItsStorageSizeHint()
    {
        Type[] types = ConverterTypes().ToArray();
        CollectionAssert.AreEquivalent(ExpectedSizes.Keys.ToArray(), types, "a converter was added without a row in ExpectedSizes");

        foreach (Type type in types)
        {
            var converter = (ValueConverter)Activator.CreateInstance(type)!;
            Assert.AreEqual(ExpectedSizes[type], converter.MappingHints?.Size, type.Name);
        }
    }

    [TestMethod]
    public void EveryConverter_HasAPublicParameterlessConstructor_AndAHintsConstructorThatAcceptsNull()
    {
        // EF instantiates converter types itself (HasConversion<TConverter>()) through a public parameterless constructor.
        Assert.HasCount(12, ConverterTypes());

        foreach (Type type in ConverterTypes())
        {
            Assert.IsNotNull(type.GetConstructor(Type.EmptyTypes), $"{type.Name} needs a public parameterless constructor");
            Assert.IsNotNull(type.GetConstructor([typeof(ConverterMappingHints)]), $"{type.Name} needs a ConverterMappingHints constructor");

            var withNullHints = (ValueConverter)Activator.CreateInstance(type, [null])!;
            var parameterless = (ValueConverter)Activator.CreateInstance(type)!;
            Assert.AreEqual(parameterless.MappingHints?.Size, withNullHints.MappingHints?.Size, type.Name);
        }
    }

    [TestMethod]
    public void EveryConverterWithAHintsConstructor_LaysCallerHintsOverItsDefaults()
    {
        foreach ((Type type, int? size) in ExpectedSizes)
        {
            // A hint the caller leaves unset keeps its default: only the precision is given, the size must survive.
            var precisionOnly = (ValueConverter)Activator.CreateInstance(type, new ConverterMappingHints(precision: 7))!;
            Assert.AreEqual(size, precisionOnly.MappingHints?.Size, $"{type.Name} precision-only");
            Assert.AreEqual(7, precisionOnly.MappingHints?.Precision, $"{type.Name} precision-only");

            // A hint the caller sets wins.
            var sized = (ValueConverter)Activator.CreateInstance(type, new ConverterMappingHints(size: 99))!;
            Assert.AreEqual(99, sized.MappingHints?.Size, $"{type.Name} explicit size");
        }
    }

    [TestMethod]
    public void BinaryConverters_ExposeTheirStorageSize_AsAConstant()
    {
        Assert.AreEqual(32, Hash32ValueConverter.StorageSize);
        Assert.AreEqual(32, UInt256BytesValueConverter.StorageSize);
        Assert.AreEqual(32, Int256BytesValueConverter.StorageSize);
        Assert.AreEqual(66, SignatureValueConverter.StorageSize);
        Assert.AreEqual(4, FunctionSelectorBinaryValueConverter.StorageSize);
        Assert.AreEqual(8, InstructionDiscriminatorBinaryValueConverter.StorageSize);
        Assert.AreEqual(33, AddressValueConverter.MaxPayloadLength);
    }

    // ----------------- provider values -----------------

    /// <param name="Name">Converter name.</param>
    /// <param name="Read">Reads a provider value through the converter's typed delegate.</param>
    /// <param name="Valid">Well-formed provider values with the value each must read as.</param>
    /// <param name="Malformed">Provider values that are not well-formed, each with a description.</param>
    private sealed record Case(string Name, Func<byte[], object?> Read, (byte[] Bytes, object Expected)[] Valid, (string Why, byte[] Bytes)[] Malformed)
    {
        public override string ToString() => Name;
    }

    private static byte[] WithFirstByte(int length, byte first)
    {
        byte[] bytes = new byte[length];
        bytes[0] = first;
        return bytes;
    }

    private static byte[] SolanaZero() => WithFirstByte(33, (byte)AddressType.Solana);

    private static readonly Case[] Cases =
    [
        new("Address", b => new AddressValueConverter().ConvertFromProviderTyped(b),
            [(new byte[21], Address.ZeroEvm), (SolanaZero(), Address.ZeroSolana)],
            [("empty", []), ("unknown type", WithFirstByte(21, 7)), ("EVM too short", new byte[20]), ("EVM too long", new byte[22]),
             ("Solana too short", WithFirstByte(32, (byte)AddressType.Solana)), ("Solana too long", WithFirstByte(34, (byte)AddressType.Solana))]),
        new("FunctionSelectorBinary", b => new FunctionSelectorBinaryValueConverter().ConvertFromProviderTyped(b),
            [(new byte[4], FunctionSelector.Zero), ([0xa9, 0x05, 0x9c, 0xbb], FunctionSelector.Erc20.Transfer)],
            [("empty", []), ("3 bytes", new byte[3]), ("5 bytes", new byte[5]), ("36 bytes", new byte[36])]),
        new("Hash32", b => new Hash32ValueConverter().ConvertFromProviderTyped(b),
            [(new byte[32], Hash32.Zero), (ConverterTestData.Repeated(0xFF, 32), new Hash32(ConverterTestData.Repeated(0xFF, 32)))],
            [("empty", []), ("31 bytes", new byte[31]), ("33 bytes", new byte[33])]),
        new("InstructionDiscriminatorBinary", b => new InstructionDiscriminatorBinaryValueConverter().ConvertFromProviderTyped(b),
            [(new byte[8], InstructionDiscriminator.Zero), (ConverterTestData.Repeated(0xFF, 8), new InstructionDiscriminator(ulong.MaxValue))],
            [("empty", []), ("7 bytes", new byte[7]), ("9 bytes", new byte[9])]),
        new("Int256Bytes", b => new Int256BytesValueConverter().ConvertFromProviderTyped(b),
            [(WithFirstByte(32, 0x80), int256.Zero), (new byte[32], int256.MinValue)],
            [("empty", []), ("31 bytes", new byte[31]), ("33 bytes", new byte[33])]),
        new("UInt256Bytes", b => new UInt256BytesValueConverter().ConvertFromProviderTyped(b),
            [(new byte[32], uint256.Zero), (ConverterTestData.Repeated(0xFF, 32), uint256.MaxValue)],
            [("empty", []), ("31 bytes", new byte[31]), ("33 bytes", new byte[33])]),
        new("Signature", b => new SignatureValueConverter().ConvertFromProviderTyped(b),
            [(new byte[66], Signature.ZeroEvm), (WithFirstByte(66, (byte)SignatureType.Solana), Signature.ZeroSolana)],
            [("empty", []), ("65 bytes", new byte[65]), ("67 bytes", new byte[67]), ("unknown type", WithFirstByte(66, 9)),
             ("Solana row with v", new byte[66].Select((x, i) => i == 0 ? (byte)SignatureType.Solana : i == 65 ? (byte)1 : x).ToArray())]),
    ];

    [TestMethod]
    public void WellFormedProviderValues_ReadAsTheExpectedValue()
    {
        foreach (Case c in Cases)
            foreach ((byte[] bytes, object expected) in c.Valid)
                Assert.AreEqual(expected, c.Read(bytes), $"{c.Name}: {Convert.ToHexString(bytes)}");
    }

    [TestMethod]
    public void MalformedProviderValues_ThrowFormatException_ForEveryConverter()
    {
        foreach (Case c in Cases)
            foreach ((string why, byte[] bytes) in c.Malformed)
                Assert.ThrowsExactly<FormatException>(() => c.Read(bytes), $"{c.Name}: {why}");
    }

    [TestMethod]
    public void NullProviderValue_ThrowsArgumentNullException_ForEveryConverter()
    {
        foreach (Case c in Cases)
            Assert.ThrowsExactly<ArgumentNullException>(() => c.Read(null!), c.Name);
    }

    [TestMethod]
    public void ArbitraryProviderBytes_NeverThrowAnythingButFormatException()
    {
        // Random arrays of every length up to 100, plus every single-byte mutation of a valid payload: a converter may
        // accept the value or reject it with FormatException, never ArgumentException, IndexOutOfRange or Overflow.
        var random = new Random(2026);
        foreach (Case c in Cases)
        {
            var inputs = new List<byte[]>();
            for (int i = 0; i < 2000; i++)
                inputs.Add(ConverterTestData.RandomBytes(random, i % 101));
            foreach ((byte[] valid, _) in c.Valid)
                for (int position = 0; position < valid.Length; position++)
                    foreach (byte replacement in new byte[] { 0x00, 0x01, 0x7F, 0x80, 0xFF })
                    {
                        byte[] mutated = (byte[])valid.Clone();
                        mutated[position] = replacement;
                        inputs.Add(mutated);
                    }

            foreach (byte[] input in inputs)
            {
                try
                {
                    c.Read(input);
                }
                catch (FormatException)
                {
                }
                catch (Exception ex)
                {
                    Assert.Fail($"{c.Name} threw {ex.GetType().Name} for {Convert.ToHexString(input)}: {ex.Message}");
                }
            }
        }
    }
}
