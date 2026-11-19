namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// A second, independently written reference encoder, a literal transcription of the Solidity ABI specification's formal rules
/// (enc(X) = head(X(1)) ... head(X(k)) tail(X(1)) ... tail(X(k)); a dynamic member's head is the byte offset of its tail from the
/// start of enc(X), a static member's head is its encoding and its tail is empty). It differs from <see cref="ABIReference"/> in
/// structure: it works by value-returning concatenation (every value is encoded standalone, then composed) instead of measuring first
/// and writing into positions of one buffer, and it shares no helper with it. Test-only.
/// </summary>
internal static class ABIIndependentEncoder
{
    internal static byte[] EncodeArguments(RTupleType type, RSeq values) => Sequence(type.Items, values.Items);

    private static byte[] Encode(RType type, RValue value)
    {
        switch (type)
        {
            case RElementary:
                return ((RWord)value).Word;
            case RBlobType:
                byte[] data = ((RBlob)value).Data;
                return Concat(Number((ulong)data.Length), data, new byte[(32 - data.Length % 32) % 32]);
            case RTupleType tuple:
                return Sequence(tuple.Items, ((RSeq)value).Items);
            case RFixedArrayType fixedArray:
                RValue[] fixedItems = ((RSeq)value).Items;
                return Sequence(fixedItems.Select(_ => fixedArray.Element).ToArray(), fixedItems);
            case RDynamicArrayType dynamicArray:
                RValue[] items = ((RSeq)value).Items;
                return Concat(Number((ulong)items.Length), Sequence(items.Select(_ => dynamicArray.Element).ToArray(), items));
            default:
                throw new InvalidOperationException();
        }
    }

    private static byte[] Sequence(RType[] types, RValue[] values)
    {
        var encodings = new byte[types.Length][];
        int headLength = 0;
        for (int i = 0; i < types.Length; i++)
        {
            encodings[i] = Encode(types[i], values[i]);
            headLength += IsDynamicType(types[i]) ? 32 : encodings[i].Length;
        }

        var head = new List<byte>();
        var tail = new List<byte>();
        for (int i = 0; i < types.Length; i++)
        {
            if (IsDynamicType(types[i]))
            {
                head.AddRange(Number((ulong)(headLength + tail.Count)));
                tail.AddRange(encodings[i]);
            }
            else
            {
                head.AddRange(encodings[i]);
            }
        }

        return [.. head, .. tail];
    }

    // Own recursive definition of dynamic-ness (bytes, string, T[], T[k] with dynamic T, tuple with a dynamic member).
    private static bool IsDynamicType(RType type) => type switch
    {
        RBlobType => true,
        RDynamicArrayType => true,
        RFixedArrayType fixedArray => IsDynamicType(fixedArray.Element),
        RTupleType tuple => tuple.Items.Any(IsDynamicType),
        _ => false,
    };

    private static byte[] Number(ulong value)
    {
        var word = new byte[32];
        for (int i = 0; i < 8; i++)
        {
            word[31 - i] = (byte)(value >> (8 * i));
        }

        return word;
    }

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];
}
