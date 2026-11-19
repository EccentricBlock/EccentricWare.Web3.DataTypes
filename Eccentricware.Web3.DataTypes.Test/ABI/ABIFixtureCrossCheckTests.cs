using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIFixtureCatalog;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Runs every independently produced fixture (cast / Solidity docs, see ABIFixtures) through the decoder under the DEFAULT limits and
/// cross-checks it with the test-local reference model: the permissive reference decode of the cast bytes re-encodes to the same bytes
/// (so cast and the reference encoder agree), the library accepts them, and every head range, value range, kind and typed accessor
/// of every value equals what the reference says.
/// </summary>
[TestClass]
public sealed class ABIFixtureCrossCheckTests
{
    [TestMethod]
    public void TheCatalogCoversEveryFixtureConstant()
    {
        var declared = typeof(ABIFixtures).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name != nameof(ABIFixtures.ToolVersion))
            .Select(f => f.Name)
            .Where(n => !System.Text.RegularExpressions.Regex.IsMatch(n, @"^Bytes\d+Payload$") && !n.StartsWith("TransferChild", StringComparison.Ordinal))
            .ToHashSet();
        var cataloged = All.Select(f => f.Name).ToHashSet();
        Assert.IsTrue(declared.SetEquals(cataloged), "uncataloged: " + string.Join(",", declared.Except(cataloged)) + " unknown: " + string.Join(",", cataloged.Except(declared)));
        Assert.AreEqual(All.Length, cataloged.Count);
    }

    [TestMethod]
    public void EveryFixture_IsAcceptedUnderDefaultLimits_AndMatchesTheReferenceModelOnEveryPath()
    {
        foreach (Fixture fixture in All)
        {
            byte[] bytes = ArgumentBytes(fixture);
            ABISchema schema = CompileTuple(fixture.Tuple);
            ABIData data = DecodeOk(bytes, schema);
            Assert.AreEqual(fixture.Tuple, data.Schema!.CanonicalType, fixture.Name);

            RTupleType type = ABIReference.ParseTuple(fixture.Tuple);
            Assert.IsTrue(ABIReference.TryDecodePermissive(type, bytes, out RSeq? value), fixture.Name + ": reference decode");
            ABIReferenceAssertions.AssertEveryPathMatches(in data, type, value!, bytes);
        }
    }

    [TestMethod]
    public void EveryProperPrefixOfEveryFixture_IsTruncated()
    {
        foreach (Fixture fixture in All)
        {
            byte[] bytes = ArgumentBytes(fixture);
            ABISchema schema = CompileTuple(fixture.Tuple);
            for (int length = 0; length < bytes.Length; length++)
            {
                DecodeFails(bytes[..length], schema, ABIStatus.Truncated, why: $"{fixture.Name} prefix {length}/{bytes.Length}");
            }
        }
    }

    [TestMethod]
    public void AppendedBytes_AfterAnyFixture_AreNonCanonicalTrailingData()
    {
        foreach (Fixture fixture in All)
        {
            byte[] bytes = ArgumentBytes(fixture);
            ABISchema schema = CompileTuple(fixture.Tuple);
            foreach (int extra in new[] { 1, 31, 32, 64 })
            {
                ABIError error = DecodeFails([.. bytes, .. new byte[extra]], schema, ABIStatus.NonCanonicalLayout, why: $"{fixture.Name} +{extra}");
                Assert.AreEqual(bytes.Length, error.DocumentOffset, $"{fixture.Name} +{extra}: the root ends where the canonical value ends");
            }

            DecodeFails([.. bytes, 0xff], schema, ABIStatus.NonCanonicalLayout, why: fixture.Name + " garbage suffix");
        }
    }

    [TestMethod]
    public void EveryFixture_WithAnyOneNonZeroByteAppendedOrWordAdded_IsStillRejectedUnderTheCallPath()
    {
        // The same document reached through ABICallData behaves identically (one validator).
        foreach (Fixture fixture in All.Where(f => f.IsCall))
        {
            byte[] call = Hex(fixture.Hex);
            ABISchema schema = CompileTuple(fixture.Tuple);
            var limits = ABIDecodeLimits.Default;
            Assert.IsFalse(ABIData.TryDecodeComplete([.. call[4..], 0x01], schema, in limits, out _, out var status), fixture.Name);
            Assert.AreEqual(ABIStatus.NonCanonicalLayout, status, fixture.Name);
        }
    }
}
