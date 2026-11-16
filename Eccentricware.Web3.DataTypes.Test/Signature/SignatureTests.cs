using EccentricWare.Web3.DataTypes.Utils;
using System.Runtime.CompilerServices;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Constants, construction, byte conversion, r/s/v components and zero values for <see cref="Signature"/>.</summary>
[TestClass]
public sealed class SignatureTests
{
    // ----------------- layout, constants, enum -----------------

    [TestMethod]
    public void Layout_IsSeventyTwoBytes_SoArraysStrideOnEightByteBoundaries()
    {
        Assert.AreEqual(72, Unsafe.SizeOf<Signature>());
    }

    [TestMethod]
    public void Constants_PinTheDocumentedSizes()
    {
        Assert.AreEqual(65, Signature.EvmByteLength);
        Assert.AreEqual(64, Signature.SolanaByteLength);
        Assert.AreEqual(130, Signature.EvmHexLength);
        Assert.AreEqual(128, Signature.SolanaHexLength);
        Assert.AreEqual(96, Signature.SolanaMaxBase58Length);
        Assert.AreEqual(88, Signature.SolanaMaxBase58EncodedLength);

        // 88 is the number of Base58 digits of 2^512 - 1, computed independently.
        Assert.AreEqual(88, (int)Math.Ceiling(512 * Math.Log(2) / Math.Log(58)));
    }

    [TestMethod]
    public void Constants_MatchWhatTheValuesReport()
    {
        Assert.AreEqual(65, SignatureTestData.SampleEvm.ByteLength);
        Assert.AreEqual(64, SignatureTestData.SampleSolana.ByteLength);
        Assert.AreEqual(130, SignatureTestData.SampleEvm.HexLength);
        Assert.AreEqual(128, SignatureTestData.SampleSolana.HexLength);
    }

    [TestMethod]
    public void SignatureType_PinsThePersistedValues()
    {
        // SignatureValueConverter stores the numeric value and ordering depends on it.
        Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(SignatureType)));
        Assert.AreEqual((byte)0, Convert.ToByte(SignatureType.Evm));
        Assert.AreEqual((byte)1, Convert.ToByte(SignatureType.Solana));
        Assert.HasCount(2, Enum.GetValues<SignatureType>());
    }

    // ----------------- zero and default -----------------

    [TestMethod]
    public void Default_IsZeroEvm()
    {
        Signature value = default;
        Assert.AreEqual(Signature.Zero, value);
        Assert.AreEqual(Signature.ZeroEvm, value);
        Assert.AreEqual(SignatureType.Evm, value.Type);
        Assert.AreEqual(0, value.V);
        Assert.AreEqual(65, value.ByteLength);
        Assert.AreEqual(130, value.HexLength);
        Assert.IsTrue(value.IsZero);
        Assert.IsTrue(value.R.IsZero);
        Assert.IsTrue(value.S.IsZero);
        Assert.AreNotEqual(Signature.ZeroSolana, value);
    }

    [TestMethod]
    public void ZeroValues_AreZero_ButDifferByType()
    {
        Assert.IsTrue(Signature.ZeroEvm.IsZero);
        Assert.IsTrue(Signature.ZeroSolana.IsZero);
        Assert.AreNotEqual(Signature.ZeroEvm, Signature.ZeroSolana);
        Assert.AreEqual(SignatureType.Solana, Signature.ZeroSolana.Type);
        Assert.AreEqual(SignatureType.Evm, Signature.ZeroEvm.Type);
        Assert.AreEqual(65, Signature.ZeroEvm.ByteLength);
        Assert.AreEqual(64, Signature.ZeroSolana.ByteLength);
    }

    [TestMethod]
    public void ZeroValues_HaveAllZeroBytesAndText()
    {
        CollectionAssert.AreEqual(new byte[65], default(Signature).ToBytes());
        CollectionAssert.AreEqual(new byte[65], Signature.ZeroEvm.ToBytes());
        CollectionAssert.AreEqual(new byte[64], Signature.ZeroSolana.ToBytes());

        Assert.AreEqual("0x" + new string('0', 130), default(Signature).ToString());
        Assert.AreEqual("0x" + new string('0', 128), Signature.ZeroSolana.ToString());

        Assert.AreEqual(default, Signature.Parse(("0x" + new string('0', 130)).AsSpan()));
        Assert.AreEqual(Signature.ZeroEvm, Signature.Parse(new string('0', 130).AsSpan()));
        Assert.AreEqual(Signature.ZeroSolana, Signature.Parse(new string('0', 128).AsSpan()));
    }

    [TestMethod]
    public void IsZero_False_WhenAnyByteOrVIsSet()
    {
        for (int index = 0; index < 65; index++)
        {
            byte[] bytes = new byte[65];
            bytes[index] = 1;
            Assert.IsFalse(Signature.FromEvmBytes(bytes).IsZero, $"EVM byte {index}");
        }

        for (int index = 0; index < 64; index++)
        {
            byte[] bytes = new byte[64];
            bytes[index] = 1;
            Assert.IsFalse(Signature.FromSolanaBytes(bytes).IsZero, $"Solana byte {index}");
        }
    }

    // ----------------- bytes -----------------

    [TestMethod]
    public void FromEvmBytes_WriteBytes_RoundTripsEveryByteInOrder()
    {
        byte[] input = SignatureTestData.Ascending65();
        Signature signature = Signature.FromEvmBytes(input);

        Assert.AreEqual(SignatureType.Evm, signature.Type);
        Assert.AreEqual(0x40, signature.V);
        CollectionAssert.AreEqual(input, signature.ToBytes());
    }

    [TestMethod]
    public void FromSolanaBytes_WriteBytes_RoundTripsEveryByteInOrder()
    {
        byte[] input = SignatureTestData.Ascending64();
        Signature signature = Signature.FromSolanaBytes(input);

        Assert.AreEqual(SignatureType.Solana, signature.Type);
        Assert.AreEqual(0, signature.V);
        CollectionAssert.AreEqual(input, signature.ToBytes());
    }

    [TestMethod]
    public void EvmV_IsPreservedForEveryValue()
    {
        for (int v = 0; v < 256; v++)
        {
            byte[] bytes = SignatureTestData.Ascending65();
            bytes[64] = (byte)v;

            Signature signature = Signature.FromEvmBytes(bytes);
            Assert.AreEqual((byte)v, signature.V);
            Assert.AreEqual((byte)v, signature.ToBytes()[64]);
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(32)]
    [DataRow(64)]
    [DataRow(66)]
    [DataRow(130)]
    public void FromEvmBytes_WrongLength_ThrowsNamingTheParameter(int length)
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => Signature.FromEvmBytes(new byte[length]));
        Assert.AreEqual("signature65", error.ParamName);
        StringAssert.Contains(error.Message, "65");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(32)]
    [DataRow(63)]
    [DataRow(65)]
    [DataRow(128)]
    public void FromSolanaBytes_WrongLength_ThrowsNamingTheParameter(int length)
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => Signature.FromSolanaBytes(new byte[length]));
        Assert.AreEqual("signature64", error.ParamName);
        StringAssert.Contains(error.Message, "64");
    }

    [TestMethod]
    public void WriteBytes_WritesExactlyByteLength_AndLeavesTheRestUntouched()
    {
        byte[] evm = SignatureTestData.Filled(70, 0xEE);
        SignatureTestData.SampleEvm.WriteBytes(evm);
        CollectionAssert.AreEqual(SignatureTestData.Ascending65(), evm.AsSpan(0, 65).ToArray());
        Assert.IsTrue(evm.AsSpan(65).ToArray().All(b => b == 0xEE));

        // A Solana signature must not touch byte 64 (it has no v).
        byte[] solana = SignatureTestData.Filled(70, 0xEE);
        SignatureTestData.SampleSolana.WriteBytes(solana);
        CollectionAssert.AreEqual(SignatureTestData.Ascending64(), solana.AsSpan(0, 64).ToArray());
        Assert.IsTrue(solana.AsSpan(64).ToArray().All(b => b == 0xEE));
    }

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(false, 1)]
    [DataRow(false, 64)]
    [DataRow(true, 0)]
    [DataRow(true, 1)]
    [DataRow(true, 63)]
    public void WriteBytes_DestinationTooSmall_ThrowsAndWritesNothing(bool solana, int length)
    {
        Signature signature = solana ? SignatureTestData.SampleSolana : SignatureTestData.SampleEvm;
        byte[] destination = new byte[length];

        var error = Assert.ThrowsExactly<ArgumentException>(() => signature.WriteBytes(destination));
        Assert.AreEqual("destinationBytes", error.ParamName);
        Assert.IsTrue(destination.All(b => b == 0));
    }

    [TestMethod]
    public void ToBytes_ReturnsAFreshCopyEachTime()
    {
        Signature signature = SignatureTestData.SampleEvm;
        byte[] first = signature.ToBytes();
        byte[] second = signature.ToBytes();

        Assert.AreNotSame(first, second);
        first.AsSpan().Fill(0xAA);
        CollectionAssert.AreEqual(SignatureTestData.Ascending65(), signature.ToBytes());
        CollectionAssert.AreEqual(SignatureTestData.Ascending65(), second);
    }

    [TestMethod]
    public void Signature_DoesNotAliasItsInputsOrOutputs()
    {
        byte[] evmInput = SignatureTestData.Ascending65();
        byte[] solanaInput = SignatureTestData.Ascending64();
        byte[] r = SignatureTestData.Ascending65().AsSpan(0, 32).ToArray();
        byte[] s = SignatureTestData.Ascending65().AsSpan(32, 32).ToArray();

        Signature evm = Signature.FromEvmBytes(evmInput);
        Signature solana = Signature.FromSolanaBytes(solanaInput);
        Signature components = Signature.FromEvmComponents(r, s, 0x40);

        evmInput.AsSpan().Fill(0xAA);
        solanaInput.AsSpan().Fill(0xAA);
        r.AsSpan().Fill(0xAA);
        s.AsSpan().Fill(0xAA);

        Assert.AreEqual(SignatureTestData.SampleEvm, evm);
        Assert.AreEqual(SignatureTestData.SampleSolana, solana);
        Assert.AreEqual(SignatureTestData.SampleEvm, components);

        // Buffers filled by the writers are plain copies as well.
        byte[] written = new byte[65];
        byte[] writtenR = new byte[32];
        byte[] writtenS = new byte[32];
        evm.WriteBytes(written);
        evm.WriteR(writtenR);
        evm.WriteS(writtenS);
        written.AsSpan().Fill(0xBB);
        writtenR.AsSpan().Fill(0xBB);
        writtenS.AsSpan().Fill(0xBB);
        Assert.AreEqual(SignatureTestData.SampleEvm, evm);
        CollectionAssert.AreEqual(SignatureTestData.Ascending65(), evm.ToBytes());
    }

    // ----------------- r / s / v components -----------------

    [TestMethod]
    public void FromEvmComponents_MatchesFromEvmBytes()
    {
        byte[] bytes = SignatureTestData.Ascending65();
        Signature fromComponents = Signature.FromEvmComponents(bytes.AsSpan(0, 32), bytes.AsSpan(32, 32), bytes[64]);
        Assert.AreEqual(Signature.FromEvmBytes(bytes), fromComponents);
        Assert.AreEqual(SignatureType.Evm, fromComponents.Type);
    }

    [TestMethod]
    [DataRow((byte)0)]
    [DataRow((byte)1)]
    [DataRow((byte)27)]
    [DataRow((byte)28)]
    [DataRow((byte)0x80)]
    [DataRow((byte)255)]
    public void FromEvmComponents_PassesVThrough(byte v)
    {
        byte[] bytes = SignatureTestData.Ascending65();
        Signature signature = Signature.FromEvmComponents(bytes.AsSpan(0, 32), bytes.AsSpan(32, 32), v);

        Assert.AreEqual(v, signature.V);
        bytes[64] = v;
        CollectionAssert.AreEqual(bytes, signature.ToBytes());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(31)]
    [DataRow(33)]
    [DataRow(64)]
    public void FromEvmComponents_WrongRLength_NamesR32(int length)
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => Signature.FromEvmComponents(new byte[length], new byte[32], 27));
        Assert.AreEqual("r32", error.ParamName);
        StringAssert.Contains(error.Message, "32");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(31)]
    [DataRow(33)]
    [DataRow(64)]
    public void FromEvmComponents_WrongSLength_NamesS32(int length)
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => Signature.FromEvmComponents(new byte[32], new byte[length], 27));
        Assert.AreEqual("s32", error.ParamName);
        StringAssert.Contains(error.Message, "32");
    }

    [TestMethod]
    public void FromEvmComponents_BothWrong_ReportsRFirst()
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => Signature.FromEvmComponents(new byte[1], new byte[2], 27));
        Assert.AreEqual("r32", error.ParamName);
    }

    [TestMethod]
    public void RAndS_AreTheFirstAndSecondThirtyTwoBytes_AndExcludeV()
    {
        byte[] bytes = SignatureTestData.Ascending65();
        Signature signature = Signature.FromEvmBytes(bytes);

        CollectionAssert.AreEqual(bytes.AsSpan(0, 32).ToArray(), signature.R.ToBigEndianBytes());
        CollectionAssert.AreEqual(bytes.AsSpan(32, 32).ToArray(), signature.S.ToBigEndianBytes());
        Assert.AreEqual(Hash32.Parse("0x" + SignatureTestData.Ascending65Hex[..64]), signature.R);
        Assert.AreEqual(Hash32.Parse("0x" + SignatureTestData.Ascending65Hex[64..128]), signature.S);

        // v does not leak into r or s.
        bytes[64] = 0xFF;
        Signature otherV = Signature.FromEvmBytes(bytes);
        Assert.AreEqual(signature.R, otherV.R);
        Assert.AreEqual(signature.S, otherV.S);
    }

    [TestMethod]
    public void RAndS_OnSolana_AreTheTwoHalvesOfThePayload()
    {
        byte[] bytes = SignatureTestData.Ascending64();
        Signature signature = Signature.FromSolanaBytes(bytes);

        CollectionAssert.AreEqual(bytes.AsSpan(0, 32).ToArray(), signature.R.ToBigEndianBytes());
        CollectionAssert.AreEqual(bytes.AsSpan(32, 32).ToArray(), signature.S.ToBigEndianBytes());
    }

    [TestMethod]
    public void RealEvmSignatures_ExposeRSAndV()
    {
        // Real ECDSA signatures from an independent implementation; r, s and v are checked against slices of the hex.
        foreach ((string hex, string rHex, string sHex, byte v) in new[]
        {
            (SignatureTestData.EvmRealHex, SignatureTestData.EvmRealRHex, SignatureTestData.EvmRealSHex, SignatureTestData.EvmRealV),
            (SignatureTestData.EvmRealBHex, SignatureTestData.EvmRealBRHex, SignatureTestData.EvmRealBSHex, SignatureTestData.EvmRealBV),
        })
        {
            Signature signature = Signature.FromEvmBytes(Convert.FromHexString(hex));

            Assert.AreEqual(rHex + sHex + v.ToString("x2"), hex);
            CollectionAssert.AreEqual(Convert.FromHexString(rHex), signature.R.ToBigEndianBytes());
            CollectionAssert.AreEqual(Convert.FromHexString(sHex), signature.S.ToBigEndianBytes());
            Assert.AreEqual(v, signature.V);
            Assert.IsTrue(v is 0x1b or 0x1c);
            Assert.AreEqual("0x" + hex, signature.ToString());
            Assert.AreEqual(signature, Signature.Parse(("0x" + hex).AsSpan()));
        }
    }

    // ----------------- WriteR / WriteS -----------------

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void WriteRAndWriteS_WriteTheComponent_AndNothingElse(bool solana, bool writeS)
    {
        byte[] bytes = SignatureTestData.Ascending65();
        Signature signature = solana ? Signature.FromSolanaBytes(bytes.AsSpan(0, 64)) : Signature.FromEvmBytes(bytes);
        byte[] expected = bytes.AsSpan(writeS ? 32 : 0, 32).ToArray();

        byte[] exact = new byte[32];
        if (writeS) signature.WriteS(exact); else signature.WriteR(exact);
        CollectionAssert.AreEqual(expected, exact);

        byte[] larger = SignatureTestData.Filled(40, 0xEE);
        if (writeS) signature.WriteS(larger); else signature.WriteR(larger);
        CollectionAssert.AreEqual(expected, larger.AsSpan(0, 32).ToArray());
        Assert.IsTrue(larger.AsSpan(32).ToArray().All(b => b == 0xEE), "bytes past 32 must stay untouched");
    }

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(false, 31)]
    [DataRow(true, 0)]
    [DataRow(true, 31)]
    public void WriteRAndWriteS_DestinationTooSmall_ThrowAndWriteNothing(bool solana, int length)
    {
        Signature signature = solana ? SignatureTestData.SampleSolana : SignatureTestData.SampleEvm;

        byte[] forR = SignatureTestData.Filled(length, 0xEE);
        var rError = Assert.ThrowsExactly<ArgumentException>(() => signature.WriteR(forR));
        Assert.AreEqual("destination32", rError.ParamName);
        Assert.IsTrue(forR.All(b => b == 0xEE));

        byte[] forS = SignatureTestData.Filled(length, 0xEE);
        var sError = Assert.ThrowsExactly<ArgumentException>(() => signature.WriteS(forS));
        Assert.AreEqual("destination32", sError.ParamName);
        Assert.IsTrue(forS.All(b => b == 0xEE));
    }
}
