namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Small deterministic helpers shared by the value converter tests.</summary>
internal static class ConverterTestData
{
    public static byte[] RandomBytes(Random random, int length)
    {
        byte[] bytes = new byte[length];
        random.NextBytes(bytes);
        return bytes;
    }

    public static byte[] Repeated(byte value, int length) => Enumerable.Repeat(value, length).ToArray();

    /// <summary>Bytes 0x01, 0x02, ... of the given length (each byte is its 1-based offset).</summary>
    public static byte[] Ascending(int length) => Enumerable.Range(1, length).Select(i => (byte)i).ToArray();

    public static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    public static int Sign(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => Math.Sign(left.SequenceCompareTo(right));
}
