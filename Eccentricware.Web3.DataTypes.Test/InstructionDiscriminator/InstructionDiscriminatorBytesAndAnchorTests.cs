using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Constants, layout, byte construction/extraction boundaries, and real Anchor discriminators.</summary>
[TestClass]
public sealed class InstructionDiscriminatorBytesAndAnchorTests
{
    private static readonly byte[] AnchorInitializeBytes = [0xaf, 0xaf, 0x6d, 0x1f, 0x0d, 0x98, 0x9b, 0xed];

    // ----------------- constants and layout -----------------

    [TestMethod]
    public void Constants_AndLayout_DescribeAnEightByteSixteenDigitValue()
    {
        Assert.AreEqual(8, InstructionDiscriminator.ByteLength);
        Assert.AreEqual(16, InstructionDiscriminator.HexLength);
        Assert.AreEqual(InstructionDiscriminator.ByteLength, Unsafe.SizeOf<InstructionDiscriminator>());
        Assert.AreEqual(InstructionDiscriminator.ByteLength, Marshal.SizeOf<InstructionDiscriminator>());

        InstructionDiscriminator[] values = [new(0x0102030405060708UL), InstructionDiscriminator.Zero, new(ulong.MaxValue)];
        CollectionAssert.AreEqual(new[] { 0x0102030405060708UL, 0UL, ulong.MaxValue }, MemoryMarshal.Cast<InstructionDiscriminator, ulong>(values).ToArray());
        Assert.AreEqual(values.Length * 8, MemoryMarshal.AsBytes<InstructionDiscriminator>(values).Length);
    }

    [TestMethod]
    public void ZeroAndDefault_AreTheAllZeroValue_AndFormatToSixteenZeros()
    {
        Assert.AreEqual(default, InstructionDiscriminator.Zero);
        Assert.AreEqual(0UL, InstructionDiscriminator.Zero.Value);
        Assert.AreEqual("0x0000000000000000", InstructionDiscriminator.Zero.ToString());
    }

    // ----------------- construction from bytes -----------------

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(4)]
    [DataRow(7)]
    public void SpanConstructor_TooShort_ThrowsWithParameterNameAndMessage(int length)
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() => _ = new InstructionDiscriminator(AnchorInitializeBytes.AsSpan(0, length)));

        Assert.AreEqual("bytes", ex.ParamName);
        StringAssert.Contains(ex.Message, "8 bytes");
    }

    [TestMethod]
    public void SpanConstructor_ExactAndLonger_ReadTheFirstEightBytesBigEndian()
    {
        Assert.AreEqual(0xafaf6d1f0d989bedUL, new InstructionDiscriminator(AnchorInitializeBytes).Value);
        Assert.AreEqual(0xafaf6d1f0d989bedUL, new InstructionDiscriminator([.. AnchorInitializeBytes, 0xde, 0xad, 0xbe, 0xef]).Value);
        Assert.AreEqual(0xaf6d1f0d989bed11UL, new InstructionDiscriminator(((byte[])[.. AnchorInitializeBytes, 0x11]).AsSpan(1)).Value);
    }

    [TestMethod]
    public void SpanConstructor_PlacesEveryByteInItsOwnLane()
    {
        for (int lane = 0; lane < 8; lane++)
        {
            byte[] bytes = new byte[8];
            bytes[lane] = 0xAB;
            Assert.AreEqual(0xABUL << (8 * (7 - lane)), new InstructionDiscriminator(bytes).Value, $"lane {lane}");
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(7)]
    public void FromInstructionData_TooShort_ThrowsWithParameterName(int length)
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() => InstructionDiscriminator.FromInstructionData(AnchorInitializeBytes.AsSpan(0, length)));
        Assert.AreEqual("instructionData", ex.ParamName);
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(7, false)]
    [DataRow(8, true)]
    public void FromInstructionData_AndTryFromInstructionData_AgreeAtTheLengthBoundary(int length, bool valid)
    {
        ReadOnlySpan<byte> data = AnchorInitializeBytes.AsSpan(0, length);

        Assert.AreEqual(valid, InstructionDiscriminator.TryFromInstructionData(data, out var discriminator));
        Assert.AreEqual(valid ? 0xafaf6d1f0d989bedUL : 0UL, discriminator.Value);
        if (valid)
            Assert.AreEqual(discriminator, InstructionDiscriminator.FromInstructionData(data));
    }

    [TestMethod]
    public void TryFromInstructionData_LongData_TakesOnlyTheDiscriminatorPrefix()
    {
        byte[] data = new byte[8 + 100];
        AnchorInitializeBytes.CopyTo(data, 0);
        data.AsSpan(8).Fill(0xFF);

        Assert.IsTrue(InstructionDiscriminator.TryFromInstructionData(data, out var discriminator));
        Assert.AreEqual(0xafaf6d1f0d989bedUL, discriminator.Value);
    }

    // ----------------- WriteBytes -----------------

    [TestMethod]
    public void WriteBytes_ExactSize_WritesBigEndian_AndRoundTripsThroughTheConstructor()
    {
        foreach (ulong value in new[] { 0UL, 1UL, 0xafaf6d1f0d989bedUL, 0x8000000000000000UL, ulong.MaxValue })
        {
            byte[] destination = new byte[8];
            new InstructionDiscriminator(value).WriteBytes(destination);

            byte[] expected = new byte[8];
            for (int i = 0; i < 8; i++)
                expected[i] = (byte)(value >> (8 * (7 - i)));
            CollectionAssert.AreEqual(expected, destination);
            Assert.AreEqual(value, new InstructionDiscriminator(destination).Value);
        }
    }

    [TestMethod]
    public void WriteBytes_LargerDestination_WritesOnlyTheFirstEightBytes()
    {
        byte[] destination = new byte[11];
        destination.AsSpan().Fill(9);
        new InstructionDiscriminator(0xafaf6d1f0d989bedUL).WriteBytes(destination);

        CollectionAssert.AreEqual(new byte[] { 0xaf, 0xaf, 0x6d, 0x1f, 0x0d, 0x98, 0x9b, 0xed, 9, 9, 9 }, destination);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(7)]
    public void WriteBytes_TooSmall_ThrowsWithParameterName_AndLeavesTheDestinationUntouched(int length)
    {
        byte[] destination = new byte[7];
        destination.AsSpan().Fill(7);
        var ex = Assert.ThrowsExactly<ArgumentException>(() => new InstructionDiscriminator(ulong.MaxValue).WriteBytes(destination.AsSpan(0, length)));

        Assert.AreEqual("destination", ex.ParamName);
        CollectionAssert.AreEqual(Enumerable.Repeat((byte)7, 7).ToArray(), destination);
    }

    // ----------------- real Anchor discriminators -----------------

    [TestMethod]
    public void AnchorInstructionDiscriminator_IsTheFirstEightBytesOfSha256OfGlobalName()
    {
        // Anchor derives an instruction discriminator as sha256("global:<snake_case_name>")[..8]; the literals below were
        // computed with python3 hashlib, and the BCL SHA256 gives the same result as a second independent oracle.
        (string Name, ulong Expected)[] known =
        [
            ("global:initialize", 0xafaf6d1f0d989bedUL),
            ("global:transfer", 0xa334c8e78c0345baUL),
        ];

        foreach (var (name, expected) in known)
        {
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(name));
            var fromHash = InstructionDiscriminator.FromInstructionData(digest);

            Assert.AreEqual(expected, fromHash.Value, name);
            Assert.AreEqual("0x" + expected.ToString("x16"), fromHash.ToString(), name);
            Assert.AreEqual(fromHash, InstructionDiscriminator.Parse(Convert.ToHexString(digest.AsSpan(0, 8))), name);
        }
    }

    [TestMethod]
    public void AnchorInstructionData_WithArguments_YieldsTheDiscriminatorFromItsPrefix()
    {
        // Instruction data = discriminator || borsh-encoded arguments (here a u64 amount, little endian).
        byte[] data = [.. AnchorInitializeBytes, 0x40, 0x42, 0x0f, 0x00, 0x00, 0x00, 0x00, 0x00];

        var discriminator = InstructionDiscriminator.FromInstructionData(data);

        Assert.AreEqual(new InstructionDiscriminator(AnchorInitializeBytes), discriminator);
        Assert.AreEqual("afaf6d1f0d989bed", discriminator.ToString("x"));
    }
}
