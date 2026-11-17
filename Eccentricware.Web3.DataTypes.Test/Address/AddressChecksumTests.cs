using System.Globalization;
using System.Text;
using EccentricWare.Web3.DataTypes.Utils;
using static EccentricWare.Web3.DataTypes.Tests.AddressTestHelpers;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>EIP-55 checksum encoding of EVM addresses: specification vectors, extra reference vectors, invariants and the lenient parse contract.</summary>
[TestClass]
public sealed class AddressChecksumTests
{
    // The complete EIP-55 test vector set: all caps, all lower, and mixed case.
    [TestMethod]
    [DataRow("0x52908400098527886E0F7030069857D2E4169EE7")]
    [DataRow("0x8617E340B3D01FA5F11F306F4090FD50E238070D")]
    [DataRow("0xde709f2102306220921060314715629080e2fb77")]
    [DataRow("0x27b1fdb04752bbc536007a920d24acb045561c26")]
    [DataRow("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed")]
    [DataRow("0xfB6916095ca1df60bB79Ce92cE3Ea74c37c5d359")]
    [DataRow("0xdbF03B407c01E7cD3CBea99509d93f8DDDC8C6FB")]
    [DataRow("0xD1220A0cf47c7B9Be7A2E6BA89F429762e7b9aDb")]
    public void Checksum_Eip55Vectors_AllEntryPoints(string expected)
    {
        Address a = Address.FromEvmBytes(Convert.FromHexString(expected.Substring(2)));

        AssertAllEntryPointsProduce(a, expected);

        // The checksummed text parses back to the same address.
        Assert.AreEqual(a, Address.Parse(expected));
    }

    // Lowercase payload -> expected checksum, from an independent pure-Python Keccak-256 (verified against the empty-input
    // vector and the eight EIP-55 specification vectors above). Covers the all-ones, digit-only and repeating-letter edges.
    [TestMethod]
    [DataRow("ffffffffffffffffffffffffffffffffffffffff", "0xFFfFfFffFFfffFFfFFfFFFFFffFFFffffFfFFFfF")]
    [DataRow("fffffffffffffffffffffffffffffffffffffffe", "0xffffFFFfFFffffffffffffffFfFFFfffFFFfFFfE")]
    [DataRow("abcdefabcdefabcdefabcdefabcdefabcdefabcd", "0xABcdEFABcdEFabcdEfAbCdefabcdeFABcDEFabCD")]
    [DataRow("deadbeefdeadbeefdeadbeefdeadbeefdeadbeef", "0xDeaDbeefdEAdbeefdEadbEEFdeadbeEFdEaDbeeF")]
    [DataRow("0000000000000000000000000000000000000001", "0x0000000000000000000000000000000000000001")]
    [DataRow("0000000000000000000000000000000000000000", "0x0000000000000000000000000000000000000000")]
    [DataRow("0123456789012345678901234567890123456789", "0x0123456789012345678901234567890123456789")]
    [DataRow("00000000000000000000000000000000000000ff", "0x00000000000000000000000000000000000000ff")]
    public void Checksum_IndependentReferenceVectors(string lower, string expected)
    {
        AssertAllEntryPointsProduce(Address.FromEvmBytes(Convert.FromHexString(lower)), expected);
    }

    // The branch-free case fold must equal the plain per-digit rule on every input: digit i is upper-cased when it is a letter and
    // hash nibble i (high nibble first) is >= 8. Random addresses exercise every letter/nibble combination; the structured ones
    // cover all-digit, all-letter, single-bit and boundary values.
    [TestMethod]
    public void Checksum_MatchesThePerDigitReferenceRule_ForRandomAndStructuredAddresses()
    {
        var rng = new Random(55);
        var bytes = new byte[Address.EvmByteLength];
        int checkedCount = 0;

        void Check(byte[] raw)
        {
            string lower = Convert.ToHexStringLower(raw);
            Assert.AreEqual("0x" + ReferenceChecksum(lower), Address.FromEvmBytes(raw).ToChecksumString(), lower);
            checkedCount++;
        }

        for (int i = 0; i < 20_000; i++)
        {
            rng.NextBytes(bytes);
            Check(bytes);
        }

        Check(new byte[Address.EvmByteLength]);
        Check(Enumerable.Repeat((byte)0xFF, Address.EvmByteLength).ToArray());
        Check(Enumerable.Repeat((byte)0xAB, Address.EvmByteLength).ToArray());
        Check(Enumerable.Repeat((byte)0x99, Address.EvmByteLength).ToArray());
        for (int bit = 0; bit < Address.EvmByteLength * 8; bit++)
        {
            var single = new byte[Address.EvmByteLength];
            single[bit >> 3] = (byte)(0x80 >> (bit & 7));
            Check(single);
        }

        Assert.IsTrue(checkedCount >= 20_000 + 4 + 160);
    }

    private static string ReferenceChecksum(string lowerHex40)
    {
        byte[] hash = new byte[32];
        Keccak256.ComputeHash(Encoding.ASCII.GetBytes(lowerHex40), hash);
        var chars = lowerHex40.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            int nibble = (hash[i >> 1] >> ((i & 1) == 0 ? 4 : 0)) & 0x0F;
            if (chars[i] >= 'a' && nibble >= 8)
                chars[i] = char.ToUpperInvariant(chars[i]);
        }

        return new string(chars);
    }

    private static void AssertAllEntryPointsProduce(Address a, string expected)
    {
        Assert.AreEqual(expected, a.ToChecksumString());
        Assert.AreEqual(expected, a.ToString("c", CultureInfo.InvariantCulture));
        Assert.AreEqual(expected, a.ToString("C", CultureInfo.InvariantCulture));

        Span<char> chars = stackalloc char[Address.EvmHexLength + 2];
        Assert.IsTrue(a.TryFormat(chars, out int charsWritten, "C", CultureInfo.InvariantCulture));
        Assert.AreEqual(expected, new string(chars.Slice(0, charsWritten)));

        Span<byte> utf8 = stackalloc byte[Address.EvmHexLength + 2];
        Assert.IsTrue(a.TryFormat(utf8, out int bytesWritten, "c", CultureInfo.InvariantCulture));
        Assert.AreEqual(expected, Encoding.ASCII.GetString(utf8.Slice(0, bytesWritten)));
    }

    [TestMethod]
    public void Checksum_Invariants_LowercaseMatchesToString_AndIsIdempotent()
    {
        foreach (Address a in RandomEvm(200, 551))
        {
            string checksum = a.ToChecksumString();

            Assert.AreEqual(a.ToString(), checksum.ToLowerInvariant());
            Assert.AreEqual(checksum, Address.Parse(checksum).ToChecksumString());
            Assert.AreEqual(a, Address.Parse(checksum));
        }
    }

    // The case pattern follows the spec rule: hash the lowercase hex text with Keccak-256 and upper-case digit i when
    // hash nibble i is >= 8. Uses the library's Keccak256 (covered by its own tests) as the hash primitive.
    [TestMethod]
    public void Checksum_CasePattern_FollowsTheEip55Rule_ForRandomAddresses()
    {
        byte[] hash = new byte[32];

        foreach (Address a in RandomEvm(2_000, 552))
        {
            string lower = Convert.ToHexStringLower(a.ToBytes());
            Keccak256.ComputeHash(Encoding.ASCII.GetBytes(lower), hash);
            string hashHex = Convert.ToHexStringLower(hash);

            var expected = new StringBuilder("0x");
            for (int i = 0; i < lower.Length; i++)
                expected.Append(char.IsAsciiLetter(lower[i]) && hashHex[i] >= '8' ? char.ToUpperInvariant(lower[i]) : lower[i]);

            Assert.AreEqual(expected.ToString(), a.ToChecksumString());
        }
    }

    // Documents current, deliberate behaviour (Address.Parse remarks): mixed case is accepted without verifying the
    // checksum and normalised to lower case. A future checksum validation would have to change this test on purpose.
    [TestMethod]
    public void Parse_ChecksummedString_Normalises_ToLower_On_ToString()
    {
        Address valid = Address.Parse("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed");
        Address wrongCase = Address.Parse("0x5AAeb6053F3E94C9b9A09f33669435E7Ef1BeAed");

        Assert.AreEqual(AddressType.Evm, valid.Type);
        Assert.AreEqual("0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed", valid.ToString());
        Assert.AreEqual(valid, wrongCase);
        Assert.AreEqual("0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed", wrongCase.ToString());
    }

    [TestMethod]
    public void ToChecksumString_NonEvm_Throws()
    {
        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>(() => Address.ZeroSolana.ToChecksumString());

        StringAssert.Contains(ex.Message, "EVM");
    }
}
