using System.Buffers.Binary;
using System.Numerics;
using System.Text.Unicode;
using EccentricWare.Web3.DataTypes.ABI;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>Compares a decoded <see cref="ABIData"/> with the independent reference model: every location, and every typed accessor on every value.</summary>
internal static class ABIReferenceAssertions
{
    internal static ABITypeKind KindOf(RType type) => type switch
    {
        RElementary e => e.Kind,
        RBlobType b => b.IsString ? ABITypeKind.String : ABITypeKind.Bytes,
        RTupleType => ABITypeKind.Tuple,
        RFixedArrayType => ABITypeKind.FixedArray,
        _ => ABITypeKind.DynamicArray,
    };

    /// <summary>Encodes <paramref name="value"/> canonically and asserts the library locates and reads every value exactly as the reference says.</summary>
    internal static void AssertEveryPathMatches(in ABIData data, RTupleType type, RSeq value, byte[] expectedBytes)
    {
        var locations = new List<RLocation>();
        byte[] canonical = ABIReference.EncodeRoot(type, value, locations);
        CollectionAssert.AreEqual(expectedBytes, canonical, "reference encoding of the decoded value differs from the input");
        foreach (RLocation location in locations)
        {
            AssertLocation(in data, location);
        }
    }

    private static void AssertLocation(in ABIData data, RLocation loc)
    {
        int[] path = loc.Path;
        string where = $"{loc.Type.Text} at [{string.Join(",", path)}]";
        Assert.IsTrue(data.TryLocate(path, out ABIFieldLocation field, out ABIStatus status), $"{where}: locate failed with {status}");
        Assert.AreEqual(loc.Head, field.HeadRange, where + " head");
        Assert.AreEqual(loc.ValueRange, field.ValueRange, where + " value");
        Assert.AreEqual(KindOf(loc.Type), field.Kind, where + " kind");

        switch (loc.Type)
        {
            case RElementary e:
                AssertElementary(in data, e, ((RWord)loc.Value).Word, path, where);
                break;

            case RBlobType blob:
                byte[] payload = ((RBlob)loc.Value).Data;
                Assert.IsTrue(data.TryGetBytePayload(path, out var bytes, out ABIRange range, out status), where);
                CollectionAssert.AreEqual(payload, bytes.ToArray(), where + " payload");
                Assert.AreEqual(new ABIRange(loc.ValueRange.Offset + 32, payload.Length), range, where + " payload range");
                if (blob.IsString)
                {
                    bool valid = Utf8.IsValid(payload);
                    Assert.AreEqual(valid, data.TryGetStringUtf8Validated(path, out var text, out ABIRange textRange, out status), where + " utf8");
                    Assert.AreEqual(valid ? ABIStatus.Success : ABIStatus.InvalidUtf8, status, where);
                    Assert.AreEqual(valid ? range : default, textRange, where);
                    Assert.AreEqual(valid ? payload.Length : 0, text.Length, where);
                }
                else
                {
                    Assert.IsFalse(data.TryGetStringUtf8Validated(path, out _, out _, out status), where);
                    Assert.AreEqual(ABIStatus.TypeMismatch, status, where);
                }

                break;

            case RFixedArrayType fixedArray:
                Assert.IsTrue(data.TryGetArrayLength(path, out int fixedLength, out status), where);
                Assert.AreEqual(fixedArray.Length, fixedLength, where);
                break;

            case RDynamicArrayType:
                Assert.IsTrue(data.TryGetArrayLength(path, out int dynamicLength, out status), where);
                Assert.AreEqual(((RSeq)loc.Value).Items.Length, dynamicLength, where);
                break;

            case RTupleType:
                Assert.IsFalse(data.TryGetWord(path, out _, out status), where);
                Assert.AreEqual(ABIStatus.TypeMismatch, status, where);
                break;
        }
    }

    private static void AssertElementary(in ABIData data, RElementary e, byte[] word, int[] path, string where)
    {
        ABIStatus status;
        if (e.Kind is ABITypeKind.Fixed or ABITypeKind.UFixed)
        {
            // Scale-bearing words never leave without their scale (spec 5.1): the raw word accessor refuses them.
            Assert.IsFalse(data.TryGetWord(path, out var refused, out status), where);
            Assert.AreEqual(ABIStatus.TypeMismatch, status, where);
            Assert.IsTrue(refused.IsEmpty, where);
        }
        else
        {
            Assert.IsTrue(data.TryGetWord(path, out var raw, out status), where);
            CollectionAssert.AreEqual(word, raw.ToArray(), where + " word");
        }

        switch (e.Kind)
        {
            case ABITypeKind.UInt:
                Assert.IsTrue(data.TryGetUInt256Word(path, out var u256, out status), where);
                CollectionAssert.AreEqual(word, u256.ToArray(), where);
                BigInteger unsigned = ABIReference.Unsigned(word);
                bool fits = unsigned <= ulong.MaxValue;
                Assert.AreEqual(fits, data.TryGetUInt64(path, out ulong u64, out status), where + " uint64");
                Assert.AreEqual(fits ? ABIStatus.Success : ABIStatus.ValueOutOfRange, status, where);
                Assert.AreEqual(fits ? (ulong)unsigned : 0UL, u64, where);
                Assert.IsFalse(data.TryGetInt256Word(path, out _, out status), where);
                Assert.AreEqual(ABIStatus.TypeMismatch, status, where);
                break;

            case ABITypeKind.Int:
                Assert.IsTrue(data.TryGetInt256Word(path, out var i256, out status), where);
                CollectionAssert.AreEqual(word, i256.ToArray(), where);
                BigInteger signed = ABIReference.Signed(word);
                bool fitsLong = signed >= long.MinValue && signed <= long.MaxValue;
                Assert.AreEqual(fitsLong, data.TryGetInt64(path, out long i64, out status), where + " int64");
                Assert.AreEqual(fitsLong ? ABIStatus.Success : ABIStatus.ValueOutOfRange, status, where);
                Assert.AreEqual(fitsLong ? (long)signed : 0L, i64, where);
                Assert.IsFalse(data.TryGetUInt64(path, out _, out status), where);
                Assert.AreEqual(ABIStatus.TypeMismatch, status, where);
                break;

            case ABITypeKind.Address:
                Assert.IsTrue(data.TryGetAddress(path, out var address, out status), where);
                CollectionAssert.AreEqual(word[12..], address.ToArray(), where);
                break;

            case ABITypeKind.Bool:
                Assert.IsTrue(data.TryGetBool(path, out bool flag, out status), where);
                Assert.AreEqual(word[31] == 1, flag, where);
                break;

            case ABITypeKind.FixedBytes:
                Assert.IsTrue(data.TryGetFixedBytes(path, out var fixedBytes, out status), where);
                CollectionAssert.AreEqual(word[..e.Width], fixedBytes.ToArray(), where);
                break;

            case ABITypeKind.Function:
                Assert.IsTrue(data.TryGetFunction(path, out var target, out uint selector, out status), where);
                CollectionAssert.AreEqual(word[..20], target.ToArray(), where);
                Assert.AreEqual(BinaryPrimitives.ReadUInt32BigEndian(word.AsSpan(20, 4)), selector, where);
                break;

            case ABITypeKind.Fixed:
                Assert.IsTrue(data.TryGetSignedScaledWord(path, out var signedScaled, out byte signedScale, out status), where);
                CollectionAssert.AreEqual(word, signedScaled.ToArray(), where);
                Assert.AreEqual(e.Scale, signedScale, where);
                Assert.IsFalse(data.TryGetInt64(path, out _, out status), where);
                Assert.AreEqual(ABIStatus.TypeMismatch, status, where);
                Assert.IsFalse(data.TryGetUnsignedScaledWord(path, out _, out _, out status), where);
                Assert.AreEqual(ABIStatus.TypeMismatch, status, where);
                break;

            case ABITypeKind.UFixed:
                Assert.IsTrue(data.TryGetUnsignedScaledWord(path, out var unsignedScaled, out byte unsignedScale, out status), where);
                CollectionAssert.AreEqual(word, unsignedScaled.ToArray(), where);
                Assert.AreEqual(e.Scale, unsignedScale, where);
                Assert.IsFalse(data.TryGetUInt64(path, out _, out status), where);
                Assert.AreEqual(ABIStatus.TypeMismatch, status, where);
                Assert.IsFalse(data.TryGetSignedScaledWord(path, out _, out _, out status), where);
                Assert.AreEqual(ABIStatus.TypeMismatch, status, where);
                break;
        }

        // A bytes payload read of any elementary field is a type mismatch, never a reinterpretation.
        Assert.IsFalse(data.TryGetBytePayload(path, out _, out _, out status), where);
        Assert.AreEqual(ABIStatus.TypeMismatch, status, where);
        Assert.IsFalse(data.TryGetArrayLength(path, out _, out status), where);
        Assert.AreEqual(ABIStatus.TypeMismatch, status, where);
    }
}
