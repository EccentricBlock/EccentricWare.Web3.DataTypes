using System.Buffers.Binary;
using System.Text;
using EccentricWare.Web3.DataTypes.Utils;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// The 256-bit Base58 codec used by <see cref="Address"/> (<see cref="ByteUtils.TryDecodeBase58ToUInt256BigEndian"/> and
/// <see cref="ByteUtils.EncodeBase58UInt256BigEndianToUtf8"/>) against known vectors, failure behaviour and buffer limits.
/// </summary>
[TestClass]
public sealed class ByteUtils_Base58AddressVectorTests
{
    // Base58 text, 32-byte payload. The first two are real on-chain keys; the rest come from an independent Python
    // Base58 reference.
    [TestMethod]
    [DataRow("11111111111111111111111111111111", "0000000000000000000000000000000000000000000000000000000000000000")]
    [DataRow("11111111111111111111111111111112", "0000000000000000000000000000000000000000000000000000000000000001")]
    [DataRow("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA", "06ddf6e1d765a193d9cbe146ceeb79ac1cb485ed5f5b37913a8cf5857eff00a9")]
    [DataRow("JEKNVnkbo3jma5nREBBJCDoXFVeKkD56V3xKrvRmWxFG", "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")]
    [DataRow("1thX6LZfHDZZKUs92febYZhYRcXddmzfzF2NvTkPNE", "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f")]
    [DataRow("1111111112N8uVh5wJmg3Fwjpm5WQKLVUS4kug7V", "000000000000000000012392d9cec411421f7fc37479a762ca36197d08e5d664")]
    public void KnownVectors_EncodeAndDecode_InBothDirections(string base58, string payloadHex)
    {
        byte[] payload = Convert.FromHexString(payloadHex);
        ulong[] limbs = Limbs(payload);

        Assert.IsTrue(ByteUtils.TryDecodeBase58ToUInt256BigEndian(Encoding.ASCII.GetBytes(base58), out ulong u0, out ulong u1, out ulong u2, out ulong u3));
        CollectionAssert.AreEqual(limbs, new[] { u0, u1, u2, u3 });

        byte[] destination = new byte[Address.MaxBase58Length];
        int written = ByteUtils.EncodeBase58UInt256BigEndianToUtf8(limbs[0], limbs[1], limbs[2], limbs[3], destination);
        Assert.AreEqual(base58, Encoding.ASCII.GetString(destination, 0, written));
    }

    [TestMethod]
    public void Decode_LeavesOutputsZero_OnFailure()
    {
        // Overflow found in a later chunk, an invalid digit and a non-canonical value all fail with zeroed outputs.
        foreach (string text in new[] { new string('z', 44), new string('1', 5) + new string('z', 38) + "0", "1TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA" })
        {
            Assert.IsFalse(ByteUtils.TryDecodeBase58ToUInt256BigEndian(Encoding.ASCII.GetBytes(text), out ulong u0, out ulong u1, out ulong u2, out ulong u3), text);
            Assert.AreEqual(0UL, u0 | u1 | u2 | u3, text);
        }
    }

    [TestMethod]
    public void Encode_DestinationTooSmall_ReturnsMinusOne_AndWritesNothing()
    {
        // TokenkegQ... needs 43 characters.
        ulong[] limbs = Limbs(Convert.FromHexString("06ddf6e1d765a193d9cbe146ceeb79ac1cb485ed5f5b37913a8cf5857eff00a9"));

        byte[] destination = Enumerable.Repeat((byte)'#', 42).ToArray();
        Assert.AreEqual(-1, ByteUtils.EncodeBase58UInt256BigEndianToUtf8(limbs[0], limbs[1], limbs[2], limbs[3], destination));
        Assert.IsTrue(destination.All(b => b == (byte)'#'));

        byte[] exact = new byte[43];
        Assert.AreEqual(43, ByteUtils.EncodeBase58UInt256BigEndianToUtf8(limbs[0], limbs[1], limbs[2], limbs[3], exact));
        Assert.AreEqual("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA", Encoding.ASCII.GetString(exact));
    }

    private static ulong[] Limbs(byte[] payload) =>
    [
        BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(0, 8)),
        BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(8, 8)),
        BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(16, 8)),
        BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(24, 8)),
    ];
}
