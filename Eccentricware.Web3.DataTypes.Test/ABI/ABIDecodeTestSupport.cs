using System.Text;
using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>Shared helpers for the decoder tests. Expected values are written by hand or come from ABIFixtures, never from the code under test.</summary>
internal static class ABIDecodeTestSupport
{
    internal static byte[] Hex(string hex) => Convert.FromHexString(hex);

    /// <summary>A 32-byte big-endian word holding a small number.</summary>
    internal static byte[] Word(ulong value)
    {
        var word = new byte[32];
        for (int i = 0; i < 8; i++)
        {
            word[31 - i] = (byte)(value >> (8 * i));
        }

        return word;
    }

    /// <summary>A 32-byte word from up to 64 hex digits, right-aligned (leading zeros implied): <c>WordHex("ff")</c> is 31 zero bytes then 0xff.</summary>
    internal static byte[] WordHex(string hex) => Hex(hex.PadLeft(64, '0'));

    /// <summary>A word with all 32 bytes set to <paramref name="value"/>.</summary>
    internal static byte[] Filled(byte value) => Enumerable.Repeat(value, 32).ToArray();

    /// <summary>Concatenates byte arrays (words, payloads, selectors).</summary>
    internal static byte[] Cat(params byte[][] parts) => [.. parts.SelectMany(p => p)];

    /// <summary>A blob padded with zeros to a multiple of 32 bytes (empty stays empty).</summary>
    internal static byte[] Padded(byte[] data) => [.. data, .. new byte[(32 - (data.Length & 31)) & 31]];

    /// <summary>The encoding of a dynamic bytes/string value: length word, payload, zero padding.</summary>
    internal static byte[] BytesValue(byte[] payload) => Cat(Word((ulong)payload.Length), Padded(payload));

    internal static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    internal static int[] Path(params int[] indexes) => indexes;

    internal static ABIDecodeLimits Limits(
        int? bytes = null, int? depth = null, int? array = null, int? visited = null, int? work = null)
    {
        var d = ABIDecodeLimits.Default;
        return new ABIDecodeLimits(bytes ?? d.MaxEncodedBytes, depth ?? d.MaxValueDepth, array ?? d.MaxArrayElements, visited ?? d.MaxVisitedValues, work ?? d.MaxWorkUnits);
    }

    /// <summary>Limits that never interfere with a test about something else.</summary>
    internal static ABIDecodeLimits Generous => new(16 * 1024 * 1024, ABIDecodeLimits.HardMaxValueDepth, 1_000_000, 10_000_000, 100_000_000);

    internal static ABIFunctionSchema Function(string signature)
    {
        var limits = ABICompileLimits.Default;
        Assert.IsTrue(ABISchemaCompiler.TryCompileFunctionSignature(signature, in limits, out var function, out var error), $"compile '{signature}': {error}");
        return function;
    }

    /// <summary>Strictly decodes and asserts success; returns the valid view.</summary>
    internal static ABIData DecodeOk(byte[] bytes, ABISchema schema, ABIDecodeLimits? limits = null)
    {
        var effective = limits ?? ABIDecodeLimits.Default;
        Assert.IsTrue(ABIData.TryDecodeComplete(bytes, schema, in effective, out var data, out var status, out var error), $"expected success, got {status} {error}");
        Assert.AreEqual(ABIStatus.Success, status);
        Assert.IsTrue(data.IsValid);
        Assert.IsNotNull(data.Schema);
        Assert.AreEqual(bytes.Length, data.EncodedBytes.Length);
        Assert.AreEqual(ABIStatus.Success, error.Status);
        return data;
    }

    /// <summary>Asserts a failed decode: status, no valid view, defaulted view members, and a diagnostic error carrying the same status.</summary>
    internal static ABIError DecodeFails(byte[] bytes, ABISchema schema, ABIStatus expected, ABIDecodeLimits? limits = null, string? why = null)
    {
        var effective = limits ?? ABIDecodeLimits.Default;
        bool ok = ABIData.TryDecodeComplete(bytes, schema, in effective, out var data, out var status, out var error);
        string label = why ?? schema.CanonicalType;
        Assert.IsFalse(ok, $"{label}: expected {expected} but decode succeeded");
        Assert.AreEqual(expected, status, $"{label}: {error}");
        Assert.AreEqual(expected, error.Status, label);
        Assert.IsFalse(data.IsValid, label);
        Assert.IsNull(data.Schema, label);
        Assert.IsTrue(data.EncodedBytes.IsEmpty, label);
        return error;
    }

    /// <summary>Decodes a document built from a canonical tuple text and bytes.</summary>
    internal static ABIData DecodeOk(string tupleText, byte[] bytes, ABIDecodeLimits? limits = null) => DecodeOk(bytes, CompileTuple(tupleText), limits);

    internal static ABIError DecodeFails(string tupleText, byte[] bytes, ABIStatus expected, ABIDecodeLimits? limits = null) =>
        DecodeFails(bytes, CompileTuple(tupleText), expected, limits, why: tupleText);

    internal static ABIFieldLocation Locate(in ABIData data, params int[] path)
    {
        Assert.IsTrue(data.TryLocate(path, out var field, out var status), $"locate [{string.Join(",", path)}] failed with {status}");
        Assert.AreEqual(ABIStatus.Success, status);
        return field;
    }

    internal static void AssertLocated(in ABIData data, int[] path, ABITypeKind kind, (int Offset, int Length) head, (int Offset, int Length) value)
    {
        var field = Locate(in data, path);
        string where = $"path [{string.Join(",", path)}]";
        Assert.AreEqual(kind, field.Kind, where + " kind");
        Assert.AreEqual(new ABIRange(head.Offset, head.Length), field.HeadRange, where + " head");
        Assert.AreEqual(new ABIRange(value.Offset, value.Length), field.ValueRange, where + " value");
    }
}
