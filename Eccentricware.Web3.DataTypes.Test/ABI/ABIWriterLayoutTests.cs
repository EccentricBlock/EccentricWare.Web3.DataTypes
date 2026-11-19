using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIWriteTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Spec 10.3 layout items checked word by word against hand-derived layouts (the offsets are worked out in the comments), so a wrong
/// base or a wrong tail cursor cannot hide behind a reference encoder that shares the mistake: static members after dynamic ones, several
/// dynamic siblings, fixed arrays of dynamic tuples, nested array count backfill, and the effect of values that change between measuring
/// and writing.
/// </summary>
[TestClass]
public sealed class ABIWriterLayoutTests
{
    private static byte[] Words(params ulong[] values) => Cat([.. values.Select(Word)]);

    [TestMethod]
    public void StaticMembersAfterDynamicOnes_KeepTheirHeadSlots_AndOffsetsFollowTheCanonicalTails()
    {
        // (bytes,uint256,string,uint256) with 0x0102, 9, "xyz", 10:
        // head 4 words = 128: pointer to bytes 0x80, static 9, pointer to string 0xc0 (0x80 + length word + one data word), static 10.
        byte[] written = WriteOk([Ev.Blob([1, 2]), Ev.U64(9), Ev.Text(Ascii("xyz")), Ev.U64(10)], CompileTuple("(bytes,uint256,string,uint256)"));
        byte[] expected = Cat(
            Words(0x80, 9, 0xc0, 10),
            Word(2), [1, 2], new byte[30],
            Word(3), Ascii("xyz"), new byte[29]);
        CollectionAssert.AreEqual(expected, written);
    }

    [TestMethod]
    public void SeveralDynamicSiblings_GetConsecutiveTails_InDeclarationOrder()
    {
        // (uint256[],uint256[],bytes) with [1,2], [10,20], empty: head 3 words = 96; first array 0x60 (count + 2 words = 96 bytes) -> second 0xc0 -> bytes 0x120.
        byte[] written = WriteOk([Ev.Begin("dynamic", 2), Ev.U64(1), Ev.U64(2), Ev.End, Ev.Begin("dynamic", 2), Ev.U64(10), Ev.U64(20), Ev.End, Ev.Blob([])], CompileTuple("(uint256[],uint256[],bytes)"));
        CollectionAssert.AreEqual(Words(0x60, 0xc0, 0x120, 2, 1, 2, 2, 10, 20, 0), written);
    }

    [TestMethod]
    public void NestedArrayCounts_AreBackfilledAtTheStartOfEachArray_AndOffsetsAreRelativeToTheElementTable()
    {
        // uint256[][] [[1,2],[3],[]]:
        //   0: 0x20  (root pointer)           32: 3 (outer count)         table base = 64
        //   64: 0x60 (first at 64+0x60=160)   96: 0xc0 (second at 256)    128: 0x100 (third at 320)
        //   160: 2,1,2   256: 1,3   320: 0  -> 352 bytes
        List<Ev> script =
        [
            Ev.Begin("dynamic", 3),
            Ev.Begin("dynamic", 2), Ev.U64(1), Ev.U64(2), Ev.End,
            Ev.Begin("dynamic", 1), Ev.U64(3), Ev.End,
            Ev.Begin("dynamic", 0), Ev.End,
            Ev.End,
        ];
        byte[] written = WriteOk(script, CompileTuple("(uint256[][])"));
        CollectionAssert.AreEqual(Words(0x20, 3, 0x60, 0xc0, 0x100, 2, 1, 2, 1, 3, 0), written);
        Assert.AreEqual(352, written.Length);
    }

    [TestMethod]
    public void FixedArrayOfDynamicTuples_OffsetsAreRelativeToTheArrayStart_NotTheOuterTuple()
    {
        // (uint256,bytes)[2] [(1,0x01),(2,0x0202)]:
        //   0: 0x20 (root pointer)
        //   array at 32 (no count word): head 2 words: 0x40 and 0xc0 (each tuple = 2 head words + length word + 1 data word = 128 bytes)
        //   tuple 0 at 32+0x40 = 96:  1, 0x40, 1, 01..   tuple 1 at 32+0xc0 = 224: 2, 0x40, 2, 0202..
        List<Ev> script =
        [
            Ev.Begin("fixed"),
            Ev.Begin("tuple"), Ev.U64(1), Ev.Blob([0x01]), Ev.End,
            Ev.Begin("tuple"), Ev.U64(2), Ev.Blob([0x02, 0x02]), Ev.End,
            Ev.End,
        ];
        byte[] written = WriteOk(script, CompileTuple("((uint256,bytes)[2])"));
        byte[] expected = Cat(
            Words(0x20, 0x40, 0xc0),
            Words(1, 0x40, 1), [0x01], new byte[31],
            Words(2, 0x40, 2), [0x02, 0x02], new byte[30]);
        CollectionAssert.AreEqual(expected, written);
    }

    [TestMethod]
    public void NestedDynamicTuple_ResetsTheOffsetBase_ToItsOwnStart()
    {
        // (uint256,(string,uint256[])) with 5, ("ab",[7]):
        //   root: 5, pointer 0x40 (tuple at 64).  Tuple at 64: head 2 words: string pointer 0x40, array pointer 0x80 (relative to 64!)
        //   string at 64+0x40=128: len 2 + "ab"+pad;   array at 64+0x80=192: count 1, 7.
        List<Ev> script = [Ev.U64(5), Ev.Begin("tuple"), Ev.Text(Ascii("ab")), Ev.Begin("dynamic", 1), Ev.U64(7), Ev.End, Ev.End];
        byte[] written = WriteOk(script, CompileTuple("(uint256,(string,uint256[]))"));
        byte[] expected = Cat(Words(5, 0x40), Words(0x40, 0x80), Word(2), Ascii("ab"), new byte[30], Words(1, 7));
        CollectionAssert.AreEqual(expected, written);
    }

    [TestMethod]
    public void DynamicArrayOfDynamicTuples_ElementOffsetsStartAfterTheCountWord()
    {
        // (uint256,bytes)[] with [(1,"a"),(2,"b")]: root pointer 0x20; count 2 at 32; table base 64: offsets 0x40, 0xc0 (each tuple 128 bytes).
        List<Ev> script =
        [
            Ev.Begin("dynamic", 2),
            Ev.Begin("tuple"), Ev.U64(1), Ev.Blob([0x61]), Ev.End,
            Ev.Begin("tuple"), Ev.U64(2), Ev.Blob([0x62]), Ev.End,
            Ev.End,
        ];
        byte[] written = WriteOk(script, CompileTuple("((uint256,bytes)[])"));
        byte[] expected = Cat(
            Words(0x20, 2, 0x40, 0xc0),
            Words(1, 0x40, 1), [0x61], new byte[31],
            Words(2, 0x40, 1), [0x62], new byte[31]);
        CollectionAssert.AreEqual(expected, written);
    }

    [TestMethod]
    public void MultiWordStaticMembers_AdvanceTheHeadByTheirFullWidth()
    {
        // (uint256[3],bool,bytes): head = 3 + 1 + 1 pointer = 5 words; the bytes pointer is 0xa0, not 0x60.
        List<Ev> script = [Ev.Begin("fixed"), Ev.U64(1), Ev.U64(2), Ev.U64(3), Ev.End, Ev.Bool(true), Ev.Blob([0x12, 0x34])];
        byte[] written = WriteOk(script, CompileTuple("(uint256[3],bool,bytes)"));
        CollectionAssert.AreEqual(Cat(Words(1, 2, 3, 1, 0xa0, 2), [0x12, 0x34], new byte[30]), written);
    }

    [TestMethod]
    public void OuterStaticTuple_WithADynamicSiblingAfterIt_IsInlineAndShiftsThePointer()
    {
        // ((uint256,uint256),bytes): the static tuple takes 2 inline words, so the bytes pointer is 0x60.
        byte[] written = WriteOk([Ev.Begin("tuple"), Ev.U64(1), Ev.U64(2), Ev.End, Ev.Blob([9])], CompileTuple("((uint256,uint256),bytes)"));
        CollectionAssert.AreEqual(Cat(Words(1, 2, 0x60, 1), [9], new byte[31]), written);
    }

    // ------------------------------------------------------------------ values that change between measuring and writing

    [TestMethod]
    public void ValuesChangedBetweenMeasureAndWrite_CannotOverrunTheDestination_AndTheWriterReportsTheTruth()
    {
        // The library cannot detect that a caller changed a value between the passes (spec 7.4/10.3), but the writer re-validates everything,
        // so the worst outcome is a failed write or a different length, never a write outside the destination.
        ABISchema schema = CompileTuple("(bytes)");
        byte[] measuredValue = new byte[10];
        RunResult measured = RunMeasurer([Ev.Blob(measuredValue)], schema, GenerousEncode);
        Assert.AreEqual(96, measured.Length);

        // Grown: a 40-byte value no longer fits the 96-byte buffer sized from the measurement.
        byte[] buffer = new byte[measured.Length + 8];
        Array.Fill(buffer, Garbage);
        RunResult grown = RunWriter([Ev.Blob(new byte[40])], schema, buffer, GenerousEncode);
        Assert.AreEqual(ABIStatus.InsufficientDestination, grown.Status);
        Assert.IsTrue(buffer.Skip(measured.Length).All(b => b == Garbage), "nothing may be written beyond the measured size");

        // Shrunk: the writer reports the shorter, true length; the caller must use it rather than the measured one.
        RunResult shrunk = RunWriter([Ev.Blob(new byte[3])], schema, buffer, GenerousEncode);
        Assert.IsTrue(shrunk.Ok);
        Assert.AreEqual(96, shrunk.Length, "3 bytes pad to the same single word");
        RunResult smaller = RunWriter([Ev.Blob([])], schema, buffer, GenerousEncode);
        Assert.AreEqual(64, smaller.Length, "the written length, not the measured one, is the document length");
    }
}
