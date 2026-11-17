using System.Numerics;
using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpWriterTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.TypedAdapterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// The typed writers emit straight into the destination (limb stores, one of them left-aligned and overwritten by the next
/// store, and a reserved payload region) instead of a stack temporary. These tests pin that no store lands outside the
/// item's bytes, that the output equals the oracle for every width, and that every failure leaves the destination and the
/// writer untouched. The writer is always given a destination longer than the item, with sentinel bytes inside its span.
/// </summary>
[TestClass]
public sealed class TypedWriterDirectEmitTests
{
    private const int Tail = 40;

    private delegate RLPStatus WriteItem(ref RLPWriter writer);

    /// <summary>Every item shape the direct emitters produce: zero, a literal, every uint256 byte width, an address and a hash.</summary>
    private static List<(string Name, WriteItem Write, byte[] Expected)> Items()
    {
        var items = new List<(string, WriteItem, byte[])>
        {
            ("uint256 zero", (ref RLPWriter w) => w.TryWriteUInt256(default), EncodeString([])),
            ("uint256 0x05", (ref RLPWriter w) => w.TryWriteUInt256(new uint256(5)), EncodeString([0x05])),
        };

        for (var width = 1; width <= 32; width++)
        {
            // Distinct bytes (first, first+7, ...), so a swapped limb, a wrong position or a missing shift shows.
            var magnitude = Pattern(width, first: 0x81);
            var number = new BigInteger(magnitude, isUnsigned: true, isBigEndian: true);
            var value = new uint256(Padded32(number));
            items.Add(($"uint256 width {width}", (ref RLPWriter w) => w.TryWriteUInt256(value), EncodeString(magnitude)));
        }

        var addressBytes = Pattern(20, first: 0x01);
        var hashBytes = Pattern(32, first: 0x00);
        var address = Address.FromEvmBytes(addressBytes);
        var hash = new Hash32(hashBytes);
        items.Add(("address", (ref RLPWriter w) => w.TryWriteAddress(address), EncodeString(addressBytes)));
        items.Add(("hash32", (ref RLPWriter w) => w.TryWriteHash32(hash), EncodeString(hashBytes)));
        return items;
    }

    [TestMethod]
    public void RootItem_WritesOnlyItsOwnBytes_AndEqualsTheOracle()
    {
        Span<int> frames = stackalloc int[1];
        foreach (var (name, write, expected) in Items())
        {
            var buffer = Filled(expected.Length + Tail);
            var writer = new RLPWriter(buffer, frames);

            Assert.AreEqual(RLPStatus.Success, write(ref writer), name);
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), name);

            Assert.AreEqual(expected.Length, written, name);
            CollectionAssert.AreEqual(expected, buffer.AsSpan(0, expected.Length).ToArray(), name);
            Assert.IsTrue(IsFilled(buffer.AsSpan(expected.Length)), $"{name}: a byte after the item changed");
        }
    }

    [TestMethod]
    public void LastChildOfAList_WritesOnlyItsOwnBytes_AndTheListIsTheOracle()
    {
        Span<int> frames = stackalloc int[2];
        foreach (var (name, write, item) in Items())
        {
            var expected = WrapInList([0x05, 0x06, .. item]);
            var buffer = Filled(expected.Length + Tail);
            var writer = new RLPWriter(buffer, frames);

            Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(2 + item.Length), name);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(5), name);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(6), name);
            Assert.AreEqual(RLPStatus.Success, write(ref writer), name);
            Assert.AreEqual(RLPStatus.Success, writer.TryEndList(), name);
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), name);

            Assert.AreEqual(expected.Length, written, name);
            CollectionAssert.AreEqual(expected, buffer.AsSpan(0, expected.Length).ToArray(), name);
            Assert.IsTrue(IsFilled(buffer.AsSpan(expected.Length)), $"{name}: a byte after the list changed");
        }
    }

    [TestMethod]
    public void ItemLargerThanTheAnnouncedList_IsAnInvalidListBoundary_AndChangesNothing()
    {
        Span<int> frames = stackalloc int[2];
        foreach (var (name, write, item) in Items())
        {
            // The list announces one byte less than the item needs; the destination itself has plenty of room.
            var announced = item.Length - 1;
            var expectedList = new byte[1 + announced];
            expectedList[0] = (byte)(0xc0 + announced);
            Array.Fill(expectedList, (byte)0x07, 1, announced);

            var buffer = Filled(1 + announced + Tail);
            var writer = new RLPWriter(buffer, frames);
            Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(announced), name);
            var afterHeader = buffer.ToArray();

            Assert.AreEqual(RLPStatus.InvalidListBoundary, write(ref writer), name);
            CollectionAssert.AreEqual(afterHeader, buffer, $"{name}: the failed write changed the destination");

            // The writer is unchanged: the missing children can still be added and the list closed.
            for (var i = 0; i < announced; i++)
            {
                Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(7), name);
            }

            Assert.AreEqual(RLPStatus.Success, writer.TryEndList(), name);
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), name);
            Assert.AreEqual(expectedList.Length, written, name);
            CollectionAssert.AreEqual(expectedList, buffer.AsSpan(0, written).ToArray(), name);
            Assert.IsTrue(IsFilled(buffer.AsSpan(written)), name);
        }
    }

    [TestMethod]
    public void ItemLargerThanTheRemainingDestination_IsDestinationTooSmall_AndChangesNothing()
    {
        Span<int> frames = stackalloc int[1];
        foreach (var (name, write, item) in Items())
        {
            var buffer = Filled(item.Length - 1);
            var writer = new RLPWriter(buffer, frames);

            Assert.AreEqual(RLPStatus.DestinationTooSmall, write(ref writer), name);
            Assert.IsTrue(IsFilled(buffer), $"{name}: the failed write changed the destination");

            // Still usable: a literal fits whenever the item was at least two bytes, otherwise there is no room at all.
            Assert.AreEqual(item.Length > 1 ? RLPStatus.Success : RLPStatus.DestinationTooSmall, writer.TryWriteUInt64(7), name);
        }
    }

    [TestMethod]
    public void SecondRoot_IsExtraRoot_AndChangesNothing()
    {
        Span<int> frames = stackalloc int[1];
        foreach (var (name, write, _) in Items())
        {
            var buffer = Filled(64);
            var writer = new RLPWriter(buffer, frames);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(7), name);
            var snapshot = buffer.ToArray();

            Assert.AreEqual(RLPStatus.ExtraRoot, write(ref writer), name);
            CollectionAssert.AreEqual(snapshot, buffer, name);
        }
    }
}
