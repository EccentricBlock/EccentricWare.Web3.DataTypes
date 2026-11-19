using System.Buffers.Binary;
using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

internal enum EvKind
{
    UInt64,
    Int64,
    UInt256,
    Int256,
    Address,
    Bool,
    FixedBytes,
    Function,
    Bytes,
    String,
    SignedScaled,
    UnsignedScaled,
    BeginTuple,
    BeginFixedArray,
    BeginDynamicArray,
    End,
}

/// <summary>One writer event. A script of events is replayed against ABIWriter, ABIMeasurer and ABICallEncoder, and can be edited to build misuse sequences.</summary>
internal readonly record struct Ev(EvKind Kind, byte[]? Data = null, ulong U = 0, long I = 0, uint Selector = 0, int Count = 0)
{
    internal static Ev Begin(string kind, int count = 0) => kind switch
    {
        "tuple" => new Ev(EvKind.BeginTuple),
        "fixed" => new Ev(EvKind.BeginFixedArray),
        _ => new Ev(EvKind.BeginDynamicArray, Count: count),
    };

    internal static Ev End => new(EvKind.End);

    internal static Ev U64(ulong value) => new(EvKind.UInt64, U: value);

    internal static Ev I64(long value) => new(EvKind.Int64, I: value);

    internal static Ev Uint256(byte[] word) => new(EvKind.UInt256, word);

    internal static Ev Int256(byte[] word) => new(EvKind.Int256, word);

    internal static Ev Addr(byte[] address20) => new(EvKind.Address, address20);

    internal static Ev Bool(bool value) => new(EvKind.Bool, U: value ? 1UL : 0UL);

    internal static Ev FixedB(byte[] bytes) => new(EvKind.FixedBytes, bytes);

    internal static Ev Func(byte[] address20, uint selector) => new(EvKind.Function, address20, Selector: selector);

    internal static Ev Blob(byte[] data) => new(EvKind.Bytes, data);

    internal static Ev Text(byte[] utf8) => new(EvKind.String, utf8);
}

internal readonly record struct RunResult(bool Ok, ABIStatus Status, int Length, int FailedAt);

/// <summary>Replays scripts against the three public encoders and builds scripts from reference-model values.</summary>
internal static class ABIWriteTestSupport
{
    internal static ABIEncodeLimits EncLimits(int? bytes = null, int? depth = null, int? array = null, int? values = null, int? work = null)
    {
        var d = ABIEncodeLimits.Default;
        return new ABIEncodeLimits(bytes ?? d.MaxEncodedBytes, depth ?? d.MaxValueDepth, array ?? d.MaxArrayElements, values ?? d.MaxWrittenValues, work ?? d.MaxWorkUnits);
    }

    internal static ABIEncodeLimits GenerousEncode => new(16 * 1024 * 1024, ABIEncodeLimits.HardMaxValueDepth, 1_000_000, 10_000_000, 100_000_000);

    internal static byte[] Addr20(byte fill) => Enumerable.Repeat(fill, 20).ToArray();

    internal static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    // ------------------------------------------------------------------ scripts from reference values

    /// <summary>The event script that supplies <paramref name="value"/> (a root tuple's members) to a writer for <paramref name="type"/>.</summary>
    internal static List<Ev> Script(RTupleType type, RSeq value, bool rawStrings = false)
    {
        var events = new List<Ev>();
        for (int i = 0; i < type.Items.Length; i++)
        {
            Emit(type.Items[i], value.Items[i], events, rawStrings);
        }

        return events;
    }

    /// <summary>With <paramref name="rawStrings"/> every string is supplied through the raw bytes entry point (any content, no UTF-8 claim).</summary>
    private static void Emit(RType type, RValue value, List<Ev> events, bool rawStrings)
    {
        switch (type)
        {
            case RElementary e:
                byte[] w = ((RWord)value).Word;
                events.Add(e.Kind switch
                {
                    ABITypeKind.UInt => Ev.Uint256(w),
                    ABITypeKind.Int => Ev.Int256(w),
                    ABITypeKind.Address => Ev.Addr(w[12..]),
                    ABITypeKind.Bool => Ev.Bool(w[31] == 1),
                    ABITypeKind.FixedBytes => Ev.FixedB(w[..e.Width]),
                    ABITypeKind.Function => Ev.Func(w[..20], BinaryPrimitives.ReadUInt32BigEndian(w.AsSpan(20, 4))),
                    ABITypeKind.Fixed => new Ev(EvKind.SignedScaled, w),
                    ABITypeKind.UFixed => new Ev(EvKind.UnsignedScaled, w),
                    _ => throw new InvalidOperationException(),
                });
                break;
            case RBlobType blob:
                byte[] data = ((RBlob)value).Data;
                events.Add(blob.IsString && !rawStrings ? Ev.Text(data) : Ev.Blob(data));
                break;
            case RTupleType tuple:
                events.Add(Ev.Begin("tuple"));
                EmitItems(tuple.Items, ((RSeq)value).Items, events, rawStrings);
                events.Add(Ev.End);
                break;
            case RFixedArrayType fixedArray:
                events.Add(Ev.Begin("fixed"));
                foreach (RValue item in ((RSeq)value).Items)
                {
                    Emit(fixedArray.Element, item, events, rawStrings);
                }

                events.Add(Ev.End);
                break;
            case RDynamicArrayType dynamicArray:
                RValue[] items = ((RSeq)value).Items;
                events.Add(Ev.Begin("dynamic", items.Length));
                foreach (RValue item in items)
                {
                    Emit(dynamicArray.Element, item, events, rawStrings);
                }

                events.Add(Ev.End);
                break;
        }
    }

    private static void EmitItems(RType[] types, RValue[] values, List<Ev> events, bool rawStrings)
    {
        for (int i = 0; i < types.Length; i++)
        {
            Emit(types[i], values[i], events, rawStrings);
        }
    }

    /// <summary>True when every <c>string</c> payload in the value tree is strictly valid UTF-8 (what the writer requires of a string slot).</summary>
    internal static bool StringsAreText(RType type, RValue value) => type switch
    {
        RBlobType { IsString: true } => System.Text.Unicode.Utf8.IsValid(((RBlob)value).Data),
        RTupleType tuple => tuple.Items.Select((t, i) => StringsAreText(t, ((RSeq)value).Items[i])).All(ok => ok),
        RFixedArrayType fixedArray => ((RSeq)value).Items.All(v => StringsAreText(fixedArray.Element, v)),
        RDynamicArrayType dynamicArray => ((RSeq)value).Items.All(v => StringsAreText(dynamicArray.Element, v)),
        _ => true,
    };

    /// <summary>Copies a value tree replacing every <c>string</c> payload with valid UTF-8 (the generator's random bytes are rarely valid), keeping the shape.</summary>
    internal static RValue Utf8Sanitised(RType type, RValue value, DeterministicRng rng)
    {
        switch (type)
        {
            case RBlobType { IsString: true }:
                byte[] source = ((RBlob)value).Data;
                var text = new List<byte>();
                foreach (byte b in source)
                {
                    switch (rng.Next(6))
                    {
                        case 0: text.AddRange(Utf8("é")); break; // 2 bytes
                        case 1: text.AddRange(Utf8("€")); break; // 3 bytes
                        case 2: text.AddRange(Utf8("😀")); break; // 4 bytes
                        default: text.Add((byte)(0x20 + b % 0x5f)); break; // printable ASCII
                    }
                }

                return new RBlob([.. text]);
            case RTupleType tuple:
                return new RSeq(tuple.Items.Select((t, i) => Utf8Sanitised(t, ((RSeq)value).Items[i], rng)).ToArray());
            case RFixedArrayType fixedArray:
                return new RSeq(((RSeq)value).Items.Select(v => Utf8Sanitised(fixedArray.Element, v, rng)).ToArray());
            case RDynamicArrayType dynamicArray:
                return new RSeq(((RSeq)value).Items.Select(v => Utf8Sanitised(dynamicArray.Element, v, rng)).ToArray());
            default:
                return value;
        }
    }

    /// <summary>A random root, a value for it with valid UTF-8 strings, and the reference encoding of that value.</summary>
    internal static (RTupleType Type, RSeq Value, byte[] Expected, ABISchema Schema, List<Ev> Script) Generate(ulong seed, int depth = 3)
    {
        var rng = new DeterministicRng(seed);
        RTupleType type = ABIReferenceGenerator.RandomRoot(rng, depth);
        var value = (RSeq)Utf8Sanitised(type, ABIReferenceGenerator.RandomValue(rng, type), rng);
        return (type, value, ABIReference.EncodeRoot(type, value), CompileTuple(type.Text), Script(type, value));
    }

    // ------------------------------------------------------------------ replay

    internal static bool Apply(ref ABIWriter w, in Ev ev, out ABIStatus s) => ev.Kind switch
    {
        EvKind.UInt64 => w.TryWriteUInt64(ev.U, out s),
        EvKind.Int64 => w.TryWriteInt64(ev.I, out s),
        EvKind.UInt256 => w.TryWriteUInt256Word(ev.Data, out s),
        EvKind.Int256 => w.TryWriteInt256Word(ev.Data, out s),
        EvKind.Address => w.TryWriteAddress(ev.Data, out s),
        EvKind.Bool => w.TryWriteBool(ev.U != 0, out s),
        EvKind.FixedBytes => w.TryWriteFixedBytes(ev.Data, out s),
        EvKind.Function => w.TryWriteFunction(ev.Data, ev.Selector, out s),
        EvKind.Bytes => w.TryWriteBytes(ev.Data, out s),
        EvKind.String => w.TryWriteStringUtf8(ev.Data, out s),
        EvKind.SignedScaled => w.TryWriteSignedScaledWord(ev.Data, out s),
        EvKind.UnsignedScaled => w.TryWriteUnsignedScaledWord(ev.Data, out s),
        EvKind.BeginTuple => w.TryBeginTuple(out s),
        EvKind.BeginFixedArray => w.TryBeginFixedArray(out s),
        EvKind.BeginDynamicArray => w.TryBeginDynamicArray(ev.Count, out s),
        EvKind.End => w.TryEndContainer(out s),
        _ => throw new InvalidOperationException(),
    };

    internal static bool Apply(ref ABIMeasurer w, in Ev ev, out ABIStatus s) => ev.Kind switch
    {
        EvKind.UInt64 => w.TryWriteUInt64(ev.U, out s),
        EvKind.Int64 => w.TryWriteInt64(ev.I, out s),
        EvKind.UInt256 => w.TryWriteUInt256Word(ev.Data, out s),
        EvKind.Int256 => w.TryWriteInt256Word(ev.Data, out s),
        EvKind.Address => w.TryWriteAddress(ev.Data, out s),
        EvKind.Bool => w.TryWriteBool(ev.U != 0, out s),
        EvKind.FixedBytes => w.TryWriteFixedBytes(ev.Data, out s),
        EvKind.Function => w.TryWriteFunction(ev.Data, ev.Selector, out s),
        EvKind.Bytes => w.TryWriteBytes(ev.Data, out s),
        EvKind.String => w.TryWriteStringUtf8(ev.Data, out s),
        EvKind.SignedScaled => w.TryWriteSignedScaledWord(ev.Data, out s),
        EvKind.UnsignedScaled => w.TryWriteUnsignedScaledWord(ev.Data, out s),
        EvKind.BeginTuple => w.TryBeginTuple(out s),
        EvKind.BeginFixedArray => w.TryBeginFixedArray(out s),
        EvKind.BeginDynamicArray => w.TryBeginDynamicArray(ev.Count, out s),
        EvKind.End => w.TryEndContainer(out s),
        _ => throw new InvalidOperationException(),
    };

    internal static bool Apply(ref ABICallEncoder w, in Ev ev, out ABIStatus s) => ev.Kind switch
    {
        EvKind.UInt64 => w.TryWriteUInt64(ev.U, out s),
        EvKind.Int64 => w.TryWriteInt64(ev.I, out s),
        EvKind.UInt256 => w.TryWriteUInt256Word(ev.Data, out s),
        EvKind.Int256 => w.TryWriteInt256Word(ev.Data, out s),
        EvKind.Address => w.TryWriteAddress(ev.Data, out s),
        EvKind.Bool => w.TryWriteBool(ev.U != 0, out s),
        EvKind.FixedBytes => w.TryWriteFixedBytes(ev.Data, out s),
        EvKind.Function => w.TryWriteFunction(ev.Data, ev.Selector, out s),
        EvKind.Bytes => w.TryWriteBytes(ev.Data, out s),
        EvKind.String => w.TryWriteStringUtf8(ev.Data, out s),
        EvKind.SignedScaled => w.TryWriteSignedScaledWord(ev.Data, out s),
        EvKind.UnsignedScaled => w.TryWriteUnsignedScaledWord(ev.Data, out s),
        EvKind.BeginTuple => w.TryBeginTuple(out s),
        EvKind.BeginFixedArray => w.TryBeginFixedArray(out s),
        EvKind.BeginDynamicArray => w.TryBeginDynamicArray(ev.Count, out s),
        EvKind.End => w.TryEndContainer(out s),
        _ => throw new InvalidOperationException(),
    };

    internal static RunResult RunWriter(IReadOnlyList<Ev> script, ABISchema schema, Span<byte> destination, ABIEncodeLimits limits, int frames = 32, bool finish = true)
    {
        Span<ABIWriteFrame> scratch = stackalloc ABIWriteFrame[32];
        if (!ABIWriter.TryBegin(schema, destination, scratch.Slice(0, frames), in limits, out ABIWriter w, out ABIStatus s))
        {
            return new RunResult(false, s, 0, -1);
        }

        for (int i = 0; i < script.Count; i++)
        {
            if (!Apply(ref w, script[i], out s))
            {
                return new RunResult(false, s, 0, i);
            }
        }

        if (!finish)
        {
            return new RunResult(true, ABIStatus.Success, 0, -1);
        }

        return w.TryFinish(out int length, out s)
            ? new RunResult(true, ABIStatus.Success, length, -1)
            : new RunResult(false, s, 0, script.Count);
    }

    internal static RunResult RunMeasurer(IReadOnlyList<Ev> script, ABISchema schema, ABIEncodeLimits limits, int frames = 32)
    {
        Span<ABIMeasureFrame> scratch = stackalloc ABIMeasureFrame[32];
        if (!ABIMeasurer.TryBegin(schema, scratch.Slice(0, frames), in limits, out ABIMeasurer m, out ABIStatus s))
        {
            return new RunResult(false, s, 0, -1);
        }

        for (int i = 0; i < script.Count; i++)
        {
            if (!Apply(ref m, script[i], out s))
            {
                return new RunResult(false, s, 0, i);
            }
        }

        return m.TryFinish(out int length, out s)
            ? new RunResult(true, ABIStatus.Success, length, -1)
            : new RunResult(false, s, 0, script.Count);
    }

    internal static RunResult RunCall(IReadOnlyList<Ev> script, ABIFunctionSchema function, Span<byte> destination, ABIEncodeLimits limits, int frames = 32)
    {
        Span<ABIWriteFrame> scratch = stackalloc ABIWriteFrame[32];
        if (!ABICallEncoder.TryBegin(function, destination, scratch.Slice(0, frames), in limits, out ABICallEncoder w, out ABIStatus s))
        {
            return new RunResult(false, s, 0, -1);
        }

        for (int i = 0; i < script.Count; i++)
        {
            if (!Apply(ref w, script[i], out s))
            {
                return new RunResult(false, s, 0, i);
            }
        }

        return w.TryFinish(out int length, out s)
            ? new RunResult(true, ABIStatus.Success, length, -1)
            : new RunResult(false, s, 0, script.Count);
    }

    // ------------------------------------------------------------------ assertions

    internal const byte Garbage = 0xA5;

    /// <summary>Writes into a garbage-filled buffer with slack, asserts success, that the slack is untouched, and returns the document bytes.</summary>
    internal static byte[] WriteOk(IReadOnlyList<Ev> script, ABISchema schema, ABIEncodeLimits? limits = null, int slack = 64)
    {
        ABIEncodeLimits effective = limits ?? GenerousEncode;
        RunResult measured = RunMeasurer(script, schema, effective);
        Assert.IsTrue(measured.Ok, $"measure {schema.CanonicalType}: {measured.Status} at event {measured.FailedAt}");

        var buffer = new byte[measured.Length + slack];
        Array.Fill(buffer, Garbage);
        RunResult written = RunWriter(script, schema, buffer, effective);
        Assert.IsTrue(written.Ok, $"write {schema.CanonicalType}: {written.Status} at event {written.FailedAt}");
        Assert.AreEqual(measured.Length, written.Length, $"measured vs written length of {schema.CanonicalType}");
        for (int i = written.Length; i < buffer.Length; i++)
        {
            Assert.AreEqual(Garbage, buffer[i], $"byte {i} beyond the document was modified");
        }

        return buffer[..written.Length];
    }

    /// <summary>Writes a call into a garbage-filled buffer and returns exactly the call bytes (selector first).</summary>
    internal static byte[] CallOk(IReadOnlyList<Ev> script, ABIFunctionSchema function, ABIEncodeLimits? limits = null, int slack = 64)
    {
        int size = 4 + WriteOk(script, function.InputSchema, limits).Length;
        var buffer = new byte[size + slack];
        Array.Fill(buffer, Garbage);
        RunResult result = RunCall(script, function, buffer, limits ?? GenerousEncode);
        Assert.IsTrue(result.Ok, $"call {function.CanonicalSignature}: {result.Status} at event {result.FailedAt}");
        Assert.AreEqual(size, result.Length);
        for (int i = size; i < buffer.Length; i++)
        {
            Assert.AreEqual(Garbage, buffer[i], $"byte {i} beyond the call was modified");
        }

        return buffer[..size];
    }

    /// <summary>Strictly decodes <paramref name="bytes"/> and checks every path against the reference value.</summary>
    internal static void AssertDecodesTo(byte[] bytes, ABISchema schema, RTupleType type, RSeq value)
    {
        var limits = ABIDecodeLimits.Default with { MaxWorkUnits = 10_000_000, MaxVisitedValues = 1_000_000 };
        Assert.IsTrue(ABIData.TryDecodeComplete(bytes, schema, in limits, out ABIData data, out ABIStatus status), $"decode of written {schema.CanonicalType}: {status}");
        ABIReferenceAssertions.AssertEveryPathMatches(in data, type, value, bytes);
    }

    internal static string ToHex(byte[] bytes) => Convert.ToHexStringLower(bytes);
}
