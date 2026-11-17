using System.Runtime.InteropServices;
using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>Shared helpers for the encoder and writer tests: sentinel buffers and an oracle-tree walker over <see cref="RLPWriter"/>.</summary>
internal static class RlpWriterTestSupport
{
    public const byte Sentinel = 0xee;

    /// <summary>Frame slots large enough for every generated tree (the oracle caps list depth at 64).</summary>
    public const int AmpleFrames = 70;

    public static byte[] Filled(int length, byte fill = Sentinel)
    {
        var buffer = new byte[length];
        Array.Fill(buffer, fill);
        return buffer;
    }

    public static bool IsFilled(ReadOnlySpan<byte> bytes, byte fill = Sentinel)
    {
        foreach (var b in bytes)
        {
            if (b != fill)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A span that reports <paramref name="length"/> elements over a small backing array, to reach int-overflow sizes
    /// without allocating gigabytes. Only valid for code that decides on the length alone (sizing, overflow and
    /// capacity rejection) and never reads or copies the elements; a single-byte span is safe to read.
    /// </summary>
    public static ReadOnlySpan<byte> FakeLengthSpan(int length)
    {
        var backing = new byte[8];
        return MemoryMarshal.CreateReadOnlySpan(ref MemoryMarshal.GetArrayDataReference(backing), length);
    }

    /// <summary>
    /// Writes <paramref name="node"/> into <paramref name="writer"/>: strings with <c>TryWriteByteString</c>, lists with
    /// <c>TryBeginList(RlpReference.PayloadLength)</c>, the children, then <c>TryEndList</c>. Returns the first failure.
    /// </summary>
    public static RLPStatus WriteNode(ref RLPWriter writer, RlpNode node)
    {
        if (node is RlpString text)
        {
            return writer.TryWriteByteString(text.Value);
        }

        var list = (RlpList)node;
        var status = writer.TryBeginList(RlpReference.PayloadLength(list));
        if (status != RLPStatus.Success)
        {
            return status;
        }

        foreach (var child in list.Items)
        {
            status = WriteNode(ref writer, child);
            if (status != RLPStatus.Success)
            {
                return status;
            }
        }

        return writer.TryEndList();
    }

    /// <summary>Builds <paramref name="node"/> with a writer over an exactly sized destination; asserts every step succeeds.</summary>
    public static byte[] BuildWithWriter(RlpNode node, int frameSlots = AmpleFrames)
    {
        var expectedLength = RlpReference.Encode(node).Length;
        var destination = Filled(expectedLength + 4);
        var writer = new RLPWriter(destination, new int[frameSlots]);

        Assert.AreEqual(RLPStatus.Success, WriteNode(ref writer, node));
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(expectedLength, written);
        Assert.IsTrue(IsFilled(destination.AsSpan(written)), "bytes past the encoding are untouched");
        return destination.AsSpan(0, written).ToArray();
    }

    /// <summary>One step of a flattened tree: a list header (with payload length), a string, or a list end.</summary>
    public readonly record struct WriterOp(WriterOpKind Kind, byte[] Payload, int ListPayloadLength);

    public enum WriterOpKind
    {
        BeginList,
        String,
        EndList,
    }

    public static List<WriterOp> Flatten(RlpNode node)
    {
        var ops = new List<WriterOp>();
        FlattenInto(node, ops);
        return ops;
    }

    private static void FlattenInto(RlpNode node, List<WriterOp> ops)
    {
        if (node is RlpString text)
        {
            ops.Add(new WriterOp(WriterOpKind.String, text.Value, 0));
            return;
        }

        var list = (RlpList)node;
        ops.Add(new WriterOp(WriterOpKind.BeginList, [], RlpReference.PayloadLength(list)));
        foreach (var child in list.Items)
        {
            FlattenInto(child, ops);
        }

        ops.Add(new WriterOp(WriterOpKind.EndList, [], 0));
    }

    /// <summary>Applies <paramref name="op"/> to the writer.</summary>
    public static RLPStatus Apply(ref RLPWriter writer, in WriterOp op) => op.Kind switch
    {
        WriterOpKind.BeginList => writer.TryBeginList(op.ListPayloadLength),
        WriterOpKind.String => writer.TryWriteByteString(op.Payload),
        _ => writer.TryEndList(),
    };
}
