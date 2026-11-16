namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// <see cref="HexBytes.GetHashCode"/>. The hash is seeded per process, so no test pins a hash value: tests assert only
/// equality (deterministic within a process) and inequality for inputs that differ in hashed positions
/// (a collision has probability about 2^-32 per assertion).
/// </summary>
[TestClass]
public sealed class HexBytesHashCodeTests
{
    private const int Cap = HexBytes.MaxFullHashBytes;
    private const int Half = Cap / 2;

    private static byte[] RandomBytes(int length, int seed)
    {
        byte[] bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static int Hash(byte[] bytes) => new HexBytes(bytes).GetHashCode();

    [TestMethod]
    public void SamplingConstants_ArePinned()
    {
        // The unsampled-range tests below derive their indexes from the constant; this pins its documented value.
        Assert.AreEqual(4096, HexBytes.MaxFullHashBytes);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(4096)]
    [DataRow(4097)]
    [DataRow(10_000)]
    public void EqualContent_HashesEqual_AcrossEveryConstructionPath(int length)
    {
        byte[] bytes = RandomBytes(length, length);
        int expected = Hash(bytes);

        Assert.AreEqual(expected, HexBytes.FromArrayUnsafe((byte[])bytes.Clone()).GetHashCode());
        Assert.AreEqual(expected, new HexBytes(bytes.AsSpan()).GetHashCode());
        Assert.AreEqual(expected, HexBytes.Parse(Convert.ToHexStringLower(bytes)).GetHashCode());
        Assert.AreEqual(expected, new HexBytes(new byte[] { 0xEE }.Concat(bytes).ToArray()).Slice(1).GetHashCode());
        Assert.AreEqual(expected, (new HexBytes(bytes.AsSpan(0, 1)) + new HexBytes(bytes.AsSpan(1))).GetHashCode());
        Assert.AreEqual(expected, new HexBytes(new HexBytes(bytes).ToArray()).GetHashCode());
    }

    [TestMethod]
    public void SingleByteChangeAtAnyPosition_ChangesTheHash_ForPayloadsUpTo4KiB()
    {
        foreach (int length in new[] { 1, 4, 7, 8, 9, 31, 32, 33, 68, 100, 513, 2048, 2049, Cap })
        {
            byte[] bytes = RandomBytes(length, length);
            int baseHash = Hash(bytes);

            for (int i = 0; i < length; i++)
            {
                bytes[i] ^= 0x01;
                Assert.AreNotEqual(baseHash, Hash(bytes), $"length={length} index={i}");
                bytes[i] ^= 0x01;
            }
        }
    }

    [TestMethod]
    public void ApproveMaxCalldata_DiffersWhenOnlyTheSpenderDiffers()
    {
        // approve(spender, type(uint256).max): identical selector, length, head and tail; only the middle varies.
        // A head/tail sampled hash would collapse all of these to one value.
        var codes = new HashSet<int>();
        const int count = 1000;
        for (int i = 0; i < count; i++)
        {
            byte[] calldata = new byte[68];
            calldata[0] = 0x09; calldata[1] = 0x5e; calldata[2] = 0xa7; calldata[3] = 0xb3;
            new Random(i + 1).NextBytes(calldata.AsSpan(16, 20));
            calldata.AsSpan(36).Fill(0xFF);
            codes.Add(Hash(calldata));
        }

        Assert.IsGreaterThanOrEqualTo(count - 5, codes.Count);
    }

    [TestMethod]
    public void Length4097_SamplesFirstAndLast2048Bytes_AndSkipsTheOneMiddleByte()
    {
        byte[] bytes = RandomBytes(Cap + 1, 4097);
        int baseHash = Hash(bytes);

        // Head is 0..2047, tail is 2049..4096; only index 2048 is unsampled (the documented shortcut).
        for (int index = 0; index < bytes.Length; index++)
        {
            bytes[index] ^= 0x01;
            if (index == Half)
                Assert.AreEqual(baseHash, Hash(bytes), $"index={index} is outside both windows");
            else
                Assert.AreNotEqual(baseHash, Hash(bytes), $"index={index} is inside a window");
            bytes[index] ^= 0x01;
        }
    }

    [TestMethod]
    public void Length10000_UnsampledRangeIsExactlyHeadEndThroughTailStart()
    {
        const int length = 10_000;
        int tailStart = length - Half;
        byte[] bytes = RandomBytes(length, 21);
        int baseHash = Hash(bytes);

        foreach (int index in new[] { Half, Half + 1, 5_000, tailStart - 2, tailStart - 1 })
        {
            byte[] mutated = (byte[])bytes.Clone();
            mutated[index] ^= 0x01;
            Assert.AreEqual(baseHash, Hash(mutated), $"index={index} must be unsampled");
            Assert.AreNotEqual(new HexBytes(bytes), new HexBytes(mutated), "values still differ");
        }

        foreach (int index in new[] { 0, 1, Half - 1, tailStart, tailStart + 1, length - 1 })
        {
            byte[] mutated = (byte[])bytes.Clone();
            mutated[index] ^= 0x01;
            Assert.AreNotEqual(baseHash, Hash(mutated), $"index={index} must be sampled");
        }
    }

    [TestMethod]
    [DataRow(1, 2)]
    [DataRow(100, 101)]
    [DataRow(Cap - 1, Cap)]
    [DataRow(Cap, Cap + 1)]
    [DataRow(Cap + 1, Cap + 2)]
    [DataRow(5000, 5001)]
    [DataRow(50_000, 49_999)]
    public void Length_IsMixedIn_WhenOnlyTheLengthDiffers(int shorter, int longer)
    {
        // All-zero payloads: every hashed window has identical content, so only the mixed-in length can differ.
        Assert.AreNotEqual(Hash(new byte[shorter]), Hash(new byte[longer]));
    }

    [TestMethod]
    public void KeysThatCollideBeyondTheSampleStillWorkAsDictionaryAndHashSetKeys()
    {
        byte[] first = RandomBytes(10_000, 4);
        byte[] second = (byte[])first.Clone();
        second[5_000] ^= 0xFF;
        var a = new HexBytes(first);
        var b = new HexBytes(second);

        Assert.AreEqual(a.GetHashCode(), b.GetHashCode()); // documented head+tail shortcut
        Assert.IsFalse(a.Equals(b));

        var map = new Dictionary<HexBytes, int> { [a] = 1, [b] = 2 };
        Assert.HasCount(2, map);
        Assert.AreEqual(1, map[new HexBytes(first)]);
        Assert.AreEqual(2, map[new HexBytes(second)]);

        var set = new HashSet<HexBytes> { a, b, new HexBytes(first) };
        Assert.HasCount(2, set);
    }

    [TestMethod]
    public void Dictionary_UsesContentEquality()
    {
        var dictionary = new Dictionary<HexBytes, int> { [new HexBytes(new byte[] { 1, 2, 3 })] = 7 };

        Assert.IsTrue(dictionary.TryGetValue(HexBytes.Parse("0x010203"), out int found));
        Assert.AreEqual(7, found);
        Assert.IsFalse(dictionary.ContainsKey(HexBytes.Parse("0x010204")));
        Assert.IsFalse(dictionary.ContainsKey(HexBytes.Empty));
    }
}
