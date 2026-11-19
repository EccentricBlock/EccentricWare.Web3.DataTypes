using System.Numerics;
using EccentricWare.Web3.DataTypes.ABI;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

// A deliberately simple, recursive, test-only model of the Solidity ABI (types, values, canonical encoder, PERMISSIVE decoder).
// It shares no code with the library: it is the independent oracle for the differential and mutation tests.
//   strict accept  <=>  permissive decode succeeds AND canonical re-encoding of the decoded value equals the input bytes.

internal abstract class RType
{
    public abstract bool IsDynamic { get; }

    /// <summary>Encoded size of a static type.</summary>
    public abstract int StaticSize { get; }

    public int HeadWidth => IsDynamic ? 32 : StaticSize;

    /// <summary>Canonical Solidity type text.</summary>
    public abstract string Text { get; }
}

internal sealed class RElementary(ABITypeKind kind, int width, int scale) : RType
{
    public ABITypeKind Kind { get; } = kind;
    public int Width { get; } = width;
    public int Scale { get; } = scale;
    public override bool IsDynamic => false;
    public override int StaticSize => 32;

    public override string Text => Kind switch
    {
        ABITypeKind.UInt => $"uint{Width}",
        ABITypeKind.Int => $"int{Width}",
        ABITypeKind.Address => "address",
        ABITypeKind.Bool => "bool",
        ABITypeKind.FixedBytes => $"bytes{Width}",
        ABITypeKind.Function => "function",
        ABITypeKind.Fixed => $"fixed{Width}x{Scale}",
        ABITypeKind.UFixed => $"ufixed{Width}x{Scale}",
        _ => throw new InvalidOperationException(),
    };
}

internal sealed class RBlobType(bool isString) : RType
{
    public bool IsString { get; } = isString;
    public override bool IsDynamic => true;
    public override int StaticSize => throw new InvalidOperationException();
    public override string Text => IsString ? "string" : "bytes";
}

internal sealed class RTupleType(RType[] items) : RType
{
    public RType[] Items { get; } = items;
    public override bool IsDynamic => Items.Any(i => i.IsDynamic);
    public override int StaticSize => Items.Sum(i => i.StaticSize);
    public override string Text => "(" + string.Join(",", Items.Select(i => i.Text)) + ")";
}

internal sealed class RFixedArrayType(RType element, int length) : RType
{
    public RType Element { get; } = element;
    public int Length { get; } = length;
    public override bool IsDynamic => Element.IsDynamic;
    public override int StaticSize => Length * Element.StaticSize;
    public override string Text => Element.Text + "[" + Length + "]";
}

internal sealed class RDynamicArrayType(RType element) : RType
{
    public RType Element { get; } = element;
    public override bool IsDynamic => true;
    public override int StaticSize => throw new InvalidOperationException();
    public override string Text => Element.Text + "[]";
}

internal abstract class RValue
{
}

/// <summary>An elementary value as its full 32-byte word.</summary>
internal sealed class RWord(byte[] word) : RValue
{
    public byte[] Word { get; } = word;
}

/// <summary>The payload of bytes or string.</summary>
internal sealed class RBlob(byte[] data) : RValue
{
    public byte[] Data { get; } = data;
}

/// <summary>The elements of a tuple or array.</summary>
internal sealed class RSeq(RValue[] items) : RValue
{
    public RValue[] Items { get; } = items;
}

/// <summary>Where the reference encoder placed one value: path from the root, head slot and whole value, both root-relative.</summary>
internal sealed record RLocation(int[] Path, RType Type, RValue Value, ABIRange Head, ABIRange ValueRange);

internal static class ABIReference
{
    // ------------------------------------------------------------------ words

    internal static byte[] ToWord(BigInteger value)
    {
        byte[] minimal = value.ToByteArray(isUnsigned: value.Sign >= 0, isBigEndian: true);
        if (minimal.Length > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        var word = new byte[32];
        if (value.Sign < 0)
        {
            Array.Fill(word, (byte)0xff);
        }

        minimal.CopyTo(word, 32 - minimal.Length);
        return word;
    }

    internal static BigInteger Unsigned(ReadOnlySpan<byte> word) => new(word, isUnsigned: true, isBigEndian: true);

    internal static BigInteger Signed(ReadOnlySpan<byte> word) => new(word, isUnsigned: false, isBigEndian: true);

    /// <summary>The strict scalar rule of spec 2.4 stated with BigInteger arithmetic.</summary>
    internal static bool IsCanonical(RElementary type, ReadOnlySpan<byte> word)
    {
        switch (type.Kind)
        {
            case ABITypeKind.UInt:
            case ABITypeKind.UFixed:
                return Unsigned(word) < BigInteger.One << type.Width;
            case ABITypeKind.Int:
            case ABITypeKind.Fixed:
                BigInteger v = Signed(word);
                BigInteger half = BigInteger.One << (type.Width - 1);
                return v >= -half && v < half;
            case ABITypeKind.Address:
                return Unsigned(word) < BigInteger.One << 160;
            case ABITypeKind.Bool:
                return Unsigned(word) <= 1;
            case ABITypeKind.FixedBytes:
                return word.Slice(type.Width).IndexOfAnyExcept((byte)0) < 0;
            case ABITypeKind.Function:
                return word.Slice(24).IndexOfAnyExcept((byte)0) < 0;
            default:
                throw new InvalidOperationException();
        }
    }

    // ------------------------------------------------------------------ canonical type text

    /// <summary>Parses canonical Solidity type text such as <c>(address,uint256[])[2]</c>; the root must be a tuple.</summary>
    internal static RTupleType ParseTuple(string text)
    {
        int position = 0;
        RType type = ParseType(text, ref position);
        if (position != text.Length || type is not RTupleType tuple)
        {
            throw new FormatException("not a canonical tuple: " + text);
        }

        return tuple;
    }

    private static RType ParseType(string text, ref int position)
    {
        RType type;
        if (text[position] == '(')
        {
            position++;
            var items = new List<RType>();
            if (text[position] == ')')
            {
                position++;
            }
            else
            {
                while (true)
                {
                    items.Add(ParseType(text, ref position));
                    if (text[position++] == ')')
                    {
                        break;
                    }
                }
            }

            type = new RTupleType([.. items]);
        }
        else
        {
            int start = position;
            while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position])))
            {
                position++;
            }

            type = ParseName(text[start..position]);
        }

        while (position < text.Length && text[position] == '[')
        {
            int close = text.IndexOf(']', position);
            string length = text[(position + 1)..close];
            type = length.Length == 0 ? new RDynamicArrayType(type) : new RFixedArrayType(type, int.Parse(length));
            position = close + 1;
        }

        return type;
    }

    private static RType ParseName(string name)
    {
        if (name == "address") return new RElementary(ABITypeKind.Address, 0, 0);
        if (name == "bool") return new RElementary(ABITypeKind.Bool, 0, 0);
        if (name == "function") return new RElementary(ABITypeKind.Function, 0, 0);
        if (name == "bytes") return new RBlobType(isString: false);
        if (name == "string") return new RBlobType(isString: true);
        if (name.StartsWith("bytes")) return new RElementary(ABITypeKind.FixedBytes, int.Parse(name[5..]), 0);
        if (name.StartsWith("uint")) return new RElementary(ABITypeKind.UInt, int.Parse(name[4..]), 0);
        if (name.StartsWith("int")) return new RElementary(ABITypeKind.Int, int.Parse(name[3..]), 0);
        bool unsignedFixed = name.StartsWith("ufixed");
        string[] parts = name[(unsignedFixed ? 6 : 5)..].Split('x');
        return new RElementary(unsignedFixed ? ABITypeKind.UFixed : ABITypeKind.Fixed, int.Parse(parts[0]), int.Parse(parts[1]));
    }

    // ------------------------------------------------------------------ canonical encoder

    internal static int Measure(RType type, RValue value)
    {
        switch (type)
        {
            case RElementary:
                return 32;
            case RBlobType:
                int length = ((RBlob)value).Data.Length;
                return 32 + ((length + 31) / 32 * 32);
            case RTupleType tuple:
                return MeasureSequence(tuple.Items, ((RSeq)value).Items);
            case RFixedArrayType fixedArray:
                return MeasureSequence(Repeat(fixedArray.Element, fixedArray.Length), ((RSeq)value).Items);
            case RDynamicArrayType dynamicArray:
                RValue[] elements = ((RSeq)value).Items;
                return 32 + MeasureSequence(Repeat(dynamicArray.Element, elements.Length), elements);
            default:
                throw new InvalidOperationException();
        }
    }

    private static int MeasureSequence(RType[] types, RValue[] values)
    {
        int size = 0;
        for (int i = 0; i < types.Length; i++)
        {
            size += types[i].HeadWidth + (types[i].IsDynamic ? Measure(types[i], values[i]) : 0);
        }

        return size;
    }

    private static RType[] Repeat(RType element, int count) => Enumerable.Repeat(element, count).ToArray();

    /// <summary>Canonically encodes the argument tuple; fills <paramref name="locations"/> with one entry per value including the root.</summary>
    internal static byte[] EncodeRoot(RTupleType root, RSeq values, List<RLocation>? locations = null)
    {
        int total = Measure(root, values);
        var buffer = new byte[total];
        locations?.Add(new RLocation([], root, values, new ABIRange(0, HeadSize(root.Items)), new ABIRange(0, total)));
        Write(root, values, buffer, 0, [], locations);
        return buffer;
    }

    private static int HeadSize(RType[] types) => types.Sum(t => t.HeadWidth);

    private static void Write(RType type, RValue value, byte[] buffer, int position, int[] path, List<RLocation>? locations)
    {
        switch (type)
        {
            case RElementary:
                ((RWord)value).Word.CopyTo(buffer, position);
                break;
            case RBlobType:
                byte[] data = ((RBlob)value).Data;
                ToWord(data.Length).CopyTo(buffer, position);
                data.CopyTo(buffer, position + 32);
                break;
            case RTupleType tuple:
                WriteSequence(tuple.Items, ((RSeq)value).Items, buffer, position, position, path, locations);
                break;
            case RFixedArrayType fixedArray:
                WriteSequence(Repeat(fixedArray.Element, fixedArray.Length), ((RSeq)value).Items, buffer, position, position, path, locations);
                break;
            case RDynamicArrayType dynamicArray:
                RValue[] elements = ((RSeq)value).Items;
                ToWord(elements.Length).CopyTo(buffer, position);
                WriteSequence(Repeat(dynamicArray.Element, elements.Length), elements, buffer, position + 32, position + 32, path, locations);
                break;
        }
    }

    private static void WriteSequence(RType[] types, RValue[] values, byte[] buffer, int headStart, int offsetBase, int[] path, List<RLocation>? locations)
    {
        int headPosition = headStart;
        int tail = headStart + HeadSize(types);
        for (int i = 0; i < types.Length; i++)
        {
            int[] childPath = [.. path, i];
            if (types[i].IsDynamic)
            {
                int size = Measure(types[i], values[i]);
                ToWord(tail - offsetBase).CopyTo(buffer, headPosition);
                locations?.Add(new RLocation(childPath, types[i], values[i], new ABIRange(headPosition, 32), new ABIRange(tail, size)));
                Write(types[i], values[i], buffer, tail, childPath, locations);
                tail += size;
                headPosition += 32;
            }
            else
            {
                int size = types[i].StaticSize;
                locations?.Add(new RLocation(childPath, types[i], values[i], new ABIRange(headPosition, size), new ABIRange(headPosition, size)));
                Write(types[i], values[i], buffer, headPosition, childPath, locations);
                headPosition += size;
            }
        }
    }

    // ------------------------------------------------------------------ permissive decoder

    /// <summary>Beyond these sizes the permissive decoder gives up (a canonical document can never come close).</summary>
    private const int MaxElements = 4096;
    private const int MaxNodes = 200_000;

    /// <summary>
    /// Decodes without enforcing canonical layout: pointers are followed wherever they point, padding is ignored, but scalars must be
    /// canonical and everything must lie inside the buffer. False when anything cannot be read.
    /// </summary>
    internal static bool TryDecodePermissive(RTupleType root, byte[] buffer, out RSeq? value)
    {
        int nodes = 0;
        bool ok = TryDecode(root, buffer, 0, ref nodes, out RValue? decoded);
        value = decoded as RSeq;
        return ok;
    }

    private static bool TryDecode(RType type, byte[] buffer, int position, ref int nodes, out RValue? value)
    {
        value = null;
        if (++nodes > MaxNodes || position < 0 || position > buffer.Length)
        {
            return false;
        }

        switch (type)
        {
            case RElementary elementary:
                if (buffer.Length - position < 32)
                {
                    return false;
                }

                byte[] word = buffer.AsSpan(position, 32).ToArray();
                if (!IsCanonical(elementary, word))
                {
                    return false;
                }

                value = new RWord(word);
                return true;

            case RBlobType:
                if (buffer.Length - position < 32 || !TryNumber(buffer, position, buffer.Length, out int length) || length > buffer.Length - position - 32)
                {
                    return false;
                }

                value = new RBlob(buffer.AsSpan(position + 32, length).ToArray());
                return true;

            case RTupleType tuple:
                return TryDecodeSequence(tuple.Items, buffer, position, position, ref nodes, out value);

            case RFixedArrayType fixedArray:
                return TryDecodeSequence(Repeat(fixedArray.Element, fixedArray.Length), buffer, position, position, ref nodes, out value);

            case RDynamicArrayType dynamicArray:
                if (buffer.Length - position < 32 || !TryNumber(buffer, position, MaxElements, out int count))
                {
                    return false;
                }

                return TryDecodeSequence(Repeat(dynamicArray.Element, count), buffer, position + 32, position + 32, ref nodes, out value);

            default:
                return false;
        }
    }

    private static bool TryDecodeSequence(RType[] types, byte[] buffer, int headStart, int offsetBase, ref int nodes, out RValue? value)
    {
        value = null;
        var items = new RValue[types.Length];
        int headPosition = headStart;
        for (int i = 0; i < types.Length; i++)
        {
            RValue? item;
            if (types[i].IsDynamic)
            {
                if (buffer.Length - headPosition < 32 || !TryNumber(buffer, headPosition, buffer.Length, out int offset))
                {
                    return false;
                }

                if (!TryDecode(types[i], buffer, offsetBase + offset, ref nodes, out item))
                {
                    return false;
                }

                headPosition += 32;
            }
            else
            {
                if (!TryDecode(types[i], buffer, headPosition, ref nodes, out item))
                {
                    return false;
                }

                headPosition += types[i].StaticSize;
            }

            items[i] = item!;
        }

        value = new RSeq(items);
        return true;
    }

    /// <summary>Reads a 256-bit unsigned number; false when it exceeds <paramref name="max"/>.</summary>
    private static bool TryNumber(byte[] buffer, int position, int max, out int number)
    {
        BigInteger n = Unsigned(buffer.AsSpan(position, 32));
        if (n > max)
        {
            number = 0;
            return false;
        }

        number = (int)n;
        return true;
    }
}

/// <summary>Deterministic generator (xorshift64*): the same seed gives the same schemas and values on every platform and runtime.</summary>
internal sealed class DeterministicRng(ulong seed)
{
    private ulong _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    internal ulong NextUInt64()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return _state * 0x2545F4914F6CDD1DUL;
    }

    /// <summary>A value in [0, <paramref name="bound"/>).</summary>
    internal int Next(int bound) => (int)(NextUInt64() % (ulong)bound);

    internal byte[] Bytes(int count)
    {
        var bytes = new byte[count];
        for (int i = 0; i < count; i++)
        {
            bytes[i] = (byte)NextUInt64();
        }

        return bytes;
    }
}

internal static class ABIReferenceGenerator
{
    private static readonly int[] Widths = [8, 16, 24, 32, 40, 64, 72, 96, 128, 160, 200, 248, 256];

    internal static RTupleType RandomRoot(DeterministicRng rng, int depth)
    {
        int arity = rng.Next(5);
        return new RTupleType(Enumerable.Range(0, arity).Select(_ => RandomType(rng, depth)).ToArray());
    }

    internal static RType RandomType(DeterministicRng rng, int depth)
    {
        int pick = rng.Next(depth > 0 ? 16 : 11);
        switch (pick)
        {
            case 0: return new RElementary(ABITypeKind.UInt, Widths[rng.Next(Widths.Length)], 0);
            case 1: return new RElementary(ABITypeKind.UInt, 256, 0);
            case 2: return new RElementary(ABITypeKind.Int, Widths[rng.Next(Widths.Length)], 0);
            case 3: return new RElementary(ABITypeKind.Address, 0, 0);
            case 4: return new RElementary(ABITypeKind.Bool, 0, 0);
            case 5: return new RElementary(ABITypeKind.FixedBytes, 1 + rng.Next(32), 0);
            case 6: return new RElementary(rng.Next(2) == 0 ? ABITypeKind.Fixed : ABITypeKind.UFixed, Widths[rng.Next(Widths.Length)], 1 + rng.Next(80));
            case 7: return new RElementary(ABITypeKind.Function, 0, 0);
            case 8: return new RBlobType(isString: false);
            case 9: return new RBlobType(isString: true);
            case 10: return new RElementary(ABITypeKind.Int, 256, 0);
            case 11:
            case 12: return new RTupleType(Enumerable.Range(0, 1 + rng.Next(4)).Select(_ => RandomType(rng, depth - 1)).ToArray());
            case 13: return new RFixedArrayType(RandomType(rng, depth - 1), 1 + rng.Next(3));
            default: return new RDynamicArrayType(RandomType(rng, depth - 1));
        }
    }

    internal static RSeq RandomValue(DeterministicRng rng, RTupleType root) => (RSeq)RandomValue(rng, root, 0);

    private static RValue RandomValue(DeterministicRng rng, RType type, int level)
    {
        switch (type)
        {
            case RElementary elementary:
                return new RWord(RandomWord(rng, elementary));
            case RBlobType:
                int length = rng.Next(4) == 0 ? rng.Next(4) * 32 + rng.Next(2) : rng.Next(70);
                return new RBlob(rng.Bytes(length));
            case RTupleType tuple:
                return new RSeq(tuple.Items.Select(i => RandomValue(rng, i, level + 1)).ToArray());
            case RFixedArrayType fixedArray:
                return new RSeq(Enumerable.Range(0, fixedArray.Length).Select(_ => RandomValue(rng, fixedArray.Element, level + 1)).ToArray());
            case RDynamicArrayType dynamicArray:
                int count = rng.Next(5);
                return new RSeq(Enumerable.Range(0, count).Select(_ => RandomValue(rng, dynamicArray.Element, level + 1)).ToArray());
            default:
                throw new InvalidOperationException();
        }
    }

    private static byte[] RandomWord(DeterministicRng rng, RElementary type)
    {
        BigInteger value;
        switch (type.Kind)
        {
            case ABITypeKind.UInt:
            case ABITypeKind.UFixed:
                BigInteger limit = BigInteger.One << type.Width;
                value = rng.Next(6) switch
                {
                    0 => BigInteger.Zero,
                    1 => limit - 1,
                    2 => BigInteger.One,
                    _ => ABIReference.Unsigned(rng.Bytes(type.Width / 8)),
                };
                return ABIReference.ToWord(value);
            case ABITypeKind.Int:
            case ABITypeKind.Fixed:
                BigInteger half = BigInteger.One << (type.Width - 1);
                value = rng.Next(7) switch
                {
                    0 => BigInteger.Zero,
                    1 => half - 1,
                    2 => -half,
                    3 => BigInteger.MinusOne,
                    _ => new BigInteger(rng.Bytes(type.Width / 8), isUnsigned: false, isBigEndian: true),
                };
                return ABIReference.ToWord(value);
            case ABITypeKind.Address:
                return ABIReference.ToWord(ABIReference.Unsigned(rng.Bytes(20)));
            case ABITypeKind.Bool:
                return ABIReference.ToWord(rng.Next(2));
            case ABITypeKind.FixedBytes:
                var fixedBytes = new byte[32];
                rng.Bytes(type.Width).CopyTo(fixedBytes, 0);
                return fixedBytes;
            case ABITypeKind.Function:
                var function = new byte[32];
                rng.Bytes(24).CopyTo(function, 0);
                return function;
            default:
                throw new InvalidOperationException();
        }
    }
}
