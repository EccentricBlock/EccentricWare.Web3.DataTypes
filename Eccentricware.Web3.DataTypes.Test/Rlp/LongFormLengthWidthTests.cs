using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// The header reader decodes the length field of long-form strings (b8..bf) and lists (f8..ff) with a dedicated read for
/// 1, 2, 3 and 4 length bytes and a general path for 5..8. Each width is checked at its first and last representable
/// values (widths 1 and 2), at values whose bytes are all different (so a swapped or misplaced byte shows), with a leading
/// zero, truncated (payload and length field, at the root and inside a list), and (4..8 bytes) above int.MaxValue.
/// </summary>
[TestClass]
public sealed class LongFormLengthWidthTests
{
    private const int StringBase = 0xb7;
    private const int ListBase = 0xf7;

    private static readonly RLPDecodeLimits Unbounded = new(int.MaxValue, 32, int.MaxValue);

    private static byte[] LengthBytes(long length, int width)
    {
        var bytes = new byte[width];
        for (var i = width - 1; i >= 0; i--)
        {
            bytes[i] = (byte)length;
            length >>= 8;
        }

        return bytes;
    }

    /// <summary>A long-form item whose length field is <paramref name="width"/> bytes; strings are zero bytes, lists are literal <c>00</c> children.</summary>
    private static byte[] Build(bool list, int width, int length, int payloadBytesPresent)
    {
        var input = new byte[1 + width + payloadBytesPresent];
        input[0] = (byte)((list ? ListBase : StringBase) + width);
        LengthBytes(length, width).CopyTo(input, 1);
        return input;
    }

    [TestMethod]
    [DataRow(false, 1, 56)]
    [DataRow(false, 1, 255)]
    [DataRow(false, 2, 256)]
    [DataRow(false, 2, 65_535)]
    [DataRow(false, 3, 65_536)]
    [DataRow(false, 3, 1_048_576)]
    [DataRow(false, 4, 16_777_216)]
    [DataRow(false, 2, 0x0102)]
    [DataRow(false, 2, 0xabcd)]
    [DataRow(false, 3, 0x010203)]
    [DataRow(false, 3, 0x0f0e0d)]
    [DataRow(false, 4, 0x01020304)]
    [DataRow(true, 1, 56)]
    [DataRow(true, 1, 255)]
    [DataRow(true, 2, 256)]
    [DataRow(true, 2, 65_535)]
    [DataRow(true, 2, 0x0102)]
    [DataRow(true, 3, 65_536)]
    [DataRow(true, 3, 1_048_576)]
    [DataRow(true, 3, 0x010203)]
    public void LengthAtBoundariesAndWithDistinctBytes_DecodesToThatPayload(bool list, int width, int length)
    {
        var input = Build(list, width, length, length);

        var status = Decode(input, Unbounded, out var document, out var error);

        Assert.AreEqual(RLPStatus.Success, status, error.ToString());
        Assert.AreEqual(list ? RLPKind.List : RLPKind.ByteString, document.Kind);
        Assert.AreEqual(length, document.PayloadLength);
        Assert.AreEqual(1 + width + length, document.EncodedLength);
        Assert.AreEqual(1 + width, document.PayloadRange.Offset);
    }

    [TestMethod]
    [DataRow(false, 1, 56)]
    [DataRow(false, 2, 256)]
    [DataRow(false, 3, 65_536)]
    [DataRow(false, 4, 16_777_216)]
    [DataRow(false, 3, 0x010203)]
    [DataRow(true, 1, 56)]
    [DataRow(true, 2, 256)]
    [DataRow(true, 3, 65_536)]
    [DataRow(true, 3, 0x010203)]
    public void OneByteLessThanAnnounced_IsTruncatedAtTheEndOfTheInput(bool list, int width, int length)
    {
        var input = Build(list, width, length, length - 1);

        var status = Decode(input, Unbounded, out var document, out var error);

        Assert.AreEqual(RLPStatus.TruncatedInput, status);
        Assert.AreEqual(input.Length, error.Offset);
        Assert.IsFalse(document.IsValid);
    }

    [TestMethod]
    [DataRow(false, 2)]
    [DataRow(false, 3)]
    [DataRow(false, 4)]
    [DataRow(false, 5)]
    [DataRow(false, 8)]
    [DataRow(true, 2)]
    [DataRow(true, 3)]
    [DataRow(true, 4)]
    public void LeadingZeroLengthByte_IsNonCanonicalAtTheFirstLengthByte(bool list, int width)
    {
        // The value (100) is representable in one byte, so any wider field has a leading zero.
        var input = new byte[1 + width + 100];
        input[0] = (byte)((list ? ListBase : StringBase) + width);
        input[width] = 100;

        var status = Decode(input, Unbounded, out var document, out var error);

        Assert.AreEqual(RLPStatus.NonCanonicalEncoding, status);
        Assert.AreEqual(1, error.Offset);
        Assert.IsFalse(document.IsValid);
    }

    [TestMethod]
    [DataRow(false, 55)]
    [DataRow(false, 1)]
    [DataRow(true, 55)]
    [DataRow(true, 1)]
    public void ShortLengthInLongForm_IsNonCanonicalAtTheLengthByte(bool list, int length)
    {
        var input = Build(list, 1, length, length);

        var status = Decode(input, Unbounded, out _, out var error);

        Assert.AreEqual(RLPStatus.NonCanonicalEncoding, status);
        Assert.AreEqual(1, error.Offset);
    }

    [TestMethod]
    [DataRow(false, 4, 0xffff_ffffL)]
    [DataRow(false, 4, 0x8000_0000L)]
    [DataRow(false, 5, 0x1_0000_0000L)]
    [DataRow(false, 6, 0xffff_ffff_ffffL)]
    [DataRow(false, 8, long.MaxValue)]
    [DataRow(true, 4, 0x8000_0000L)]
    [DataRow(true, 8, long.MaxValue)]
    public void LengthAboveIntMaxValue_IsLengthOverflowAtTheFirstLengthByte(bool list, int width, long length)
    {
        var input = new byte[1 + width];
        input[0] = (byte)((list ? ListBase : StringBase) + width);
        LengthBytes(length, width).CopyTo(input, 1);

        var status = Decode(input, Unbounded, out var document, out var error);

        Assert.AreEqual(RLPStatus.LengthOverflow, status);
        Assert.AreEqual(1, error.Offset);
        Assert.IsFalse(document.IsValid);
    }

    [TestMethod]
    [DataRow(false, 5)]
    [DataRow(false, 8)]
    [DataRow(true, 6)]
    public void WideLengthFieldCutOffByTheInput_IsTruncatedAtTheEndOfTheInput(bool list, int width)
    {
        var input = new byte[width]; // the prefix plus one length byte fewer than announced
        input[0] = (byte)((list ? ListBase : StringBase) + width);
        input[1] = 1;

        var status = Decode(input, Unbounded, out _, out var error);

        Assert.AreEqual(RLPStatus.TruncatedInput, status);
        Assert.AreEqual(input.Length, error.Offset);
    }

    [TestMethod]
    [DataRow("b9 01")]
    [DataRow("ba 01 02")]
    [DataRow("bb 01 02 03")]
    [DataRow("f9 01")]
    [DataRow("fa 01 02")]
    [DataRow("fb 01 02 03")]
    public void LengthFieldCutOffByTheInputAtTheRoot_IsTruncatedAtTheEndOfTheInput(string hex)
    {
        var input = H(hex);

        var status = Decode(input, Unbounded, out var document, out var error);

        Assert.AreEqual(RLPStatus.TruncatedInput, status);
        Assert.AreEqual(input.Length, error.Offset);
        Assert.IsFalse(document.IsValid);
    }

    [TestMethod]
    [DataRow("c4 c1 b9 01 02", 2)]
    [DataRow("c5 c1 ba 01 02 03", 2)]
    [DataRow("c6 c1 bb 01 02 03 04", 2)]
    [DataRow("c4 c1 f9 01 02", 2)]
    public void LengthFieldOutsideTheEnclosingListButInsideTheDocument_IsAnInvalidListBoundaryAtTheItem(string hex, int offset)
    {
        // The inner list c1 holds only the long-form prefix byte; its length bytes are the root's following siblings.
        var input = H(hex);

        var status = Decode(input, Unbounded, out var document, out var error);

        Assert.AreEqual(RLPStatus.InvalidListBoundary, status);
        Assert.AreEqual(offset, error.Offset);
        Assert.IsFalse(document.IsValid);
    }
}
