using System.Numerics;
using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.TypedAdapterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Independent model of the legacy <c>v</c> arithmetic, written from the EIP-155 text and using only
/// <see cref="BigInteger"/>: no production code, no shifts or masks shared with the adapters. EIP-155: with a chain id,
/// <c>v = chainId * 2 + 35 + parity</c>; without one (pre-EIP-155), <c>v = 27 + parity</c>.
/// </summary>
internal static class EvmSignatureOracle
{
    /// <summary>The expected outcome of reading a legacy <c>v</c> given as an integer.</summary>
    public readonly record struct LegacyExpectation(RLPStatus Status, int Parity, ulong? ChainId);

    /// <summary>Chain ids the readers, writers and sizers must handle: real networks, 32/64-bit edges and the extremes.</summary>
    public static readonly (string Name, ulong ChainId)[] RealChainIds =
    [
        ("Ethereum", 1),
        ("BNB Smart Chain", 56),
        ("Polygon PoS", 137),
        ("Polygon zkEVM", 1101),
        ("Base", 8453),
        ("Arbitrum One", 42161),
        ("Avalanche C-Chain", 43114),
        ("Linea", 59144),
        ("Plume", 98866),
        ("2^32 - 1", 4294967295UL),
        ("2^32", 4294967296UL),
        ("2^53 - 1", 9007199254740991UL),
        ("2^63 - 1", 9223372036854775807UL),
        ("ulong.MaxValue", ulong.MaxValue),
    ];

    /// <summary>Chain ids where the encoded width or the literal-byte boundary of <c>v</c> changes, plus zero.</summary>
    public static IEnumerable<ulong> WidthBoundaryChainIds()
    {
        var ids = new SortedSet<ulong> { 0, 1, 2, 45, 46, 47, 48, 109, 110, 111, 126, 127, 128, 129, ulong.MaxValue - 1, ulong.MaxValue };
        for (var bytes = 1; bytes <= 8; bytes++)
        {
            // v crosses a width at 2^(8n); the chain id at the crossing is about half of that.
            var around = 1UL << (8 * bytes - 1);
            for (long delta = -20; delta <= 20; delta++)
            {
                var candidate = (BigInteger)around + delta;
                if (candidate >= 0 && candidate <= ulong.MaxValue)
                {
                    ids.Add((ulong)candidate);
                }
            }

            var full = bytes == 8 ? ulong.MaxValue : (1UL << (8 * bytes)) - 1;
            ids.Add(full);
            ids.Add(full - 1);
            if (bytes < 8)
            {
                ids.Add(full + 1);
            }
        }

        return ids;
    }

    /// <summary>The legacy <c>v</c> for a parity and an optional chain id.</summary>
    public static BigInteger LegacyV(int parity, ulong? chainId) =>
        chainId is ulong chain ? (new BigInteger(chain) * 2) + 35 + parity : 27 + parity;

    /// <summary>The oracle's canonical one-item encoding of <see cref="LegacyV"/>.</summary>
    public static byte[] LegacyVItem(int parity, ulong? chainId) => EncodeString(Minimal(LegacyV(parity, chainId)));

    /// <summary>The oracle's canonical one-item encoding of a typed y parity.</summary>
    public static byte[] YParityItem(int parity) => EncodeString(Minimal(parity));

    /// <summary>What reading the integer <paramref name="v"/> as a legacy <c>v</c> must give (go-ethereum: 27/28 unprotected, v &gt;= 35 protected).</summary>
    public static LegacyExpectation ExpectLegacyV(BigInteger v)
    {
        if (v == 27 || v == 28)
        {
            return new LegacyExpectation(RLPStatus.Success, (int)(v - 27), null);
        }

        if (v < 35)
        {
            return new LegacyExpectation(RLPStatus.InvalidSignatureV, 0, null);
        }

        var chain = BigInteger.DivRem(v - 35, 2, out var parity);
        return chain > ulong.MaxValue
            ? new LegacyExpectation(RLPStatus.IntegerOverflow, 0, null)
            : new LegacyExpectation(RLPStatus.Success, (int)parity, (ulong)chain);
    }

    /// <summary>The status of a legacy <c>v</c> payload, applying the canonical-integer and width rules before the domain.</summary>
    public static LegacyExpectation ExpectLegacyVPayload(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return ExpectLegacyV(0);
        }

        if (payload[0] == 0)
        {
            return new LegacyExpectation(RLPStatus.NonCanonicalInteger, 0, null);
        }

        if (payload.Length > 9)
        {
            return new LegacyExpectation(RLPStatus.IntegerOverflow, 0, null);
        }

        return ExpectLegacyV(new BigInteger(payload, isUnsigned: true, isBigEndian: true));
    }

    /// <summary>The status of a typed y parity payload.</summary>
    public static RLPStatus ExpectYParityPayload(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return RLPStatus.Success;
        }

        if (payload[0] == 0)
        {
            return RLPStatus.NonCanonicalInteger;
        }

        if (payload.Length > 8)
        {
            return RLPStatus.IntegerOverflow;
        }

        var value = new BigInteger(payload, isUnsigned: true, isBigEndian: true);
        return value == 1 ? RLPStatus.Success : RLPStatus.InvalidSignatureV;
    }

    /// <summary>What reading three decoded items as a signature must give; <see cref="R32"/> and <see cref="S32"/> are null on failure.</summary>
    public readonly record struct SignatureExpectation(RLPStatus Status, int Parity, ulong? ChainId, byte[]? R32, byte[]? S32);

    /// <summary>Expected outcome of <c>TryGetEvmLegacySignature(v, r, s)</c> for oracle items: <c>v</c> first, then <c>r</c>, then <c>s</c>; a list is WrongKind.</summary>
    public static SignatureExpectation ExpectLegacySignature(RlpNode v, RlpNode r, RlpNode s)
    {
        if (v is not RlpString vText)
        {
            return Failed(RLPStatus.WrongKind);
        }

        var legacy = ExpectLegacyVPayload(vText.Value);
        return legacy.Status == RLPStatus.Success ? ExpectScalars(r, s, legacy.Parity, legacy.ChainId) : Failed(legacy.Status);
    }

    /// <summary>Expected outcome of <c>TryGetEvmYParitySignature(yParity, r, s)</c> for oracle items.</summary>
    public static SignatureExpectation ExpectYParitySignature(RlpNode yParity, RlpNode r, RlpNode s)
    {
        if (yParity is not RlpString text)
        {
            return Failed(RLPStatus.WrongKind);
        }

        var status = ExpectYParityPayload(text.Value);
        return status == RLPStatus.Success ? ExpectScalars(r, s, text.Value.Length, null) : Failed(status);
    }

    private static SignatureExpectation Failed(RLPStatus status) => new(status, 0, null, null, null);

    private static SignatureExpectation ExpectScalars(RlpNode r, RlpNode s, int parity, ulong? chainId)
    {
        var rStatus = ExpectScalar(r, out var rValue);
        if (rStatus != RLPStatus.Success)
        {
            return Failed(rStatus);
        }

        var sStatus = ExpectScalar(s, out var sValue);
        return sStatus != RLPStatus.Success
            ? Failed(sStatus)
            : new SignatureExpectation(RLPStatus.Success, parity, chainId, Padded32(rValue), Padded32(sValue));
    }

    /// <summary>An unsigned integer of at most 32 bytes: empty is zero, a leading zero byte is non-canonical, more than 32 bytes overflows; a list is WrongKind.</summary>
    private static RLPStatus ExpectScalar(RlpNode node, out BigInteger value)
    {
        value = BigInteger.Zero;
        if (node is not RlpString text)
        {
            return RLPStatus.WrongKind;
        }

        var payload = text.Value;
        if (payload.Length == 0)
        {
            return RLPStatus.Success;
        }

        if (payload[0] == 0)
        {
            return RLPStatus.NonCanonicalInteger;
        }

        if (payload.Length > 32)
        {
            return RLPStatus.IntegerOverflow;
        }

        value = new BigInteger(payload, isUnsigned: true, isBigEndian: true);
        return RLPStatus.Success;
    }
}
