using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Byte-level construction and extraction, boundary lengths, and the integer conversions.</summary>
[TestClass]
public sealed class FunctionSelectorBytesAndConversionTests
{
    private static readonly byte[] TransferBytes = [0xa9, 0x05, 0x9c, 0xbb];

    // ----------------- layout -----------------

    [TestMethod]
    public void Layout_IsAFourByteStructCastableToUInt()
    {
        Assert.AreEqual(4, Unsafe.SizeOf<FunctionSelector>());
        Assert.AreEqual(4, Marshal.SizeOf<FunctionSelector>());

        // BinarySearchSorted and ContainsSimd reinterpret the span as uint: element size and value must line up.
        FunctionSelector[] selectors = [new(0x01020304u), new(0xFFFFFFFFu), FunctionSelector.Zero, new(0x80000000u)];
        CollectionAssert.AreEqual(new[] { 0x01020304u, 0xFFFFFFFFu, 0u, 0x80000000u }, MemoryMarshal.Cast<FunctionSelector, uint>(selectors).ToArray());
        Assert.AreEqual(selectors.Length * 4, MemoryMarshal.AsBytes<FunctionSelector>(selectors).Length);
    }

    // ----------------- byte construction -----------------

    [TestMethod]
    public void FourByteConstructor_PlacesEachByteInItsOwnLane()
    {
        Assert.AreEqual(0x01000000u, new FunctionSelector(1, 0, 0, 0).Value);
        Assert.AreEqual(0x00020000u, new FunctionSelector(0, 2, 0, 0).Value);
        Assert.AreEqual(0x00000300u, new FunctionSelector(0, 0, 3, 0).Value);
        Assert.AreEqual(0x00000004u, new FunctionSelector(0, 0, 0, 4).Value);
        Assert.AreEqual(0xFFFFFFFFu, new FunctionSelector(255, 255, 255, 255).Value);
        Assert.AreEqual(0x80000000u, new FunctionSelector(0x80, 0, 0, 0).Value);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void SpanConstructor_TooShort_ThrowsWithParameterName(int length)
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() => _ = new FunctionSelector(TransferBytes.AsSpan(0, length)));
        Assert.AreEqual("bytes", ex.ParamName);
        StringAssert.Contains(ex.Message, "4 bytes");
    }

    [TestMethod]
    public void SpanConstructor_ExactAndLonger_ReadTheFirstFourBytes()
    {
        Assert.AreEqual(0xa9059cbbu, new FunctionSelector(TransferBytes).Value);
        Assert.AreEqual(0xa9059cbbu, new FunctionSelector([0xa9, 0x05, 0x9c, 0xbb, 0xde, 0xad, 0xbe, 0xef]).Value);
        Assert.AreEqual(0x059cbbdeu, new FunctionSelector(new byte[] { 0xa9, 0x05, 0x9c, 0xbb, 0xde }.AsSpan(1)).Value, "offset slices are honoured");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    public void FromCalldata_TooShort_ThrowsWithParameterName(int length)
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() => FunctionSelector.FromCalldata(TransferBytes.AsSpan(0, length)));
        Assert.AreEqual("calldata", ex.ParamName);
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(3, false)]
    [DataRow(4, true)]
    public void FromCalldata_AndTryFromCalldata_AgreeAtTheLengthBoundary(int length, bool valid)
    {
        ReadOnlySpan<byte> calldata = TransferBytes.AsSpan(0, length);

        Assert.AreEqual(valid, FunctionSelector.TryFromCalldata(calldata, out var selector));
        Assert.AreEqual(valid ? 0xa9059cbbu : 0u, selector.Value);
        if (valid)
            Assert.AreEqual(selector, FunctionSelector.FromCalldata(calldata));
    }

    [TestMethod]
    public void TryFromCalldata_LongCalldata_TakesOnlyTheFirstFourBytes()
    {
        byte[] calldata = new byte[4 + 32 * 2];
        TransferBytes.CopyTo(calldata, 0);
        calldata.AsSpan(4).Fill(0xFF);

        Assert.IsTrue(FunctionSelector.TryFromCalldata(calldata, out var selector));
        Assert.AreEqual(FunctionSelector.Erc20.Transfer, selector);
    }

    // ----------------- WriteBytes -----------------

    [TestMethod]
    public void WriteBytes_ExactSize_WritesBigEndian_AndRoundTripsThroughTheConstructor()
    {
        foreach (uint value in new[] { 0u, 1u, 0xa9059cbbu, 0x80000000u, 0xFFFFFFFFu })
        {
            byte[] destination = new byte[4];
            new FunctionSelector(value).WriteBytes(destination);

            CollectionAssert.AreEqual(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value }, destination);
            Assert.AreEqual(value, new FunctionSelector(destination).Value);
        }
    }

    [TestMethod]
    public void WriteBytes_LargerDestination_WritesOnlyTheFirstFourBytes()
    {
        byte[] destination = [9, 9, 9, 9, 9, 9];
        FunctionSelector.Erc20.Transfer.WriteBytes(destination);

        CollectionAssert.AreEqual(new byte[] { 0xa9, 0x05, 0x9c, 0xbb, 9, 9 }, destination);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(3)]
    public void WriteBytes_TooSmall_ThrowsWithParameterName_AndLeavesTheDestinationUntouched(int length)
    {
        byte[] destination = [7, 7, 7];
        var ex = Assert.ThrowsExactly<ArgumentException>(() => FunctionSelector.Erc20.Transfer.WriteBytes(destination.AsSpan(0, length)));

        Assert.AreEqual("destination", ex.ParamName);
        CollectionAssert.AreEqual(new byte[] { 7, 7, 7 }, destination);
    }

    // ----------------- integer conversions -----------------

    [TestMethod]
    [DataRow(0, 0u)]
    [DataRow(1, 1u)]
    [DataRow(-1, 0xFFFFFFFFu)]
    [DataRow(int.MaxValue, 0x7FFFFFFFu)]
    [DataRow(int.MinValue, 0x80000000u)]
    public void Int32Conversions_AreBitwise_AndRoundTrip(int signed, uint unsigned)
    {
        FunctionSelector selector = FunctionSelector.FromInt32(signed);

        Assert.AreEqual(unsigned, selector.Value);
        Assert.AreEqual(signed, selector.AsInt32);
        Assert.AreEqual(signed, (int)selector);
        Assert.AreEqual(unsigned, (uint)selector);
        Assert.AreEqual(selector, (FunctionSelector)unsigned);
        Assert.AreEqual(selector, new FunctionSelector(unsigned));
    }

    [TestMethod]
    public void ExplicitInt32Conversion_DoesNotThrow_EvenInACheckedContext_ForValuesAboveIntMax()
    {
        var selector = new FunctionSelector(0xFFFFFFFFu);

        Assert.AreEqual(-1, checked((int)selector));
        Assert.AreEqual(0xFFFFFFFFu, checked((uint)selector));
        Assert.AreEqual(selector, checked(FunctionSelector.FromInt32(-1)));
    }

    [TestMethod]
    public void IsZero_OnlyForTheZeroSelector()
    {
        Assert.IsTrue(default(FunctionSelector).IsZero);
        Assert.IsTrue(FunctionSelector.Zero.IsZero);
        Assert.IsTrue(new FunctionSelector(0).IsZero);
        Assert.IsTrue(FunctionSelector.FromInt32(0).IsZero);
        foreach (uint value in new[] { 1u, 0x100u, 0x01000000u, 0x80000000u, 0xFFFFFFFFu })
            Assert.IsFalse(new FunctionSelector(value).IsZero, value.ToString("x8"));
    }
}
