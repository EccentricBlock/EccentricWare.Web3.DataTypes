using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// The in-memory layout that <c>UInt256BulkSimd</c> (a 32-byte vector load) and <c>UInt256Math</c> (limb span reinterpretation)
/// rely on: four contiguous ulong limbs, least significant first, no padding.
/// </summary>
[TestClass]
public sealed class UInt256LayoutTests
{
    private static void RequireLittleEndianHost()
    {
        if (!BitConverter.IsLittleEndian)
            Assert.Inconclusive("Limb layout expectations assume a little-endian host.");
    }

    [TestMethod]
    public void Size_Is32Bytes()
    {
        Assert.AreEqual(32, Unsafe.SizeOf<uint256>());
        Assert.AreEqual(32, Marshal.SizeOf<uint256>());
    }

    [TestMethod]
    public void FieldOffsets_AreContiguousLeastSignificantFirst()
    {
        // Fails loudly if the private field names change; the offsets are what the unsafe readers depend on.
        Assert.AreEqual(0, (int)Marshal.OffsetOf<uint256>("_u0"));
        Assert.AreEqual(8, (int)Marshal.OffsetOf<uint256>("_u1"));
        Assert.AreEqual(16, (int)Marshal.OffsetOf<uint256>("_u2"));
        Assert.AreEqual(24, (int)Marshal.OffsetOf<uint256>("_u3"));
    }

    [TestMethod]
    public void MemoryView_AsUlongs_IsLimbOrder()
    {
        RequireLittleEndianHost();
        uint256 x = new(1UL, 2UL, 3UL, 4UL);

        uint256[] single = [x];
        CollectionAssert.AreEqual(new ulong[] { 1, 2, 3, 4 }, MemoryMarshal.Cast<uint256, ulong>(single).ToArray());
        Assert.AreEqual(1UL, Unsafe.As<uint256, ulong>(ref x));
    }

    [TestMethod]
    public void Array_HasContiguous32ByteStride()
    {
        RequireLittleEndianHost();
        uint256[] values = [new(1UL, 2UL, 3UL, 4UL), new(5UL, 6UL, 7UL, 8UL), new(9UL, 10UL, 11UL, 12UL)];

        ReadOnlySpan<ulong> limbs = MemoryMarshal.Cast<uint256, ulong>(values);

        CollectionAssert.AreEqual(new ulong[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, limbs.ToArray());
    }

    [TestMethod]
    public void VectorLoad_SeesTheFourLimbs()
    {
        RequireLittleEndianHost();
        uint256 x = new(0x1111UL, 0x2222UL, 0x3333UL, 0x4444UL);

        Vector256<ulong> vector = Unsafe.ReadUnaligned<Vector256<ulong>>(ref Unsafe.As<uint256, byte>(ref x));

        Assert.AreEqual(0x1111UL, vector.GetElement(0));
        Assert.AreEqual(0x2222UL, vector.GetElement(1));
        Assert.AreEqual(0x3333UL, vector.GetElement(2));
        Assert.AreEqual(0x4444UL, vector.GetElement(3));
    }

    [TestMethod]
    public void Default_IsZero()
    {
        Assert.AreEqual(uint256.Zero, default(uint256));
        Assert.AreEqual(uint256.Zero, new uint256());
        Assert.IsTrue(new uint256[3].All(v => v.IsZero));
    }
}
