using EccentricWare.Web3.DataTypes.ValueConverters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>EF Core value converter for <see cref="Address"/>: 1 type byte followed by the 20 / 32 payload bytes.</summary>
[TestClass]
public sealed class AddressValueConverterTests
{
    private const string EvmHex = "52908400098527886e0f7030069857d2e4169ee7";

    // Base58 "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA" decoded by an independent reference implementation (python).
    private const string SolanaHex = "06ddf6e1d765a193d9cbe146ceeb79ac1cb485ed5f5b37913a8cf5857eff00a9";

    private static readonly Address Evm = Address.Parse("0x" + EvmHex);
    private static readonly Address Solana = Address.Parse("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
    private static readonly AddressValueConverter Converter = new();

    // ----------------- layout -----------------

    [TestMethod]
    public void ToProvider_Evm_IsTypeByteThen20Bytes()
    {
        byte[] payload = AddressValueConverter.AddressToBytes(Evm);

        Assert.HasCount(1 + Address.EvmByteLength, payload);
        CollectionAssert.AreEqual(Convert.FromHexString("00" + EvmHex), payload);
    }

    [TestMethod]
    public void ToProvider_Solana_IsTypeByteThen32Bytes()
    {
        byte[] payload = AddressValueConverter.AddressToBytes(Solana);

        Assert.HasCount(1 + Address.SolanaByteLength, payload);
        CollectionAssert.AreEqual(Convert.FromHexString("01" + SolanaHex), payload);
    }

    [TestMethod]
    public void ToProvider_ZeroAndDefault_AreDistinguishedByTypeByteAndLength()
    {
        CollectionAssert.AreEqual(new byte[21], AddressValueConverter.AddressToBytes(Address.ZeroEvm));
        CollectionAssert.AreEqual(new byte[21], AddressValueConverter.AddressToBytes(default));

        byte[] zeroSolana = new byte[33];
        zeroSolana[0] = 1;
        CollectionAssert.AreEqual(zeroSolana, AddressValueConverter.AddressToBytes(Address.ZeroSolana));
    }

    [TestMethod]
    public void ToProvider_AllOnesAddresses_KeepTheirPayloadAndType()
    {
        CollectionAssert.AreEqual(Convert.FromHexString("00" + new string('f', 40)),
            AddressValueConverter.AddressToBytes(Address.FromEvmBytes(ConverterTestData.Repeated(0xFF, 20))));
        CollectionAssert.AreEqual(Convert.FromHexString("01" + new string('f', 64)),
            AddressValueConverter.AddressToBytes(Address.FromSolanaBytes(ConverterTestData.Repeated(0xFF, 32))));
    }

    [TestMethod]
    public void ToProvider_ReturnsAFreshArrayEveryCall()
    {
        byte[] first = AddressValueConverter.AddressToBytes(Evm);
        first[1] = 0;

        Assert.AreEqual(0x52, AddressValueConverter.AddressToBytes(Evm)[1]);
    }

    [TestMethod]
    public void MaxPayloadLength_IsTheLongestPayloadThatIsProduced()
    {
        Assert.AreEqual(33, AddressValueConverter.MaxPayloadLength);
        Assert.AreEqual(AddressValueConverter.MaxPayloadLength, AddressValueConverter.AddressToBytes(Solana).Length);
        Assert.IsLessThan(AddressValueConverter.MaxPayloadLength, AddressValueConverter.AddressToBytes(Evm).Length);
    }

    // ----------------- round trip -----------------

    [TestMethod]
    public void RoundTrip_PreservesValueAndFamily_IncludingZeroAddresses()
    {
        foreach (Address value in new[] { Evm, Solana, Address.ZeroEvm, Address.ZeroSolana, default, Address.FromEvmBytes(ConverterTestData.Repeated(0xFF, 20)), Address.FromSolanaBytes(ConverterTestData.Repeated(0xFF, 32)) })
        {
            Address back = AddressValueConverter.BytesToAddress(AddressValueConverter.AddressToBytes(value));

            Assert.AreEqual(value, back);
            Assert.AreEqual(value.Type, back.Type);
        }
    }

    [TestMethod]
    public void RoundTrip_RandomAddresses_BothFamilies()
    {
        var random = new Random(33);
        for (int i = 0; i < 500; i++)
        {
            Address value = i % 2 == 0
                ? Address.FromEvmBytes(ConverterTestData.RandomBytes(random, 20))
                : Address.FromSolanaBytes(ConverterTestData.RandomBytes(random, 32));

            Assert.AreEqual(value, Converter.ConvertFromProviderTyped(Converter.ConvertToProviderTyped(value)));
        }
    }

    [TestMethod]
    public void ConverterExpressions_RoundTrip()
    {
        Func<Address, byte[]> toProvider = Converter.ConvertToProviderExpression.Compile();
        Func<byte[], Address> fromProvider = Converter.ConvertFromProviderExpression.Compile();

        Assert.AreEqual(Solana, fromProvider(toProvider(Solana)));
        Assert.AreEqual(Evm, fromProvider(toProvider(Evm)));
    }

    [TestMethod]
    public void Untyped_RoundTripsABoxedValue_AndPassesNullThrough()
    {
        object? provider = Converter.ConvertToProvider(Solana);

        Assert.IsInstanceOfType<byte[]>(provider);
        Assert.AreEqual(Solana, Converter.ConvertFromProvider(provider));
        Assert.IsNull(Converter.ConvertToProvider(null));
        Assert.IsNull(Converter.ConvertFromProvider(null));
    }

    // ----------------- ordering -----------------

    [TestMethod]
    public void StoredBytes_SortLikeTheAddress_TypeFirst()
    {
        Address[] ascending =
        [
            Address.ZeroEvm,
            Address.FromEvmBytes(Convert.FromHexString("0000000000000000000000000000000000000001")),
            Evm,
            Address.FromEvmBytes(ConverterTestData.Repeated(0xFF, 20)),
            Address.ZeroSolana,
            Address.FromSolanaBytes(Convert.FromHexString(new string('0', 62) + "01")),
            Solana,
            Address.FromSolanaBytes(ConverterTestData.Repeated(0xFF, 32)),
        ];

        for (int i = 0; i < ascending.Length; i++)
            for (int j = 0; j < ascending.Length; j++)
            {
                int stored = ConverterTestData.Sign(Converter.ConvertToProviderTyped(ascending[i]), Converter.ConvertToProviderTyped(ascending[j]));
                Assert.AreEqual(i.CompareTo(j), stored, $"index {i} vs {j}");
                Assert.AreEqual(i.CompareTo(j), Math.Sign(ascending[i].CompareTo(ascending[j])), $"Address.CompareTo index {i} vs {j}");
            }
    }

    // ----------------- invalid data -----------------

    [TestMethod]
    public void FromProvider_Null_ThrowsArgumentNullException_AndEmpty_ThrowsFormatException()
    {
        Assert.AreEqual("payload", Assert.ThrowsExactly<ArgumentNullException>(() => AddressValueConverter.BytesToAddress(null!)).ParamName);
        Assert.ThrowsExactly<FormatException>(() => AddressValueConverter.BytesToAddress([]));
    }

    [TestMethod]
    public void FromProvider_WrongPayloadLength_ThrowsFormatException()
    {
        Assert.ThrowsExactly<FormatException>(() => AddressValueConverter.BytesToAddress([(byte)AddressType.Evm]));
        Assert.ThrowsExactly<FormatException>(() => AddressValueConverter.BytesToAddress(new byte[1 + Address.EvmByteLength - 1]));
        Assert.ThrowsExactly<FormatException>(() => AddressValueConverter.BytesToAddress(NewPayload(AddressType.Evm, Address.SolanaByteLength)));
        Assert.ThrowsExactly<FormatException>(() => AddressValueConverter.BytesToAddress(NewPayload(AddressType.Solana, Address.EvmByteLength)));
        Assert.ThrowsExactly<FormatException>(() => AddressValueConverter.BytesToAddress(NewPayload(AddressType.Solana, Address.SolanaByteLength + 1)));
        Assert.ThrowsExactly<FormatException>(() => AddressValueConverter.BytesToAddress(NewPayload(AddressType.Evm, 1024 * 1024)));
    }

    [TestMethod]
    public void FromProvider_UnknownAddressType_ThrowsFormatException()
    {
        foreach (byte type in new byte[] { 2, 3, 0x7F, 0xFF })
        {
            byte[] payload = NewPayload(AddressType.Evm, Address.EvmByteLength);
            payload[0] = type;
            Assert.ThrowsExactly<FormatException>(() => AddressValueConverter.BytesToAddress(payload), $"type byte {type}");

            payload = NewPayload(AddressType.Solana, Address.SolanaByteLength);
            payload[0] = type;
            Assert.ThrowsExactly<FormatException>(() => AddressValueConverter.BytesToAddress(payload), $"type byte {type} with a Solana-length payload");
        }
    }

    // ----------------- hints -----------------

    [TestMethod]
    public void MappingHints_DefaultToTheMaximumPayloadSize_CallerSizeWins_AndOtherHintsAreKept()
    {
        Assert.AreEqual(33, Converter.MappingHints?.Size);

        Assert.AreEqual(64, new AddressValueConverter(new ConverterMappingHints(size: 64)).MappingHints?.Size);

        // Hints that do not mention a size still get the default size.
        var withoutSize = new AddressValueConverter(new ConverterMappingHints(unicode: false));
        Assert.AreEqual(33, withoutSize.MappingHints?.Size);
        Assert.AreEqual(false, withoutSize.MappingHints?.IsUnicode);
    }

    private static byte[] NewPayload(AddressType type, int addressLength)
    {
        byte[] payload = new byte[1 + addressLength];
        payload[0] = (byte)type;
        return payload;
    }
}
