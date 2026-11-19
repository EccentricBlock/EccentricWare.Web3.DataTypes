using System.Buffers.Binary;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// The byte format of a writer script, so a coverage-guided fuzzer can drive <see cref="ABIFuzzTarget.RunWriterScript"/> with raw bytes:
/// <c>[schema index][event]*</c>. An event is <c>[opcode &amp; 15][payload]</c>; payloads are fixed width and zero padded when the input
/// ends early, except blobs, which take what is left. Every byte string parses (there is no invalid script), so mutations always reach the writer.
/// <code>
/// 0 UInt64 u64 | 1 Int64 i64 | 2 UInt256 word32 | 3 Int256 word32 | 4 Address 20 | 5 Bool 1 | 6 FixedBytes len(1, mod 34) bytes
/// 7 Function 20 + selector 4 | 8 Bytes len(2) data | 9 String len(2) data | 10 SignedScaled word32 | 11 UnsignedScaled word32
/// 12 BeginTuple | 13 BeginFixedArray | 14 BeginDynamicArray count(4, signed) | 15 End
/// </code>
/// </summary>
internal static class ABIFuzzScript
{
    /// <summary>Events past this number are ignored, so one input cannot make the target arbitrarily slow.</summary>
    internal const int MaxEvents = 512;

    internal static List<Ev> Parse(ReadOnlySpan<byte> bytes)
    {
        var events = new List<Ev>();
        int i = 0;
        while (i < bytes.Length && events.Count < MaxEvents)
        {
            switch (bytes[i++] & 15)
            {
                case 0: events.Add(new Ev(EvKind.UInt64, U: ReadUInt64(bytes, ref i))); break;
                case 1: events.Add(new Ev(EvKind.Int64, I: unchecked((long)ReadUInt64(bytes, ref i)))); break;
                case 2: events.Add(new Ev(EvKind.UInt256, Take(bytes, ref i, 32))); break;
                case 3: events.Add(new Ev(EvKind.Int256, Take(bytes, ref i, 32))); break;
                case 4: events.Add(new Ev(EvKind.Address, Take(bytes, ref i, 20))); break;
                case 5: events.Add(new Ev(EvKind.Bool, U: Take(bytes, ref i, 1)[0] != 0 ? 1UL : 0UL)); break;
                case 6:
                    int length = Take(bytes, ref i, 1)[0] % 34;
                    events.Add(new Ev(EvKind.FixedBytes, Take(bytes, ref i, length)));
                    break;
                case 7:
                    byte[] address = Take(bytes, ref i, 20);
                    events.Add(new Ev(EvKind.Function, address, Selector: BinaryPrimitives.ReadUInt32BigEndian(Take(bytes, ref i, 4))));
                    break;
                case 8: events.Add(new Ev(EvKind.Bytes, ReadBlob(bytes, ref i))); break;
                case 9: events.Add(new Ev(EvKind.String, ReadBlob(bytes, ref i))); break;
                case 10: events.Add(new Ev(EvKind.SignedScaled, Take(bytes, ref i, 32))); break;
                case 11: events.Add(new Ev(EvKind.UnsignedScaled, Take(bytes, ref i, 32))); break;
                case 12: events.Add(new Ev(EvKind.BeginTuple)); break;
                case 13: events.Add(new Ev(EvKind.BeginFixedArray)); break;
                case 14: events.Add(new Ev(EvKind.BeginDynamicArray, Count: BinaryPrimitives.ReadInt32BigEndian(Take(bytes, ref i, 4)))); break;
                default: events.Add(Ev.End); break;
            }
        }

        return events;
    }

    /// <summary>The inverse of <see cref="Parse"/> for events the format can represent (blobs up to 65,535 bytes, fixed bytes up to 33).</summary>
    internal static byte[] Encode(int schemaIndex, IEnumerable<Ev> events)
    {
        var bytes = new List<byte> { (byte)schemaIndex };
        foreach (Ev ev in events)
        {
            switch (ev.Kind)
            {
                case EvKind.UInt64: bytes.Add(0); AddU64(bytes, ev.U); break;
                case EvKind.Int64: bytes.Add(1); AddU64(bytes, unchecked((ulong)ev.I)); break;
                case EvKind.UInt256: bytes.Add(2); bytes.AddRange(Pad(ev.Data!, 32)); break;
                case EvKind.Int256: bytes.Add(3); bytes.AddRange(Pad(ev.Data!, 32)); break;
                case EvKind.Address: bytes.Add(4); bytes.AddRange(Pad(ev.Data!, 20)); break;
                case EvKind.Bool: bytes.Add(5); bytes.Add((byte)(ev.U != 0 ? 1 : 0)); break;
                case EvKind.FixedBytes: bytes.Add(6); bytes.Add((byte)ev.Data!.Length); bytes.AddRange(ev.Data); break;
                case EvKind.Function:
                    bytes.Add(7);
                    bytes.AddRange(Pad(ev.Data!, 20));
                    var selector = new byte[4];
                    BinaryPrimitives.WriteUInt32BigEndian(selector, ev.Selector);
                    bytes.AddRange(selector);
                    break;
                case EvKind.Bytes: bytes.Add(8); AddBlob(bytes, ev.Data!); break;
                case EvKind.String: bytes.Add(9); AddBlob(bytes, ev.Data!); break;
                case EvKind.SignedScaled: bytes.Add(10); bytes.AddRange(Pad(ev.Data!, 32)); break;
                case EvKind.UnsignedScaled: bytes.Add(11); bytes.AddRange(Pad(ev.Data!, 32)); break;
                case EvKind.BeginTuple: bytes.Add(12); break;
                case EvKind.BeginFixedArray: bytes.Add(13); break;
                case EvKind.BeginDynamicArray:
                    bytes.Add(14);
                    var count = new byte[4];
                    BinaryPrimitives.WriteInt32BigEndian(count, ev.Count);
                    bytes.AddRange(count);
                    break;
                default: bytes.Add(15); break;
            }
        }

        return [.. bytes];
    }

    private static byte[] Pad(byte[] data, int length)
    {
        if (data.Length != length)
        {
            throw new InvalidOperationException($"event payload of {data.Length} bytes cannot be represented as {length}");
        }

        return data;
    }

    private static void AddU64(List<byte> bytes, ulong value)
    {
        var buffer = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
        bytes.AddRange(buffer);
    }

    private static void AddBlob(List<byte> bytes, byte[] data)
    {
        if (data.Length > ushort.MaxValue)
        {
            throw new InvalidOperationException("blob too long for the script format");
        }

        bytes.Add((byte)(data.Length >> 8));
        bytes.Add((byte)data.Length);
        bytes.AddRange(data);
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> bytes, ref int i) => BinaryPrimitives.ReadUInt64BigEndian(Take(bytes, ref i, 8));

    /// <summary>Exactly <paramref name="count"/> bytes; zero padded when the input ends first.</summary>
    private static byte[] Take(ReadOnlySpan<byte> bytes, ref int i, int count)
    {
        var result = new byte[count];
        int available = Math.Min(count, bytes.Length - i);
        bytes.Slice(i, available).CopyTo(result);
        i += available;
        return result;
    }

    private static byte[] ReadBlob(ReadOnlySpan<byte> bytes, ref int i)
    {
        int length = BinaryPrimitives.ReadUInt16BigEndian(Take(bytes, ref i, 2));
        length = Math.Min(length, bytes.Length - i);
        return Take(bytes, ref i, length);
    }
}
