using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="HexBytes"/>: construction, ownership (copy versus no-copy), basic properties, and conversions.</summary>
[TestClass]
public sealed class HexBytesConstructionTests
{
    private static byte[] BackingArray(ReadOnlyMemory<byte> memory)
    {
        Assert.IsTrue(MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment));
        Assert.AreEqual(0, segment.Offset);
        return segment.Array!;
    }

    private static bool SameStart(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        => Unsafe.AreSame(in MemoryMarshal.GetReference(a), in MemoryMarshal.GetReference(b));

    // ----------------- empty / default / zero -----------------

    [TestMethod]
    public void Empty_Default_AndEveryEmptyInput_AreEqualAndEmpty()
    {
        (string Name, HexBytes Value)[] values =
        [
            ("Empty", HexBytes.Empty),
            ("default", default),
            ("empty array", new HexBytes(Array.Empty<byte>())),
            ("null array", new HexBytes((byte[])null!)),
            ("empty span", new HexBytes(ReadOnlySpan<byte>.Empty)),
            ("FromArrayUnsafe(null)", HexBytes.FromArrayUnsafe(null)),
            ("FromArrayUnsafe([])", HexBytes.FromArrayUnsafe([])),
            ("Slice to nothing", new HexBytes(new byte[] { 1 }).Slice(1)),
        ];

        foreach ((string name, HexBytes value) in values)
        {
            Assert.IsTrue(value.IsEmpty, name);
            Assert.AreEqual(0, value.Length, name);
            Assert.AreEqual(0, value.HexLength, name);
            Assert.IsTrue(value.Span.IsEmpty, name);
            Assert.IsTrue(value.Memory.IsEmpty, name);
            Assert.AreEqual(HexBytes.Empty, value, name);
            Assert.IsTrue(value == default, name);
            Assert.AreEqual(0, value.GetHashCode(), name); // pins the documented "empty hashes to 0" implementation detail
        }
    }

    [TestMethod]
    public void Default_BoxedEquals_Empty()
    {
        Assert.IsTrue(((object)default(HexBytes)).Equals(HexBytes.Empty));
        Assert.IsTrue(((object)HexBytes.Empty).Equals(default(HexBytes)));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(32)]
    public void ZeroValuedBytes_AreNotEmpty(int length)
    {
        var zeros = new HexBytes(new byte[length]);

        Assert.IsFalse(zeros.IsEmpty);
        Assert.AreEqual(length, zeros.Length);
        Assert.AreNotEqual(HexBytes.Empty, zeros);
        Assert.IsTrue(zeros != HexBytes.Empty);
        Assert.IsGreaterThan(0, zeros.CompareTo(HexBytes.Empty));
        Assert.AreEqual("0x" + new string('0', 2 * length), zeros.ToString());
    }

    // ----------------- constructors and ownership -----------------

    [TestMethod]
    public void Constructors_CopyTheSource()
    {
        byte[] source = [1, 2, 3, 4];
        var fromArray = new HexBytes(source);
        var fromSpan = new HexBytes(source.AsSpan());

        source[0] = 0xFF;

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, fromArray.ToArray());
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, fromSpan.ToArray());
        Assert.AreNotSame(source, BackingArray(fromArray.Memory));
        Assert.AreNotSame(source, BackingArray(fromSpan.Memory));
    }

    [TestMethod]
    public void FromArrayUnsafe_WrapsTheArrayWithoutCopying()
    {
        byte[] array = [1, 2, 3];
        HexBytes value = HexBytes.FromArrayUnsafe(array);

        Assert.AreSame(array, BackingArray(value.Memory));

        // Documented contract: the caller must not mutate the array; doing so is visible, which proves there is no copy.
        array[0] = 0x7F;
        Assert.AreEqual((byte)0x7F, value[0]);
    }

    [TestMethod]
    public void ImplicitConversions_FromArrayAndSpan_CopyTheSource()
    {
        byte[] bytes = [1, 2, 3];
        HexBytes fromArray = bytes;
        HexBytes fromSpan = (ReadOnlySpan<byte>)bytes;

        bytes[0] = 0xEE;

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, fromArray.ToArray());
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, fromSpan.ToArray());
        Assert.AreEqual(fromArray, fromSpan);
    }

    [TestMethod]
    public void ImplicitConversions_NullArray_IsEmpty()
    {
        HexBytes fromNull = (byte[])null!;
        Assert.AreEqual(HexBytes.Empty, fromNull);
    }

    [TestMethod]
    public void ImplicitConversions_ToSpanAndMemory_ExposeTheContent_AndEmptyIsEmpty()
    {
        byte[] bytes = [1, 2, 3];
        HexBytes value = new HexBytes(bytes);

        ReadOnlySpan<byte> asSpan = value;
        ReadOnlyMemory<byte> asMemory = value;
        Assert.IsTrue(asSpan.SequenceEqual(bytes));
        Assert.IsTrue(asMemory.Span.SequenceEqual(bytes));

        ReadOnlySpan<byte> emptySpan = HexBytes.Empty;
        ReadOnlyMemory<byte> emptyMemory = default(HexBytes);
        Assert.IsTrue(emptySpan.IsEmpty);
        Assert.IsTrue(emptyMemory.IsEmpty);
    }

    // ----------------- properties -----------------

    [TestMethod]
    [DataRow(1)]
    [DataRow(32)]
    [DataRow(1000)]
    public void Length_IsEmpty_HexLength_ForNonEmptyValues(int length)
    {
        var value = new HexBytes(new byte[length]);

        Assert.AreEqual(length, value.Length);
        Assert.IsFalse(value.IsEmpty);
        Assert.AreEqual(2 * length, value.HexLength);
        Assert.AreEqual(2 * length, value.ToString("x").Length);
    }

    [TestMethod]
    public void Span_And_Memory_ExposeTheSameBytes()
    {
        var value = HexBytes.FromArrayUnsafe([9, 8, 7]);

        Assert.AreEqual(3, value.Memory.Length);
        CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, value.Memory.ToArray());
        Assert.IsTrue(value.Span.SequenceEqual(value.Memory.Span));
        Assert.IsTrue(SameStart(value.Span, value.Memory.Span));
    }

    [TestMethod]
    public void Indexer_ReturnsByte_AndThrowsOutOfRange()
    {
        var value = new HexBytes(new byte[] { 10, 20, 30 });
        Assert.AreEqual((byte)10, value[0]);
        Assert.AreEqual((byte)20, value[1]);
        Assert.AreEqual((byte)30, value[2]);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = value[-1]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = value[int.MinValue]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = HexBytes.Empty[0]);

        ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = value[3]);
        Assert.AreEqual("index", error.ParamName);
    }

    // ----------------- ToArray -----------------

    [TestMethod]
    public void ToArray_ReturnsIndependentCopies()
    {
        var value = new HexBytes(new byte[] { 1, 2, 3 });
        byte[] first = value.ToArray();
        byte[] second = value.ToArray();

        first[0] = 9;

        Assert.AreNotSame(first, second);
        Assert.AreEqual((byte)1, value[0]);
        Assert.AreEqual((byte)1, second[0]);
    }

    [TestMethod]
    public void ToArray_OfEmpty_IsNonNullAndEmpty()
    {
        Assert.IsNotNull(HexBytes.Empty.ToArray());
        Assert.IsEmpty(HexBytes.Empty.ToArray());
        Assert.IsEmpty(default(HexBytes).ToArray());
    }
}
