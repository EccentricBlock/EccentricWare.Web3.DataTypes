namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Invalid hex text must be rejected before a result array is allocated. The UTF-8 parser always worked this way; the char
/// parser allocated <c>length / 2</c> bytes first and only then discovered the bad digit. Runs serially: it handles multi-megabyte text.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class HexBytesCharParseAllocationTests
{
    private const int Digits = 16 * 1024 * 1024;

    private static long Smallest(Func<bool> parse, int trials = 3)
    {
        long best = long.MaxValue;
        for (int i = 0; i < trials; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.IsFalse(parse());
            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return best;
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void InvalidDigitAtTheEnd_AllocatesNoResultArray(bool prefix, bool quoted)
    {
        string body = new string('a', Digits) + "g";
        string text = (quoted ? "\"" : "") + (prefix ? "0x" : "") + body + (quoted ? "\"" : "");
        _ = HexBytes.TryParse(text.AsSpan(0, 64), out _); // warm up the code path on a short input

        long allocated = Smallest(() => HexBytes.TryParse(text.AsSpan(), out _));

        // Before the fix this was about 8 MiB (the result array).
        Assert.IsLessThan(64 * 1024, allocated, $"allocated {allocated} bytes");
    }

    [TestMethod]
    public void ValidText_StillAllocatesExactlyTheResult()
    {
        string text = new('a', Digits);
        HexBytes parsed = default;
        long best = long.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.IsTrue(HexBytes.TryParse(text.AsSpan(), out parsed));
            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.AreEqual(Digits / 2, parsed.Length);
        Assert.IsLessThan((Digits / 2) + 256, best, $"allocated {best} bytes");
    }
}
