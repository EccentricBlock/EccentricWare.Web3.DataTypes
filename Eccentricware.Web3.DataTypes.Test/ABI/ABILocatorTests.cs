using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIFixtureCatalog;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Spec 5.3 and 10.3: complete <c>ValueRange</c> for dynamic composites from the next dynamic sibling or the enclosing end, with a step-count
/// proof that locating cost does not grow with unrelated siblings or descendants, plus the defensive behaviour on a document that was changed
/// after validation.
/// </summary>
[TestClass]
public sealed class ABILocatorTests
{
    private struct StepCounter : IABILocateCounter
    {
        public int Steps;

        public void Step() => Steps++;
    }

    private static int StepsToLocate(ABISchema schema, byte[] document, params int[] path)
    {
        var counter = new StepCounter();
        ABIStatus status = ABILocator.TryLocate(schema, document, path, ref counter, out ABILocated located);
        Assert.AreEqual(ABIStatus.Success, status, $"path [{string.Join(",", path)}]");
        Assert.IsTrue(located.ValueOffset >= 0 && located.ValueLength >= 0 && located.ValueOffset + located.ValueLength <= document.Length);
        return counter.Steps;
    }

    // ------------------------------------------------------------------ ValueRange from the next dynamic sibling / enclosing end

    [TestMethod]
    public void FirstMiddleAndLastDynamicField_EachEndAtTheNextDynamicTail_OrTheDocumentEnd()
    {
        // (bytes,uint256,bytes,uint256,bytes): head 160 (ptr 0xa0 | 1 | ptr | 2 | ptr). Tails: "aaa" 64 B at 160, 33 bytes -> 96 B at 224, empty 32 B at 320.
        byte[] a = BytesValue(Ascii("aaa"));
        byte[] b = BytesValue(new byte[33]);
        byte[] c = BytesValue([]);
        byte[] doc = Cat(Word(160), Word(1), Word(160 + (ulong)a.Length), Word(2), Word(160 + (ulong)(a.Length + b.Length)), a, b, c);
        Assert.AreEqual(352, doc.Length);
        var data = DecodeOk("(bytes,uint256,bytes,uint256,bytes)", doc);
        AssertLocated(in data, [0], ABITypeKind.Bytes, (0, 32), (160, 64));
        AssertLocated(in data, [1], ABITypeKind.UInt, (32, 32), (32, 32));
        AssertLocated(in data, [2], ABITypeKind.Bytes, (64, 32), (224, 96));
        AssertLocated(in data, [3], ABITypeKind.UInt, (96, 32), (96, 32));
        AssertLocated(in data, [4], ABITypeKind.Bytes, (128, 32), (320, 32)); // last: ends at the document end
    }

    [TestMethod]
    public void NestedLastDynamicChild_InheritsTheEndOfItsAncestors()
    {
        // ((bytes,(bytes))): root pointer 0x20; T at 32 = [ptr 0x40][ptr 0x80][ "a" 64 B ][ (bytes): ptr 0x20 + 64 B = 96 B ] = 224 B; total 256.
        byte[] doc = Cat(Word(0x20), Word(0x40), Word(0x80), BytesValue(Ascii("a")), Word(0x20), BytesValue(Ascii("b")));
        Assert.AreEqual(256, doc.Length);
        var data = DecodeOk("((bytes,(bytes)))", doc);
        AssertLocated(in data, [0], ABITypeKind.Tuple, (0, 32), (32, 224));
        AssertLocated(in data, [0, 0], ABITypeKind.Bytes, (32, 32), (96, 64));            // ends at its next dynamic sibling's tail (160)
        AssertLocated(in data, [0, 1], ABITypeKind.Tuple, (64, 32), (160, 96));           // last child of T: ends at T's end (256) = the document end
        AssertLocated(in data, [0, 1, 0], ABITypeKind.Bytes, (160, 32), (192, 64));       // and again one level deeper
    }

    [TestMethod]
    public void DynamicTupleInsideADynamicArray_And_ArrayOfDynamicArrays_UseTheNextArraySlot()
    {
        // (uint256[][]) = [[1],[2,3]]: outer at 32, table at 64 (0x40, 0xa0), inner arrays at 128 (64 B) and 192 (96 B); total 288.
        byte[] doc = Cat(Word(0x20), Word(2), Word(0x40), Word(0x80), Word(1), Word(1), Word(2), Word(2), Word(3));
        Assert.AreEqual(288, doc.Length);
        var data = DecodeOk("(uint256[][])", doc);
        AssertLocated(in data, [0, 0], ABITypeKind.DynamicArray, (64, 32), (128, 64));
        AssertLocated(in data, [0, 1], ABITypeKind.DynamicArray, (96, 32), (192, 96));
    }

    [TestMethod]
    public void ZeroWidthMembers_HaveZeroLengthRanges_AtTheCurrentHeadPosition()
    {
        // ((),bytes,()): head = 0 + 32 + 0. The empty tuples sit at head offsets 0 and 32.
        byte[] doc = Cat(Word(0x20), BytesValue(Ascii("z")));
        var data = DecodeOk("((),bytes,())", doc);
        AssertLocated(in data, [], ABITypeKind.Tuple, (0, 32), (0, 96));
        AssertLocated(in data, [0], ABITypeKind.Tuple, (0, 0), (0, 0));
        AssertLocated(in data, [1], ABITypeKind.Bytes, (0, 32), (32, 64));
        AssertLocated(in data, [2], ABITypeKind.Tuple, (32, 0), (32, 0));

        // Elements of an empty-tuple array each get a zero-length range at the end of the head table.
        var many = DecodeOk("(()[])", Cat(Word(0x20), Word(3)));
        for (int i = 0; i < 3; i++)
        {
            AssertLocated(in many, [0, i], ABITypeKind.Tuple, (64, 0), (64, 0));
        }
    }

    // ------------------------------------------------------------------ step counts do not depend on siblings or descendants (10.3)

    private static (ABISchema Schema, byte[] Document) ManyDynamicSiblings(int count, int firstPayloadBytes = 1)
    {
        string tuple = "(" + string.Join(",", Enumerable.Repeat("bytes", count)) + ")";
        var tails = new List<byte[]>();
        for (int i = 0; i < count; i++)
        {
            tails.Add(BytesValue(new byte[i == 0 ? firstPayloadBytes : 1]));
        }

        ulong offset = (ulong)count * 32;
        var heads = new List<byte[]>();
        foreach (byte[] tail in tails)
        {
            heads.Add(Word(offset));
            offset += (ulong)tail.Length;
        }

        return (CompileTuple(tuple, With(tupleElements: 2000, nodes: 4096)), Cat([.. heads, .. tails]));
    }

    [TestMethod]
    public void LocatingADynamicField_CostsTheSameNumberOfSteps_WhateverTheSiblingCount()
    {
        // path [i] costs: 1 descent + the field's own offset word + the next dynamic sibling's offset word (absent for the last field).
        foreach (int count in new[] { 3, 8, 64, 512, 1000 })
        {
            var (schema, doc) = ManyDynamicSiblings(count);
            DecodeOk(doc, schema);
            Assert.AreEqual(3, StepsToLocate(schema, doc, 0), $"first of {count}");
            Assert.AreEqual(3, StepsToLocate(schema, doc, count / 2), $"middle of {count}");
            Assert.AreEqual(3, StepsToLocate(schema, doc, count - 2), $"second to last of {count}");
            Assert.AreEqual(2, StepsToLocate(schema, doc, count - 1), $"last of {count}");
        }
    }

    [TestMethod]
    public void StaticSiblingsBetweenDynamicOnes_AreSkippedWithoutScanning()
    {
        // (bytes, uint256 x K, bytes): the next dynamic sibling of position 0 is position K + 1, found from compiled metadata.
        foreach (int staticCount in new[] { 0, 1, 100, 900 })
        {
            string tuple = "(bytes," + string.Concat(Enumerable.Repeat("uint256,", staticCount)) + "bytes)";
            ABISchema schema = CompileTuple(tuple, With(tupleElements: 2000));
            int head = (2 + staticCount) * 32;
            byte[] first = BytesValue(Ascii("a"));
            byte[] doc = Cat(Word((ulong)head), new byte[staticCount * 32], Word((ulong)(head + first.Length)), first, BytesValue(Ascii("b")));
            DecodeOk(doc, schema);
            Assert.AreEqual(3, StepsToLocate(schema, doc, 0), $"{staticCount} statics between");
            Assert.AreEqual(2, StepsToLocate(schema, doc, staticCount + 1));
            var data = DecodeOk(doc, schema);
            AssertLocated(in data, [0], ABITypeKind.Bytes, (0, 32), (head, 64));
            AssertLocated(in data, [staticCount + 1], ABITypeKind.Bytes, ((1 + staticCount) * 32, 32), (head + 64, 64));
        }
    }

    [TestMethod]
    public void DynamicArrayElement_CostsTheSameNumberOfSteps_WhateverTheElementCount()
    {
        // path [0, i]: root level (descent + the array's pointer slot = 2), then the array level (descent, count word, element slot, and the next slot unless last).
        foreach (int count in new[] { 3, 10, 100, 1000, 4000 })
        {
            byte[] doc = ArrayOfBytes(count);
            ABISchema schema = CompileTuple("(bytes[])");
            DecodeOk(doc, schema, Limits(bytes: 1 << 24, array: 5_000, work: 1_000_000, visited: 1_000_000));
            Assert.AreEqual(6, StepsToLocate(schema, doc, 0, 0), $"first of {count}");
            Assert.AreEqual(6, StepsToLocate(schema, doc, 0, count / 2));
            Assert.AreEqual(5, StepsToLocate(schema, doc, 0, count - 1), $"last of {count}");
            Assert.AreEqual(2, StepsToLocate(schema, doc, 0), "the array itself");
        }
    }

    private static byte[] ArrayOfBytes(int count)
    {
        var parts = new List<byte[]> { Word(0x20), Word((ulong)count) };
        ulong offset = (ulong)count * 32;
        var tails = new List<byte[]>();
        for (int i = 0; i < count; i++)
        {
            parts.Add(Word(offset));
            byte[] tail = BytesValue(Ascii("x"));
            tails.Add(tail);
            offset += (ulong)tail.Length;
        }

        parts.AddRange(tails);
        return Cat([.. parts]);
    }

    [TestMethod]
    public void LocatingTheFirstOfTwoFields_DoesNotDependOnTheSizeOfItsDescendants()
    {
        // (bytes,bytes): the first payload is 1 byte or 128 KiB; the locator never touches it.
        ABISchema schema = CompileTuple("(bytes,bytes)");
        byte[] small = Cat(Word(0x40), Word(0x80), BytesValue(new byte[1]), BytesValue(new byte[1]));
        byte[] large = Cat(Word(0x40), Word(0x40 + 32 + 131_072), BytesValue(new byte[131_072]), BytesValue(new byte[1]));
        DecodeOk(small, schema);
        DecodeOk(large, schema);
        Assert.AreEqual(StepsToLocate(schema, small, 0), StepsToLocate(schema, large, 0));
        Assert.AreEqual(StepsToLocate(schema, small, 1), StepsToLocate(schema, large, 1));

        // A nested subtree of 1 vs 500 elements under the located field: same steps to locate the field itself.
        ABISchema nested = CompileTuple("(uint256[][],bytes)");
        byte[] OneInner(int inner) => Cat(Word(0x40), Word(0x40 + 96 + (ulong)inner * 32), Word(1), Word(0x20), Word((ulong)inner), new byte[inner * 32], BytesValue(Ascii("t")));
        DecodeOk(OneInner(1), nested);
        DecodeOk(OneInner(400), nested);
        Assert.AreEqual(StepsToLocate(nested, OneInner(1), 0), StepsToLocate(nested, OneInner(400), 0));
        Assert.AreEqual(StepsToLocate(nested, OneInner(1), 1), StepsToLocate(nested, OneInner(400), 1));
    }

    [TestMethod]
    public void StepsGrowLinearlyWithPathDepth_NotWithDocumentSize()
    {
        // uint256[]...[] with one element per level: path of depth d costs at most 5 steps per level.
        var costs = new List<int>();
        foreach (int dimensions in new[] { 2, 6, 12, 24 })
        {
            string tuple = "(uint256" + string.Concat(Enumerable.Repeat("[]", dimensions)) + ")";
            byte[] value = Cat(Word(1), Word(7));
            for (int level = 1; level < dimensions; level++)
            {
                value = Cat(Word(1), Word(0x20), value);
            }

            byte[] doc = Cat(Word(0x20), value);
            ABISchema schema = CompileTuple(tuple);
            DecodeOk(doc, schema, Limits(depth: 32));
            int[] path = new int[dimensions + 1];
            int steps = StepsToLocate(schema, doc, path);
            costs.Add(steps);
            Assert.IsTrue(steps <= 5 * (dimensions + 1), $"{dimensions} dimensions took {steps} steps");
        }

        for (int i = 1; i < costs.Count; i++)
        {
            Assert.IsTrue(costs[i] > costs[i - 1], "deeper paths cost more steps");
        }
    }

    // ------------------------------------------------------------------ defensive behaviour on a changed document

    [TestMethod]
    public void AfterTheBytesAreMutated_EveryAccessorStillNeverThrows_AndNeverReturnsARangeOutsideTheDocument()
    {
        var rng = new DeterministicRng(0xBADC0DE);
        ulong[] special = [0, 1, 0x1f, 0x20, 0x40, 0x60, 0x1000, 0x7fffffff, 0x80000000, ulong.MaxValue];
        foreach (Fixture fixture in All)
        {
            byte[] bytes = ArgumentBytes(fixture);
            if (bytes.Length == 0)
            {
                continue;
            }

            RTupleType type = ABIReference.ParseTuple(fixture.Tuple);
            Assert.IsTrue(ABIReference.TryDecodePermissive(type, bytes, out RSeq? value));
            var locations = new List<RLocation>();
            ABIReference.EncodeRoot(type, value!, locations);

            ABISchema schema = CompileTuple(fixture.Tuple);
            ABIData data = DecodeOk(bytes, schema);
            byte[] original = (byte[])bytes.Clone();
            for (int round = 0; round < 60; round++)
            {
                Array.Copy(original, bytes, original.Length);
                int mutations = 1 + rng.Next(3);
                for (int m = 0; m < mutations; m++)
                {
                    int word = rng.Next(Math.Max(1, bytes.Length / 32));
                    if (rng.Next(3) == 0)
                    {
                        bytes[Math.Min(bytes.Length - 1, word * 32 + rng.Next(32))] ^= (byte)(1 << rng.Next(8));
                    }
                    else if (word * 32 + 32 <= bytes.Length)
                    {
                        Word(special[rng.Next(special.Length)]).CopyTo(bytes, word * 32);
                    }
                }

                foreach (RLocation location in locations)
                {
                    AssertSafe(in data, location.Path, bytes.Length);
                }
            }
        }
    }

    [TestMethod]
    public void InconsistentPointers_AfterValidation_AreDefensivelyRejectedWithExactStatuses()
    {
        ABISchema schema = CompileTuple("(bytes,bytes)");
        byte[] valid = Cat(Word(0x40), Word(0x80), BytesValue(Ascii("a")), BytesValue(Ascii("b")));
        DecodeOk(valid, schema);

        ABIStatus Locate(byte[] document, int index)
        {
            var counter = new StepCounter();
            ABIStatus status = ABILocator.TryLocate(schema, document, [index], ref counter, out ABILocated located);
            if (status != ABIStatus.Success)
            {
                Assert.AreEqual(default, located);
            }

            return status;
        }

        byte[] With(int slot, byte[] pointer)
        {
            byte[] copy = (byte[])valid.Clone();
            pointer.CopyTo(copy, slot * 32);
            return copy;
        }

        Assert.AreEqual(ABIStatus.Success, Locate(valid, 0));
        Assert.AreEqual(ABIStatus.InvalidOffset, Locate(With(1, Word(0x40)), 0), "the next tail starts where this one starts: its end would precede its first word");
        Assert.AreEqual(ABIStatus.InvalidOffset, Locate(With(1, Word(0x20)), 0), "next sibling pointer into the head");
        Assert.AreEqual(ABIStatus.InvalidOffset, Locate(With(1, Word(0x20)), 1));
        Assert.AreEqual(ABIStatus.InvalidOffset, Locate(With(0, Word(0x1000)), 0), "pointer beyond the container");
        Assert.AreEqual(ABIStatus.InvalidOffset, Locate(With(1, WordHex("10000000000000040")), 1), "pointer wider than 32 bits");
        Assert.AreEqual(ABIStatus.InvalidOffset, Locate(With(1, Filled(0xff)), 0));
        Assert.AreEqual(ABIStatus.InvalidOffset, Locate(With(0, Word(0x7fff_ffff)), 0));
    }

    [TestMethod]
    public void ACorruptedDynamicArrayCount_IsRejectedWithoutOverflow()
    {
        ABISchema schema = CompileTuple("(uint256[])");
        byte[] valid = Cat(Word(0x20), Word(2), Word(1), Word(2));
        foreach ((byte[] count, ABIStatus expected) in new[]
        {
            (Word(0x7fff_ffff), ABIStatus.Truncated),        // 2^31 - 1 elements would need 64 GiB of head
            (Word(0x8000_0000), ABIStatus.InvalidOffset),    // not an int
            (Filled(0xff), ABIStatus.InvalidOffset),
            (Word(3), ABIStatus.Truncated),                  // one element more than the bytes hold
        })
        {
            byte[] copy = (byte[])valid.Clone();
            count.CopyTo(copy, 32);
            var counter = new StepCounter();
            ABIStatus status = ABILocator.TryLocate(schema, copy, [0, 0], ref counter, out _);
            Assert.AreEqual(expected, status, Convert.ToHexString(count));
        }
    }

    [TestMethod]
    public void EveryPrefixOfAValidDocument_NeverMakesTheLocatorThrow()
    {
        foreach (Fixture fixture in All)
        {
            byte[] bytes = ArgumentBytes(fixture);
            ABISchema schema = CompileTuple(fixture.Tuple);
            RTupleType type = ABIReference.ParseTuple(fixture.Tuple);
            Assert.IsTrue(ABIReference.TryDecodePermissive(type, bytes, out RSeq? value));
            var locations = new List<RLocation>();
            ABIReference.EncodeRoot(type, value!, locations);
            for (int length = 0; length < bytes.Length; length += 7)
            {
                foreach (RLocation location in locations)
                {
                    var counter = new StepCounter();
                    ABIStatus status = ABILocator.TryLocate(schema, bytes.AsSpan(0, length), location.Path, ref counter, out ABILocated located);
                    if (status == ABIStatus.Success)
                    {
                        Assert.IsTrue(located.ValueOffset >= 0 && located.ValueLength >= 0 && located.ValueLength <= length - located.ValueOffset, fixture.Name);
                    }
                }
            }
        }
    }

    private static void AssertSafe(in ABIData data, int[] path, int length)
    {
        if (data.TryLocate(path, out var field, out _))
        {
            foreach (ABIRange range in new[] { field.HeadRange, field.ValueRange })
            {
                Assert.IsTrue(range.Offset >= 0 && range.Length >= 0 && range.Length <= length - range.Offset, $"range {range} outside {length} bytes at [{string.Join(",", path)}]");
            }
        }
        else
        {
            Assert.AreEqual(default, field);
        }

        if (data.TryGetBytePayload(path, out var payload, out var payloadRange, out _))
        {
            Assert.IsTrue(payloadRange.Offset >= 0 && payloadRange.Length >= 0 && payloadRange.Length <= length - payloadRange.Offset);
            Assert.AreEqual(payloadRange.Length, payload.Length);
        }

        _ = data.TryGetArrayLength(path, out _, out _);
        _ = data.TryGetWord(path, out var word, out _);
        Assert.IsTrue(word.Length is 0 or 32);
        _ = data.TryGetUInt64(path, out _, out _);
        _ = data.TryGetInt64(path, out _, out _);
        _ = data.TryGetAddress(path, out _, out _);
        _ = data.TryGetStringUtf8Validated(path, out _, out _, out _);
    }
}
