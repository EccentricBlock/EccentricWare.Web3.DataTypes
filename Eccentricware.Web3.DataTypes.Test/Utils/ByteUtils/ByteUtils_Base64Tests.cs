using EccentricWare.Web3.DataTypes.Utils;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class ByteUtils_Base64Tests
{
    private static byte[] Utf8(string s) => Encoding.ASCII.GetBytes(s);

    private static byte[] Decode(string text, int destinationSize = 512)
    {
        byte[] dst = new byte[destinationSize];
        Assert.IsTrue(ByteUtils.TryDecodeBase64Utf8(Utf8(text), dst, out int written), text);
        return dst.AsSpan(0, written).ToArray();
    }

    [TestMethod]
    public void TryDecodeBase64Utf8_Standard_Works()
    {
        byte[] payload = Encoding.UTF8.GetBytes("hello world");

        CollectionAssert.AreEqual(payload, Decode(Convert.ToBase64String(payload)));
    }

    [TestMethod]
    public void TryDecodeBase64Utf8_StandardWithoutPadding_RestoresPadding()
    {
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("hello"), Decode("aGVsbG8"));
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("hell"), Decode("aGVsbA"));
    }

    // FBFF -> "+/8=", FBEF -> "++8=", FFFF -> "//8=": each payload forces '+' and/or '/' into the standard form, so the URL-safe
    // spelling really contains '-' and/or '_' and the mapping arm is the only way the decode can succeed.
    [TestMethod]
    [DataRow("FBFF", "+/8=", "-_8")]
    [DataRow("FBEF", "++8=", "--8")]
    [DataRow("FFFF", "//8=", "__8")]
    public void TryDecodeBase64Utf8_UrlSafeAlphabet_Works_WithAndWithoutPadding(string payloadHex, string standard, string urlSafeUnpadded)
    {
        byte[] payload = Convert.FromHexString(payloadHex);
        Assert.AreEqual(standard, Convert.ToBase64String(payload), "oracle self-check: payload must contain + and/or /");

        CollectionAssert.AreEqual(payload, Decode(urlSafeUnpadded));
        CollectionAssert.AreEqual(payload, Decode(urlSafeUnpadded + "="));
    }

    [TestMethod]
    public void TryDecodeBase64Utf8_UrlSafe_LongPayloadCoveringAllSixtyFourSymbols()
    {
        byte[] payload = new byte[192];
        for (int i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i * 37 + 11);
        string standard = Convert.ToBase64String(payload);
        string urlSafe = standard.Replace('+', '-').Replace('/', '_');
        Assert.IsTrue(urlSafe.Contains('-') && urlSafe.Contains('_'), "payload must produce both URL-safe symbols");

        CollectionAssert.AreEqual(payload, Decode(standard));
        CollectionAssert.AreEqual(payload, Decode(urlSafe));
        CollectionAssert.AreEqual(payload, Decode(urlSafe.TrimEnd('=')));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("###")]
    [DataRow("A")]
    [DataRow("A===")]
    [DataRow("====")]
    [DataRow("AAAAA")]
    [DataRow("aGVsbG8*")]
    public void TryDecodeBase64Utf8_InvalidText_ReturnsFalseWithZeroBytesWritten(string text)
    {
        byte[] dst = new byte[64];

        Assert.IsFalse(ByteUtils.TryDecodeBase64Utf8(Utf8(text), dst, out int written));
        Assert.AreEqual(0, written);
    }

    [TestMethod]
    [DataRow("41418041")]
    [DataRow("FF414141")]
    [DataRow("41414100")]
    public void TryDecodeBase64Utf8_NonAsciiOrControlBytes_ReturnFalse(string utf8Hex)
    {
        Assert.IsFalse(ByteUtils.TryDecodeBase64Utf8(Convert.FromHexString(utf8Hex), new byte[64], out int written));
        Assert.AreEqual(0, written);
    }

    [TestMethod]
    public void TryDecodeBase64Utf8_DestinationTooSmall_ReturnsFalse_OnBothDecodePaths()
    {
        byte[] payload = Encoding.UTF8.GetBytes("this is longer than 1 byte");
        string padded = Convert.ToBase64String(payload);

        // Padded input takes the first path; unpadded URL-safe input is normalised and takes the second.
        foreach (string text in new[] { padded, padded.TrimEnd('=') })
        {
            Assert.IsFalse(ByteUtils.TryDecodeBase64Utf8(Utf8(text), new byte[1], out int written), text);
            Assert.AreEqual(0, written, text);
            Assert.IsFalse(ByteUtils.TryDecodeBase64Utf8(Utf8(text), new byte[payload.Length - 1], out written), text);
            Assert.AreEqual(0, written, text);
        }
    }

    [TestMethod]
    public void TryDecodeBase64Utf8_ExactSizeDestination_Succeeds_OnBothDecodePaths()
    {
        byte[] payload = Enumerable.Range(0, 25).Select(i => (byte)(i * 9)).ToArray();
        string padded = Convert.ToBase64String(payload);
        Assert.AreEqual(2, padded.Length - padded.TrimEnd('=').Length, "oracle self-check: payload needs padding");

        CollectionAssert.AreEqual(payload, Decode(padded, payload.Length));
        CollectionAssert.AreEqual(payload, Decode(padded.TrimEnd('='), payload.Length));
    }

    // Normalised (padded) length above 256 is rejected on the second path. Inputs here are VALID Base64, so only the size guard
    // can reject them: 193 bytes -> 258 unpadded chars -> 260 normalised. Padded input of any length uses the first path.
    [TestMethod]
    [DataRow(191, 255, true)]   // normalised 256: at the limit
    [DataRow(192, 256, true)]   // no padding needed: first path
    [DataRow(193, 258, false)]  // normalised 260: guard rejects
    [DataRow(194, 259, false)]  // normalised 260: guard rejects
    public void TryDecodeBase64Utf8_UnpaddedInput_NormalisedSizeGuard(int payloadLength, int unpaddedLength, bool accepted)
    {
        byte[] payload = new byte[payloadLength];
        new Random(1234).NextBytes(payload);
        string unpadded = Convert.ToBase64String(payload).TrimEnd('=');
        Assert.AreEqual(unpaddedLength, unpadded.Length, "oracle self-check");

        byte[] dst = new byte[512];
        bool ok = ByteUtils.TryDecodeBase64Utf8(Utf8(unpadded), dst, out int written);

        Assert.AreEqual(accepted, ok);
        if (accepted)
            CollectionAssert.AreEqual(payload, dst.AsSpan(0, written).ToArray());
        else
            Assert.AreEqual(0, written);
    }

    [TestMethod]
    [DataRow(193)]
    [DataRow(1000)]
    public void TryDecodeBase64Utf8_PaddedInput_IsAcceptedAtAnyLength_DocumentsCurrentBehaviour(int payloadLength)
    {
        // The size guard only limits the normalising path; padded standard input is decoded directly, so it is not capped.
        byte[] payload = new byte[payloadLength];
        new Random(99).NextBytes(payload);

        CollectionAssert.AreEqual(payload, Decode(Convert.ToBase64String(payload), 2048));
    }

    [TestMethod]
    public void TryDecodeBase64Utf8_OversizedUrlSafeInput_IsRejected()
    {
        byte[] payload = new byte[301];
        new Random(5).NextBytes(payload);
        string urlSafe = Convert.ToBase64String(payload).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        Assert.IsFalse(ByteUtils.TryDecodeBase64Utf8(Utf8(urlSafe), new byte[512], out int written));
        Assert.AreEqual(0, written);
    }
}
